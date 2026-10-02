using System;
using System.Collections.Generic;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    internal static class WagonBindingEmitExtensions
    {
        /// <summary>
        /// Emits one manifest pull statement for this wagon binding.
        /// </summary>
        internal static void EmitPull(
            this WagonBinding wagon,
            CodegenWriter writer,
            PullContext context,
            CodegenContext codegen = null,
            int wagonIndex = -1)
        {
            if (wagon.IsOut)
            {
                writer.AppendIndented(wagon.TypeDisplay)
                    .Append(' ')
                    .Append(context.LocalVariableName)
                    .Append(" = default;");
                writer.EndLine();
                return;
            }

            var manifest = context.ManifestVariable;

            if (wagon.IsOptional)
            {
                var fallback = wagon.OptionalFallback ?? ("default(" + wagon.TypeDisplay + ")");
                writer.AppendIndented(wagon.TypeDisplay)
                    .Append(' ')
                    .Append(context.LocalVariableName)
                    .Append(';');
                writer.EndLine();
                writer.AppendIndented("if (")
                    .Append(manifest)
                    .Append(".HasWagonUnchecked(")
                    .Append(context.NameExpression)
                    .Append("))");
                writer.EndLine();
                using (writer.Block())
                {
                    writer.AppendIndented(context.LocalVariableName)
                        .Append(" = ")
                        .Append(manifest)
                        .Append(".PullWagonUnchecked<")
                        .Append(wagon.PullTypeDisplay)
                        .Append(">(")
                        .Append(context.NameExpression)
                        .Append(");");
                    writer.EndLine();
                }

                EmitOptionalSubstitutes(writer, context, codegen, wagonIndex, fallback);
                return;
            }

            writer.AppendIndented("var ").Append(context.LocalVariableName).Append(" = ").Append(manifest).Append(".PullWagonUnchecked<")
                .Append(wagon.PullTypeDisplay)
                .Append(">(")
                .Append(context.NameExpression)
                .Append(");");
            writer.EndLine();
        }

        private static void EmitOptionalSubstitutes(
            CodegenWriter writer,
            PullContext context,
            CodegenContext codegen,
            int wagonIndex,
            string canonicalFallback)
        {
            var groups = GroupAlternateSites(codegen, wagonIndex, canonicalFallback);
            for (var i = 0; i < groups.Count; i++)
            {
                writer.AppendIndented("else if (");
                var sites = groups[i].Sites;
                for (var s = 0; s < sites.Count; s++)
                {
                    if (s > 0)
                    {
                        writer.Append(" || ");
                    }

                    var site = sites[s];
                    writer.Append("string.Equals(")
                        .Append(codegen.CallerChainKeyExpression)
                        .Append(", \"")
                        .Append(StringHelpers.Escape(site.ChainId))
                        .Append("\", StringComparison.Ordinal) && chainStationIndex == ")
                        .Append(site.StationIndex);
                }

                writer.Append(')');
                writer.EndLine();
                using (writer.Block())
                {
                    EmitAssign(writer, context.LocalVariableName, groups[i].Expression);
                }
            }

            writer.AppendLine("else");
            using (writer.Block())
            {
                EmitAssign(writer, context.LocalVariableName, canonicalFallback);
            }
        }

        private static List<FallbackGroup> GroupAlternateSites(
            CodegenContext codegen,
            int wagonIndex,
            string canonicalFallback)
        {
            var groups = new List<FallbackGroup>();
            var sites = codegen?.OptionalFallbackSites;
            if (sites == null || wagonIndex < 0)
            {
                return groups;
            }

            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                var schema = site.Schema;
                if (schema == null || string.IsNullOrEmpty(site.ChainId) || wagonIndex >= schema.Wagons.Length)
                {
                    continue;
                }

                var rendered = OptionalFallbackSites.Render(schema.Wagons[wagonIndex]);
                if (rendered == null || string.Equals(rendered, canonicalFallback, StringComparison.Ordinal))
                {
                    continue;
                }

                FallbackGroup group = null;
                for (var g = 0; g < groups.Count; g++)
                {
                    if (string.Equals(groups[g].Expression, rendered, StringComparison.Ordinal))
                    {
                        group = groups[g];
                        break;
                    }
                }

                if (group == null)
                {
                    group = new FallbackGroup(rendered);
                    groups.Add(group);
                }

                group.Sites.Add(site);
            }

            return groups;
        }

        private static void EmitAssign(CodegenWriter writer, string localName, string expression)
        {
            writer.AppendIndented(localName)
                .Append(" = ")
                .Append(expression)
                .Append(';');
            writer.EndLine();
        }

        private sealed class FallbackGroup
        {
            public FallbackGroup(string expression)
            {
                Expression = expression;
                Sites = new List<ChainSiteBinding>();
            }

            public string Expression { get; }

            public List<ChainSiteBinding> Sites { get; }
        }
    }
}
