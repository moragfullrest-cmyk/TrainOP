namespace TrainOP
{
    /// <summary>
    /// Kind of a station handler input parameter.
    /// Mirrors generator <c>HandlerInputKind</c>: wagon slots vs framework parameters.
    /// </summary>
    internal enum HandlerInputKind
    {
        /// <summary>Named wagon parameter.</summary>
        Wagon,

        /// <summary><see cref="CargoManifest"/> parameter.</summary>
        CargoManifest,

        /// <summary><see cref="RedSignal"/> parameter.</summary>
        RedSignal,

        /// <summary><see cref="SignalIssue"/> parameter.</summary>
        SignalIssue,

        /// <summary><see cref="System.Threading.CancellationToken"/> parameter.</summary>
        CancellationToken
    }
}
