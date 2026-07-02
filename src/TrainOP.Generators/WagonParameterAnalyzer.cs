using Microsoft.CodeAnalysis;

namespace TrainOP.Generators
{
    internal static class WagonParameterAnalyzer
    {
        public static bool IsByReference(IParameterSymbol parameter)
        {
            return parameter.RefKind == RefKind.Ref;
        }

        public static bool IsOptionalNullableValueType(ITypeSymbol parameterType, out ITypeSymbol underlyingType)
        {
            underlyingType = null;
            if (parameterType == null)
            {
                return false;
            }

            if (parameterType is INamedTypeSymbol named
                && named.OriginalDefinition != null
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
            {
                underlyingType = named.TypeArguments[0];
                return true;
            }

            return false;
        }

        public static string GetPullTypeDisplay(ITypeSymbol parameterType, ITypeSymbol underlyingType, bool isOptional)
        {
            if (isOptional && underlyingType != null)
            {
                return underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            return parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        public static ITypeSymbol GetEffectiveTypeSymbol(ITypeSymbol parameterType, ITypeSymbol underlyingType, bool isOptional)
        {
            if (isOptional && underlyingType != null)
            {
                return underlyingType;
            }

            return parameterType;
        }
    }
}
