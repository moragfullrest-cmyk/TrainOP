using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Describes the shape of wagons produced by a station handler return value.
    /// </summary>
    internal sealed class ReturnShape
    {
        public static ReturnShape Unknown { get; } = new ReturnShape(
            ImmutableArray<WagonBinding>.Empty,
            isCargoManifest: false,
            isValueTuple: false,
            isUnknown: true,
            returnTypeDisplay: "global::System.Object");

        public static ReturnShape Void { get; } = new ReturnShape(
            ImmutableArray<WagonBinding>.Empty,
            isCargoManifest: false,
            isValueTuple: false,
            isVoid: true);

        /// <summary>
        /// Creates a return shape with member bindings and classification flags.
        /// </summary>
        public ReturnShape(
            ImmutableArray<WagonBinding> members,
            bool isCargoManifest,
            bool isValueTuple,
            bool isUnknown = false,
            bool isVoid = false,
            string returnTypeDisplay = null,
            bool useGenericReturn = false,
            bool isExplicitSignalReturn = false,
            bool isRuntimeSignalReturn = false,
            bool hasDefaultItemNTupleElements = false,
            ImmutableArray<Location> tupleReturnLocations = default)
        {
            Members = members;
            IsCargoManifest = isCargoManifest;
            IsValueTuple = isValueTuple;
            IsUnknown = isUnknown;
            IsVoid = isVoid;
            ReturnTypeDisplay = returnTypeDisplay;
            UseGenericReturn = useGenericReturn;
            IsExplicitSignalReturn = isExplicitSignalReturn;
            IsRuntimeSignalReturn = isRuntimeSignalReturn;
            HasDefaultItemNTupleElements = hasDefaultItemNTupleElements;
            TupleReturnLocations = tupleReturnLocations.IsDefault
                ? ImmutableArray<Location>.Empty
                : tupleReturnLocations;
        }

        public ImmutableArray<WagonBinding> Members { get; }

        public bool IsCargoManifest { get; }

        public bool IsValueTuple { get; }

        public bool IsUnknown { get; }

        public bool IsVoid { get; }

        public string ReturnTypeDisplay { get; }

        public bool UseGenericReturn { get; }

        public bool IsExplicitSignalReturn { get; }

        public bool IsRuntimeSignalReturn { get; }

        /// <summary>
        /// True when at least one tuple element uses the compiler default ItemN name
        /// without an explicit <c>NameColon</c> in source (inferred or explicit names do not count).
        /// </summary>
        public bool HasDefaultItemNTupleElements { get; }

        public ImmutableArray<Location> TupleReturnLocations { get; }
    }
}
