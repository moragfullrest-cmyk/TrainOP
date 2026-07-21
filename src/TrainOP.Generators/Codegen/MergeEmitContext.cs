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
            bool removeOmittedRegularInputs)
        {
            WagonNamesExpression = wagonNamesExpression;
            DataVariable = dataVariable;
            RefFlagsExpression = refFlagsExpression;
            RefLocalValuesExpression = refLocalValuesExpression;
            RemoveOmittedRegularInputs = removeOmittedRegularInputs;
        }

        public string WagonNamesExpression { get; }

        public string DataVariable { get; }

        public string RefFlagsExpression { get; }

        public string RefLocalValuesExpression { get; }

        public bool RemoveOmittedRegularInputs { get; }

        public static string BuildWagonNameExpression(string wagonNamesExpression, int wagonIndex)
        {
            return wagonNamesExpression + "[" + wagonIndex + "]";
        }
    }
}
