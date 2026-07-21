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
    /// Infers the wagon return shape of a data-oriented station handler.
    /// Member discovery for object returns: public instance properties, then public instance fields
    /// (parity with runtime <c>WagonStationReturn.GetMemberNames</c>).
    /// </summary>
    internal static class HandlerReturnSchemaInference
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
            else if (ReturnTypeDisplayHelper.IsSignalBaseReturn(returnType))
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

            if (ReturnTypeDisplayHelper.IsSignalBaseReturn(returnType))
            {
                return new ReturnShape(
                    ImmutableArray<WagonBinding>.Empty,
                    isCargoManifest: false,
                    isValueTuple: false,
                    isUnknown: true,
                    returnTypeDisplay: ReturnTypeDisplayHelper.SignalReturnTypeDisplay,
                    useGenericReturn: false,
                    isExplicitSignalReturn: true);
            }

            var returnTypeDisplay = ReturnTypeDisplayHelper.BuildDisplay(returnType);
            var useGenericReturn = ReturnTypeDisplayHelper.UseGenericReturn(returnType);
            var fallbackLocation = handlerLocation
                ?? (handlerSymbol.Locations.Length > 0 ? handlerSymbol.Locations[0] : null);

            if (returnType == null)
            {
                return WithReturnType(ReturnShape.Unknown, returnTypeDisplay, useGenericReturn);
            }

            if (ReturnTypeDisplayHelper.IsRuntimeSignalReturn(returnType))
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

            if (ReturnTypeDisplayHelper.IsExplicitSignalReturn(returnType))
            {
                return new ReturnShape(
                    ImmutableArray<WagonBinding>.Empty,
                    isCargoManifest: false,
                    isValueTuple: false,
                    isUnknown: true,
                    returnTypeDisplay: ReturnTypeDisplayHelper.SignalReturnTypeDisplay,
                    useGenericReturn: false,
                    isExplicitSignalReturn: true);
            }

            if (FrameworkParameterSchemaClassifier.IsCargoManifest(returnType))
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
                    InferFromGreenPayload(greenPayload, inputWagons, semanticModel, fallbackLocation, body, bodyExpression),
                    returnTypeDisplay,
                    useGenericReturn);
            }

            var shape = InferFromType(returnType, inputWagons, semanticModel, fallbackLocation, body, bodyExpression);
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
                shape.HasDefaultItemNTupleElements,
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
            CSharpSyntaxNode body,
            ExpressionSyntax bodyExpression)
        {
            var shape = InferFromType(greenPayload, inputWagons, semanticModel, fallbackLocation, body, bodyExpression);
            if (!shape.IsCargoManifest && shape.Members.IsDefaultOrEmpty)
            {
                return ReturnShape.Unknown;
            }

            return shape;
        }

        /// <summary>
        /// Picks a representative return expression for tuple naming (prefers a branch with a tuple literal).
        /// </summary>
        private static ExpressionSyntax GetBodyExpression(CSharpSyntaxNode body)
        {
            ExpressionSyntax representative = null;
            foreach (var expression in CollectReturnPathExpressions(body))
            {
                representative ??= expression;
                if (UnwrapToTupleExpression(expression) != null)
                {
                    return expression;
                }
            }

            return representative;
        }

        /// <summary>
        /// Infers the return type from a handler body by unifying all statically discoverable return paths.
        /// </summary>
        private static ITypeSymbol InferBodyReturnType(CSharpSyntaxNode body, SemanticModel semanticModel)
        {
            if (body == null)
            {
                return null;
            }

            var inferredTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();
            foreach (var expression in CollectReturnPathExpressions(body))
            {
                inferredTypes.Add(InferExpressionReturnType(expression, semanticModel));
            }

            return UnifyInferredReturnTypes(inferredTypes.ToImmutable());
        }

        /// <summary>
        /// Collects leaf return expressions from a handler body, expanding conditionals and coalesce forks.
        /// </summary>
        private static IEnumerable<ExpressionSyntax> CollectReturnPathExpressions(CSharpSyntaxNode body)
        {
            if (body == null)
            {
                yield break;
            }

            if (body is ExpressionSyntax expression)
            {
                foreach (var expanded in ExpandReturnPathExpressions(expression))
                {
                    yield return expanded;
                }

                yield break;
            }

            if (body is ArrowExpressionClauseSyntax arrow)
            {
                foreach (var expanded in ExpandReturnPathExpressions(arrow.Expression))
                {
                    yield return expanded;
                }

                yield break;
            }

            if (body is BlockSyntax block)
            {
                foreach (var node in block.DescendantNodes())
                {
                    if (node is ReturnStatementSyntax returnStatement
                        && returnStatement.Expression != null)
                    {
                        foreach (var expanded in ExpandReturnPathExpressions(returnStatement.Expression))
                        {
                            yield return expanded;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Expands conditional, coalesce, and switch expressions into per-branch return expressions.
        /// </summary>
        private static IEnumerable<ExpressionSyntax> ExpandReturnPathExpressions(ExpressionSyntax expression)
        {
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                yield break;
            }

            if (expression is ConditionalExpressionSyntax conditional)
            {
                foreach (var expanded in ExpandReturnPathExpressions(conditional.WhenTrue))
                {
                    yield return expanded;
                }

                foreach (var expanded in ExpandReturnPathExpressions(conditional.WhenFalse))
                {
                    yield return expanded;
                }

                yield break;
            }

            if (expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression))
            {
                foreach (var expanded in ExpandReturnPathExpressions(binary.Left))
                {
                    yield return expanded;
                }

                foreach (var expanded in ExpandReturnPathExpressions(binary.Right))
                {
                    yield return expanded;
                }

                yield break;
            }

            if (expression is SwitchExpressionSyntax switchExpression)
            {
                foreach (var arm in switchExpression.Arms)
                {
                    foreach (var expanded in ExpandReturnPathExpressions(arm.Expression))
                    {
                        yield return expanded;
                    }
                }

                yield break;
            }

            yield return expression;
        }

        /// <summary>
        /// Infers the return type from a single expression, including conditional branches.
        /// </summary>
        private static ITypeSymbol InferExpressionReturnType(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                return null;
            }

            if (expression is ConditionalExpressionSyntax conditional)
            {
                return UnifyInferredReturnTypes(ImmutableArray.Create(
                    InferExpressionReturnType(conditional.WhenTrue, semanticModel),
                    InferExpressionReturnType(conditional.WhenFalse, semanticModel)));
            }

            if (expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression))
            {
                return UnifyInferredReturnTypes(ImmutableArray.Create(
                    InferExpressionReturnType(binary.Left, semanticModel),
                    InferExpressionReturnType(binary.Right, semanticModel)));
            }

            if (expression is SwitchExpressionSyntax switchExpression)
            {
                var inferredTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();
                foreach (var arm in switchExpression.Arms)
                {
                    inferredTypes.Add(InferExpressionReturnType(arm.Expression, semanticModel));
                }

                return UnifyInferredReturnTypes(inferredTypes.ToImmutable());
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
        /// Unifies inferred types from multiple return paths, preferring data payloads over signal-only returns.
        /// </summary>
        private static ITypeSymbol UnifyInferredReturnTypes(ImmutableArray<ITypeSymbol> inferredTypes)
        {
            ITypeSymbol unified = null;
            foreach (var inferredType in inferredTypes)
            {
                var candidate = ExtractDataReturnType(inferredType);
                if (!IsUsefulInferredType(candidate))
                {
                    continue;
                }

                if (unified == null)
                {
                    unified = candidate;
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(unified, candidate))
                {
                    continue;
                }

                return null;
            }

            return unified;
        }

        /// <summary>
        /// Extracts a data payload type from an inferred return type, skipping signal-only branches.
        /// </summary>
        private static ITypeSymbol ExtractDataReturnType(ITypeSymbol typeSymbol)
        {
            typeSymbol = UnwrapReturnType(typeSymbol);
            if (typeSymbol == null)
            {
                return null;
            }

            if (IsGreenPayload(typeSymbol, out var greenPayload))
            {
                return greenPayload;
            }

            if (ReturnTypeDisplayHelper.IsExplicitSignalReturn(typeSymbol)
                || ReturnTypeDisplayHelper.IsRuntimeSignalReturn(typeSymbol)
                || ReturnTypeDisplayHelper.IsSignalBaseReturn(typeSymbol))
            {
                return null;
            }

            return typeSymbol;
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
        /// True when the element is compiler-default ItemN and was not written as <c>ItemN:</c> in source.
        /// Inferred names (e.g. <c>(paymentId, …)</c>) and explicit <c>Item1:</c> are not default.
        /// </summary>
        private static bool IsDefaultItemNElement(
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
        private static bool HasOnlyDefaultItemNNames(ImmutableArray<WagonBinding> members)
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
        private static ImmutableArray<Location> CollectTupleReturnLocations(
            CSharpSyntaxNode body,
            Location fallbackLocation)
        {
            var locations = ImmutableArray.CreateBuilder<Location>();
            foreach (var expression in CollectReturnPathExpressions(body))
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
