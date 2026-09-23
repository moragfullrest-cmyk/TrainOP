using Microsoft.CodeAnalysis;
using System;
using TrainOP.Generators.Handlers;
namespace TrainOP.Generators
{
    /// <summary>
    /// Classifies handler parameter and return types that are framework slots rather than wagons.
    /// </summary>
    internal static class FrameworkParameterSchemaClassifier
    {
        /// <summary>
        /// Determines whether <paramref name="typeSymbol"/> is a recognized framework parameter type.
        /// </summary>
        public static bool TryClassify(ITypeSymbol typeSymbol, out HandlerInputKind kind)
        {
            kind = HandlerInputKind.Wagon;
            if (typeSymbol == null)
            {
                return false;
            }

            if (IsCancellationToken(typeSymbol))
            {
                kind = HandlerInputKind.CancellationToken;
                return true;
            }

            if (IsCargoManifest(typeSymbol))
            {
                kind = HandlerInputKind.CargoManifest;
                return true;
            }

            if (IsRedSignal(typeSymbol))
            {
                kind = HandlerInputKind.RedSignal;
                return true;
            }

            if (IsSignalIssue(typeSymbol))
            {
                kind = HandlerInputKind.SignalIssue;
                return true;
            }

            if (IsSignalIssues(typeSymbol))
            {
                kind = HandlerInputKind.SignalIssues;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the type is CargoManifest.
        /// </summary>
        public static bool IsCargoManifest(ITypeSymbol typeSymbol)
        {
            return typeSymbol != null
                && ReturnTypeDisplayHelper.EqualsTypeName(
                    typeSymbol.ToDisplayString(),
                    ReturnTypeDisplayHelper.CargoManifestTypeName);
        }

        /// <summary>
        /// Determines whether the type is CancellationToken.
        /// </summary>
        public static bool IsCancellationToken(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (string.Equals(typeSymbol.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal)
                || string.Equals(typeSymbol.ToDisplayString(), "CancellationToken", StringComparison.Ordinal))
            {
                return true;
            }

            return string.Equals(
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                "global::System.Threading.CancellationToken",
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether the type is RedSignal.
        /// </summary>
        public static bool IsRedSignal(ITypeSymbol typeSymbol)
        {
            return typeSymbol != null
                && ReturnTypeDisplayHelper.EqualsTypeName(
                    typeSymbol.ToDisplayString(),
                    ReturnTypeDisplayHelper.RedSignalTypeName);
        }

        /// <summary>
        /// Determines whether the type is SignalIssue.
        /// </summary>
        public static bool IsSignalIssue(ITypeSymbol typeSymbol)
        {
            return typeSymbol != null
                && ReturnTypeDisplayHelper.EqualsTypeName(
                    typeSymbol.ToDisplayString(),
                    ReturnTypeDisplayHelper.SignalIssueTypeName);
        }

        /// <summary>
        /// Determines whether the type is <c>IReadOnlyList&lt;SignalIssue&gt;</c>.
        /// </summary>
        public static bool IsSignalIssues(ITypeSymbol typeSymbol)
        {
            if (!(typeSymbol is INamedTypeSymbol named)
                || !named.IsGenericType
                || named.TypeArguments.Length != 1
                || !string.Equals(named.Name, "IReadOnlyList", StringComparison.Ordinal))
            {
                return false;
            }

            var ns = named.ContainingNamespace?.ToDisplayString();
            if (!string.Equals(ns, "System.Collections.Generic", StringComparison.Ordinal))
            {
                return false;
            }

            return IsSignalIssue(named.TypeArguments[0]);
        }
    }
}
