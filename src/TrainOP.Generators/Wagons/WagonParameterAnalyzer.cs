using Microsoft.CodeAnalysis;

namespace TrainOP.Generators
{
    /// <summary>
    /// Analyzes handler parameter symbols for wagon binding metadata.
    /// </summary>
    internal static class WagonParameterAnalyzer
    {
        /// <summary>
        /// Determines whether the parameter is passed by reference.
        /// </summary>
        public static bool IsByReference(IParameterSymbol parameter)
        {
            return parameter.RefKind == RefKind.Ref;
        }

        /// <summary>
        /// Determines whether the parameter is an optional nullable value type and extracts its underlying type.
        /// </summary>
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

        /// <summary>
        /// Returns the manifest pull type display string for a wagon parameter.
        /// </summary>
        public static string GetPullTypeDisplay(ITypeSymbol parameterType, ITypeSymbol underlyingType, bool isOptional)
        {
            if (isOptional && underlyingType != null)
            {
                return ManifestWagonTypes.ToManifestTypeDisplay(underlyingType);
            }

            return ManifestWagonTypes.ToManifestTypeDisplay(parameterType);
        }

        /// <summary>
        /// Returns the effective type symbol used for wagon compatibility checks.
        /// </summary>
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
