using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Route;
namespace TrainOP.Generators
{
    /// <summary>
    /// Discovers <see cref="RouteSite"/> nodes from syntax for generators and analyzers.
    /// </summary>
    internal static class RouteSiteDiscoverer
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
        /// Resolves a station or service-station call site from incremental generator context.
        /// </summary>
        public static RouteSite TryDiscoverStation(GeneratorSyntaxContext context)
        {
            if (context.Node is not InvocationExpressionSyntax invocation)
            {
                return null;
            }

            return TryDiscoverStation(invocation, context.SemanticModel);
        }

        /// <summary>
        /// Resolves a chain anchor from incremental generator context.
        /// </summary>
        public static RouteSite TryDiscoverAnchor(GeneratorSyntaxContext context)
        {
            return TryDiscoverAnchor(context.Node, context.SemanticModel);
        }

        /// <summary>
        /// Collects all route sites from a compilation for analyzer-wide graph assembly.
        /// </summary>
        public static ImmutableArray<RouteSite> CollectAll(Compilation compilation)
        {
            if (compilation == null)
            {
                return ImmutableArray<RouteSite>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<RouteSite>();
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
        /// Merges station and anchor site arrays from incremental generator providers.
        /// </summary>
        public static ImmutableArray<RouteSite> MergeSites(
            ImmutableArray<RouteSite> stationSites,
            ImmutableArray<RouteSite> anchorSites)
        {
            if (stationSites.IsDefaultOrEmpty && anchorSites.IsDefaultOrEmpty)
            {
                return ImmutableArray<RouteSite>.Empty;
            }

            if (stationSites.IsDefaultOrEmpty)
            {
                return anchorSites;
            }

            if (anchorSites.IsDefaultOrEmpty)
            {
                return stationSites;
            }

            var builder = ImmutableArray.CreateBuilder<RouteSite>(stationSites.Length + anchorSites.Length);
            builder.AddRange(stationSites);
            builder.AddRange(anchorSites);
            return builder.ToImmutable();
        }

        private static RouteSite TryDiscoverStation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel)
        {
            if (!StationSyntaxHelper.TryParseRouteHandlerInvocation(
                    invocation,
                    out var stationKind,
                    out var memberAccess))
            {
                return null;
            }

            var result = HandlerSchemaResolver.ResolveParsedInvocation(
                invocation,
                semanticModel,
                stationKind,
                memberAccess);
            if (!result.IsSuccess)
            {
                return null;
            }

            var routeSiteKind = stationKind == HandlerStationKind.ServiceStation
                ? RouteSiteKind.ServiceStation
                : RouteSiteKind.Station;

            return RouteSite.CreateStation(
                routeSiteKind,
                invocation,
                memberAccess.Expression,
                result.StationName,
                result.Schema,
                result.HandlerLocation);
        }

        private static RouteSite TryDiscoverAnchor(SyntaxNode node, SemanticModel semanticModel)
        {
            if (node is ObjectCreationExpressionSyntax objectCreation
                && !IsObjectCreationChainReceiver(objectCreation))
            {
                return null;
            }

            if (!RouteChainWalker.TryDetectAnchorSite(node, semanticModel, out var anchor))
            {
                return null;
            }

            return RouteSite.CreateAnchor(anchor);
        }

        private static bool IsObjectCreationChainReceiver(ObjectCreationExpressionSyntax objectCreation)
        {
            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(objectCreation);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
            {
                return false;
            }

            return StationSyntaxHelper.IsStationOrServiceStationMethodName(
                memberAccess.Name.Identifier.ValueText);
        }

        private static bool IsGeneratedFile(string filePath)
        {
            return !string.IsNullOrEmpty(filePath)
                && filePath.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
