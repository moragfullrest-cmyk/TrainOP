using System.Collections.Immutable;

namespace TrainOP.Generators
{
    /// <summary>
    /// Compile-time plan for merging a typed handler return into a manifest.
    /// </summary>
    internal sealed class MergePlan
    {
        public MergePlan(ImmutableArray<MergeInputSlot> inputSlots, ImmutableArray<MergeExtraSlot> extraSlots)
        {
            InputSlots = inputSlots.IsDefault ? ImmutableArray<MergeInputSlot>.Empty : inputSlots;
            ExtraSlots = extraSlots.IsDefault ? ImmutableArray<MergeExtraSlot>.Empty : extraSlots;
        }

        public ImmutableArray<MergeInputSlot> InputSlots { get; }

        public ImmutableArray<MergeExtraSlot> ExtraSlots { get; }
    }

    /// <summary>
    /// Maps one input wagon slot to a typed return member access, or marks it unmapped.
    /// </summary>
    internal sealed class MergeInputSlot
    {
        public MergeInputSlot(
            int wagonIndex,
            string wagonName,
            string returnMemberName,
            bool isByReference)
        {
            WagonIndex = wagonIndex;
            WagonName = wagonName;
            ReturnMemberName = returnMemberName;
            IsByReference = isByReference;
        }

        public int WagonIndex { get; }

        public string WagonName { get; }

        /// <summary>When non-null, <c>stationReturn.{ReturnMemberName}</c> supplies the wagon value.</summary>
        public string ReturnMemberName { get; }

        public bool IsMapped => ReturnMemberName != null;

        public bool IsByReference { get; }
    }

    /// <summary>
    /// A return member that is not also an input wagon and should be loaded by member name.
    /// </summary>
    internal sealed class MergeExtraSlot
    {
        public MergeExtraSlot(string returnMemberName)
        {
            ReturnMemberName = returnMemberName;
        }

        public string ReturnMemberName { get; }
    }
}
