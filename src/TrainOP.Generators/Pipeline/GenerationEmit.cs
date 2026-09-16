using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;

namespace TrainOP.Generators
{
    /// <summary>
    /// Final emit entry: writes generated sources from a fully populated <see cref="GenerationModel"/>.
    /// </summary>
    internal static class GenerationEmit
    {
        /// <summary>
        /// Reports pipeline diagnostics and emits Extensions + RouteSchemas from ready IR.
        /// No discovery / stage work — only <c>AddSource</c> / <c>ReportDiagnostic</c>.
        /// </summary>
        public static void EmitAll(SourceProductionContext context, GenerationModel model)
        {
            if (model == null)
            {
                return;
            }

            foreach (var diagnostic in model.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

            RouteSchemasFile.AddSource(context, model.SchemaDescriptors);

            if (model.BranchPlans.IsDefaultOrEmpty)
            {
                return;
            }

            var mergedSchemas = model.BranchPlans
                .Select(plan => plan.Schema)
                .ToImmutableArray();

            TrainRouteExtensionsFile.AddSource(context, mergedSchemas);
        }
    }
}
