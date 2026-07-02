using System.Collections.Immutable;

namespace TrainOP.Generators.Models
{
    internal sealed class StationHandlerBinding
    {
        public StationHandlerBinding(
            ImmutableArray<WagonBinding> inputWagons,
            bool includeManifest,
            bool isAsync,
            bool hasCancellationToken,
            ReturnShape returnShape)
        {
            InputWagons = inputWagons;
            IncludeManifest = includeManifest;
            IsAsync = isAsync;
            HasCancellationToken = hasCancellationToken;
            ReturnShape = returnShape;
        }

        public ImmutableArray<WagonBinding> InputWagons { get; }

        public bool IncludeManifest { get; }

        public bool IsAsync { get; }

        public bool HasCancellationToken { get; }

        public ReturnShape ReturnShape { get; }

        public bool IsSeed => InputWagons.Length == 0 && !IncludeManifest;
    }
}
