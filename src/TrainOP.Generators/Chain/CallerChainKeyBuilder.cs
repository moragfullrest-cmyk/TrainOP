using Microsoft.CodeAnalysis;
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
        public static string Build(RouteChainAnchor anchor, Compilation compilation = null)
        {
            if (anchor == null)
            {
                return string.Empty;
            }

            if (anchor.Kind == RouteChainAnchorKind.MethodInvocation
                || anchor.Kind == RouteChainAnchorKind.FactorySchema)
            {
                if (FactoryDispatchMetadata.TryResolve(
                        anchor.FactoryMethod,
                        compilation,
                        out var factoryKey,
                        out _)
                    && !string.IsNullOrEmpty(factoryKey))
                {
                    return factoryKey;
                }

                // Old schema without CallerChainKey / unresolvable factory body: do not guess method location.
                return string.Empty;
            }

            // ObjectCreation & LocalVariable: RouteChainWalker provides the ctor call-site location.
            var memberName = anchor.ContainingMethod?.Name;
            if (string.IsNullOrEmpty(memberName))
            {
                memberName = "global";
            }

            return BuildFromLocation(anchor.Location, memberName);
        }

        /// <summary>
        /// Builds a chain key from a syntax location and member name (1-based caller line).
        /// </summary>
        internal static string BuildFromLocation(Location location, string memberName)
        {
            if (location == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(memberName))
            {
                memberName = "global";
            }

            var lineSpan = location.GetLineSpan();
            var filePath = lineSpan.Path ?? string.Empty;

            // CallerLineNumber is 1-based. Roslyn LinePosition.Line is 0-based.
            var lineNumber = lineSpan.StartLinePosition.Line + 1;

            return CallerChainKeyHasher.Build(filePath, lineNumber, memberName);
        }
    }
}
