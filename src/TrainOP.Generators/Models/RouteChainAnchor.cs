using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Root expression and metadata for a detected TrainRoute station chain.
    /// </summary>
    internal sealed class RouteChainAnchor
    {
        /// <summary>
        /// Creates a chain anchor from its kind, root expression, and optional containing method.
        /// </summary>
        public RouteChainAnchor(
            RouteChainAnchorKind kind,
            ExpressionSyntax root,
            Location location,
            IMethodSymbol containingMethod = null)
        {
            Kind = kind;
            Root = root;
            Location = location;
            ContainingMethod = containingMethod;
        }

        public RouteChainAnchorKind Kind { get; }

        /// <summary>
        /// Expression from which fluent chain traversal starts.
        /// </summary>
        public ExpressionSyntax Root { get; }

        public Location Location { get; }

        /// <summary>
        /// Method that contains the anchor expression, when available.
        /// </summary>
        public IMethodSymbol ContainingMethod { get; }
    }
}
