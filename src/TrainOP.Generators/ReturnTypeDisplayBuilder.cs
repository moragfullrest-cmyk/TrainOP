using Microsoft.CodeAnalysis;
using System;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds C# return type display strings for generated station handler Func and Action overloads.
    /// </summary>
    internal static class ReturnTypeDisplayBuilder
    {
        public const string SignalReturnTypeDisplay = "global::TrainOP.Signal";

        /// <summary>
        /// Determines whether the return type must be expressed as a generic type parameter.
        /// </summary>
        public static bool UseGenericReturn(ITypeSymbol returnType)
        {
            return returnType != null && returnType.IsAnonymousType;
        }

        /// <summary>
        /// Determines whether a handler returns a data-oriented signal wrapper (RailwaySignals DSL).
        /// </summary>
        public static bool IsExplicitSignalReturn(ITypeSymbol returnType)
        {
            return IsRedFailure(returnType) || IsGreenPass(returnType);
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
            return string.Equals(display, "TrainOP.GreenSignal", StringComparison.Ordinal)
                || string.Equals(display, "TrainOP.RedSignal", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether the return type is the abstract route Signal base (delegate annotation).
        /// </summary>
        public static bool IsSignalBaseReturn(ITypeSymbol returnType)
        {
            return returnType != null
                && string.Equals(returnType.ToDisplayString(), "TrainOP.Signal", StringComparison.Ordinal);
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

            if (returnType == null || returnType.SpecialType == SpecialType.System_Object)
            {
                return "global::System.Object";
            }

            if (returnType.IsAnonymousType)
            {
                return null;
            }

            return ManifestWagonTypes.ToReturnTypeDisplay(returnType);
        }

        private static bool IsRedFailure(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol?.ToDisplayString(), "TrainOP.RedFailure", StringComparison.Ordinal);
        }

        private static bool IsGreenPass(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol?.ToDisplayString(), "TrainOP.GreenPass", StringComparison.Ordinal);
        }
    }
}
