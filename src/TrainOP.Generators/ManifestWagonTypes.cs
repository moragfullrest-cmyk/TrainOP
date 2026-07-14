using Microsoft.CodeAnalysis;

namespace TrainOP.Generators
{
    /// <summary>
    /// Maps wagon type symbols to manifest-safe display strings.
    /// </summary>
    internal static class ManifestWagonTypes
    {
        /// <summary>
        /// Converts a type symbol to a fully qualified display string for generated manifest code.
        /// </summary>
        public static string ToManifestTypeDisplay(ITypeSymbol typeSymbol)
        {
            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_String:
                    return "global::System.String";
                case SpecialType.System_Decimal:
                    return "global::System.Decimal";
                case SpecialType.System_Int32:
                    return "global::System.Int32";
                case SpecialType.System_Int64:
                    return "global::System.Int64";
                case SpecialType.System_Boolean:
                    return "global::System.Boolean";
                default:
                    return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        /// <summary>
        /// Converts a handler return type to a display string for generated delegate signatures.
        /// </summary>
        public static string ToReturnTypeDisplay(ITypeSymbol typeSymbol)
        {
            return ToManifestTypeDisplay(typeSymbol);
        }

        /// <summary>
        /// Converts a handler parameter type to a display string for generated delegate signatures.
        /// </summary>
        public static string ToWagonParameterTypeDisplay(ITypeSymbol parameterType, ITypeSymbol underlyingType, bool isOptional)
        {
            if (isOptional && underlyingType != null)
            {
                return ToManifestTypeDisplay(underlyingType) + "?";
            }

            return ToManifestTypeDisplay(parameterType);
        }
    }
}
