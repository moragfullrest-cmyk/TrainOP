namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Kind of a station handler input slot.
    /// Wagon slots carry cargo; the rest are framework parameters skipped when projecting wagon names.
    /// </summary>
    internal enum HandlerInputKind
    {
        /// <summary>Named wagon pulled from or written back to the manifest.</summary>
        Wagon,

        /// <summary>Full <c>CargoManifest</c> parameter (regular Station only).</summary>
        CargoManifest,

        /// <summary><c>RedSignal</c> parameter (required for ServiceStation).</summary>
        RedSignal,

        /// <summary><c>SignalIssue</c> parameter (regular Station only).</summary>
        SignalIssue,

        /// <summary><c>CancellationToken</c> for cooperative cancellation.</summary>
        CancellationToken
    }
}
