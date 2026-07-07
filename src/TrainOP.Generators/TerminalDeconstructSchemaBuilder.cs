using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds terminal deconstruct schemas from source code order, mirroring <see cref="HandlerInputSchemaBuilder"/>.
    /// </summary>
    internal static class TerminalDeconstructSchemaBuilder
    {
        /// <summary>
        /// Discovers deconstruct schemas from typed RouteReport deconstruction assignments in a syntax tree.
        /// </summary>
        public static IEnumerable<TerminalWagonSchema> DiscoverFromDeconstructionSites(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel)
        {
            foreach (var assignment in syntaxTree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is not DeclarationExpressionSyntax declaration)
                {
                    continue;
                }

                if (declaration.Type is not TupleTypeSyntax tupleType)
                {
                    continue;
                }

                if (!IsTerminalTravelExpression(assignment.Right, semanticModel))
                {
                    continue;
                }

                var wagons = BuildWagonSlotsFromTupleType(tupleType, semanticModel, declaration.GetLocation());
                if (wagons.IsDefaultOrEmpty)
                {
                    continue;
                }

                yield return new TerminalWagonSchema(wagons);
            }
        }

        /// <summary>
        /// Builds a terminal schema by applying handler input order from code, then remaining simulation wagons.
        /// </summary>
        public static TerminalWagonSchema BuildFromTerminalSimulation(
            RouteChain chain,
            ImmutableArray<WagonBinding> terminalWagons)
        {
            if (terminalWagons.IsDefaultOrEmpty)
            {
                return new TerminalWagonSchema(ImmutableArray<WagonBinding>.Empty);
            }

            var remaining = new Dictionary<string, WagonBinding>(StringComparer.Ordinal);
            foreach (var wagon in terminalWagons)
            {
                remaining[wagon.Name] = wagon;
            }

            var ordered = ImmutableArray.CreateBuilder<WagonBinding>();

            if (chain.Stations.Length > 0)
            {
                var lastStation = chain.Stations[chain.Stations.Length - 1];
                foreach (var input in lastStation.Handler.InputWagons)
                {
                    if (!remaining.TryGetValue(input.Name, out var terminalWagon))
                    {
                        continue;
                    }

                    remaining.Remove(input.Name);
                    ordered.Add(MergeHandlerSlot(input, terminalWagon));
                }
            }

            foreach (var wagon in terminalWagons)
            {
                if (!remaining.TryGetValue(wagon.Name, out var terminalWagon))
                {
                    continue;
                }

                remaining.Remove(wagon.Name);
                ordered.Add(NormalizeSimulationWagon(terminalWagon));
            }

            return new TerminalWagonSchema(ordered.ToImmutable());
        }

        /// <summary>
        /// Builds wagon bindings from a tuple type in source declaration order.
        /// </summary>
        private static ImmutableArray<WagonBinding> BuildWagonSlotsFromTupleType(
            TupleTypeSyntax tupleType,
            SemanticModel semanticModel,
            Location fallbackLocation)
        {
            var wagons = ImmutableArray.CreateBuilder<WagonBinding>();
            foreach (var element in tupleType.Elements)
            {
                var slot = TryBuildWagonSlot(element, semanticModel, fallbackLocation);
                if (slot == null)
                {
                    return ImmutableArray<WagonBinding>.Empty;
                }

                wagons.Add(slot);
            }

            return wagons.ToImmutable();
        }

        /// <summary>
        /// Builds one wagon slot from a tuple element, using the same rules as handler input parameters.
        /// </summary>
        private static WagonBinding TryBuildWagonSlot(
            TupleElementSyntax element,
            SemanticModel semanticModel,
            Location fallbackLocation)
        {
            var name = element.Identifier.ValueText;
            if (!IsValidWagonName(name))
            {
                return null;
            }

            var typeSyntax = element.Type;
            if (typeSyntax == null)
            {
                return null;
            }

            var typeSymbol = semanticModel.GetTypeInfo(typeSyntax).Type;
            if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Error)
            {
                return null;
            }

            if (!ManifestWagonTypes.IsSupported(typeSymbol))
            {
                return null;
            }

            var isOptional = WagonParameterAnalyzer.IsOptionalNullableValueType(typeSymbol, out var underlyingType);
            var effectiveTypeSymbol = WagonParameterAnalyzer.GetEffectiveTypeSymbol(typeSymbol, underlyingType, isOptional);
            var typeDisplay = ManifestWagonTypes.ToWagonParameterTypeDisplay(typeSymbol, underlyingType, isOptional);
            var pullTypeDisplay = WagonParameterAnalyzer.GetPullTypeDisplay(typeSymbol, underlyingType, isOptional);
            var location = element.Identifier.IsKind(SyntaxKind.None)
                ? fallbackLocation
                : element.Identifier.GetLocation();

            return new WagonBinding(
                name,
                typeDisplay,
                effectiveTypeSymbol,
                location,
                isByReference: false,
                isOptional,
                pullTypeDisplay);
        }

        /// <summary>
        /// Prefers handler input type metadata and keeps the manifest wagon name from simulation.
        /// </summary>
        private static WagonBinding MergeHandlerSlot(WagonBinding handlerInput, WagonBinding terminalWagon)
        {
            return new WagonBinding(
                terminalWagon.Name,
                handlerInput.TypeDisplay,
                handlerInput.TypeSymbol ?? terminalWagon.TypeSymbol,
                handlerInput.Location ?? terminalWagon.Location,
                handlerInput.IsByReference,
                handlerInput.IsOptional,
                handlerInput.PullTypeDisplay);
        }

        /// <summary>
        /// Normalizes simulation wagon bindings to handler-style type displays.
        /// </summary>
        private static WagonBinding NormalizeSimulationWagon(WagonBinding wagon)
        {
            var isOptional = WagonParameterAnalyzer.IsOptionalNullableValueType(wagon.TypeSymbol, out var underlyingType);
            var effectiveTypeSymbol = WagonParameterAnalyzer.GetEffectiveTypeSymbol(wagon.TypeSymbol, underlyingType, isOptional);
            var typeDisplay = ManifestWagonTypes.ToWagonParameterTypeDisplay(wagon.TypeSymbol, underlyingType, isOptional);
            var pullTypeDisplay = WagonParameterAnalyzer.GetPullTypeDisplay(wagon.TypeSymbol, underlyingType, isOptional);

            return new WagonBinding(
                wagon.Name,
                typeDisplay,
                effectiveTypeSymbol,
                wagon.Location,
                wagon.IsByReference,
                isOptional,
                pullTypeDisplay);
        }

        /// <summary>
        /// Determines whether an expression is a terminal travel result suitable for deconstruction.
        /// </summary>
        private static bool IsTerminalTravelExpression(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            var unwrapped = UnwrapExpression(expression);
            if (unwrapped == null)
            {
                return false;
            }

            if (unwrapped is InvocationExpressionSyntax invocation
                && IsTravelInvocation(invocation, semanticModel))
            {
                return true;
            }

            var typeSymbol = semanticModel.GetTypeInfo(unwrapped).Type;
            return IsRouteReport(typeSymbol);
        }

        /// <summary>
        /// Determines whether an invocation is Travel or TravelAsync.
        /// </summary>
        private static bool IsTravelInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            {
                return false;
            }

            return string.Equals(method.Name, "Travel", StringComparison.Ordinal)
                || string.Equals(method.Name, "TravelAsync", StringComparison.Ordinal);
        }

        /// <summary>
        /// Unwraps parentheses and await to reach the underlying expression.
        /// </summary>
        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (expression != null)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        break;
                    case AwaitExpressionSyntax awaitExpression:
                        expression = awaitExpression.Expression;
                        break;
                    default:
                        return expression;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether a type symbol is RouteReport.
        /// </summary>
        private static bool IsRouteReport(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol?.ToDisplayString(), "TrainOP.RouteReport", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether a name is a valid wagon identifier.
        /// </summary>
        private static bool IsValidWagonName(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                return false;
            }

            if (string.Equals(wagonName, "_", StringComparison.Ordinal))
            {
                return false;
            }

            if (!SyntaxFacts.IsValidIdentifier(wagonName))
            {
                return false;
            }

            return !SyntaxFacts.IsKeywordKind(SyntaxFacts.GetKeywordKind(wagonName));
        }
    }
}
