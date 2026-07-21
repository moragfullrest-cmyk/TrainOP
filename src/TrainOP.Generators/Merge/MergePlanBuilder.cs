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

            return schema.Output.CanEmitTypedDataMerge(schema.IsServiceStation, returnMembersField);
        }

        /// <summary>
        /// Builds a compile-time merge plan for a handler with a known return shape.
        /// </summary>
        public static MergePlan Build(StationHandlerBinding schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            var wagons = schema.Wagons;
            var members = schema.ReturnShape.Members;
            var returnMemberNames = schema.Output.ReturnMemberNames;
            var isValueTuple = schema.ReturnShape.IsValueTuple;

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
                var returnMemberName = ResolveReturnMemberName(
                    wagon.Name,
                    i,
                    isValueTuple,
                    returnMemberNames,
                    memberByName,
                    members.Length);

                if (returnMemberName != null)
                {
                    consumedReturnMembers.Add(returnMemberName);
                }

                inputSlots.Add(new MergeInputSlot(
                    i,
                    wagon.Name,
                    returnMemberName,
                    wagon.IsByReference));
            }

            var extraSlots = ImmutableArray.CreateBuilder<MergeExtraSlot>();
            for (var i = 0; i < members.Length; i++)
            {
                var memberName = members[i].Name;
                if (!inputWagonNames.Contains(memberName)
                    && !consumedReturnMembers.Contains(memberName))
                {
                    extraSlots.Add(new MergeExtraSlot(memberName));
                }
            }

            return new MergePlan(inputSlots.ToImmutable(), extraSlots.ToImmutable());
        }

        /// <summary>
        /// Resolves which return member supplies an input wagon (mirrors runtime wagon resolution order).
        /// </summary>
        private static string ResolveReturnMemberName(
            string wagonName,
            int wagonIndex,
            bool isValueTuple,
            string[] returnMemberNames,
            Dictionary<string, WagonBinding> memberByName,
            int memberCount)
        {
            if (memberByName.ContainsKey(wagonName))
            {
                return wagonName;
            }

            if (isValueTuple
                && returnMemberNames != null
                && wagonIndex < returnMemberNames.Length
                && !string.Equals(returnMemberNames[wagonIndex], wagonName, StringComparison.Ordinal)
                && memberByName.ContainsKey(returnMemberNames[wagonIndex]))
            {
                return returnMemberNames[wagonIndex];
            }

            if (isValueTuple && wagonIndex < memberCount)
            {
                var ordinalName = "Item" + (wagonIndex + 1);
                if (memberByName.ContainsKey(ordinalName))
                {
                    return ordinalName;
                }
            }

            return null;
        }
    }
}
