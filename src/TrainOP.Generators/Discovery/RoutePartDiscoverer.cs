using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Parts;

namespace TrainOP.Generators
{
    /// <summary>
    /// Discovers route parts from syntax for generators and analyzers.
    /// </summary>
    internal static class RoutePartDiscoverer
    {
        /// <summary>
        /// Syntactic predicate for station and service-station handler call sites.
        /// </summary>
        public static bool IsCandidateStationSite(SyntaxNode node)
        {
            return StationSyntaxHelper.IsCandidateRouteHandlerInvocation(node);
        }

        /// <summary>
        /// Syntactic predicate for route chain anchor candidates.
        /// </summary>
        public static bool IsCandidateAnchorSite(SyntaxNode node)
        {
            if (node is ObjectCreationExpressionSyntax)
            {
                return true;
            }

            if (node is IdentifierNameSyntax)
            {
                return true;
            }

            return node is InvocationExpressionSyntax;
        }

        /// <summary>
        /// Resolves a station or service-station link from incremental generator context.
        /// </summary>
        public static IRoutePart TryDiscoverStation(GeneratorSyntaxContext context)
        {
            if (context.Node is not InvocationExpressionSyntax invocation)
            {
                return null;
            }

            return TryDiscoverStation(invocation, context.SemanticModel);
        }

        /// <summary>
        /// Resolves a chain-origin part from incremental generator context.
        /// </summary>
        public static IRoutePart TryDiscoverAnchor(GeneratorSyntaxContext context)
        {
            return TryDiscoverAnchor(context.Node, context.SemanticModel);
        }

        /// <summary>
        /// Collects station links and origin parts from a compilation for analyzer-wide graph assembly.
        /// </summary>
        public static ImmutableArray<IRoutePart> CollectAll(Compilation compilation)
        {
            if (compilation == null)
            {
                return ImmutableArray<IRoutePart>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<IRoutePart>();
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (IsGeneratedFile(syntaxTree.FilePath))
                {
                    continue;
                }

                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                foreach (var node in syntaxTree.GetRoot().DescendantNodes())
                {
                    if (node is InvocationExpressionSyntax invocation
                        && StationSyntaxHelper.IsCandidateRouteHandlerInvocation(node))
                    {
                        var station = TryDiscoverStation(invocation, semanticModel);
                        if (station != null)
                        {
                            builder.Add(station);
                        }
                    }
                    else if (IsCandidateAnchorSite(node))
                    {
                        var anchor = TryDiscoverAnchor(node, semanticModel);
                        if (anchor != null)
                        {
                            builder.Add(anchor);
                        }
                    }
                }
            }

            return builder.ToImmutable();
        }

        /// <summary>
        /// Merges station-link and origin-part arrays from incremental generator providers.
        /// </summary>
        public static ImmutableArray<IRoutePart> MergeParts(
            ImmutableArray<IRoutePart> stationParts,
            ImmutableArray<IRoutePart> anchorParts)
        {
            if (stationParts.IsDefaultOrEmpty && anchorParts.IsDefaultOrEmpty)
            {
                return ImmutableArray<IRoutePart>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<IRoutePart>();
            AppendNonNull(builder, stationParts);
            AppendNonNull(builder, anchorParts);
            return builder.ToImmutable();
        }

        private static void AppendNonNull(
            ImmutableArray<IRoutePart>.Builder builder,
            ImmutableArray<IRoutePart> parts)
        {
            if (parts.IsDefaultOrEmpty)
            {
                return;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part != null)
                {
                    builder.Add(part);
                }
            }
        }

        private static IRoutePart TryDiscoverStation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel)
        {
            return StationLinkMaterializer.TryMaterialize(invocation, semanticModel, out var link)
                ? link
                : null;
        }

        private static IRoutePart TryDiscoverAnchor(SyntaxNode node, SemanticModel semanticModel)
        {
            if (node is ObjectCreationExpressionSyntax objectCreation
                && !StationSyntaxHelper.IsRouteHandlerReceiver(objectCreation))
            {
                return null;
            }

            if (!AnchorStage.TryResolvePart(node, semanticModel, out var part)
                || !RouteOriginPorts.TryGetRoot(part, out _))
            {
                return null;
            }

            return part;
        }

        private static bool IsGeneratedFile(string filePath)
        {
            return !string.IsNullOrEmpty(filePath)
                && filePath.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
