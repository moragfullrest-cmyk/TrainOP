using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds stable chain identifiers from detected route chain anchors.
    /// </summary>
    internal static class RouteChainIdBuilder
    {
        /// <summary>
        /// Builds a stable chain identifier for a detected route chain anchor.
        /// </summary>
        public static string Build(RouteChainAnchor anchor)
        {
            var methodFqn = BuildContainingMethodFqn(anchor.ContainingMethod);
            if (anchor.Kind == RouteChainAnchorKind.LocalVariable
                && anchor.Root is IdentifierNameSyntax identifier)
            {
                return methodFqn + "@" + identifier.Identifier.ValueText;
            }

            if (anchor.Kind == RouteChainAnchorKind.ObjectCreation
                && anchor.Root is ObjectCreationExpressionSyntax objectCreation
                && TryGetAssignedLocalVariableName(objectCreation, out var assignedVariableName))
            {
                return methodFqn + "@" + assignedVariableName;
            }

            return methodFqn;
        }

        private static bool TryGetAssignedLocalVariableName(
            ObjectCreationExpressionSyntax objectCreation,
            out string variableName)
        {
            variableName = null;
            var current = objectCreation.Parent;
            while (current != null)
            {
                if (current is EqualsValueClauseSyntax equalsValue
                    && equalsValue.Parent is VariableDeclaratorSyntax declarator)
                {
                    variableName = declarator.Identifier.ValueText;
                    return !string.IsNullOrEmpty(variableName);
                }

                current = current.Parent;
            }

            return false;
        }

        private static string BuildContainingMethodFqn(IMethodSymbol containingMethod)
        {
            if (containingMethod == null)
            {
                return "global";
            }

            return containingMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                + "."
                + containingMethod.Name;
        }
    }
}
