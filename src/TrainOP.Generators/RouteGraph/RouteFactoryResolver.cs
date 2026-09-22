using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Resolves factory terminal wagons via inline analysis or exported schema lookup.
    /// </summary>
    internal static class RouteFactoryResolver
    {
        /// <summary>
        /// Resolves terminal wagons for a factory method using schema or inline analysis.
        /// </summary>
        public static bool TryResolve(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            Location diagnosticLocation,
            out ImmutableArray<WagonBinding> terminalWagons,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            terminalWagons = ImmutableArray<WagonBinding>.Empty;
            diagnostics = ImmutableArray<Diagnostic>.Empty;

            if (factoryMethod == null || compilation == null)
            {
                return false;
            }

            if (StationSyntaxHelper.IsTrainRouteFactoryReturnType(factoryMethod.ReturnType))
            {
                if (FactoryAccessibilityHelper.RequiresSchemaLookup(factoryMethod, compilation))
                {
                    if (ExternalRouteSchemaResolver.TryResolve(factoryMethod, compilation, out terminalWagons))
                    {
                        return true;
                    }

                    if (IsExternalAssemblyFactory(factoryMethod, compilation))
                    {
                        diagnostics = ImmutableArray.Create(Diagnostic.Create(
                            TrainRouteDiagnostics.ExternalFactorySchemaMissing,
                            diagnosticLocation,
                            factoryMethod.ToDisplayString()));
                    }

                    return false;
                }

                return TryResolveInline(factoryMethod, compilation, diagnosticLocation, out terminalWagons, out diagnostics);
            }

            if (StationSyntaxHelper.TryGetSingleOutTrainRouteParameter(factoryMethod, out var outParameter))
            {
                return TryResolveOut(
                    factoryMethod,
                    outParameter,
                    compilation,
                    diagnosticLocation,
                    out terminalWagons,
                    out diagnostics);
            }

            return false;
        }

        /// <summary>
        /// Resolves terminal wagons by analyzing assignments to an <c>out TrainRoute</c> parameter.
        /// </summary>
        public static bool TryResolveOut(
            IMethodSymbol factoryMethod,
            IParameterSymbol outParameter,
            Compilation compilation,
            Location diagnosticLocation,
            out ImmutableArray<WagonBinding> terminalWagons,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            terminalWagons = ImmutableArray<WagonBinding>.Empty;
            diagnostics = ImmutableArray<Diagnostic>.Empty;

            if (factoryMethod == null
                || outParameter == null
                || compilation == null
                || FactoryAccessibilityHelper.RequiresSchemaLookup(factoryMethod, compilation)
                || !FactoryAccessibilityHelper.IsInlineAnalyzable(factoryMethod, compilation))
            {
                return false;
            }

            var validation = RouteFactoryPathValidator.ValidateOut(factoryMethod, outParameter, compilation);
            diagnostics = validation.Diagnostics;
            if (!validation.IsValid)
            {
                return false;
            }

            terminalWagons = validation.TerminalWagons;
            return true;
        }

        /// <summary>
        /// Resolves terminal wagons by analyzing the factory method body in the current compilation.
        /// </summary>
        public static bool TryResolveInline(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            Location diagnosticLocation,
            out ImmutableArray<WagonBinding> terminalWagons,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            terminalWagons = ImmutableArray<WagonBinding>.Empty;
            diagnostics = ImmutableArray<Diagnostic>.Empty;

            if (!FactoryAccessibilityHelper.IsInlineAnalyzable(factoryMethod, compilation))
            {
                return false;
            }

            var validation = RouteFactoryPathValidator.Validate(factoryMethod, compilation);
            diagnostics = validation.Diagnostics;
            if (!validation.IsValid)
            {
                return false;
            }

            terminalWagons = validation.TerminalWagons;
            return true;
        }

        private static bool IsExternalAssemblyFactory(IMethodSymbol factoryMethod, Compilation compilation)
        {
            return factoryMethod.ContainingAssembly != null
                && compilation.Assembly != null
                && !SymbolEqualityComparer.Default.Equals(
                    factoryMethod.ContainingAssembly,
                    compilation.Assembly);
        }
    }
}
