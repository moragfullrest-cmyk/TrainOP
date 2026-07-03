using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    internal static class TerminalWagonSchemaValidator
    {
        public static bool IsValid(TerminalWagonSchema schema)
        {
            if (schema == null || schema.Wagons.IsDefaultOrEmpty)
            {
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var wagon in schema.Wagons)
            {
                if (string.IsNullOrWhiteSpace(wagon.Name) || !names.Add(wagon.Name))
                {
                    return false;
                }

                if (!ManifestWagonTypes.IsSupported(wagon.TypeSymbol))
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal static class ManifestWagonTypes
    {
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
