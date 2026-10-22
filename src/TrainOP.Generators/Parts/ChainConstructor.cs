using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Assembles route parts into a chain by connecting edges and validating each join.
    /// </summary>
    /// <remarks>
    /// Primary BuildChains construct step: Bind / Append / Join / Extend via
    /// <see cref="LinearChainConnector"/>, <see cref="JoinChainConnector"/>,
    /// <see cref="ExtensionChainConnector"/>.
    /// </remarks>
    internal sealed class ChainConstructor
    {
        private readonly List<IRoutePart> _parts = new List<IRoutePart>();
        private readonly List<PartEdge> _edges = new List<PartEdge>();

        /// <summary>
        /// Parts accepted so far (order of first appearance).
        /// </summary>
        public ImmutableArray<IRoutePart> Parts => _parts.ToImmutableArray();

        /// <summary>
        /// Validated edges in connection order.
        /// </summary>
        public ImmutableArray<PartEdge> Edges => _edges.ToImmutableArray();

        /// <summary>
        /// Registers an origin part without an edge (fluent head before first Append).
        /// </summary>
        public void Add(IRoutePart part)
        {
            Track(part);
        }

        /// <summary>
        /// Binds an origin seed to a local binding window.
        /// </summary>
        public bool TryBind(
            IRoutePart seed,
            IRoutePart local,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            return TryConnect(seed, local, PartEdgeKind.Bind, out diagnostics);
        }

        /// <summary>
        /// Appends a station (or service station) link after an upstream part.
        /// </summary>
        public bool TryAppend(
            IRoutePart upstream,
            IRoutePart stationLink,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            return TryConnect(upstream, stationLink, PartEdgeKind.Append, out diagnostics);
        }

        /// <summary>
        /// Joins a fork arm into a join seed.
        /// </summary>
        public bool TryJoin(
            IRoutePart arm,
            IRoutePart joinSeed,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            return TryConnect(arm, joinSeed, PartEdgeKind.Join, out diagnostics);
        }

        /// <summary>
        /// Extends a factory call with a continuation tail.
        /// </summary>
        public bool TryExtend(
            IRoutePart factoryCall,
            IRoutePart extensionTail,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            return TryConnect(factoryCall, extensionTail, PartEdgeKind.Extend, out diagnostics);
        }

        private bool TryConnect(
            IRoutePart from,
            IRoutePart to,
            PartEdgeKind kind,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            var edge = new PartEdge(from, to, kind);
            if (!TryValidate(edge, out diagnostics))
            {
                return false;
            }

            Track(from);
            Track(to);
            _edges.Add(edge);
            return true;
        }

        private void Track(IRoutePart part)
        {
            if (!_parts.Contains(part))
            {
                _parts.Add(part);
            }
        }

        /// <summary>
        /// Validates that <paramref name="edge"/> may be connected.
        /// </summary>
        /// <returns><see langword="true"/> when the edge is accepted.</returns>
        private static bool TryValidate(
            PartEdge edge,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            diagnostics = ImmutableArray<Diagnostic>.Empty;

            return edge.Kind switch
            {
                PartEdgeKind.Bind => IsOriginSeed(edge.From) && edge.To is LocalBinding,
                PartEdgeKind.Append => IsAppendUpstream(edge.From) && edge.To is StationLink,
                // J2 fills port rules; accept structurally for now.
                PartEdgeKind.Join => edge.From is JoinArm && edge.To is JoinSeed,
                // E1 fills port rules; accept structurally for now.
                PartEdgeKind.Extend => edge.From is FactoryCall && edge.To is ExtensionTail,
                _ => false
            };
        }

        private static bool IsOriginSeed(IRoutePart part) =>
            part is CreationSeed or FactoryCall;

        private static bool IsAppendUpstream(IRoutePart part) =>
            part is CreationSeed or FactoryCall or LocalBinding or StationLink or JoinSeed;
    }
}
