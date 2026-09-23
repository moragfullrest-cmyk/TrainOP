using System;
using System.Collections.Generic;
using System.Text;

namespace TrainOP.Generators.Handlers
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
        /// True when distinct return shapes share one CLR Func but cannot share consolidated
        /// <c>ReturnMembers</c> metadata (e.g. named vs default-ItemN tuples). Anonymous /
        /// <c>object</c> shapes merge into one member-name list and do not need per-site dispatch.
        /// </summary>
        public static bool RequiresPerSiteReturnMetadata(IReadOnlyList<ReturnShape> returnShapes)
        {
            if (returnShapes == null || returnShapes.Count <= 1)
            {
                return false;
            }

            return !CanMergeReturnMemberNamesAcrossShapes(returnShapes);
        }

        /// <summary>
        /// Determines whether typed data merge can be emitted for this output.
        /// </summary>
        /// <param name="returnMembersField">Generated field or local holding return member names.</param>
        /// <param name="allowGenericReturn">
        /// When true, anonymous / <c>object</c> returns with known members are eligible
        /// (member values are read at runtime; used for ServiceStation overlay).
        /// </param>
        public bool CanEmitTypedDataMerge(string returnMembersField, bool allowGenericReturn = false)
        {
            if (Mode == HandlerOutputMode.Void
                || Mode == HandlerOutputMode.Unknown
                || Mode == HandlerOutputMode.CargoManifest
                || Mode == HandlerOutputMode.ExplicitSignal
                || (!allowGenericReturn && Shape.UseGenericReturn)
                || Shape.Members.IsDefaultOrEmpty
                || returnMembersField == null)
            {
                return false;
            }

            return !IsSignalOnlyReturnType(Shape.ReturnTypeDisplay);
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

            return returnTypeDisplay == ReturnTypeDisplayHelper.SignalReturnTypeDisplay
                || returnTypeDisplay == ReturnTypeDisplayHelper.RedFailureReturnTypeDisplay
                || returnTypeDisplay == ReturnTypeDisplayHelper.WhitePassReturnTypeDisplay
                || returnTypeDisplay == ReturnTypeDisplayHelper.CargoManifestReturnTypeDisplay;
        }
    }
}
