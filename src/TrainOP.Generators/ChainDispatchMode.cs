using Microsoft.CodeAnalysis.Diagnostics;
using System;

namespace TrainOP.Generators
{
    /// <summary>
    /// Compile-time chain-dispatch strategy selected by MSBuild / analyzer config.
    /// </summary>
    internal enum ChainDispatchMode
    {
        /// <summary>
        /// Resolve wagon names via ParameterInfo at registration time.
        /// </summary>
        Reflection = 0,

        /// <summary>
        /// Caller identity + chainStationIndex dispatch (no Roslyn interceptors).
        /// </summary>
        Caller = 1,
    }

    /// <summary>
    /// Reads chain-dispatch mode from analyzer config, aligned with MSBuild interceptor opt-in.
    /// </summary>
    internal static class ChainDispatchModeReader
    {
        public const string BuildPropertyKey = "build_property.TrainOP_ChainDispatchMode";
        public const string InterceptorsNamespacesKey = "build_property.InterceptorsNamespaces";
        private const string GeneratedNamespace = "TrainOP.Generated";

        /// <summary>
        /// Resolves effective mode. Prefer explicit <c>TrainOP_ChainDispatchMode</c>;
        /// if unset, infer from whether <c>InterceptorsNamespaces</c> includes TrainOP.Generated
        /// (avoids CS9137 when MSBuild disabled interceptors but the property did not flow).
        /// </summary>
        public static ChainDispatchMode Read(AnalyzerConfigOptionsProvider optionsProvider)
        {
            if (optionsProvider != null
                && TryGetBuildProperty(optionsProvider, "TrainOP_ChainDispatchMode", out var raw)
                && !string.IsNullOrWhiteSpace(raw))
            {
                if (string.Equals(raw, "reflection", StringComparison.OrdinalIgnoreCase))
                {
                    return ChainDispatchMode.Reflection;
                }

                if (string.Equals(raw, "caller", StringComparison.OrdinalIgnoreCase))
                {
                    return ChainDispatchMode.Caller;
                }

                // Deprecated aliases.
                if (string.Equals(raw, "experimental", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "stable", StringComparison.OrdinalIgnoreCase))
                {
                    return ChainDispatchMode.Caller;
                }
            }

            // Safe default for the full caller replacement.
            return ChainDispatchMode.Caller;
        }

        /// <summary>
        /// Returns true when interceptors should be emitted.
        /// </summary>
        public static bool UsesInterceptors(ChainDispatchMode mode)
        {
            // Interceptors are removed in caller replacement.
            return false;
        }

        private static bool TryGetBuildProperty(
            AnalyzerConfigOptionsProvider optionsProvider,
            string propertyName,
            out string value)
        {
            var key = "build_property." + propertyName;
            if (optionsProvider.GlobalOptions.TryGetValue(key, out value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            // MSBuild editorconfig keys may be lowercased depending on SDK version.
            return optionsProvider.GlobalOptions.TryGetValue(key.ToLowerInvariant(), out value)
                && !string.IsNullOrWhiteSpace(value);
        }
    }
}
