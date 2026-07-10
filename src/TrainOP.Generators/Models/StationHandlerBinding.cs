using System.Collections.Immutable;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Describes a data-oriented station handler's inputs, flags, and inferred return shape.
    /// </summary>
    internal sealed class StationHandlerBinding
    {
        /// <summary>
        /// Creates a handler binding from wagon inputs, flags, and return shape metadata.
        /// </summary>
        public StationHandlerBinding(
            ImmutableArray<WagonBinding> inputWagons,
            bool includeManifest,
            bool isAsync,
            bool hasCancellationToken,
            ReturnShape returnShape,
            bool isServiceStation = false,
            bool includeRedSignal = false,
            bool includeSignalIssue = false,
            bool hasRefWagons = false)
        {
            InputWagons = inputWagons;
            IncludeManifest = includeManifest;
            IsAsync = isAsync;
            HasCancellationToken = hasCancellationToken;
            ReturnShape = returnShape;
            IsServiceStation = isServiceStation;
            IncludeRedSignal = includeRedSignal;
            IncludeSignalIssue = includeSignalIssue;
            HasRefWagons = hasRefWagons;
            ExtensionMethodName = ResolveDefaultExtensionMethodName(isServiceStation, isAsync);
        }

        public ImmutableArray<WagonBinding> InputWagons { get; }

        public ImmutableArray<WagonBinding> Wagons => InputWagons;

        public bool IncludeManifest { get; }

        public bool IsAsync { get; }

        public bool HasCancellationToken { get; }

        public bool HasRefWagons { get; }

        public ReturnShape ReturnShape { get; }

        public bool IsServiceStation { get; }

        public bool IncludeRedSignal { get; }

        public bool IncludeSignalIssue { get; }

        public string ExtensionMethodName { get; }

        public bool IsSeed => InputWagons.Length == 0 && !IncludeManifest && !IsServiceStation;

        public bool RemoveOmittedRegularInputs => InputWagons.Length > 0;

        private static string ResolveDefaultExtensionMethodName(bool isServiceStation, bool _)
        {
            return isServiceStation ? "ServiceStation" : "Station";
        }
    }
}
