using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrainOP.Generators
{
    /// <summary>
    /// Metadata helpers for handler wagon parameter symbols.
    /// </summary>
    internal static class WagonParameterMetadata
    {
        /// <summary>
        /// Determines whether the parameter is passed by reference.
        /// </summary>
        public static bool IsByReference(IParameterSymbol parameter)
        {
            return parameter.RefKind == RefKind.Ref;
        }

        /// <summary>
        /// Determines whether the parameter is an <c>out</c> wagon.
        /// </summary>
        public static bool IsOut(IParameterSymbol parameter)
        {
            return parameter.RefKind == RefKind.Out;
        }

        /// <summary>
        /// Determines whether the parameter is a <c>ref readonly</c> wagon.
        /// </summary>
        public static bool IsRefReadonly(IParameterSymbol parameter)
        {
            return parameter.RefKind == RefKind.RefReadOnlyParameter;
        }

        /// <summary>
        /// Determines whether the parameter is an <c>in</c> wagon.
        /// </summary>
        public static bool IsIn(IParameterSymbol parameter)
        {
            return parameter.RefKind == RefKind.In;
        }

        /// <summary>
        /// Determines whether the parameter is a <c>params</c> collection wagon.
        /// </summary>
        public static bool IsParams(IParameterSymbol parameter)
        {
            if (parameter == null)
            {
                return false;
            }

            if (parameter.IsParams || SyntaxHasParamsModifier(parameter.DeclaringSyntaxReferences, parameter.Name))
            {
                return true;
            }

            return parameter.ContainingSymbol != null
                && SyntaxHasParamsModifier(parameter.ContainingSymbol.DeclaringSyntaxReferences, parameter.Name);
        }

        /// <summary>
        /// True when a <c>params</c> parameter with this name is declared in the method that contains the handler expression.
        /// Method-group symbols sometimes omit <see cref="IParameterSymbol.IsParams"/>.
        /// </summary>
        public static bool EnclosingMethodDeclaresParams(SyntaxNode handlerExpression, string parameterName)
        {
            if (handlerExpression == null || string.IsNullOrEmpty(parameterName))
            {
                return false;
            }

            for (var node = handlerExpression.Parent; node != null; node = node.Parent)
            {
                if (node is not MethodDeclarationSyntax
                    && node is not LocalFunctionStatementSyntax
                    && node is not ConstructorDeclarationSyntax)
                {
                    continue;
                }

                foreach (var descendant in node.DescendantNodes())
                {
                    if (descendant is ParameterSyntax parameterSyntax
                        && string.Equals(parameterSyntax.Identifier.ValueText, parameterName, System.StringComparison.Ordinal)
                        && parameterSyntax.Modifiers.Any(SyntaxKind.ParamsKeyword))
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }

        private static bool SyntaxHasParamsModifier(
            System.Collections.Immutable.ImmutableArray<SyntaxReference> references,
            string parameterName)
        {
            if (references.IsDefaultOrEmpty)
            {
                return false;
            }

            foreach (var reference in references)
            {
                if (HasParamsModifier(reference.GetSyntax(), parameterName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasParamsModifier(SyntaxNode node, string parameterName)
        {
            if (node is ParameterSyntax parameterSyntax)
            {
                return string.Equals(parameterSyntax.Identifier.ValueText, parameterName, System.StringComparison.Ordinal)
                    && parameterSyntax.Modifiers.Any(SyntaxKind.ParamsKeyword);
            }

            SeparatedSyntaxList<ParameterSyntax>? parameters = node switch
            {
                LocalFunctionStatementSyntax localFunction => localFunction.ParameterList?.Parameters,
                MethodDeclarationSyntax method => method.ParameterList?.Parameters,
                ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList?.Parameters,
                AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList?.Parameters,
                _ => null
            };

            if (parameters == null)
            {
                return false;
            }

            foreach (var syntaxParameter in parameters.Value)
            {
                if (string.Equals(syntaxParameter.Identifier.ValueText, parameterName, System.StringComparison.Ordinal)
                    && syntaxParameter.Modifiers.Any(SyntaxKind.ParamsKeyword))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Classifies a wagon parameter as required, optional, or a non-constant default (TOP022).
        /// </summary>
        public static WagonOption Classify(IParameterSymbol parameter, SemanticModel semanticModel = null)
        {
            if (parameter == null)
            {
                return WagonOption.Required;
            }

            var parameterType = parameter.Type;
            ITypeSymbol underlyingType = null;
            var nullableValue = IsOptionalNullableValueType(parameterType, out underlyingType);
            var annotatedReference = parameterType != null
                && parameterType.IsReferenceType
                && parameterType.NullableAnnotation == NullableAnnotation.Annotated
                && IsNullableAnnotationEnabled(parameter, semanticModel);

            var defaultExpression = FindDefaultExpression(parameter);
            if (defaultExpression != null)
            {
                if (semanticModel == null
                    || !semanticModel.GetConstantValue(defaultExpression).HasValue
                    || !TryFormatConstant(semanticModel.GetConstantValue(defaultExpression).Value, parameterType, out var syntaxFallback))
                {
                    return new WagonOption(false, true, null, underlyingType);
                }

                return new WagonOption(true, false, syntaxFallback, underlyingType);
            }

            if (parameter.HasExplicitDefaultValue)
            {
                if (!TryFormatConstant(parameter.ExplicitDefaultValue, parameterType, out var fallback))
                {
                    return new WagonOption(false, true, null, underlyingType);
                }

                return new WagonOption(true, false, fallback, underlyingType);
            }

            if (nullableValue || annotatedReference)
            {
                return new WagonOption(true, false, null, underlyingType);
            }

            return WagonOption.Required;
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

            if (isOptional
                && parameterType != null
                && parameterType.IsReferenceType
                && parameterType.NullableAnnotation == NullableAnnotation.Annotated)
            {
                return parameterType.WithNullableAnnotation(NullableAnnotation.None);
            }

            return parameterType;
        }

        /// <summary>
        /// True when nullable annotations are enabled at the parameter, so <c>string?</c> is an intentional annotation.
        /// A disabled context keeps <c>string?</c> required.
        /// </summary>
        private static bool IsNullableAnnotationEnabled(IParameterSymbol parameter, SemanticModel semanticModel)
        {
            if (semanticModel == null || parameter == null)
            {
                return false;
            }

            Location location = null;
            foreach (var candidate in parameter.Locations)
            {
                if (candidate != null && candidate.IsInSource)
                {
                    location = candidate;
                    break;
                }
            }

            if (location == null && !parameter.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            {
                location = parameter.DeclaringSyntaxReferences[0].GetSyntax().GetLocation();
            }

            if (location == null || !location.IsInSource)
            {
                return false;
            }

            var context = semanticModel.GetNullableContext(location.SourceSpan.Start);
            return (context & NullableContext.AnnotationsEnabled) != 0;
        }

        private static ExpressionSyntax FindDefaultExpression(IParameterSymbol parameter)
        {
            var references = parameter.DeclaringSyntaxReferences;
            if (references.IsDefaultOrEmpty)
            {
                return null;
            }

            foreach (var reference in references)
            {
                if (reference.GetSyntax() is ParameterSyntax syntax && syntax.Default != null)
                {
                    return syntax.Default.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Formats a compile-time default. A null expression means <c>default(parameter type)</c>.
        /// </summary>
        private static bool TryFormatConstant(object value, ITypeSymbol parameterType, out string expression)
        {
            expression = null;
            if (parameterType == null)
            {
                return false;
            }

            if (value == null)
            {
                if (parameterType.IsReferenceType || IsOptionalNullableValueType(parameterType, out _))
                {
                    expression = "null";
                }

                return parameterType.IsReferenceType || parameterType.IsValueType;
            }

            switch (value)
            {
                case bool boolean:
                    expression = boolean ? "true" : "false";
                    return true;
                case char character:
                    expression = "'\\u" + ((ushort)character).ToString("X4", CultureInfo.InvariantCulture) + "'";
                    return true;
                case string text:
                    expression = "\"" + EscapeLiteral(text) + "\"";
                    return true;
            }

            if (value is Enum)
            {
                var raw = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                expression = "(" + EnumDisplay(parameterType) + ")" + raw.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (!IsFiniteNumber(value))
            {
                return false;
            }

            string digits;
            try
            {
                digits = Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }

            if (string.IsNullOrEmpty(digits))
            {
                return false;
            }

            var target = UnwrapNullable(parameterType);
            if (target != null && target.TypeKind == TypeKind.Enum)
            {
                expression = "(" + target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" + digits;
                return true;
            }

            var special = target?.SpecialType ?? parameterType.SpecialType;
            expression = special switch
            {
                SpecialType.System_Decimal => digits + "m",
                SpecialType.System_Single => digits + "f",
                SpecialType.System_Double => digits + "d",
                SpecialType.System_Int64 => digits + "L",
                SpecialType.System_UInt32 => digits + "u",
                SpecialType.System_UInt64 => digits + "UL",
                SpecialType.System_Int32 => digits,
                SpecialType.System_Int16 => digits,
                SpecialType.System_UInt16 => digits,
                SpecialType.System_Byte => digits,
                SpecialType.System_SByte => digits,
                SpecialType.System_Boolean => digits,
                _ => null
            };

            return expression != null;
        }

        private static string EnumDisplay(ITypeSymbol parameterType)
        {
            var target = UnwrapNullable(parameterType) ?? parameterType;
            return target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol parameterType)
        {
            if (IsOptionalNullableValueType(parameterType, out var underlying))
            {
                return underlying;
            }

            return parameterType;
        }

        private static bool IsFiniteNumber(object value)
        {
            switch (value)
            {
                case float number:
                    return !float.IsNaN(number) && !float.IsInfinity(number);
                case double number:
                    return !double.IsNaN(number) && !double.IsInfinity(number);
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case decimal _:
                    return true;
                default:
                    return false;
            }
        }

        private static string EscapeLiteral(string text)
        {
            return StringHelpers.Escape(text)
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }

    /// <summary>
    /// Optional-wagon classification for one handler parameter.
    /// </summary>
    internal readonly struct WagonOption
    {
        public WagonOption(bool isOptional, bool hasNonConstantDefault, string fallbackExpression, ITypeSymbol underlyingType)
        {
            IsOptional = isOptional;
            HasNonConstantDefault = hasNonConstantDefault;
            FallbackExpression = fallbackExpression;
            UnderlyingType = underlyingType;
        }

        public static WagonOption Required { get; } = new WagonOption(false, false, null, null);

        public bool IsOptional { get; }

        public bool HasNonConstantDefault { get; }

        /// <summary>Assigned when the key is missing. Null means <c>default(parameter type)</c>.</summary>
        public string FallbackExpression { get; }

        public ITypeSymbol UnderlyingType { get; }
    }
}
