using Microsoft.CodeAnalysis;

namespace TrainOP.Generators
{
    /// <summary>
    /// Diagnostic descriptors reported by TrainOP source generators and analyzers.
    /// </summary>
    internal static class TrainRouteDiagnostics
    {
        /// <summary>
        /// Reported when a station requires a wagon that is not available from earlier stations in the chain.
        /// </summary>
        public static readonly DiagnosticDescriptor MissingWagon = new DiagnosticDescriptor(
            id: "TOP001",
            title: "Required wagon is not available in the route chain",
            messageFormat: "Station '{0}' requires wagon '{1}', which is not available from earlier stations in this route",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when the same wagon name carries incompatible types at different stations in the chain.
        /// </summary>
        public static readonly DiagnosticDescriptor WagonTypeConflict = new DiagnosticDescriptor(
            id: "TOP002",
            title: "Wagon type conflict in route chain",
            messageFormat: "Wagon '{0}' has conflicting types: '{1}' (produced at '{2}') vs '{3}' (required at '{4}')",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when a wagon removed at one station is required again by a later station.
        /// </summary>
        public static readonly DiagnosticDescriptor WagonRemovedButRequired = new DiagnosticDescriptor(
            id: "TOP003",
            title: "Removed wagon is required later in the route chain",
            messageFormat: "Wagon '{0}' was removed at station '{1}' but is required at station '{2}'",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Warns that returning CargoManifest replaces the entire manifest and may drop unstated wagons.
        /// </summary>
        public static readonly DiagnosticDescriptor CargoManifestReplacement = new DiagnosticDescriptor(
            id: "TOP004",
            title: "Station returns CargoManifest",
            messageFormat: "Station '{0}' returns CargoManifest, which replaces the entire manifest; wagons not in the return value may be lost",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when a data-oriented handler is not attached to a TrainRoute fluent chain.
        /// </summary>
        public static readonly DiagnosticDescriptor OrphanDataHandler = new DiagnosticDescriptor(
            id: "TOP006",
            title: "Data-oriented handler is outside a TrainRoute chain",
            messageFormat: "Data-oriented handler must be part of a TrainRoute chain starting with 'new TrainRoute()'",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Informs that a seed station produced a wagon never consumed by downstream stations.
        /// </summary>
        public static readonly DiagnosticDescriptor UnusedSeedWagon = new DiagnosticDescriptor(
            id: "TOP007",
            title: "Seed wagon is never consumed",
            messageFormat: "Wagon '{0}' produced at seed station '{1}' is never consumed by later stations",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when handlers sharing the same parameter type signature use different wagon names.
        /// </summary>
        public static readonly DiagnosticDescriptor ConflictingWagonNames = new DiagnosticDescriptor(
            id: "TOP008",
            title: "Conflicting wagon names for the same handler type signature",
            messageFormat: "Handler wagon names ({0}) do not match the canonical names ({1}) for this parameter type sequence. Use the same manifest keys everywhere handlers share the same parameter types.",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
