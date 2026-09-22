using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Origin part: <c>new TrainRoute()</c> (legacy ObjectCreation).
    /// </summary>
    internal sealed class CreationSeed : IRoutePart
    {
        /// <summary>
        /// Creates a creation seed from a materialized <c>new TrainRoute()</c> site.
        /// </summary>
        public CreationSeed(
            ObjectCreationExpressionSyntax root,
            Location location,
            IMethodSymbol containingMethod = null)
        {
            Root = root;
            Location = location;
            ContainingMethod = containingMethod;
        }

        /// <summary>
        /// Object-creation expression that creates the <c>TrainRoute</c>.
        /// </summary>
        public ObjectCreationExpressionSyntax Root { get; }

        /// <inheritdoc />
        public Location Location { get; }

        /// <summary>
        /// Method containing the creation expression, when available.
        /// </summary>
        public IMethodSymbol ContainingMethod { get; }
    }
}
