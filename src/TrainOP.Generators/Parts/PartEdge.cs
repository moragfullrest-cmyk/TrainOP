namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Directed edge between two route parts produced by <c>ChainConstructor.Connect</c>.
    /// </summary>
    internal readonly struct PartEdge
    {
        /// <summary>
        /// Creates an edge from <paramref name="from"/> to <paramref name="to"/> of the given kind.
        /// </summary>
        public PartEdge(IRoutePart from, IRoutePart to, PartEdgeKind kind)
        {
            From = from;
            To = to;
            Kind = kind;
        }

        /// <summary>
        /// Upstream part (seed, local, factory, or arm).
        /// </summary>
        public IRoutePart From { get; }

        /// <summary>
        /// Downstream part (local, station link, join seed, or extension tail).
        /// </summary>
        public IRoutePart To { get; }

        /// <summary>
        /// Structural role of this connection.
        /// </summary>
        public PartEdgeKind Kind { get; }
    }
}
