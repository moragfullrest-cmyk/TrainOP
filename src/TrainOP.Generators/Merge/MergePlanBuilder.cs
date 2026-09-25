using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Builds static merge plans from handler input/output schema (parity with <see cref="TrainOP.StationMerge"/>).
    /// </summary>
    internal static class MergePlanBuilder
    {
        /// <summary>
        /// Determines whether a fully static merge plan can be built for typed codegen.
        /// </summary>
        public static bool CanBuildStaticPlan(StationHandlerBinding schema, string returnMembersField)
        {
            if (schema == null)
            {
                return false;
            }

            return schema.Output.CanEmitTypedDataMerge(
                returnMembersField,
                allowGenericReturn: schema.IsServiceStation);
        }

        /// <summary>
        /// Builds a compile-time merge plan for a handler with a known return shape.
        /// Default ItemN tuple elements become allocated extras (not positional input maps).
        /// </summary>
        public static MergePlan Build(StationHandlerBinding schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            var wagons = schema.Wagons;
            var members = schema.ReturnShape.Members;
            var allocateDefaultItemN = schema.ReturnShape.HasDefaultItemNTupleElements;

            var memberByName = new Dictionary<string, WagonBinding>(StringComparer.Ordinal);
            for (var i = 0; i < members.Length; i++)
            {
                memberByName[members[i].Name] = members[i];
            }

            var inputWagonNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < wagons.Length; i++)
            {
                inputWagonNames.Add(wagons[i].Name);
            }

            var inputSlots = ImmutableArray.CreateBuilder<MergeInputSlot>();
            var consumedReturnMembers = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < wagons.Length; i++)
            {
                var wagon = wagons[i];
                string returnMemberName = null;
                if (!wagon.IsOut
                    && !wagon.IsReadOnlyPass
                    && memberByName.ContainsKey(wagon.Name)
                    && !ShouldAllocateDefaultItemMember(
                        allocateDefaultItemN,
                        wagon.Name,
                        IndexOfMember(members, wagon.Name)))
                {
                    returnMemberName = wagon.Name;
                    consumedReturnMembers.Add(returnMemberName);
                }

                inputSlots.Add(new MergeInputSlot(
                    i,
                    wagon.Name,
                    returnMemberName,
                    wagon.IsByReference,
                    wagon.RetainsSlot));
            }

            var extraSlots = ImmutableArray.CreateBuilder<MergeExtraSlot>();
            for (var i = 0; i < members.Length; i++)
            {
                var memberName = members[i].Name;
                if (consumedReturnMembers.Contains(memberName))
                {
                    continue;
                }

                if (inputWagonNames.Contains(memberName)
                    && !ShouldAllocateDefaultItemMember(allocateDefaultItemN, memberName, i))
                {
                    continue;
                }

                extraSlots.Add(new MergeExtraSlot(
                    memberName,
                    ShouldAllocateDefaultItemMember(allocateDefaultItemN, memberName, i)));
            }

            return new MergePlan(inputSlots.ToImmutable(), extraSlots.ToImmutable());
        }

        private static int IndexOfMember(ImmutableArray<WagonBinding> members, string name)
        {
            for (var i = 0; i < members.Length; i++)
            {
                if (string.Equals(members[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Default ItemN elements become new allocated wagons when the return shape opted into allocation.
        /// </summary>
        internal static bool ShouldAllocateDefaultItemMember(
            bool allocateDefaultItemNElements,
            string memberName,
            int memberIndex)
        {
            if (!allocateDefaultItemNElements || memberIndex < 0 || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            return StringHelpers.IsDefaultTupleElementName(memberName, memberIndex);
        }
    }
}
