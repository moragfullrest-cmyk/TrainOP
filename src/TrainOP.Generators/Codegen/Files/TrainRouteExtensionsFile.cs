using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits the generated <c>TrainRouteStationExtensions</c> source file.
    /// </summary>
    internal static class TrainRouteExtensionsFile
    {
        internal const string SourceHintName = "TrainRouteStation.Extensions.g.cs";

        /// <summary>
        /// Emits extension source for all merged handler schemas.
        /// </summary>
        internal static string Emit(ImmutableArray<MergedStationSchema> schemas)
        {
            var writer = new CodegenWriter(new StringBuilder());
            var emissionState = new EmissionState();
            var metadataConsolidation = EmissionState.BuildMetadataConsolidation(schemas);
            writer.EmitExtensionFileUsings();
            writer.EmitExtensionFileHeader();

            var emittedCount = 0;
            for (var i = 0; i < schemas.Length; i++)
            {
                var merged = schemas[i];
                var emissionKey = merged.CanonicalBinding.BuildGroupingKey(merged.DelegateTypeId);
                if (!emissionState.TryAddSignature(emissionKey))
                {
                    continue;
                }

                if (emittedCount > 0)
                {
                    writer.AppendLine();
                }

                merged.EmitMembers(writer, emissionState, metadataConsolidation);
                emittedCount++;
            }

            writer.EmitExtensionFileFooter();
            return writer.ToString();
        }

        /// <summary>
        /// Adds the generated extensions file to the production context.
        /// </summary>
        internal static void AddSource(SourceProductionContext context, ImmutableArray<MergedStationSchema> schemas)
        {
            context.AddSource(SourceHintName, SourceText.From(Emit(schemas), Encoding.UTF8));
        }
    }
}
