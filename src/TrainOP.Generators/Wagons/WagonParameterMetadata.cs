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
