using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Holds handler binding metadata discovered from a single station invocation site.
    /// </summary>
    internal sealed class StationCallInfo
    {
        /// <summary>
        /// Creates a station call record with handler binding and source location.
        /// </summary>
        public StationCallInfo(
            StationHandlerBinding handlerBinding,
            Location location,
            InvocationExpressionSyntax invocation)
        {
            HandlerBinding = handlerBinding;
            Location = location;
            Invocation = invocation;
            InvocationLocation = invocation.GetLocation();
        }

        public StationHandlerBinding HandlerBinding { get; }

        public Location Location { get; }

        public InvocationExpressionSyntax Invocation { get; }

        public Location InvocationLocation { get; }
    }
}
