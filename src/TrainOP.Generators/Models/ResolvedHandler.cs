using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// A handler argument resolved to a method symbol with optional body syntax in the current compilation.
    /// </summary>
    internal sealed class ResolvedHandler
    {
        /// <summary>
        /// Creates a resolved handler record.
        /// </summary>
        public ResolvedHandler(
            HandlerKind kind,
            IMethodSymbol symbol,
            CSharpSyntaxNode body,
            Location location,
            ExpressionSyntax expression)
        {
            Kind = kind;
            Symbol = symbol;
            Body = body;
            Location = location;
            Expression = expression;
        }

        public HandlerKind Kind { get; }

        public IMethodSymbol Symbol { get; }

        public CSharpSyntaxNode Body { get; }

        public Location Location { get; }

        public ExpressionSyntax Expression { get; }
    }
}
