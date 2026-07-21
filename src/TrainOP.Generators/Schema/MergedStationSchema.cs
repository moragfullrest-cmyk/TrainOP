using System;
using System.Collections.Generic;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;
namespace TrainOP.Generators
{
    /// <summary>
    /// Merges a canonical handler binding with combined return-shape metadata for emission.
    /// </summary>
    internal sealed class MergedStationSchema
    {
        private readonly List<ReturnShape> _returnShapes = new List<ReturnShape>();
        private List<ChainSiteBinding> _chainBindings = new List<ChainSiteBinding>();

        /// <summary>
        /// Creates a merged schema from a canonical handler binding and delegate type id.
        /// </summary>
        public MergedStationSchema(StationHandlerBinding canonicalBinding, string delegateTypeId)
        {
            CanonicalBinding = canonicalBinding;
            DelegateTypeId = delegateTypeId;
        }

        /// <summary>Canonical handler binding for this delegate signature group.</summary>
        public StationHandlerBinding CanonicalBinding { get; }

        public string DelegateTypeId { get; }

        public IReadOnlyList<ChainSiteBinding> ChainBindings => _chainBindings;

        public bool UsesChainDispatch =>
            _chainBindings.Count > 0
            && HasDistinctWagonNameSets()
            && !CanonicalBinding.IsServiceStation;

        private bool HasDistinctWagonNameSets()
        {
            var wagonNameSets = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < _chainBindings.Count; i++)
            {
                wagonNameSets.Add(HandlerInputParameters.FormatWagonNames(_chainBindings[i].Schema.Wagons));
            }

            return wagonNameSets.Count > 1;
        }

        public string[] ReturnMembers =>
            HandlerOutputParameters.MergeReturnMemberNames(_returnShapes);

        /// <summary>
        /// Attaches chain-site bindings discovered for this delegate signature group.
        /// </summary>
        public void SetChainBindings(List<ChainSiteBinding> chainBindings)
        {
            _chainBindings = chainBindings ?? new List<ChainSiteBinding>();
        }

        /// <summary>
        /// Adds distinct return shapes from another merged schema with the same emission signature.
        /// </summary>
        public void MergeFrom(MergedStationSchema other)
        {
            if (other == null)
            {
                return;
            }

            for (var i = 0; i < other._returnShapes.Count; i++)
            {
                AddReturnShape(other._returnShapes[i]);
            }
        }

        /// <summary>
        /// Adds a distinct return shape to the merged metadata set.
        /// </summary>
        public void AddReturnShape(ReturnShape returnShape)
        {
            for (var i = 0; i < _returnShapes.Count; i++)
            {
                if (ReturnShapesEqual(_returnShapes[i], returnShape))
                {
                    return;
                }
            }

            _returnShapes.Add(returnShape);
        }

        /// <summary>
        /// Determines whether two return shapes are equivalent for merge purposes.
        /// </summary>
        public static bool ReturnShapesEqual(ReturnShape left, ReturnShape right)
        {
            if (left.IsUnknown != right.IsUnknown
                || left.IsVoid != right.IsVoid
                || left.IsCargoManifest != right.IsCargoManifest
                || left.IsValueTuple != right.IsValueTuple
                || left.Members.Length != right.Members.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Members.Length; i++)
            {
                if (!string.Equals(left.Members[i].Name, right.Members[i].Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
