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
            var returnType = HandlerReturnTypeShape.UnwrapReturnType(handlerSymbol.ReturnType);
            if (HandlerReturnTypeShape.IsVoidReturn(returnType))
            {
                return ReturnShape.Void;
            }

            if (returnType == null || returnType.SpecialType == SpecialType.System_Object)
            {
                returnType = InferBodyReturnType(body, semanticModel);
            }
            else if (ReturnTypeDisplayHelper.IsSignalBaseReturn(returnType))
            {
                var fromBody = HandlerReturnTypeShape.UnwrapReturnType(InferBodyReturnType(body, semanticModel));
                if (fromBody != null)
                {
                    returnType = fromBody;
                }
            }

            returnType = HandlerReturnTypeShape.UnwrapReturnType(returnType);

            if (HandlerReturnTypeShape.IsVoidReturn(returnType))
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
            if (HandlerReturnTypeShape.IsGreenPayload(returnType, out var greenPayload))
            {
                return WithReturnType(
                    InferFromGreenPayload(greenPayload, inputWagons, semanticModel, fallbackLocation, body, bodyExpression),
                    returnTypeDisplay,
                    useGenericReturn);
            }

            var shape = HandlerReturnTypeShape.InferFromType(returnType, inputWagons, semanticModel, fallbackLocation, body, bodyExpression);
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
            var shape = HandlerReturnTypeShape.InferFromType(greenPayload, inputWagons, semanticModel, fallbackLocation, body, bodyExpression);
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
            return ReturnPathExpressionExpander.Expand(expression);
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
                if (HandlerReturnTypeShape.IsGreenPayload(invocationType, out var greenPayload))
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
            typeSymbol = HandlerReturnTypeShape.UnwrapReturnType(typeSymbol);
            if (typeSymbol == null)
            {
                return null;
            }

            if (HandlerReturnTypeShape.IsGreenPayload(typeSymbol, out var greenPayload))
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
    }
}
