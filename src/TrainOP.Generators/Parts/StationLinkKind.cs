namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Distinguishes <c>.Station</c> from <c>.ServiceStation</c> links.
    /// </summary>
    internal enum StationLinkKind
    {
        /// <summary>Data-oriented <c>.Station(...)</c>.</summary>
        Station,

        /// <summary>Data-oriented <c>.ServiceStation(...)</c>.</summary>
        ServiceStation,
    }
}
