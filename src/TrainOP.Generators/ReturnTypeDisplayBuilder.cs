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
        /// Determines whether a handler returns an explicit route signal.
        /// </summary>
        public static bool IsExplicitSignalReturn(ITypeSymbol returnType)
        {
            if (returnType == null)
            {
                return false;
            }

            if (IsRedFailure(returnType) || IsGreenPass(returnType))
            {
                return true;
            }

            return DerivesFromSignal(returnType);
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

        private static bool DerivesFromSignal(ITypeSymbol typeSymbol)
        {
            for (var current = typeSymbol; current != null; current = current.BaseType)
            {
                if (string.Equals(current.ToDisplayString(), "TrainOP.Signal", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
