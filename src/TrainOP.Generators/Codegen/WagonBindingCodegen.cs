using System.Text;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Emits shared wagon pull and type display code for generated adapters.
    /// </summary>
    internal static class WagonBindingCodegen
    {
        /// <summary>
        /// Emits manifest pull statements for all handler input wagons.
        /// </summary>
        public static void EmitPullWagonStatements(StringBuilder source, StationHandlerBinding schema, string manifestVariable = "manifest")
        {
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                EmitPullWagonStatement(source, schema.Wagons[i], manifestVariable);
            }
        }

        /// <summary>
        /// Emits one manifest pull statement for a wagon binding.
        /// </summary>
        public static void EmitPullWagonStatement(StringBuilder source, WagonBinding wagon, string manifestVariable = "manifest")
        {
            if (wagon.IsOptional)
            {
                source.Append("                var ").Append(wagon.Name).Append(" = ").Append(manifestVariable).Append(".HasWagon(\"")
                    .Append(StringHelpers.Escape(wagon.Name))
                    .Append("\") ? ").Append(manifestVariable).Append(".PullWagon<")
                    .Append(GetManifestPullTypeDisplay(wagon))
                    .Append(">(\"")
                    .Append(StringHelpers.Escape(wagon.Name))
                    .Append("\") : default(")
                    .Append(wagon.TypeDisplay)
                    .AppendLine(");");
                return;
            }

            source.Append("                var ").Append(wagon.Name).Append(" = ").Append(manifestVariable).Append(".PullWagon<")
                .Append(GetManifestPullTypeDisplay(wagon))
                .Append(">(\"")
                .Append(StringHelpers.Escape(wagon.Name))
                .AppendLine("\");");
        }

        /// <summary>
        /// Emits a manifest pull expression without assigning to a local variable.
        /// </summary>
        public static void EmitPullWagonExpression(StringBuilder source, WagonBinding wagon, string manifestVariable = "manifest")
        {
            source.Append(manifestVariable)
                .Append(".PullWagon<")
                .Append(GetManifestPullTypeDisplay(wagon))
                .Append(">(\"")
                .Append(StringHelpers.Escape(wagon.Name))
                .Append("\")");
        }

        /// <summary>
        /// Returns the manifest pull type display for a wagon binding.
        /// </summary>
        public static string GetManifestPullTypeDisplay(WagonBinding wagon)
        {
            return ManifestWagonTypes.ToManifestTypeDisplay(wagon.TypeSymbol);
        }
    }
}
