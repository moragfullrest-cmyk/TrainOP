using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Per-schema emission settings for adapter bodies and merge expressions.
    /// </summary>
    internal sealed class CodegenContext
    {
        private CodegenContext(
            PullStrategy pull,
            bool useNeutralWagonNames,
            string wagonNamesExpression,
            string returnMembersExpression,
            string refFlagsExpression,
            string allocateDefaultItemNExpression,
            bool allowTypedMerge,
            string inputNamesVariable,
            string stationLabelExpression,
            NamingScope names)
        {
            Pull = pull;
            UseNeutralWagonNames = useNeutralWagonNames;
            WagonNamesExpression = wagonNamesExpression;
            ReturnMembersExpression = returnMembersExpression;
            RefFlagsExpression = refFlagsExpression;
            AllocateDefaultItemNExpression = allocateDefaultItemNExpression;
            AllowTypedMerge = allowTypedMerge;
            InputNamesVariable = inputNamesVariable;
            StationLabelExpression = stationLabelExpression;
            Names = names;
        }

        /// <summary>How wagon pull statements are emitted inside the adapter body.</summary>
        public PullStrategy Pull { get; }

        /// <summary>When true, handler args use <c>wagon0</c>… and SignalIssue is <c>issue</c>.</summary>
        public bool UseNeutralWagonNames { get; }

        /// <summary>Expression for wagon name array passed to merge (field or local).</summary>
        public string WagonNamesExpression { get; }

        /// <summary>Expression for return member names, or null.</summary>
        public string ReturnMembersExpression { get; }

        /// <summary>Expression for ref flags array, or null when absent.</summary>
        public string RefFlagsExpression { get; }

        /// <summary>Expression for default-ItemN allocation flag (<c>true</c>/<c>false</c> or local).</summary>
        public string AllocateDefaultItemNExpression { get; }

        /// <summary>
        /// When false, adapters must use <c>StationMerge.ToSignal</c> so per-site return metadata applies
        /// (heterogeneous named vs default-ItemN tuple returns in one CLR signature group).
        /// </summary>
        public bool AllowTypedMerge { get; }

        /// <summary>Name-array variable for <see cref="PullStrategy.NameArray"/> (default <c>inputNames</c>).</summary>
        public string InputNamesVariable { get; }

        /// <summary>Station label passed to route registration (default <c>stationName</c>).</summary>
        public string StationLabelExpression { get; }

        /// <summary>Generated field and method names for this delegate group.</summary>
        public NamingScope Names { get; }

        /// <summary>
        /// Context for canonical adapters with static metadata fields.
        /// </summary>
        public static CodegenContext ForCanonical(NamingScope names, bool allocateDefaultItemN)
        {
            return new CodegenContext(
                PullStrategy.LiteralNames,
                useNeutralWagonNames: false,
                wagonNamesExpression: names.WagonNamesField,
                returnMembersExpression: names.ReturnMembersField,
                refFlagsExpression: names.RefFlagsField,
                allocateDefaultItemNExpression: allocateDefaultItemN ? "true" : "false",
                allowTypedMerge: true,
                inputNamesVariable: "inputNames",
                stationLabelExpression: "stationName",
                names);
        }

        /// <summary>
        /// Context for chain-dispatch adapters with runtime binding locals.
        /// </summary>
        public static CodegenContext ForChain(NamingScope names, bool allowTypedMerge = true)
        {
            return new CodegenContext(
                PullStrategy.NameArray,
                useNeutralWagonNames: true,
                wagonNamesExpression: "inputNames",
                returnMembersExpression: "returnMembers",
                refFlagsExpression: "refFlags",
                allocateDefaultItemNExpression: "allocateDefaultItemN",
                allowTypedMerge: allowTypedMerge,
                inputNamesVariable: "inputNames",
                stationLabelExpression: "stationName",
                names);
        }
    }
}
