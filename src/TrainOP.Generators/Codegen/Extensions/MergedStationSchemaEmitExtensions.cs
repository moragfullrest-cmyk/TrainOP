using System.Collections.Generic;

namespace TrainOP.Generators
{
    internal static class MergedStationSchemaEmitExtensions
    {
        /// <summary>
        /// Emits generated members for one merged schema (chain or canonical strategy).
        /// </summary>
        internal static void EmitMembers(
            this MergedStationSchema schema,
            CodegenWriter writer,
            EmissionState emissionState,
            IReadOnlyDictionary<string, MergedStationSchema> metadataConsolidation)
        {
            if (schema.UsesChainDispatch)
            {
                ChainAwareEmission.Emit(writer.Builder, schema, emissionState);
                return;
            }

            CanonicalEmission.Emit(writer, schema, metadataConsolidation, emissionState);
        }
    }
}
