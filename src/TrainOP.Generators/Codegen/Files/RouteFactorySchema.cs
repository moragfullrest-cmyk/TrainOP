using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
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
        /// Emits attributes and the schema holder type into generated source.
        /// </summary>
        internal void Emit(StringBuilder source)
        {
            var ownerDisplay = Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var schemaTypeName = BuildSchemaTypeName(Method);
            source.Append("    [RouteSchemaFor(typeof(")
                .Append(ownerDisplay)
                .Append("), \"")
                .Append(Method.Name)
                .AppendLine("\")]");

            for (var i = 0; i < TerminalWagons.Length; i++)
            {
                var wagon = TerminalWagons[i];
                source.Append("    [RouteSchemaWagon(\"")
                    .Append(StringHelpers.Escape(wagon.Name) ?? string.Empty)
                    .Append("\", typeof(")
                    .Append(wagon.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine("))]");
            }

            source.Append("    internal static class ")
                .Append(schemaTypeName)
                .AppendLine();
            source.AppendLine("    {");
            source.Append("        public static readonly WagonSlot[] TerminalWagons = new WagonSlot[]");
            source.AppendLine();
            source.AppendLine("        {");

            for (var i = 0; i < TerminalWagons.Length; i++)
            {
                var wagon = TerminalWagons[i];
                source.Append("            new WagonSlot(\"")
                    .Append(StringHelpers.Escape(wagon.Name) ?? string.Empty)
                    .Append("\", typeof(")
                    .Append(wagon.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append("))");

                source.AppendLine(i == TerminalWagons.Length - 1 ? string.Empty : ",");
            }

            source.AppendLine("        };");
            source.AppendLine("    }");
            source.AppendLine();
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
