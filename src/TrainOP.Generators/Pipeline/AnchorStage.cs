using Microsoft.CodeAnalysis;
using TrainOP.Generators.Parts;

namespace TrainOP.Generators
{
    /// <summary>
    /// Resolves chain origins (<c>new</c> / local / factory /
    /// join-assign / external schema import) into route parts.
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
    }
}
