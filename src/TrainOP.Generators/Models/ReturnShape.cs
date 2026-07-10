using System.Collections.Immutable;

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
            bool isExplicitSignalReturn = false)
        {
            Members = members;
            IsCargoManifest = isCargoManifest;
            IsValueTuple = isValueTuple;
            IsUnknown = isUnknown;
            IsVoid = isVoid;
            ReturnTypeDisplay = returnTypeDisplay;
            UseGenericReturn = useGenericReturn;
            IsExplicitSignalReturn = isExplicitSignalReturn;
        }

        public ImmutableArray<WagonBinding> Members { get; }

        public bool IsCargoManifest { get; }

        public bool IsValueTuple { get; }

        public bool IsUnknown { get; }

        public bool IsVoid { get; }

        public string ReturnTypeDisplay { get; }

        public bool UseGenericReturn { get; }

        public bool IsExplicitSignalReturn { get; }
    }
}
