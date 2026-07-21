using System.Collections.Generic;
using System.Text;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits all members for a canonical (non-chain) merged station schema.
    /// </summary>
    internal static class CanonicalEmission
    {
        /// <summary>
        /// Emits metadata fields, delegate declaration, and the public extension method.
        /// </summary>
        internal static void Emit(
            CodegenWriter writer,
            MergedStationSchema merged,
            IReadOnlyDictionary<string, MergedStationSchema> metadataConsolidation,
            EmissionState emissionState)
        {
            var metadataKey = EmissionState.BuildMetadataKey(merged);
            var emitMetadata = emissionState.TryAddMetadataKey(metadataKey);
            var metadata = metadataConsolidation[metadataKey];
            Emit(writer.Builder, merged, metadata, emitMetadata);
        }

        /// <summary>
        /// Emits delegate, metadata fields, and extension method members for one merged schema.
        /// </summary>
        internal static void Emit(
            StringBuilder source,
            MergedStationSchema merged,
            MergedStationSchema metadata,
            bool emitMetadata)
        {
            var handlerBinding = merged.CanonicalBinding;
            var names = NamingScope.ForDelegate(merged.DelegateTypeId, handlerBinding, metadata.ReturnMembers);
            var context = CodegenContext.ForCanonical(names);
            if (emitMetadata)
            {
                handlerBinding.Input.EmitMetadataFields(source, names, metadata.ReturnMembers);
                source.AppendLine();

                if (handlerBinding.RequiresCustomDelegate())
                {
                    handlerBinding.EmitCustomDelegateDeclaration(source, names.DelegateName);
                    source.AppendLine();
                }
            }

            handlerBinding.EmitPublicExtensionMethod(source, names, context, incrementChainOrdinal: true);
        }
    }
}
