using System.Collections.Immutable;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Combined handler schema: <see cref="Input"/> parameters + <see cref="Output"/> return shape.
    /// Start here when reading how a data-oriented Station / ServiceStation handler is described.
    /// </summary>
    internal sealed class StationHandlerBinding
    {
        /// <summary>
        /// Creates a handler binding from explicit input/output components.
        /// </summary>
        public StationHandlerBinding(
            HandlerInputParameters input,
            HandlerOutputParameters output,
            bool isAsync)
        {
            Input = input ?? throw new System.ArgumentNullException(nameof(input));
            Output = output ?? throw new System.ArgumentNullException(nameof(output));
            IsAsync = isAsync;
            ExtensionMethodName = ResolveDefaultExtensionMethodName(input.IsServiceStation);
        }

        /// <summary>Handler inputs (wagons + framework slots) and call order.</summary>
        public HandlerInputParameters Input { get; }

        /// <summary>Handler output mode, members, and return shape.</summary>
        public HandlerOutputParameters Output { get; }

        public ImmutableArray<WagonBinding> InputWagons => Input.Wagons;

        public ImmutableArray<WagonBinding> Wagons => Input.Wagons;

        public bool IncludeManifest => Input.IncludeManifest;

        public bool IsAsync { get; }

        public bool HasCancellationToken => Input.HasCancellationToken;

        public bool HasRefWagons => Input.HasRefWagons;

        public ReturnShape ReturnShape => Output.Shape;

        public bool IsServiceStation => Input.IsServiceStation;

        public bool IncludeRedSignal => Input.IncludeRedSignal;

        public bool IncludeSignalIssue => Input.IncludeSignalIssue;

        public string ExtensionMethodName { get; }

        public bool RemoveOmittedRegularInputs => Input.Wagons.Length > 0;

        private static string ResolveDefaultExtensionMethodName(bool isServiceStation)
        {
            return isServiceStation ? "ServiceStation" : "Station";
        }
    }
}
