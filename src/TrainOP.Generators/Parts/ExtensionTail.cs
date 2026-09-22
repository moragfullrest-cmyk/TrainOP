using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Continuation stations after a factory call (factory-extension tail).
    /// </summary>
    internal sealed class ExtensionTail : IRoutePart
    {
        /// <summary>
        /// Creates an extension tail of station links ending at <paramref name="endpoint"/>.
        /// </summary>
        public ExtensionTail(
            ExpressionSyntax endpoint,
            ImmutableArray<StationLink> stations,
            Location location = null)
        {
            Endpoint = endpoint;
            Stations = stations.IsDefault ? ImmutableArray<StationLink>.Empty : stations;
            Location = location
                ?? endpoint?.GetLocation()
                ?? (Stations.Length > 0 ? Stations[Stations.Length - 1].Location : Location.None);
        }

        /// <summary>
        /// Last expression in the extension fluent chain.
        /// </summary>
        public ExpressionSyntax Endpoint { get; }

        /// <summary>
        /// Stations appended after the factory call (order preserved).
        /// </summary>
        public ImmutableArray<StationLink> Stations { get; }

        /// <inheritdoc />
        public Location Location { get; }
    }
}
