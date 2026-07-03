using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    internal static class HandlerReturnInference
    {
        public static ReturnShape Infer(
            LambdaExpressionSyntax lambdaSyntax,
            IMethodSymbol lambdaSymbol,
            SemanticModel semanticModel,
            ImmutableArray<WagonBinding> inputWagons)
        {
            var returnType = UnwrapReturnType(lambdaSymbol.ReturnType);
            if (returnType == null || returnType.SpecialType == SpecialType.System_Object)
            {
                returnType = InferBodyReturnType(GetLambdaBody(lambdaSyntax), semanticModel);
                returnType = UnwrapReturnType(returnType);
            }

            if (returnType == null)
            {
                return ReturnShape.Unknown;
            }

            if (IsCargoManifest(returnType))
            {
                return new ReturnShape(ImmutableArray<WagonBinding>.Empty, isCargoManifest: true, isValueTuple: false);
            }

            if (IsRedFailure(returnType))
            {
                return ReturnShape.Unknown;
            }

            if (IsGreenPass(returnType))
            {
                return ReturnShape.Unknown;
            }

            if (IsGreenPayload(returnType, out var greenPayload))
            {
                return InferFromGreenPayload(greenPayload, inputWagons, semanticModel, lambdaSyntax.GetLocation());
            }

            var shape = InferFromType(returnType, inputWagons, semanticModel, lambdaSyntax.GetLocation());
            if (!shape.IsCargoManifest && shape.Members.IsDefaultOrEmpty)
            {
                return ReturnShape.Unknown;
            }

            return shape;
        }

        private static ReturnShape InferFromGreenPayload(
            ITypeSymbol greenPayload,
            ImmutableArray<WagonBinding> inputWagons,
            SemanticModel semanticModel,
            Location fallbackLocation)
        {
            var shape = InferFromType(greenPayload, inputWagons, semanticModel, fallbackLocation);
            if (!shape.IsCargoManifest && shape.Members.IsDefaultOrEmpty)
            {
                return ReturnShape.Unknown;
            }

            return shape;
        }

        private static CSharpSyntaxNode GetLambdaBody(LambdaExpressionSyntax lambdaSyntax)
        {
            if (lambdaSyntax is SimpleLambdaExpressionSyntax simpleLambda)
            {
                return simpleLambda.Body;
            }

            if (lambdaSyntax is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
            {
                return parenthesizedLambda.Body;
            }

            return null;
        }

        private static ITypeSymbol InferBodyReturnType(CSharpSyntaxNode body, SemanticModel semanticModel)
        {
            if (body == null)
            {
                return null;
            }

            if (body is ExpressionSyntax expression)
            {
                return InferExpressionReturnType(expression, semanticModel);
            }

            if (body is BlockSyntax block)
            {
                var returnStatement = block.DescendantNodes()
                    .OfType<ReturnStatementSyntax>()
                    .LastOrDefault(statement => statement.Expression != null);

                return returnStatement?.Expression == null
                    ? null
                    : InferExpressionReturnType(returnStatement.Expression, semanticModel);
            }

            return null;
        }

        private static ITypeSymbol InferExpressionReturnType(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            if (expression is ConditionalExpressionSyntax conditional)
            {
                var whenTrueType = InferExpressionReturnType(conditional.WhenTrue, semanticModel);
                if (IsUsefulInferredType(whenTrueType))
                {
                    return whenTrueType;
                }

                var whenFalseType = InferExpressionReturnType(conditional.WhenFalse, semanticModel);
                if (IsUsefulInferredType(whenFalseType))
                {
                    return whenFalseType;
                }
            }

            if (expression is InvocationExpressionSyntax invocation)
            {
                var invocationType = semanticModel.GetTypeInfo(invocation).Type;
                if (IsGreenPayload(invocationType, out var greenPayload))
                {
                    return greenPayload;
                }

                if (invocationType != null && invocationType.SpecialType != SpecialType.System_Object)
                {
                    return invocationType;
                }
            }

            var typeInfo = semanticModel.GetTypeInfo(expression);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        private static bool IsUsefulInferredType(ITypeSymbol typeSymbol)
        {
            return typeSymbol != null && typeSymbol.SpecialType != SpecialType.System_Object;
        }

        private static ReturnShape InferFromType(
            ITypeSymbol returnType,
            ImmutableArray<WagonBinding> inputWagons,
            SemanticModel semanticModel,
            Location fallbackLocation)
        {
            if (returnType == null)
            {
                return ReturnShape.Unknown;
            }

            if (IsValueTuple(returnType))
            {
                var members = ImmutableArray.CreateBuilder<WagonBinding>();
                var isUnnamedValueTuple = false;
                if (returnType is INamedTypeSymbol namedTuple)
                {
                    isUnnamedValueTuple = IsUnnamedValueTuple(namedTuple);
                    var typeArguments = namedTuple.TypeArguments;
                    var elementNames = namedTuple.TupleElements;
                    for (var i = 0; i < typeArguments.Length; i++)
                    {
                        string name;
                        if (elementNames != null
                            && i < elementNames.Length
                            && !IsDefaultTupleElementName(elementNames[i].Name, i))
                        {
                            name = elementNames[i].Name;
                        }
                        else if (isUnnamedValueTuple && i < inputWagons.Length)
                        {
                            name = inputWagons[i].Name;
                        }
                        else
                        {
                            name = "Item" + (i + 1);
                        }

                        members.Add(new WagonBinding(
                            name,
                            typeArguments[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            typeArguments[i],
                            fallbackLocation));
                    }
                }

                return new ReturnShape(
                    members.ToImmutable(),
                    isCargoManifest: false,
                    isValueTuple: true,
                    isUnnamedValueTuple: isUnnamedValueTuple);
            }

            var bindings = ImmutableArray.CreateBuilder<WagonBinding>();
            foreach (var member in returnType.GetMembers())
            {
                if (member is IPropertySymbol property
                    && property.DeclaredAccessibility == Accessibility.Public
                    && !property.IsStatic)
                {
                    bindings.Add(new WagonBinding(
                        property.Name,
                        property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        property.Type,
                        fallbackLocation));
                }
                else if (member is IFieldSymbol field
                    && field.DeclaredAccessibility == Accessibility.Public
                    && !field.IsStatic)
                {
                    bindings.Add(new WagonBinding(
                        field.Name,
                        field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        field.Type,
                        fallbackLocation));
                }
            }

            return new ReturnShape(bindings.ToImmutable(), isCargoManifest: false, isValueTuple: false);
        }

        private static ITypeSymbol UnwrapReturnType(ITypeSymbol returnType)
        {
            if (returnType == null)
            {
                return null;
            }

            if (returnType is INamedTypeSymbol named
                && named.IsGenericType
                && string.Equals(named.ConstructedFrom.ToDisplayString(), "System.Threading.Tasks.Task", StringComparison.Ordinal))
            {
                return named.TypeArguments[0];
            }

            return returnType;
        }

        private static bool IsCargoManifest(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol?.ToDisplayString(), "TrainOP.CargoManifest", StringComparison.Ordinal);
        }

        private static bool IsRedFailure(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol?.ToDisplayString(), "TrainOP.RedFailure", StringComparison.Ordinal);
        }

        private static bool IsGreenPass(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol?.ToDisplayString(), "TrainOP.GreenPass", StringComparison.Ordinal);
        }

        private static bool IsGreenPayload(ITypeSymbol typeSymbol, out ITypeSymbol payloadType)
        {
            payloadType = null;
            if (typeSymbol is INamedTypeSymbol named
                && named.IsGenericType
                && string.Equals(named.ConstructedFrom.Name, "GreenPayload", StringComparison.Ordinal))
            {
                payloadType = named.TypeArguments[0];
                return true;
            }

            return false;
        }

        private static bool IsValueTuple(ITypeSymbol typeSymbol)
        {
            if (!(typeSymbol is INamedTypeSymbol named) || !named.IsTupleType)
            {
                return false;
            }

            var fullName = named.TupleUnderlyingType?.ToDisplayString() ?? named.ToDisplayString();
            return fullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal)
                || fullName.StartsWith("(", StringComparison.Ordinal);
        }

        private static bool IsUnnamedValueTuple(INamedTypeSymbol namedTuple)
        {
            var elementNames = namedTuple.TupleElements;
            if (elementNames.IsDefaultOrEmpty)
            {
                return true;
            }

            for (var i = 0; i < elementNames.Length; i++)
            {
                if (!IsDefaultTupleElementName(elementNames[i].Name, i))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDefaultTupleElementName(string name, int index)
        {
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            if (!name.StartsWith("Item", StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(name.Substring(4), out var parsed)
                && parsed == index + 1;
        }
    }
}
