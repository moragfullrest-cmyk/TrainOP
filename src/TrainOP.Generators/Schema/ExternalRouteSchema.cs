using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Resolved route factory schema metadata from a referenced (or generated) assembly.
    /// </summary>
    internal sealed class ExternalRouteSchema
    {
        /// <summary>
        /// Creates a resolved external route schema.
        /// </summary>
        public ExternalRouteSchema(
            ImmutableArray<WagonBinding> terminalWagons,
            string callerChainKey,
            int stationCount)
        {
            TerminalWagons = terminalWagons.IsDefault
                ? ImmutableArray<WagonBinding>.Empty
                : terminalWagons;
            CallerChainKey = callerChainKey ?? string.Empty;
            StationCount = stationCount < 0 ? 0 : stationCount;
        }

        /// <summary>Terminal wagon slots exported by the factory schema.</summary>
        public ImmutableArray<WagonBinding> TerminalWagons { get; }

        /// <summary>
        /// Caller chain key for the factory's <c>new TrainRoute()</c> site.
        /// Empty when the schema predates dispatch metadata.
        /// </summary>
        public string CallerChainKey { get; }

        /// <summary>
        /// Number of Station/ServiceStation registrations inside the factory before return.
        /// </summary>
        public int StationCount { get; }

        /// <summary>
        /// True when schema carries dispatch identity required for factory-extension chain-dispatch.
        /// </summary>
        public bool HasDispatchIdentity => !string.IsNullOrEmpty(CallerChainKey);
    }
}
