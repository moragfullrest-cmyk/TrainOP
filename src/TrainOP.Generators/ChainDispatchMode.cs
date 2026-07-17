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
        /// Roslyn interceptors (SDK 9.0.200+).
        /// </summary>
        Stable = 0,

        /// <summary>
        /// Roslyn interceptors with experimental Features flag (SDK 8.0.400+).
        /// </summary>
        Experimental = 1,

        /// <summary>
        /// Resolve wagon names via ParameterInfo at registration time (SDK &lt; 8.0.400).
        /// </summary>
        Reflection = 2,
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

                if (string.Equals(raw, "experimental", StringComparison.OrdinalIgnoreCase))
                {
                    return ChainDispatchMode.Experimental;
                }

                if (string.Equals(raw, "stable", StringComparison.OrdinalIgnoreCase))
                {
                    return ChainDispatchMode.Stable;
                }
            }

            if (optionsProvider != null
                && TryGetBuildProperty(optionsProvider, "InterceptorsNamespaces", out var namespaces)
                && !string.IsNullOrWhiteSpace(namespaces)
                && namespaces.IndexOf(GeneratedNamespace, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (TryGetBuildProperty(optionsProvider, "Features", out var features)
                    && !string.IsNullOrWhiteSpace(features)
                    && features.IndexOf("Interceptors", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ChainDispatchMode.Experimental;
                }

                return ChainDispatchMode.Stable;
            }

            // Safe default: do not emit interceptors without MSBuild opt-in (prevents CS9137).
            return ChainDispatchMode.Reflection;
        }

        /// <summary>
        /// Returns true when interceptors should be emitted.
        /// </summary>
        public static bool UsesInterceptors(ChainDispatchMode mode)
        {
            return mode == ChainDispatchMode.Stable || mode == ChainDispatchMode.Experimental;
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
