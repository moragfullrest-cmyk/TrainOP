using System;
using System.Text;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    internal static class WagonBindingEmitExtensions
    {
        /// <summary>
        /// Emits one manifest pull statement for this wagon binding.
        /// </summary>
        internal static void EmitPull(this WagonBinding wagon, StringBuilder source, PullContext context)
        {
            var indent = context.StatementIndent;
            var manifest = context.ManifestVariable;
            var pullType = wagon.GetManifestPullTypeDisplay();

            if (wagon.IsOptional)
            {
                source.Append(indent).Append("var ").Append(context.LocalVariableName).Append(" = ").Append(manifest).Append(".HasWagon(")
                    .Append(context.NameExpression)
                    .Append(") ? ").Append(manifest).Append(".PullWagon<")
                    .Append(pullType)
                    .Append(">(")
                    .Append(context.NameExpression)
                    .Append(") : default(")
                    .Append(wagon.TypeDisplay)
                    .AppendLine(");");
                return;
            }

            source.Append(indent).Append("var ").Append(context.LocalVariableName).Append(" = ").Append(manifest).Append(".PullWagon<")
                .Append(pullType)
                .Append(">(")
                .Append(context.NameExpression)
                .AppendLine(");");
        }

        /// <summary>
        /// Returns the manifest pull type display for this wagon binding.
        /// </summary>
        internal static string GetManifestPullTypeDisplay(this WagonBinding wagon)
        {
            return ManifestWagonTypes.ToManifestTypeDisplay(wagon.TypeSymbol);
        }
    }
}
