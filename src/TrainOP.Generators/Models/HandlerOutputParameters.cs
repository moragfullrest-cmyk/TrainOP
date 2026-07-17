using System;
using System.Collections.Generic;
using System.Text;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Canonical description of a handler's output: return shape plus projections used by merge/codegen.
    /// </summary>
    internal sealed class HandlerOutputParameters
    {
        /// <summary>
        /// Wraps a <see cref="ReturnShape"/> and derives mode / member-name projections.
        /// </summary>
        public static HandlerOutputParameters From(ReturnShape shape)
        {
            if (shape == null)
            {
                shape = ReturnShape.Unknown;
            }

            return new HandlerOutputParameters(shape, ResolveMode(shape), BuildReturnMemberNames(shape));
        }

        private HandlerOutputParameters(ReturnShape shape, HandlerOutputMode mode, string[] returnMemberNames)
        {
            Shape = shape;
            Mode = mode;
            ReturnMemberNames = returnMemberNames;
        }

        /// <summary>Underlying return-shape metadata from inference.</summary>
        public ReturnShape Shape { get; }

        /// <summary>Simplified output mode for readers and eligibility checks.</summary>
        public HandlerOutputMode Mode { get; }

        /// <summary>
        /// Named return members for merge metadata, or <c>null</c> when the shape has no known members.
        /// </summary>
        public string[] ReturnMemberNames { get; }

        /// <summary>
        /// Extracts return member names from a single return shape (null when not usable for merge).
        /// </summary>
        public static string[] BuildReturnMemberNames(ReturnShape returnShape)
        {
            if (returnShape == null
                || returnShape.IsUnknown
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

        /// <summary>
        /// Determines whether typed data merge can be emitted for this output and station kind.
        /// </summary>
        public bool CanEmitTypedDataMerge(bool isServiceStation, string returnMembersField)
        {
            if (isServiceStation
                || Mode == HandlerOutputMode.Void
                || Mode == HandlerOutputMode.Unknown
                || Mode == HandlerOutputMode.CargoManifest
                || Mode == HandlerOutputMode.ExplicitSignal
                || Shape.UseGenericReturn
                || Shape.Members.IsDefaultOrEmpty
                || returnMembersField == null)
            {
                return false;
            }

            return !IsSignalOnlyReturnType(Shape.ReturnTypeDisplay);
        }

        /// <summary>
        /// Builds an inline compile-time <c>string[]</c> literal for known return member names.
        /// </summary>
        public string BuildCompileTimeReturnMembersExpression(Func<string, string> escape)
        {
            if (Shape.Members.IsDefaultOrEmpty)
            {
                return null;
            }

            var builder = new StringBuilder();
            builder.Append("new string[] { ");
            for (var i = 0; i < Shape.Members.Length; i++)
            {
                builder.Append("\"").Append(escape(Shape.Members[i].Name)).Append("\"");
                if (i < Shape.Members.Length - 1)
                {
                    builder.Append(", ");
                }
            }

            builder.Append(" }");
            return builder.ToString();
        }

        private static HandlerOutputMode ResolveMode(ReturnShape shape)
        {
            if (shape.IsVoid)
            {
                return HandlerOutputMode.Void;
            }

            if (shape.IsUnknown)
            {
                return HandlerOutputMode.Unknown;
            }

            if (shape.IsCargoManifest)
            {
                return HandlerOutputMode.CargoManifest;
            }

            if (shape.IsRuntimeSignalReturn)
            {
                return HandlerOutputMode.RuntimeSignal;
            }

            if (shape.IsExplicitSignalReturn)
            {
                return HandlerOutputMode.ExplicitSignal;
            }

            if (!shape.Members.IsDefaultOrEmpty)
            {
                return HandlerOutputMode.KnownMembers;
            }

            return HandlerOutputMode.Unknown;
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

        private static bool IsSignalOnlyReturnType(string returnTypeDisplay)
        {
            if (string.IsNullOrWhiteSpace(returnTypeDisplay))
            {
                return false;
            }

            return returnTypeDisplay == "global::TrainOP.Signal"
                || returnTypeDisplay == "global::TrainOP.RedFailure"
                || returnTypeDisplay == "global::TrainOP.GreenPass"
                || returnTypeDisplay == "global::TrainOP.CargoManifest";
        }
    }
}
