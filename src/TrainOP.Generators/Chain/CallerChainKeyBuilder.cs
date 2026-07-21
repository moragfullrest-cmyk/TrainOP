using Microsoft.CodeAnalysis;
using System.Linq;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds stable chain keys compatible with runtime <c>[CallerFilePath]</c>/<c>[CallerLineNumber]</c>/<c>[CallerMemberName]</c>.
    /// </summary>
    internal static class CallerChainKeyBuilder
    {
        /// <summary>
        /// Builds a chain key for a detected chain anchor.
        /// </summary>
        public static string Build(RouteChainAnchor anchor)
        {
            if (anchor == null)
            {
                return string.Empty;
            }

            // For ctor-stamped identity, runtime uses caller member name.
            // For factory anchors, the ctor runs inside the factory method, so we stamp by factory method metadata.
            var memberName =
                (anchor.Kind == RouteChainAnchorKind.MethodInvocation
                    || anchor.Kind == RouteChainAnchorKind.FactorySchema)
                ? anchor.FactoryMethod?.Name
                : anchor.ContainingMethod?.Name;

            if (string.IsNullOrEmpty(memberName))
            {
                memberName = "global";
            }

            // File/line are taken from the location that best approximates ctor call-site.
            // For ObjectCreation & LocalVariable anchors, RouteChainWalker already provides the location of the ctor call expression.
            // For factory anchors, we approximate with the factory method location; common expression-bodied factories keep them aligned.
            var location =
                (anchor.Kind == RouteChainAnchorKind.MethodInvocation
                    || anchor.Kind == RouteChainAnchorKind.FactorySchema)
                ? anchor.FactoryMethod?.Locations.FirstOrDefault()
                : anchor.Location;

            if (location == null)
            {
                // If we cannot read an IMethodSymbol location (e.g., metadata-only), fall back to anchor location.
                return string.Empty;
            }

            var lineSpan = location.GetLineSpan();
            var filePath = lineSpan.Path ?? string.Empty;

            // CallerLineNumber is 1-based. Roslyn LinePosition.Line is 0-based.
            var lineNumber = lineSpan.StartLinePosition.Line + 1;

            return CallerChainKeyHasher.Build(filePath, lineNumber, memberName);
        }
    }
}

