using Microsoft.CodeAnalysis;
using TrainOP.Generators.Parts;
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

            // Factory origins (invocation or identifier-rooted): ports on FactoryCall — no kind-switch,
            // and never guess call-site location when dispatch metadata is missing.
            if (FactoryCall.TryBuildCallerChainKeyFromAnchor(anchor, compilation, out var factoryKey))
            {
                return factoryKey;
            }

            if (anchor.FactoryMethod != null)
            {
                // Factory stamp present but unresolvable (e.g. schema without CallerChainKey).
                return string.Empty;
            }

            // CreationSeed / LocalBinding (non-factory): ctor / origin stamp location.
            var memberName = anchor.ContainingMethod?.Name;
            if (string.IsNullOrEmpty(memberName))
            {
                memberName = "global";
            }

            return BuildFromLocation(anchor.Location, memberName);
        }

        /// <summary>
        /// Builds a chain key from a materialized origin part (no legacy kind-switch).
        /// </summary>
        public static string Build(IRoutePart part, Compilation compilation = null)
        {
            if (part == null)
            {
                return string.Empty;
            }

            var factory = part as FactoryCall
                ?? (part as LocalBinding)?.Origin as FactoryCall;
            if (factory != null)
            {
                return factory.TryBuildCallerChainKey(compilation, out var factoryKey)
                    ? factoryKey
                    : string.Empty;
            }

            if (part is LocalBinding localBinding && localBinding.FactoryMethod != null)
            {
                return FactoryDispatchMetadata.TryResolve(
                        localBinding.FactoryMethod,
                        compilation,
                        out var stampedKey,
                        out _)
                    && !string.IsNullOrEmpty(stampedKey)
                        ? stampedKey
                        : string.Empty;
            }

            if (!LegacyRoutePartAdapter.TryToLegacyAnchor(part, out var anchor))
            {
                return string.Empty;
            }

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
