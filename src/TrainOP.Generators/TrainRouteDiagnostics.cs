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
            id: "TOP005",
            title: "Data-oriented handler is outside a TrainRoute chain",
            messageFormat: "Data-oriented handler must be part of a TrainRoute chain (direct fluent chain, local assigned from 'new TrainRoute()', or factory extension with resolvable upstream schema)",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when a public factory in a referenced assembly has no exported route schema.
        /// </summary>
        public static readonly DiagnosticDescriptor ExternalFactorySchemaMissing = new DiagnosticDescriptor(
            id: "TOP011",
            title: "External route factory has no exported schema",
            messageFormat: "Route factory '{0}' has no exported terminal schema; the join with downstream stations is not validated",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when factory return paths produce different terminal wagon sets.
        /// </summary>
        public static readonly DiagnosticDescriptor FactoryReturnPathsDiverge = new DiagnosticDescriptor(
            id: "TOP012",
            title: "Factory return paths have divergent terminal manifest state",
            messageFormat: "Route factory '{0}' has divergent terminal manifest across return paths: {1}",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when a factory return path has unknown terminal wagon state.
        /// </summary>
        public static readonly DiagnosticDescriptor FactoryReturnPathUnknown = new DiagnosticDescriptor(
            id: "TOP013",
            title: "Factory return path has unknown terminal state",
            messageFormat: "Route factory '{0}' has a return path with unknown terminal wagon state; schema export and validation are not available",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Warns that a value tuple return has no named elements and maps wagons by positional ItemN keys.
        /// </summary>
        public static readonly DiagnosticDescriptor UnnamedTupleReturn = new DiagnosticDescriptor(
            id: "TOP006",
            title: "Unnamed value tuple",
            messageFormat: "Value tuple has no named elements; manifest wagons are bound to Item1, Item2, ... by position. This makes mapping order-dependent and can break silently if you reorder tuple elements. Name each element, e.g. (paymentId: id, amount: amt).",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// Warns that a value tuple return mixes named and unnamed elements with ambiguous wagon mapping.
        /// </summary>
        public static readonly DiagnosticDescriptor MixedTupleReturn = new DiagnosticDescriptor(
            id: "TOP014",
            title: "Mixed value tuple",
            messageFormat: "Value tuple mixes named and unnamed elements; some manifest wagons use positional keys (Item1, Item2, ...). This creates ambiguous/fragile wagon mapping (especially across merges or reordering). Name every element, e.g. (paymentId: id, amount: amt).",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when handlers sharing the same parameter type signature use different wagon names.
        /// </summary>
        public static readonly DiagnosticDescriptor ConflictingWagonNames = new DiagnosticDescriptor(
            id: "TOP007",
            title: "Conflicting wagon names for the same handler type signature",
            messageFormat: "Handler wagon names ({0}) do not match the canonical names ({1}) for this parameter type sequence. Use the same manifest keys everywhere handlers share the same parameter types.",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when forking TrainRoute branches cannot be joined before a downstream Station.
        /// </summary>
        public static readonly DiagnosticDescriptor RouteBranchJoinFailed = new DiagnosticDescriptor(
            id: "TOP008",
            title: "Route branch join failed",
            messageFormat: "Cannot join route branches before station '{0}': {1}",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when a data-oriented handler returns a runtime route signal instead of data or RailwaySignals DSL.
        /// </summary>
        public static readonly DiagnosticDescriptor RuntimeSignalReturn = new DiagnosticDescriptor(
            id: "TOP010",
            title: "Station returns runtime route signal",
            messageFormat: "Station '{0}' returns '{1}'; use data returns or RailwaySignals.Green / Red / Pass instead of GreenSignal or RedSignal",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Reported when a Station handler argument is not a supported resolvable form in the current compilation.
        /// </summary>
        public static readonly DiagnosticDescriptor UnsupportedStationHandler = new DiagnosticDescriptor(
            id: "TOP009",
            title: "Unsupported station handler form",
            messageFormat: "Station handler must be a lambda, anonymous method, or method group / local function declared in the current compilation and uniquely resolvable",
            category: "TrainOP.Generators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
