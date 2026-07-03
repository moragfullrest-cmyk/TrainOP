using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds return-shape metadata used when merging station handler code generation.
    /// </summary>
    internal static class StationReturnMetadataBuilder
    {
        /// <summary>
        /// Maps input wagon names to tuple element ordinals for a single return shape.
        /// </summary>
        public static int[] BuildTupleElementOrdinals(
            ImmutableArray<WagonBinding> inputWagons,
            ReturnShape returnShape)
        {
            if (returnShape.IsUnknown
                || returnShape.IsCargoManifest
                || !returnShape.IsValueTuple
                || inputWagons.IsDefaultOrEmpty)
            {
                return null;
            }

            var ordinals = new int[inputWagons.Length];
            for (var i = 0; i < ordinals.Length; i++)
            {
                ordinals[i] = FindMemberOrdinal(inputWagons[i].Name, returnShape.Members);
            }

            return ordinals;
        }

        /// <summary>
        /// Merges tuple element ordinals across multiple return shapes when they agree.
        /// </summary>
        public static int[] MergeTupleElementOrdinals(
            ImmutableArray<WagonBinding> inputWagons,
            IReadOnlyList<ReturnShape> returnShapes)
        {
            if (inputWagons.IsDefaultOrEmpty || returnShapes == null || returnShapes.Count == 0)
            {
                return null;
            }

            int[] merged = null;
            for (var i = 0; i < returnShapes.Count; i++)
            {
                var ordinals = BuildTupleElementOrdinals(inputWagons, returnShapes[i]);
                if (ordinals == null)
                {
                    continue;
                }

                if (merged == null)
                {
                    merged = ordinals;
                    continue;
                }

                if (!OrdinalArraysEqual(merged, ordinals))
                {
                    return null;
                }
            }

            return merged;
        }

        /// <summary>
        /// Extracts return member names from a single return shape.
        /// </summary>
        public static string[] BuildReturnMemberNames(ReturnShape returnShape)
        {
            if (returnShape.IsUnknown
                || returnShape.IsCargoManifest
                || returnShape.Members.IsDefaultOrEmpty)
            {
                return null;
            }

            var names = new string[returnShape.Members.Length];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = returnShape.Members[i].Name;
            }

            return names;
        }

        /// <summary>
        /// Collects the union of return member names across multiple return shapes.
        /// </summary>
        public static string[] MergeReturnMemberNames(IReadOnlyList<ReturnShape> returnShapes)
        {
            if (returnShapes == null || returnShapes.Count == 0)
            {
                return null;
            }

            var names = new List<string>();
            for (var i = 0; i < returnShapes.Count; i++)
            {
                var shapeNames = BuildReturnMemberNames(returnShapes[i]);
                if (shapeNames == null)
                {
                    continue;
                }

                for (var j = 0; j < shapeNames.Length; j++)
                {
                    if (!names.Contains(shapeNames[j]))
                    {
                        names.Add(shapeNames[j]);
                    }
                }
            }

            return names.Count == 0 ? null : names.ToArray();
        }

        /// <summary>
        /// Compares two ordinal arrays for element-wise equality.
        /// </summary>
        private static bool OrdinalArraysEqual(int[] left, int[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Finds the ordinal index of a wagon name within return shape members.
        /// </summary>
        private static int FindMemberOrdinal(string wagonName, ImmutableArray<WagonBinding> members)
        {
            for (var i = 0; i < members.Length; i++)
            {
                if (string.Equals(members[i].Name, wagonName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
