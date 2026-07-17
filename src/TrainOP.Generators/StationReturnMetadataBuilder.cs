using System;
using System.Collections.Generic;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds return-shape metadata used when merging station handler code generation.
    /// </summary>
    internal static class StationReturnMetadataBuilder
    {
        /// <summary>
        /// Extracts return member names from a single return shape.
        /// </summary>
        public static string[] BuildReturnMemberNames(ReturnShape returnShape)
        {
            if (returnShape.IsUnknown
                || returnShape.IsVoid
                || returnShape.IsCargoManifest
                || returnShape.IsExplicitSignalReturn
                || returnShape.IsRuntimeSignalReturn
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
        /// Builds return member names for a delegate signature group.
        /// Multiple object-return shapes are merged into one deduplicated member-name list.
        /// </summary>
        public static string[] MergeReturnMemberNames(IReadOnlyList<ReturnShape> returnShapes)
        {
            if (returnShapes == null || returnShapes.Count == 0)
            {
                return null;
            }

            if (returnShapes.Count == 1)
            {
                return BuildReturnMemberNames(returnShapes[0]);
            }

            if (!CanMergeReturnMemberNamesAcrossShapes(returnShapes))
            {
                return null;
            }

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < returnShapes.Count; i++)
            {
                var shapeNames = BuildReturnMemberNames(returnShapes[i]);
                if (shapeNames == null)
                {
                    continue;
                }

                for (var j = 0; j < shapeNames.Length; j++)
                {
                    if (seen.Add(shapeNames[j]))
                    {
                        names.Add(shapeNames[j]);
                    }
                }
            }

            return names.Count == 0 ? null : names.ToArray();
        }

        private static bool CanMergeReturnMemberNamesAcrossShapes(IReadOnlyList<ReturnShape> returnShapes)
        {
            for (var i = 0; i < returnShapes.Count; i++)
            {
                var returnShape = returnShapes[i];
                if (!returnShape.UseGenericReturn
                    && !returnShape.IsUnknown
                    && !string.Equals(returnShape.ReturnTypeDisplay, "global::System.Object", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
