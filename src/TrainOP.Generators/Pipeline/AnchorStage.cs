using Microsoft.CodeAnalysis;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Resolves chain origins (<c>new</c> / local / factory /
    /// join-assign / external schema import) into parts and <see cref="RouteSite"/>.
    /// </summary>
    internal static class AnchorStage
    {
        /// <summary>
        /// Attempts to materialize a first-class route part at the given syntax node.
        /// </summary>
        internal static bool TryResolvePart(
            SyntaxNode node,
            SemanticModel semanticModel,
            out IRoutePart part)
        {
            part = null;
            if (node == null || semanticModel == null)
            {
                return false;
            }

            return RouteAnchorDetector.TryDetect(node, semanticModel, out part);
        }

        /// <summary>
        /// Resolves an origin <see cref="RouteSite"/> via parts → <see cref="RouteSite.CreateAnchor"/>.
        /// </summary>
        internal static bool TryResolveSite(
            SyntaxNode node,
            SemanticModel semanticModel,
            out RouteSite site)
        {
            site = null;
            if (!TryResolvePart(node, semanticModel, out var part))
            {
                return false;
            }

            site = RouteSite.CreateAnchor(part);
            return site != null;
        }
    }
}
