namespace TrainOP.Generators
{
    /// <summary>
    /// Context for emitting typed merge statements from a <see cref="MergePlan"/>.
    /// </summary>
    internal sealed class MergeEmitContext
    {
        public MergeEmitContext(
            string wagonNamesExpression,
            string dataVariable,
            string refFlagsExpression,
            string refLocalValuesExpression,
            bool removeOmittedRegularInputs,
            bool preserveManifestComposition,
            bool useRuntimeMemberAccess = false)
        {
            WagonNamesExpression = wagonNamesExpression;
            DataVariable = dataVariable;
            RefFlagsExpression = refFlagsExpression;
            RefLocalValuesExpression = refLocalValuesExpression;
            RemoveOmittedRegularInputs = removeOmittedRegularInputs;
            PreserveManifestComposition = preserveManifestComposition;
            UseRuntimeMemberAccess = useRuntimeMemberAccess;
        }

        public string WagonNamesExpression { get; }

        public string DataVariable { get; }

        public string RefFlagsExpression { get; }

        public string RefLocalValuesExpression { get; }

        public bool RemoveOmittedRegularInputs { get; }

        /// <summary>
        /// When true, merge updates existing keys only and never unloads or adds wagons.
        /// </summary>
        public bool PreserveManifestComposition { get; }

        /// <summary>
        /// When true, return members are read via <c>WagonStationReturn.TryGetMemberValue</c>
        /// because the station return is untyped (anonymous / <c>object</c>).
        /// </summary>
        public bool UseRuntimeMemberAccess { get; }

        public static string BuildWagonNameExpression(string wagonNamesExpression, int wagonIndex)
        {
            return wagonNamesExpression + "[" + wagonIndex + "]";
        }
    }
}
