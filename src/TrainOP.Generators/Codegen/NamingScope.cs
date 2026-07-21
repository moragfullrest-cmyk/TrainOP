using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Generated field and method names for one delegate signature group.
    /// </summary>
    internal sealed class NamingScope
    {
        private NamingScope(
            string delegateTypeId,
            string wagonNamesField,
            string refFlagsField,
            string returnMembersField,
            string coreMethodName,
            string delegateName,
            string resolveChainBindingMethod)
        {
            DelegateTypeId = delegateTypeId;
            WagonNamesField = wagonNamesField;
            RefFlagsField = refFlagsField;
            ReturnMembersField = returnMembersField;
            CoreMethodName = coreMethodName;
            DelegateName = delegateName;
            ResolveChainBindingMethod = resolveChainBindingMethod;
        }

        /// <summary>Stable hash id for this delegate signature group.</summary>
        public string DelegateTypeId { get; }

        /// <summary>Static <c>WagonNames_{DelegateTypeId}</c> field name.</summary>
        public string WagonNamesField { get; }

        /// <summary>Static <c>RefFlags_{DelegateTypeId}</c> field name, or null when absent.</summary>
        public string RefFlagsField { get; }

        /// <summary>Static <c>ReturnMembers_{DelegateTypeId}</c> field name, or null when absent.</summary>
        public string ReturnMembersField { get; }

        /// <summary>Internal core adapter method name (<c>StationCore_{DelegateTypeId}</c>).</summary>
        public string CoreMethodName { get; }

        /// <summary>Custom delegate type name (<c>TrainStationHandler_*</c> or service variant).</summary>
        public string DelegateName { get; }

        /// <summary>Chain binding resolver method name (<c>ResolveChainBinding_{DelegateTypeId}</c>).</summary>
        public string ResolveChainBindingMethod { get; }

        /// <summary>
        /// Builds naming scope for one merged schema emission group.
        /// </summary>
        public static NamingScope ForDelegate(
            string delegateTypeId,
            StationHandlerBinding binding,
            string[] returnMembers)
        {
            var delegatePrefix = binding.IsServiceStation
                ? "TrainServiceStationHandler_"
                : "TrainStationHandler_";

            return new NamingScope(
                delegateTypeId,
                wagonNamesField: "WagonNames_" + delegateTypeId,
                refFlagsField: binding.HasRefWagons ? "RefFlags_" + delegateTypeId : null,
                returnMembersField: returnMembers != null ? "ReturnMembers_" + delegateTypeId : null,
                coreMethodName: "StationCore_" + delegateTypeId,
                delegateName: delegatePrefix + delegateTypeId,
                resolveChainBindingMethod: "ResolveChainBinding_" + delegateTypeId);
        }
    }
}
