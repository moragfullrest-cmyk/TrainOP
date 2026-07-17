using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Infers the wagon return shape of a data-oriented station handler.
    /// </summary>
    internal static class HandlerReturnInference
    {
        /// <summary>
        /// Infers the return shape from a handler symbol, optional body, and input wagons.
        /// </summary>
        public static ReturnShape Infer(
            IMethodSymbol handlerSymbol,
            CSharpSyntaxNode body,
            Location handlerLocation,
            SemanticModel semanticModel,
            ImmutableArray<WagonBinding> inputWagons)
        {
            var returnType = UnwrapReturnType(handlerSymbol.ReturnType);
            if (IsVoidReturn(returnType))
            {
                return ReturnShape.Void;
            }

            if (returnType == null || returnType.SpecialType == SpecialType.System_Object)
            {
                returnType = InferBodyReturnType(body, semanticModel);
            }
            else if (ReturnTypeDisplayBuilder.IsSignalBaseReturn(returnType))
            {
                var fromBody = UnwrapReturnType(InferBodyReturnType(body, semanticModel));
                if (fromBody != null)
                {
                    returnType = fromBody;
                }
            }

            returnType = UnwrapReturnType(returnType);

            if (IsVoidReturn(returnType))
            {
                return ReturnShape.Void;
            }

            var returnTypeDisplay = ReturnTypeDisplayBuilder.BuildDisplay(returnType);
            var useGenericReturn = ReturnTypeDisplayBuilder.UseGenericReturn(returnType);
            var fallbackLocation = handlerLocation
                ?? (handlerSymbol.Locations.Length > 0 ? handlerSymbol.Locations[0] : null);

            if (returnType == null)
            {
                return WithReturnType(ReturnShape.Unknown, returnTypeDisplay, useGenericReturn);
            }

            if (ReturnTypeDisplayBuilder.IsRuntimeSignalReturn(returnType))
            {
                return new ReturnShape(
                    ImmutableArray<WagonBinding>.Empty,
                    isCargoManifest: false,
                    isValueTuple: false,
                    isUnknown: true,
                    returnTypeDisplay: returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    useGenericReturn: false,
                    isRuntimeSignalReturn: true);
            }

            if (ReturnTypeDisplayBuilder.IsExplicitSignalReturn(returnType))
            {
                return new ReturnShape(
                    ImmutableArray<WagonBinding>.Empty,
                    isCargoManifest: false,
                    isValueTuple: false,
                    isUnknown: true,
                    returnTypeDisplay: ReturnTypeDisplayBuilder.SignalReturnTypeDisplay,
                    useGenericReturn: false,
                    isExplicitSignalReturn: true);
            }

            if (IsCargoManifest(returnType))
            {
                return new ReturnShape(
                    ImmutableArray<WagonBinding>.Empty,
                    isCargoManifest: true,
                    isValueTuple: false,
                    returnTypeDisplay: returnTypeDisplay,
                    useGenericReturn: useGenericReturn);
            }

            var bodyExpression = GetBodyExpression(body);
            if (IsGreenPayload(returnType, out var greenPayload))
            {
                return WithReturnType(
                    InferFromGreenPayload(greenPayload, inputWagons, semanticModel, fallbackLocation, bodyExpression),
                    returnTypeDisplay,
                    useGenericReturn);
            }

            var shape = InferFromType(returnType, inputWagons, semanticModel, fallbackLocation, bodyExpression);
            if (!shape.IsCargoManifest && shape.Members.IsDefaultOrEmpty)
            {
                if (returnType == null || returnType.SpecialType == SpecialType.System_Object)
                {
                    return WithReturnType(ReturnShape.Unknown, null, useGenericReturn: true);
                }

                return WithReturnType(ReturnShape.Unknown, returnTypeDisplay, useGenericReturn);
            }

            return WithReturnType(shape, returnTypeDisplay, useGenericReturn);
        }

        /// <summary>
        /// Attaches return type metadata to an inferred return shape.
        /// </summary>
        private static ReturnShape WithReturnType(ReturnShape shape, string returnTypeDisplay, bool useGenericReturn)
        {
            if (shape.IsVoid)
            {
                return shape;
            }

            return new ReturnShape(
                shape.Members,
                shape.IsCargoManifest,
                shape.IsValueTuple,
                shape.IsUnknown,
                shape.IsVoid,
                returnTypeDisplay,
                useGenericReturn,
                shape.IsExplicitSignalReturn,
                shape.IsRuntimeSignalReturn,
                shape.IsUnnamedTupleReturn,
                shape.IsMixedTupleReturn,
                shape.TupleReturnLocations);
        }

        /// <summary>
        /// Infers a return shape from a GreenPayload-wrapped return type.
        /// </summary>
        private static ReturnShape InferFromGreenPayload(
            ITypeSymbol greenPayload,
            ImmutableArray<WagonBinding> inputWagons,
            SemanticModel semanticModel,
            Location fallbackLocation,
            ExpressionSyntax bodyExpression)
        {
            var shape = InferFromType(greenPayload, inputWagons, semanticModel, fallbackLocation, bodyExpression);
            if (!shape.IsCargoManifest && shape.Members.IsDefaultOrEmpty)
            {
                return ReturnShape.Unknown;
            }

            return shape;
        }

        /// <summary>
        /// Extracts the return expression from a handler body when it is a single expression or block return.
        /// </summary>
        private static ExpressionSyntax GetBodyExpression(CSharpSyntaxNode body)
        {
            if (body is ExpressionSyntax expression)
            {
                return expression;
            }

            if (body is ArrowExpressionClauseSyntax arrow)
            {
                return arrow.Expression;
            }

            if (body is BlockSyntax block)
            {
                var returnStatement = block.DescendantNodes()
                    .OfType<ReturnStatementSyntax>()
                    .LastOrDefault(statement => statement.Expression != null);

                return returnStatement?.Expression;
            }

            return null;
        }

        /// <summary>
        /// Infers the return type from a handler body expression or block.
        /// </summary>
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

            if (body is ArrowExpressionClauseSyntax arrow)
            {
                return InferExpressionReturnType(arrow.Expression, semanticModel);
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

        /// <summary>
        /// Infers the return type from a single expression, including conditional branches.
        /// </summary>
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

        /// <summary>
        /// Determines whether an inferred type is specific enough to use for return-shape analysis.
        /// </summary>
        private static bool IsUsefulInferredType(ITypeSymbol typeSymbol)
        {
            return typeSymbol != null && typeSymbol.SpecialType != SpecialType.System_Object;
        }

        /// <summary>
        /// Builds a return shape from a concrete return type symbol.
        /// </summary>
        private static ReturnShape InferFromType(
            ITypeSymbol returnType,
            ImmutableArray<WagonBinding> inputWagons,
            SemanticModel semanticModel,
            Location fallbackLocation,
            ExpressionSyntax bodyExpression)
        {
            if (returnType == null)
            {
                return ReturnShape.Unknown;
            }

            if (IsValueTuple(returnType))
            {
                var members = ImmutableArray.CreateBuilder<WagonBinding>();
                if (returnType is INamedTypeSymbol namedTuple)
                {
                    var typeArguments = namedTuple.TypeArguments;
                    var elementNames = namedTuple.TupleElements;
                    var usePositionalNames = ShouldUsePositionalTupleNames(bodyExpression);
                    for (var i = 0; i < typeArguments.Length; i++)
                    {
                        string name;
                        if (!usePositionalNames
                            && elementNames != null
                            && i < elementNames.Length
                            && !TupleElementNaming.IsDefaultName(elementNames[i].Name, i))
                        {
                            name = elementNames[i].Name;
                        }
                        else
                        {
                            name = TupleElementNaming.DefaultName(i);
                        }

                        members.Add(new WagonBinding(
                            name,
                            typeArguments[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            typeArguments[i],
                            fallbackLocation));
                    }
                }

                var memberBindings = members.ToImmutable();
                return new ReturnShape(
                    memberBindings,
                    isCargoManifest: false,
                    isValueTuple: true,
                    isUnnamedTupleReturn: IsUnnamedValueTupleReturn(memberBindings),
                    isMixedTupleReturn: IsMixedValueTupleReturn(memberBindings),
                    tupleReturnLocations: CollectTupleReturnLocations(bodyExpression, fallbackLocation));
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

        /// <summary>
        /// Determines whether a handler return type represents no return value.
        /// </summary>
        private static bool IsVoidReturn(ITypeSymbol returnType)
        {
            if (returnType == null)
            {
                return false;
            }

            if (returnType.SpecialType == SpecialType.System_Void)
            {
                return true;
            }

            return IsNonGenericTask(returnType);
        }

        /// <summary>
        /// Unwraps Task-wrapped return types to their payload type.
        /// </summary>
        private static ITypeSymbol UnwrapReturnType(ITypeSymbol returnType)
        {
            while (returnType is INamedTypeSymbol named && IsTask(named))
            {
                if (!named.IsGenericType)
                {
                    break;
                }

                returnType = named.TypeArguments[0];
            }

            return returnType;
        }

        /// <summary>
        /// Determines whether a type symbol is System.Threading.Tasks.Task.
        /// </summary>
        internal static bool IsTask(ITypeSymbol typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol named
                && string.Equals(named.Name, "Task", StringComparison.Ordinal)
                && string.Equals(
                    named.ContainingNamespace?.ToDisplayString(),
                    "System.Threading.Tasks",
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a type symbol is the non-generic void Task type.
        /// </summary>
        internal static bool IsNonGenericTask(ITypeSymbol typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol named
                && IsTask(named)
                && !named.IsGenericType;
        }

        /// <summary>
        /// Determines whether the type is CargoManifest.
        /// </summary>
        private static bool IsCargoManifest(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol?.ToDisplayString(), "TrainOP.CargoManifest", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether the type is GreenPayload and extracts its payload type.
        /// </summary>
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

        /// <summary>
        /// Determines whether a value-tuple return maps wagons only by default ItemN element names.
        /// </summary>
        private static bool IsUnnamedValueTupleReturn(ImmutableArray<WagonBinding> members)
        {
            if (members.IsDefaultOrEmpty)
            {
                return false;
            }

            for (var i = 0; i < members.Length; i++)
            {
                if (!TupleElementNaming.IsDefaultName(members[i].Name, i))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether a value-tuple return mixes default ItemN names with explicit element names.
        /// </summary>
        private static bool IsMixedValueTupleReturn(ImmutableArray<WagonBinding> members)
        {
            if (members.IsDefaultOrEmpty)
            {
                return false;
            }

            var hasDefaultName = false;
            var hasExplicitName = false;
            for (var i = 0; i < members.Length; i++)
            {
                if (TupleElementNaming.IsDefaultName(members[i].Name, i))
                {
                    hasDefaultName = true;
                }
                else
                {
                    hasExplicitName = true;
                }

                if (hasDefaultName && hasExplicitName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Collects source locations of value-tuple return expressions in a handler body.
        /// </summary>
        private static ImmutableArray<Location> CollectTupleReturnLocations(
            ExpressionSyntax bodyExpression,
            Location fallbackLocation)
        {
            if (bodyExpression == null)
            {
                return fallbackLocation != null
                    ? ImmutableArray.Create(fallbackLocation)
                    : ImmutableArray<Location>.Empty;
            }

            var locations = ImmutableArray.CreateBuilder<Location>();
            CollectTupleExpressionLocations(bodyExpression, locations);
            if (locations.Count == 0 && fallbackLocation != null)
            {
                locations.Add(fallbackLocation);
            }

            return locations.ToImmutable();
        }

        /// <summary>
        /// Walks an expression tree and records locations of value-tuple literals.
        /// </summary>
        private static void CollectTupleExpressionLocations(
            ExpressionSyntax expression,
            ImmutableArray<Location>.Builder locations)
        {
            if (expression == null)
            {
                return;
            }

            if (expression is ConditionalExpressionSyntax conditional)
            {
                CollectTupleExpressionLocations(conditional.WhenTrue, locations);
                CollectTupleExpressionLocations(conditional.WhenFalse, locations);
                return;
            }

            var tupleExpression = UnwrapToTupleExpression(expression);
            if (tupleExpression == null)
            {
                return;
            }

            var location = tupleExpression.GetLocation();
            if (location != null)
            {
                locations.Add(location);
            }
        }

        /// <summary>
        /// When a tuple literal has no explicit element names in source, merge by ItemN ordinals.
        /// </summary>
        private static bool ShouldUsePositionalTupleNames(ExpressionSyntax bodyExpression)
        {
            var tupleExpression = UnwrapToTupleExpression(bodyExpression);
            if (tupleExpression == null)
            {
                return false;
            }

            for (var i = 0; i < tupleExpression.Arguments.Count; i++)
            {
                if (tupleExpression.Arguments[i].NameColon != null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Unwraps parenthesized expressions to reach an underlying tuple literal.
        /// </summary>
        private static TupleExpressionSyntax UnwrapToTupleExpression(ExpressionSyntax expression)
        {
            while (expression != null)
            {
                if (expression is TupleExpressionSyntax tupleExpression)
                {
                    return tupleExpression;
                }

                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is InvocationExpressionSyntax invocation
                    && invocation.ArgumentList?.Arguments.Count > 0)
                {
                    expression = invocation.ArgumentList.Arguments[0].Expression;
                    continue;
                }

                break;
            }

            return null;
        }

        /// <summary>
        /// Determines whether the type is a System.ValueTuple type.
        /// </summary>
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

    }
}
