namespace TrainOP.Generators.Handlers
{
    /// <summary>
    /// Kind of a station handler input slot.
    /// Wagon slots carry cargo; the rest are framework parameters skipped when projecting wagon names.
    /// </summary>
    internal enum HandlerInputKind
    {
        /// <summary>Named wagon pulled from or written back to the manifest.</summary>
        Wagon,

        /// <summary>Full <c>CargoManifest</c> parameter (Station or ServiceStation escape hatch).</summary>
        CargoManifest,

        /// <summary><c>RedSignal</c> parameter (required for ServiceStation).</summary>
        RedSignal,

        /// <summary><c>SignalIssue</c> parameter (ServiceStation: first issue of the stop).</summary>
        SignalIssue,

        /// <summary><c>IReadOnlyList&lt;SignalIssue&gt;</c> parameter (ServiceStation: every issue of the stop).</summary>
        SignalIssues,

        /// <summary><c>IReadOnlyList&lt;StationVisit&gt;</c> parameter (ServiceStation: visits recorded before this hop).</summary>
        VisitJournal,

        /// <summary><c>CancellationToken</c> for cooperative cancellation.</summary>
        CancellationToken
    }
}
