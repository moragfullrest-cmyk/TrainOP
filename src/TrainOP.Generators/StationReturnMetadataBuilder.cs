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
    }
}
