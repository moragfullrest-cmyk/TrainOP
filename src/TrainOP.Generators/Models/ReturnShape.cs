using System.Collections.Immutable;

namespace TrainOP.Generators.Models
{
    internal sealed class ReturnShape
    {
        public static ReturnShape Unknown { get; } = new ReturnShape(
            ImmutableArray<WagonBinding>.Empty,
            isCargoManifest: false,
            isValueTuple: false,
            isUnknown: true);

        public ReturnShape(
            ImmutableArray<WagonBinding> members,
            bool isCargoManifest,
            bool isValueTuple,
            bool isUnknown = false)
        {
            Members = members;
            IsCargoManifest = isCargoManifest;
            IsValueTuple = isValueTuple;
            IsUnknown = isUnknown;
        }

        public ImmutableArray<WagonBinding> Members { get; }

        public bool IsCargoManifest { get; }

        public bool IsValueTuple { get; }

        public bool IsUnknown { get; }
    }
}
