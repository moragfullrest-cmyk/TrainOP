using Microsoft.CodeAnalysis;

namespace TrainOP.Generators
{
    /// <summary>
    /// Maps wagon type symbols to manifest-safe display strings for generated C# source.
    /// </summary>
    internal static class ManifestWagonTypes
    {
        /// <summary>
        /// Converts a type symbol to a fully qualified display string for generated manifest code.
        /// </summary>
        public static string ToManifestTypeDisplay(ITypeSymbol typeSymbol) =>
            typeSymbol.SpecialType switch
            {
                SpecialType.System_String => "global::System.String",
                SpecialType.System_Decimal => "global::System.Decimal",
                SpecialType.System_Int32 => "global::System.Int32",
                SpecialType.System_Int64 => "global::System.Int64",
                SpecialType.System_Boolean => "global::System.Boolean",
                _ => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            };

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
