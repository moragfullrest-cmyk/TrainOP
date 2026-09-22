using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Materializes a <see cref="CreationSeed"/> from <c>new TrainRoute()</c> syntax.
    /// </summary>
    internal static class CreationSeedMaterializer
    {
        /// <summary>
        /// Attempts to materialize a creation seed at <paramref name="objectCreation"/>.
        /// </summary>
        public static bool TryMaterialize(
            ObjectCreationExpressionSyntax objectCreation,
            SemanticModel semanticModel,
            out CreationSeed seed)
        {
            seed = null;
            if (objectCreation == null || semanticModel == null)
            {
                return false;
            }

            if (!StationSyntaxHelper.IsTrainRouteCreation(objectCreation, semanticModel))
            {
                return false;
            }

            seed = new CreationSeed(
                objectCreation,
                objectCreation.GetLocation(),
                GetContainingMethod(objectCreation, semanticModel));
            return true;
        }

        /// <summary>
        /// Attempts to materialize when <paramref name="node"/> is an object-creation expression.
        /// </summary>
        public static bool TryMaterialize(
            SyntaxNode node,
            SemanticModel semanticModel,
            out CreationSeed seed)
        {
            seed = null;
            return node is ObjectCreationExpressionSyntax objectCreation
                && TryMaterialize(objectCreation, semanticModel, out seed);
        }

        private static IMethodSymbol GetContainingMethod(SyntaxNode node, SemanticModel semanticModel)
        {
            var methodDeclaration = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration == null)
            {
                return null;
            }

            return semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        }
    }
}
