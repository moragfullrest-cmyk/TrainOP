using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Groups station handler bindings by signature without chain context.
    /// </summary>
    internal static class SignatureGroupingStage
    {
        /// <summary>
        /// Accumulates <see cref="DelegateSignatureGroup"/>s from station sites and chain-index
        /// schemas. Chain attachment is deferred to <see cref="AttachChainContextStage"/>.
        /// </summary>
        public static Dictionary<string, DelegateSignatureGroup> Group(RouteGraph graph)
        {
            var groups = new Dictionary<string, DelegateSignatureGroup>(StringComparer.Ordinal);
            if (graph == null)
            {
                return groups;
            }

            var processedInvocationKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var site in graph.StationSites
                .OrderBy(site => site.IdentityLocation.SourceSpan.Start))
            {
                AddDiscoveredCall(
                    groups,
                    processedInvocationKeys,
                    site.HandlerBinding,
                    site.HandlerLocation,
                    site.Invocation);
            }

            foreach (var chainBinding in graph.ChainIndex.Values
                .SelectMany(x => x)
                .OrderBy(binding => binding.InvocationLocation.SourceSpan.Start))
            {
                if (chainBinding.Schema == null || chainBinding.Invocation == null)
                {
                    continue;
                }

                AddDiscoveredCall(
                    groups,
                    processedInvocationKeys,
                    chainBinding.Schema,
                    chainBinding.InvocationLocation,
                    chainBinding.Invocation);
            }

            return groups;
        }

        private static void AddDiscoveredCall(
            Dictionary<string, DelegateSignatureGroup> groups,
            HashSet<string> processedInvocationKeys,
            StationHandlerBinding handlerBinding,
            Location location,
            InvocationExpressionSyntax invocation)
        {
            if (handlerBinding == null || invocation == null)
            {
                return;
            }

            var invocationLocation = invocation.GetLocation();
            var invocationKey = ChainSiteBindingLookup.BuildLocationKey(invocationLocation);
            if (invocationKey.Length == 0 || !processedInvocationKeys.Add(invocationKey))
            {
                return;
            }

            var typeSignature = DelegateTypeSignature.From(handlerBinding);
            var groupingKey = handlerBinding.BuildGroupingKey(typeSignature.TypeId);
            if (!groups.TryGetValue(groupingKey, out var group))
            {
                group = new DelegateSignatureGroup(typeSignature);
                groups[groupingKey] = group;
            }

            group.Add(handlerBinding, location, invocationLocation);
        }
    }
}
