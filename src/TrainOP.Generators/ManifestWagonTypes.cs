using Microsoft.CodeAnalysis;
using System;

namespace TrainOP.Generators
{
    /// <summary>
    /// Maps wagon type symbols to manifest-safe display strings and support checks.
    /// </summary>
    internal static class ManifestWagonTypes
    {
        /// <summary>
        /// Determines whether a type symbol is supported as a manifest wagon type.
        /// </summary>
        public static bool IsSupported(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (typeSymbol.TypeKind == TypeKind.Error)
            {
                return false;
            }

            if (typeSymbol.SpecialType == SpecialType.System_Object)
            {
                return false;
            }

            if (typeSymbol.IsAnonymousType)
            {
                return false;
            }

            if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
            {
                if (namedTypeSymbol.IsTupleType)
                {
                    return false;
                }

                var typeName = namedTypeSymbol.Name ?? string.Empty;
                if (string.Equals(typeName, "GreenPayload", StringComparison.Ordinal)
                    || string.Equals(typeName, "RedFailure", StringComparison.Ordinal)
                    || string.Equals(typeName, "GreenPass", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            var display = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (display.IndexOf("anonymous", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }

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
