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
            isUnnamedValueTuple: false,
            isUnknown: true);

        /// <summary>
        /// Creates a return shape with member bindings and classification flags.
        /// </summary>
        public ReturnShape(
            ImmutableArray<WagonBinding> members,
            bool isCargoManifest,
            bool isValueTuple,
            bool isUnnamedValueTuple = false,
            bool isUnknown = false)
        {
            Members = members;
            IsCargoManifest = isCargoManifest;
            IsValueTuple = isValueTuple;
            IsUnnamedValueTuple = isUnnamedValueTuple;
            IsUnknown = isUnknown;
        }

        public ImmutableArray<WagonBinding> Members { get; }

        public bool IsCargoManifest { get; }

        public bool IsValueTuple { get; }

        public bool IsUnnamedValueTuple { get; }

        public bool IsUnknown { get; }
    }
}
