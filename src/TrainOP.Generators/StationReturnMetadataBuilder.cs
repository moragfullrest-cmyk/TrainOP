using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    internal static class StationReturnMetadataBuilder
    {
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
