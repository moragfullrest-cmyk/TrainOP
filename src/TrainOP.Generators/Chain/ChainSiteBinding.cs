using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators.Chain
{
    /// <summary>
    /// Describes wagon metadata for one station call site within a route chain.
    /// </summary>
    internal sealed class ChainSiteBinding
    {
        /// <summary>
        /// Creates a chain-site binding record.
        /// </summary>
        public ChainSiteBinding(
            string chainId,
            int stationIndex,
            string stationName,
            InvocationExpressionSyntax invocation,
            StationHandlerBinding schema)
        {
            ChainId = chainId;
            StationIndex = stationIndex;
            StationName = stationName;
            Invocation = invocation;
            InvocationLocation = invocation?.GetLocation();
            Schema = schema;
        }

        public string ChainId { get; }

        public int StationIndex { get; }

        public string StationName { get; }

        public InvocationExpressionSyntax Invocation { get; }

        public Location InvocationLocation { get; }

        public StationHandlerBinding Schema { get; }

        /// <summary>Return member names projected from <see cref="Schema"/> output shape.</summary>
        public string[] ReturnMembers => Schema?.Output?.ReturnMemberNames ?? System.Array.Empty<string>();
    }
}
