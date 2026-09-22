using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Materializes a <see cref="FactoryCall"/> from a user-defined TrainRoute factory invocation.
    /// </summary>
    internal static class FactoryCallMaterializer
    {
        /// <summary>
        /// Attempts to materialize a factory call at <paramref name="expression"/>.
        /// </summary>
        public static bool TryMaterialize(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out FactoryCall factoryCall)
        {
            factoryCall = null;
            if (expression == null || semanticModel == null)
            {
                return false;
            }

            if (expression is not InvocationExpressionSyntax factoryInvocation)
            {
                return false;
            }

            if (StationSyntaxHelper.IsCandidateStationInvocation(factoryInvocation)
                || StationSyntaxHelper.IsCandidateServiceStationInvocation(factoryInvocation))
            {
                return false;
            }

            if (semanticModel.GetSymbolInfo(factoryInvocation).Symbol is not IMethodSymbol methodSymbol
                || !IsUserDefinedRouteFactory(methodSymbol))
            {
                return false;
            }

            FactoryCallKind kind;
            ImmutableArray<WagonBinding> initialWagons;

            if (StationSyntaxHelper.TryMatchOutTrainRouteArgument(
                    factoryInvocation,
                    methodSymbol,
                    semanticModel,
                    out var outParameter,
                    out _))
            {
                kind = ResolveKind(methodSymbol, semanticModel.Compilation);
                RouteFactoryResolver.TryResolveOut(
                    methodSymbol,
                    outParameter,
                    semanticModel.Compilation,
                    factoryInvocation.GetLocation(),
                    out initialWagons,
                    out _);
            }
            else if (StationSyntaxHelper.IsTrainRouteFactoryReturnType(methodSymbol.ReturnType))
            {
                kind = ResolveKind(methodSymbol, semanticModel.Compilation);
                RouteFactoryResolver.TryResolve(
                    methodSymbol,
                    semanticModel.Compilation,
                    factoryInvocation.GetLocation(),
                    out initialWagons,
                    out _);
            }
            else
            {
                return false;
            }

            factoryCall = new FactoryCall(
                factoryInvocation,
                factoryInvocation.GetLocation(),
                kind,
                methodSymbol,
                initialWagons,
                GetContainingMethod(factoryInvocation, semanticModel));
            return true;
        }

        /// <summary>
        /// Attempts to materialize when <paramref name="node"/> is a factory invocation expression.
        /// </summary>
        public static bool TryMaterialize(
            SyntaxNode node,
            SemanticModel semanticModel,
            out FactoryCall factoryCall)
        {
            factoryCall = null;
            return node is ExpressionSyntax expression
                && TryMaterialize(expression, semanticModel, out factoryCall);
        }

        private static FactoryCallKind ResolveKind(IMethodSymbol methodSymbol, Compilation compilation)
        {
            return FactoryAccessibilityHelper.RequiresSchemaLookup(methodSymbol, compilation)
                ? FactoryCallKind.Schema
                : FactoryCallKind.Inline;
        }

        /// <summary>
        /// Local functions that return <see cref="TrainOP.TrainRoute"/> are treated as ordinary factories.
        /// </summary>
        private static bool IsUserDefinedRouteFactory(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
            {
                return false;
            }

            if (methodSymbol.MethodKind != MethodKind.Ordinary
                && methodSymbol.MethodKind != MethodKind.LocalFunction
                && methodSymbol.MethodKind != MethodKind.ExplicitInterfaceImplementation)
            {
                return false;
            }

            var containingType = methodSymbol.ContainingType;
            if (containingType == null)
            {
                return true;
            }

            if (containingType.TypeKind == TypeKind.Delegate)
            {
                return false;
            }

            if (StationSyntaxHelper.IsTrainRoute(containingType))
            {
                return false;
            }

            return !string.Equals(containingType.Name, "TrainRouteStationExtensions", StringComparison.Ordinal);
        }

        private static IMethodSymbol GetContainingMethod(SyntaxNode node, SemanticModel semanticModel)
        {
            var methodDeclaration = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration == null)
            {
                return null;
            }

            return semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        }
    }
}
