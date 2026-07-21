using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Describes how a wagon name is resolved when emitting a manifest pull statement.
    /// </summary>
    internal sealed class PullContext
    {
        private PullContext(
            string localVariableName,
            string nameExpression,
            string manifestVariable)
        {
            LocalVariableName = localVariableName;
            NameExpression = nameExpression;
            ManifestVariable = manifestVariable;
        }

        /// <summary>Generated local variable name (<c>paymentId</c> or <c>wagon0</c>).</summary>
        public string LocalVariableName { get; }

        /// <summary>Expression passed to <c>PullWagon</c> / <c>HasWagon</c>.</summary>
        public string NameExpression { get; }

        /// <summary>Manifest variable used for pull (default <c>manifest</c>).</summary>
        public string ManifestVariable { get; }

        /// <summary>Pull using a compile-time literal wagon name.</summary>
        public static PullContext Literal(WagonBinding wagon, string manifestVariable = "manifest")
        {
            return new PullContext(
                wagon.Name,
                "\"" + StringHelpers.Escape(wagon.Name) + "\"",
                manifestVariable);
        }

        /// <summary>Pull using a runtime name array (<c>inputNames[i]</c>).</summary>
        public static PullContext NameArray(
            int wagonIndex,
            string namesVariable,
            string manifestVariable = "manifest")
        {
            return new PullContext(
                "wagon" + wagonIndex,
                namesVariable + "[" + wagonIndex + "]",
                manifestVariable);
        }
    }
}
