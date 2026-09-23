using Microsoft.CodeAnalysis;
using System;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds C# return type display strings for generated station handler Func and Action overloads.
    /// </summary>
    internal static class ReturnTypeDisplayHelper
    {
        public const string SignalReturnTypeDisplay = "global::TrainOP.Signal";
        public const string RedFailureReturnTypeDisplay = "global::TrainOP.RedFailure";
        public const string WhitePassReturnTypeDisplay = "global::TrainOP.WhitePass";
        public const string CargoManifestReturnTypeDisplay = "global::TrainOP.CargoManifest";
        public const string RedSignalReturnTypeDisplay = "global::TrainOP.RedSignal";
        public const string SignalIssueReturnTypeDisplay = "global::TrainOP.SignalIssue";
        public const string SignalIssuesListReturnTypeDisplay =
            "global::System.Collections.Generic.IReadOnlyList<global::TrainOP.SignalIssue>";

        public const string TrainRouteTypeName = "TrainOP.TrainRoute";
        public const string SignalTypeName = "TrainOP.Signal";
        public const string GreenSignalTypeName = "TrainOP.GreenSignal";
        public const string RedSignalTypeName = "TrainOP.RedSignal";
        public const string RedFailureTypeName = "TrainOP.RedFailure";
        public const string WhitePassTypeName = "TrainOP.WhitePass";
        public const string CargoManifestTypeName = "TrainOP.CargoManifest";
        public const string SignalIssueTypeName = "TrainOP.SignalIssue";

        /// <summary>
        /// Determines whether the return type must be expressed as <c>object</c>
        /// (anonymous types and constructed types that mention them cannot appear in generated C#).
        /// </summary>
        public static bool UseGenericReturn(ITypeSymbol returnType)
        {
            return ContainsAnonymousType(returnType);
        }

        /// <summary>
        /// Determines whether a handler returns a data-oriented signal wrapper (RailwaySignals DSL).
        /// </summary>
        public static bool IsExplicitSignalReturn(ITypeSymbol returnType)
        {
            return IsRedFailure(returnType) || IsWhitePass(returnType);
        }

        /// <summary>
        /// Determines whether a handler returns a runtime route signal (GreenSignal or RedSignal).
        /// </summary>
        public static bool IsRuntimeSignalReturn(ITypeSymbol returnType)
        {
            if (returnType == null || IsExplicitSignalReturn(returnType))
            {
                return false;
            }

            var display = returnType.ToDisplayString();
            return string.Equals(display, GreenSignalTypeName, StringComparison.Ordinal)
                || string.Equals(display, RedSignalTypeName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether the return type is the abstract route Signal base (delegate annotation).
        /// </summary>
        public static bool IsSignalBaseReturn(ITypeSymbol returnType)
        {
            return returnType != null
                && string.Equals(returnType.ToDisplayString(), SignalTypeName, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when <paramref name="display"/> matches a known TrainOP type display string.
        /// </summary>
        public static bool EqualsTypeName(string display, string typeName)
        {
            return string.Equals(display, typeName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds a fully qualified return type display string for generated delegate signatures.
        /// </summary>
        public static string BuildDisplay(ITypeSymbol returnType)
        {
            if (IsExplicitSignalReturn(returnType))
            {
                return SignalReturnTypeDisplay;
            }

            if (returnType == null
                || returnType.SpecialType == SpecialType.System_Object
                || ContainsAnonymousType(returnType))
            {
                return "global::System.Object";
            }

            return ManifestWagonTypes.ToReturnTypeDisplay(returnType);
        }

        /// <summary>
        /// Returns true when <paramref name="typeSymbol"/> is anonymous or nests an anonymous type argument.
        /// </summary>
        private static bool ContainsAnonymousType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (typeSymbol.IsAnonymousType)
            {
                return true;
            }

            if (typeSymbol is INamedTypeSymbol named && named.TypeArguments.Length > 0)
            {
                for (var i = 0; i < named.TypeArguments.Length; i++)
                {
                    if (ContainsAnonymousType(named.TypeArguments[i]))
                    {
                        return true;
                    }
                }
            }

            if (typeSymbol is IArrayTypeSymbol array)
            {
                return ContainsAnonymousType(array.ElementType);
            }

            return false;
        }

        private static bool IsRedFailure(ITypeSymbol typeSymbol)
        {
            return EqualsTypeName(typeSymbol?.ToDisplayString(), RedFailureTypeName);
        }

        private static bool IsWhitePass(ITypeSymbol typeSymbol)
        {
            return EqualsTypeName(typeSymbol?.ToDisplayString(), WhitePassTypeName);
        }
    }
}
