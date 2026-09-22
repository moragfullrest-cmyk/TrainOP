using Microsoft.CodeAnalysis;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// First-class fragment of a route graph (seed, binding, station link, join arm, etc.).
    /// </summary>
    /// <remarks>
    /// Identity for dedupe / dispatch is owned by the concrete part — callers must not
    /// switch on legacy <c>RouteChainAnchorKind</c> outside part ports.
    /// </remarks>
    internal interface IRoutePart
    {
        /// <summary>
        /// Primary source location of this part (origin, invocation, or synthetic join site).
        /// </summary>
        Location Location { get; }
    }
}
