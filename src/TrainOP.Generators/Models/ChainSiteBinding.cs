using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrainOP.Generators.Models
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
            StationHandlerBinding schema,
            string[] returnMembers)
        {
            ChainId = chainId;
            StationIndex = stationIndex;
            StationName = stationName;
            Invocation = invocation;
            InvocationLocation = invocation?.GetLocation();
            Schema = schema;
            ReturnMembers = returnMembers ?? System.Array.Empty<string>();
        }

        public string ChainId { get; }

        public int StationIndex { get; }

        public string StationName { get; }

        public InvocationExpressionSyntax Invocation { get; }

        public Location InvocationLocation { get; }

        public StationHandlerBinding Schema { get; }

        public string[] ReturnMembers { get; }
    }
}
