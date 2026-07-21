using System.Collections.Generic;
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
            var handlerBinding = merged.CanonicalBinding;
            var names = NamingScope.ForDelegate(merged.DelegateTypeId, handlerBinding, metadata.ReturnMembers);
            var context = CodegenContext.ForCanonical(names);
            if (emitMetadata)
            {
                handlerBinding.Input.EmitMetadataFields(writer, names, metadata.ReturnMembers);
                writer.AppendLine();

                if (handlerBinding.RequiresCustomDelegate())
                {
                    handlerBinding.EmitCustomDelegateDeclaration(writer, names.DelegateName);
                    writer.AppendLine();
                }
            }

            handlerBinding.EmitPublicExtensionMethod(writer, names, context, incrementChainOrdinal: true);
        }
    }
}
