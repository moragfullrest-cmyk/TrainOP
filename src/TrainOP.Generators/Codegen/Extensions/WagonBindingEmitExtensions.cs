using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    internal static class WagonBindingEmitExtensions
    {
        /// <summary>
        /// Emits one manifest pull statement for this wagon binding.
        /// </summary>
        internal static void EmitPull(this WagonBinding wagon, CodegenWriter writer, PullContext context)
        {
            var manifest = context.ManifestVariable;

            if (wagon.IsOptional)
            {
                writer.AppendIndented("var ").Append(context.LocalVariableName).Append(" = ").Append(manifest).Append(".HasWagon(")
                    .Append(context.NameExpression)
                    .Append(") ? ").Append(manifest).Append(".PullWagon<")
                    .Append(wagon.PullTypeDisplay)
                    .Append(">(")
                    .Append(context.NameExpression)
                    .Append(") : default(")
                    .Append(wagon.TypeDisplay)
                    .Append(");");
                writer.EndLine();
                return;
            }

            writer.AppendIndented("var ").Append(context.LocalVariableName).Append(" = ").Append(manifest).Append(".PullWagon<")
                .Append(wagon.PullTypeDisplay)
                .Append(">(")
                .Append(context.NameExpression)
                .Append(");");
            writer.EndLine();
        }
    }
}
