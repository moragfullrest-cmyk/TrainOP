using System.Collections.Immutable;

namespace TrainOP.Generators.Models
{
    internal sealed class ReturnShape
    {
        public static ReturnShape Unknown { get; } = new ReturnShape(
            ImmutableArray<WagonBinding>.Empty,
            isCargoManifest: false,
            isValueTuple: false,
            isUnnamedValueTuple: false,
            isUnknown: true);

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
