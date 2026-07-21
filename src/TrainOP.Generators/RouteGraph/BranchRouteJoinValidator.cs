using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Validates whether forking TrainRoute branches can be joined before a downstream Station.
    /// </summary>
    internal static class BranchRouteJoinValidator
    {
        /// <summary>
        /// Validates branch resolution, unknown terminals, and type compatibility across a join set.
        /// </summary>
        public static BranchRouteJoinValidation Validate(
            BranchRouteJoinSet joinSet,
            SemanticModel semanticModel = null)
        {
            if (joinSet == null)
            {
                return Fail(
                    location: Location.None,
                    stationName: "?",
                    reason: "no branches to join");
            }

            var location = GetDiagnosticLocation(joinSet);
            var stationName = GetDownstreamStationName(joinSet.DownstreamStation, semanticModel);

            if (joinSet.Branches.IsDefaultOrEmpty)
            {
                return Fail(location, stationName, "no branches to join");
            }

            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            if (joinSet.Branches.Any(b => !b.IsResolved))
            {
                diagnostics.Add(CreateDiagnostic(
                    location,
                    stationName,
                    "one or more branches are not resolvable TrainRoute chains"));
            }

            if (joinSet.Branches.Any(b =>
                    b.IsResolved
                    && b.Simulation != null
                    && b.Simulation.HasUnknownReturn))
            {
                diagnostics.Add(CreateDiagnostic(
                    location,
                    stationName,
                    "branch has unknown terminal wagon state"));
            }

            var resolvedKnown = joinSet.Branches
                .Where(b => b.IsResolved
                    && b.Simulation != null
                    && !b.Simulation.HasUnknownReturn)
                .ToImmutableArray();

            foreach (var conflict in FindTypeConflicts(resolvedKnown))
            {
                diagnostics.Add(CreateDiagnostic(
                    location,
                    stationName,
                    $"wagon '{conflict.Name}' has conflicting types across branches ('{conflict.LeftDisplay}' vs '{conflict.RightDisplay}')"));
            }

            if (diagnostics.Count > 0)
            {
                return new BranchRouteJoinValidation(
                    canMerge: false,
                    diagnostics: diagnostics.ToImmutable(),
                    mergedTerminalWagons: ImmutableArray<WagonBinding>.Empty);
            }

            var merged = BuildIntersection(resolvedKnown);
            return new BranchRouteJoinValidation(
                canMerge: true,
                diagnostics: ImmutableArray<Diagnostic>.Empty,
                mergedTerminalWagons: merged);
        }

        /// <summary>
        /// Builds the intersection of terminal wagon names across branches, preserving first-branch order.
        /// </summary>
        private static ImmutableArray<WagonBinding> BuildIntersection(
            ImmutableArray<BranchRouteGraph> resolvedKnown)
        {
            if (resolvedKnown.Length == 0)
            {
                return ImmutableArray<WagonBinding>.Empty;
            }

            var firstTerminals = resolvedKnown[0].Simulation.TerminalWagons;
            if (firstTerminals.IsDefaultOrEmpty)
            {
                return ImmutableArray<WagonBinding>.Empty;
            }

            var nameSets = resolvedKnown
                .Select(b => new HashSet<string>(
                    b.Simulation.TerminalWagons.Select(w => w.Name),
                    StringComparer.Ordinal))
                .ToArray();

            var builder = ImmutableArray.CreateBuilder<WagonBinding>();
            foreach (var wagon in firstTerminals)
            {
                if (nameSets.All(set => set.Contains(wagon.Name)))
                {
                    builder.Add(wagon);
                }
            }

            return builder.ToImmutable();
        }

        /// <summary>
        /// Finds wagon names that appear in two or more branches with incompatible types.
        /// </summary>
        private static ImmutableArray<TypeConflict> FindTypeConflicts(
            ImmutableArray<BranchRouteGraph> resolvedKnown)
        {
            if (resolvedKnown.Length < 2)
            {
                return ImmutableArray<TypeConflict>.Empty;
            }

            var byName = new Dictionary<string, List<WagonBinding>>(StringComparer.Ordinal);
            foreach (var branch in resolvedKnown)
            {
                foreach (var wagon in branch.Simulation.TerminalWagons)
                {
                    if (!byName.TryGetValue(wagon.Name, out var list))
                    {
                        list = new List<WagonBinding>();
                        byName[wagon.Name] = list;
                    }

                    list.Add(wagon);
                }
            }

            var conflicts = ImmutableArray.CreateBuilder<TypeConflict>();
            foreach (var pair in byName)
            {
                if (pair.Value.Count < 2)
                {
                    continue;
                }

                var representative = pair.Value[0];
                for (var i = 1; i < pair.Value.Count; i++)
                {
                    var other = pair.Value[i];
                    if (!ChainGraphSimulator.TypesCompatible(representative.TypeSymbol, other.TypeSymbol))
                    {
                        conflicts.Add(new TypeConflict(
                            pair.Key,
                            representative.TypeDisplay,
                            other.TypeDisplay));
                        break;
                    }
                }
            }

            return conflicts.ToImmutable();
        }

        private static BranchRouteJoinValidation Fail(
            Location location,
            string stationName,
            string reason)
        {
            return new BranchRouteJoinValidation(
                canMerge: false,
                diagnostics: ImmutableArray.Create(CreateDiagnostic(location, stationName, reason)),
                mergedTerminalWagons: ImmutableArray<WagonBinding>.Empty);
        }

        private static Diagnostic CreateDiagnostic(Location location, string stationName, string reason)
        {
            return Diagnostic.Create(
                TrainRouteDiagnostics.RouteBranchJoinFailed,
                location,
                stationName,
                reason);
        }

        private static Location GetDiagnosticLocation(BranchRouteJoinSet joinSet)
        {
            if (joinSet.DownstreamStation != null
                && joinSet.DownstreamStation.ArgumentList.Arguments.Count > 0)
            {
                return joinSet.DownstreamStation.ArgumentList.Arguments[0].GetLocation();
            }

            return joinSet.JoinReceiver?.GetLocation() ?? Location.None;
        }

        /// <summary>
        /// Reads the compile-time station name from argument 0 when possible, or returns <c>?</c>.
        /// </summary>
        internal static string GetDownstreamStationName(
            InvocationExpressionSyntax downstreamStation,
            SemanticModel semanticModel = null)
        {
            if (downstreamStation == null
                || downstreamStation.ArgumentList.Arguments.Count == 0)
            {
                return "?";
            }

            var expression = downstreamStation.ArgumentList.Arguments[0].Expression;
            if (semanticModel != null
                && StationSyntaxHelper.TryResolveStationName(expression, semanticModel, out var resolved))
            {
                return resolved;
            }

            if (expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            return "?";
        }

        private readonly struct TypeConflict
        {
            public TypeConflict(string name, string leftDisplay, string rightDisplay)
            {
                Name = name;
                LeftDisplay = leftDisplay;
                RightDisplay = rightDisplay;
            }

            public string Name { get; }

            public string LeftDisplay { get; }

            public string RightDisplay { get; }
        }
    }
}
