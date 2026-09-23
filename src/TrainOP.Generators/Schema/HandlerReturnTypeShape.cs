using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds return shapes from CLR/tuple types and Task/GreenPayload wrappers.
    /// </summary>
    internal static class HandlerReturnTypeShape
    {
        internal static ReturnShape InferFromType(
            ITypeSymbol returnType,
            ImmutableArray<WagonBinding> inputWagons,
            SemanticModel semanticModel,
            Location fallbackLocation,
            CSharpSyntaxNode body,
            ExpressionSyntax bodyExpression)
        {
            if (returnType == null)
            {
                return ReturnShape.Unknown;
            }

            if (IsValueTuple(returnType))
            {
                var members = ImmutableArray.CreateBuilder<WagonBinding>();
                var hasDefaultItemN = false;
                if (returnType is INamedTypeSymbol namedTuple)
                {
                    var typeArguments = namedTuple.TypeArguments;
                    var elementNames = namedTuple.TupleElements;
                    var tupleLiteral = UnwrapToTupleExpression(bodyExpression);
                    var usePositionalNames = true;
                    for (var i = 0; i < typeArguments.Length; i++)
                    {
                        var semanticName = elementNames != null && i < elementNames.Length
                            ? elementNames[i].Name
                            : StringHelpers.DefaultTupleElementName(i);
                        if (IsDefaultItemNElement(tupleLiteral, i, semanticName))
                        {
                            hasDefaultItemN = true;
                        }
                        else
                        {
                            usePositionalNames = false;
                        }
                    }

                    for (var i = 0; i < typeArguments.Length; i++)
                    {
                        string name;
                        if (!usePositionalNames
                            && elementNames != null
                            && i < elementNames.Length
                            && !string.IsNullOrEmpty(elementNames[i].Name))
                        {
                            name = elementNames[i].Name;
                        }
                        else
                        {
                            name = StringHelpers.DefaultTupleElementName(i);
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
                    hasDefaultItemNTupleElements: hasDefaultItemN
                        || (bodyExpression == null && HasOnlyDefaultItemNNames(memberBindings)),
                    tupleReturnLocations: CollectTupleReturnLocations(body, fallbackLocation));
            }

            var bindings = ImmutableArray.CreateBuilder<WagonBinding>();
            // Parity with WagonStationReturn.GetMemberNames: properties, then fields.
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
            }

            foreach (var member in returnType.GetMembers())
            {
                if (member is IFieldSymbol field
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
        internal static bool IsVoidReturn(ITypeSymbol returnType)
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
        internal static ITypeSymbol UnwrapReturnType(ITypeSymbol returnType)
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
        /// Determines whether the type is GreenPayload and extracts its payload type.
        /// </summary>
        internal static bool IsGreenPayload(ITypeSymbol typeSymbol, out ITypeSymbol payloadType)
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
        /// True when the element is compiler-default ItemN and was not written as <c>ItemN:</c> in source.
        /// Inferred names (e.g. <c>(paymentId, …)</c>) and explicit <c>Item1:</c> are not default.
        /// </summary>
        internal static bool IsDefaultItemNElement(
            TupleExpressionSyntax tupleLiteral,
            int index,
            string semanticName)
        {
            if (!StringHelpers.IsDefaultTupleElementName(semanticName, index))
            {
                return false;
            }

            if (tupleLiteral != null && index < tupleLiteral.Arguments.Count)
            {
                // Explicit NameColon (including Item1:) counts as intentional naming.
                return tupleLiteral.Arguments[index].NameColon == null;
            }

            return true;
        }

        /// <summary>
        /// Fallback when no tuple literal is available: all members are ItemN.
        /// </summary>
        internal static bool HasOnlyDefaultItemNNames(ImmutableArray<WagonBinding> members)
        {
            if (members.IsDefaultOrEmpty)
            {
                return false;
            }

            for (var i = 0; i < members.Length; i++)
            {
                if (!StringHelpers.IsDefaultTupleElementName(members[i].Name, i))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Collects source locations of value-tuple return expressions across all handler return paths.
        /// </summary>
        internal static ImmutableArray<Location> CollectTupleReturnLocations(
            CSharpSyntaxNode body,
            Location fallbackLocation)
        {
            var locations = ImmutableArray.CreateBuilder<Location>();
            foreach (var expression in HandlerReturnSchemaInference.CollectReturnPathExpressions(body))
            {
                CollectTupleExpressionLocations(expression, locations);
            }

            if (locations.Count == 0 && fallbackLocation != null)
            {
                locations.Add(fallbackLocation);
            }

            return locations.ToImmutable();
        }

        /// <summary>
        /// Walks an expression tree and records locations of value-tuple literals.
        /// </summary>
        internal static void CollectTupleExpressionLocations(
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
        /// Unwraps parenthesized expressions to reach an underlying tuple literal.
        /// </summary>
        internal static TupleExpressionSyntax UnwrapToTupleExpression(ExpressionSyntax expression)
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
        internal static bool IsValueTuple(ITypeSymbol typeSymbol)
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
