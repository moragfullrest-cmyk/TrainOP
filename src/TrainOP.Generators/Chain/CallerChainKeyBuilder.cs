using Microsoft.CodeAnalysis;
using TrainOP.Generators.Parts;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds stable chain keys compatible with runtime <c>[CallerFilePath]</c>/<c>[CallerLineNumber]</c>/<c>[CallerMemberName]</c>.
    /// </summary>
    internal static class CallerChainKeyBuilder
    {
        /// <summary>
        /// Builds a chain key from a materialized origin part.
        /// </summary>
        public static string Build(IRoutePart part, Compilation compilation = null)
        {
            if (part == null)
            {
                return string.Empty;
            }

            if (FactoryCall.TryBuildCallerChainKeyFromOrigin(part, compilation, out var factoryKey))
            {
                return factoryKey;
            }

            if (RouteOriginPorts.GetFactoryMethod(part) != null)
            {
                // Factory stamp present but unresolvable (e.g. schema without CallerChainKey).
                return string.Empty;
            }

            // CreationSeed / LocalBinding (non-factory) / JoinSeed: origin stamp location.
            var memberName = RouteOriginPorts.GetContainingMethod(part)?.Name;
            if (string.IsNullOrEmpty(memberName))
            {
                memberName = "global";
            }

            return BuildFromLocation(part.Location, memberName);
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
