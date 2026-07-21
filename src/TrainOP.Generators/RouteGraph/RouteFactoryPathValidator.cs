using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Validates that all factory return paths produce equivalent terminal wagon sets.
    /// </summary>
    internal static class RouteFactoryPathValidator
    {
        /// <summary>
        /// Result of validating a factory method's return paths.
        /// </summary>
        internal sealed class ValidationResult
        {
            public ValidationResult(
                bool isValid,
                ImmutableArray<WagonBinding> terminalWagons,
                ImmutableArray<Diagnostic> diagnostics)
            {
                IsValid = isValid;
                TerminalWagons = terminalWagons;
                Diagnostics = diagnostics;
            }

            public bool IsValid { get; }

            public ImmutableArray<WagonBinding> TerminalWagons { get; }

            public ImmutableArray<Diagnostic> Diagnostics { get; }
        }

        /// <summary>
        /// Validates all return paths of a factory method.
        /// </summary>
        public static ValidationResult Validate(IMethodSymbol factoryMethod, Compilation compilation)
        {
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            var location = factoryMethod?.Locations.FirstOrDefault() ?? Location.None;
            var displayName = factoryMethod?.ToDisplayString() ?? "?";

            var paths = RouteFactoryPathSimulator.SimulateAllReturnPaths(factoryMethod, compilation);
            if (paths.IsDefaultOrEmpty)
            {
                diagnostics.Add(Diagnostic.Create(
                    TrainRouteDiagnostics.FactoryReturnPathUnknown,
                    location,
                    displayName));
                return new ValidationResult(false, ImmutableArray<WagonBinding>.Empty, diagnostics.ToImmutable());
            }

            foreach (var path in paths)
            {
                if (path.HasUnknownReturn)
                {
                    diagnostics.Add(Diagnostic.Create(
                        TrainRouteDiagnostics.FactoryReturnPathUnknown,
                        path.Location ?? location,
                        displayName));
                }
            }

            if (diagnostics.Count > 0)
            {
                return new ValidationResult(false, ImmutableArray<WagonBinding>.Empty, diagnostics.ToImmutable());
            }

            var reference = TerminalWagonsComparer.Normalize(paths[0].TerminalWagons);
            for (var i = 1; i < paths.Length; i++)
            {
                var current = TerminalWagonsComparer.Normalize(paths[i].TerminalWagons);
                if (TerminalWagonsComparer.AreEquivalent(reference, current))
                {
                    continue;
                }

                diagnostics.Add(Diagnostic.Create(
                    TrainRouteDiagnostics.FactoryReturnPathsDiverge,
                    paths[i].Location ?? location,
                    displayName,
                    TerminalWagonsComparer.DescribeDifference(reference, current)));
            }

            if (diagnostics.Count > 0)
            {
                return new ValidationResult(false, ImmutableArray<WagonBinding>.Empty, diagnostics.ToImmutable());
            }

            return new ValidationResult(true, reference, ImmutableArray<Diagnostic>.Empty);
        }
    }
}
