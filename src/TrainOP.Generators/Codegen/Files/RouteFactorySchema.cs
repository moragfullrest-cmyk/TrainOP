using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits one exported route factory schema type.
    /// </summary>
    internal sealed class RouteFactorySchema
    {
        /// <summary>
        /// Creates a route factory schema from a factory method and its terminal wagons.
        /// </summary>
        public RouteFactorySchema(IMethodSymbol method, ImmutableArray<WagonBinding> terminalWagons)
        {
            Method = method;
            TerminalWagons = terminalWagons;
        }

        /// <summary>Exported factory method symbol.</summary>
        public IMethodSymbol Method { get; }

        /// <summary>Terminal wagon slots discovered for the factory path.</summary>
        public ImmutableArray<WagonBinding> TerminalWagons { get; }

        /// <summary>
        /// Emits schema attributes on a generated holder type.
        /// </summary>
        internal void Emit(CodegenWriter writer)
        {
            var ownerDisplay = Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var schemaTypeName = BuildSchemaTypeName(Method);
            writer.AppendIndented("[RouteSchemaFor(typeof(")
                .Append(ownerDisplay)
                .Append("), \"")
                .Append(Method.Name)
                .Append("\")]");
            writer.EndLine();

            for (var i = 0; i < TerminalWagons.Length; i++)
            {
                var wagon = TerminalWagons[i];
                writer.AppendIndented("[RouteSchemaWagon(\"")
                    .Append(StringHelpers.Escape(wagon.Name) ?? string.Empty)
                    .Append("\", typeof(")
                    .Append(wagon.TypeDisplay)
                    .Append("))]");
                writer.EndLine();
            }

            writer.AppendIndented("internal static class ")
                .Append(schemaTypeName)
                .Append(" { }");
            writer.EndLine();
            writer.AppendLine();
        }

        /// <summary>
        /// Builds the generated schema type name for a factory method.
        /// </summary>
        internal static string BuildSchemaTypeName(IMethodSymbol methodSymbol)
        {
            var typeName = methodSymbol.ContainingType.Name;
            var methodName = methodSymbol.Name;
            if (typeName.Length == 0)
            {
                return methodName + "_Schema";
            }

            return typeName + "_" + methodName + "_Schema";
        }
    }
}
