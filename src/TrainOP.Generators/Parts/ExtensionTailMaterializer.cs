using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Route;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Materializes an <see cref="ExtensionTail"/> of stations after a <see cref="FactoryCall"/>.
    /// </summary>
    internal static class ExtensionTailMaterializer
    {
        /// <summary>
        /// Walks fluent stations after <paramref name="factoryCall"/> and builds an extension tail.
        /// When <paramref name="endpoint"/> is set, stops at that expression (EndingAt semantics).
        /// </summary>
        public static bool TryMaterialize(
            FactoryCall factoryCall,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationByKey,
            ExpressionSyntax endpoint,
            bool allowEmpty,
            out ExtensionTail tail)
        {
            tail = null;
            if (factoryCall?.Root == null || semanticModel == null)
            {
                return false;
            }

            var legacyStations = ImmutableArray.CreateBuilder<StationChainLink>();
            var current = (ExpressionSyntax)factoryCall.Root;
            ExpressionSyntax tailEndpoint = current;
            var target = endpoint == null
                ? null
                : ReceiverExpressionSyntaxPeel.UnwrapTransparent(endpoint);

            while (endpoint == null || !MatchesEndpoint(current, endpoint, target))
            {
                if (!RouteChainWalker.TryAdvanceChain(
                    current,
                    semanticModel,
                    legacyStations,
                    out current,
                    null,
                    stationByKey))
                {
                    if (endpoint != null)
                    {
                        return false;
                    }

                    break;
                }

                tailEndpoint = current;
            }

            if (legacyStations.Count == 0 && !allowEmpty)
            {
                return false;
            }

            var links = ImmutableArray.CreateBuilder<StationLink>(legacyStations.Count);
            foreach (var legacy in legacyStations)
            {
                var link = StationLink.FromStationChainLink(legacy);
                if (link != null)
                {
                    links.Add(link);
                }
            }

            if (links.Count == 0 && !allowEmpty)
            {
                return false;
            }

            if (endpoint != null)
            {
                tailEndpoint = endpoint;
            }

            tail = new ExtensionTail(tailEndpoint, links.ToImmutable());
            return true;
        }

        /// <summary>
        /// Walks all stations after the factory call (requires at least one).
        /// </summary>
        public static bool TryMaterialize(
            FactoryCall factoryCall,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationByKey,
            out ExtensionTail tail)
        {
            return TryMaterialize(
                factoryCall,
                semanticModel,
                stationByKey,
                endpoint: null,
                allowEmpty: false,
                out tail);
        }

        /// <summary>
        /// Overload without prebuilt station sites.
        /// </summary>
        public static bool TryMaterialize(
            FactoryCall factoryCall,
            SemanticModel semanticModel,
            out ExtensionTail tail)
        {
            return TryMaterialize(factoryCall, semanticModel, stationByKey: null, out tail);
        }

        private static bool MatchesEndpoint(
            ExpressionSyntax current,
            ExpressionSyntax endpoint,
            ExpressionSyntax unwrappedEndpoint)
        {
            if (ReferenceEquals(current, endpoint)
                || ReferenceEquals(current, unwrappedEndpoint))
            {
                return true;
            }

            var unwrappedCurrent = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
            if (ReferenceEquals(unwrappedCurrent, endpoint)
                || ReferenceEquals(unwrappedCurrent, unwrappedEndpoint))
            {
                return true;
            }

            var outermostCurrent = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(current);
            var outermostEndpoint = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(endpoint);
            return ReferenceEquals(outermostCurrent, outermostEndpoint);
        }
    }
}
