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
                EmitChainAware(writer, schema, emissionState);
                return;
            }

            EmitCanonical(writer, schema, metadataConsolidation, emissionState);
        }

        /// <summary>
        /// Emits chain binding tables, delegate declaration, and core adapter overloads.
        /// </summary>
        private static void EmitChainAware(
            CodegenWriter writer,
            MergedStationSchema merged,
            EmissionState emissionState)
        {
            ChainBindingTable.EmitStructOnce(writer, emissionState);

            var handlerBinding = merged.CanonicalBinding;
            var names = NamingScope.ForDelegate(merged.DelegateTypeId, handlerBinding, merged.ReturnMembers);
            // Non-mergeable return shapes share one CLR Func; bake merge from the canonical binding
            // would apply the wrong ItemN / member-name plan to other call sites.
            var context = CodegenContext.ForChain(
                names,
                allowTypedMerge: !merged.RequiresPerSiteReturnMetadata(),
                optionalFallbackSites: OptionalFallbackSites.WhenTheyDiffer(handlerBinding, merged.ChainBindings));
            var table = new ChainBindingTable(names, merged.ChainBindings, handlerBinding, merged.ReturnMembers);
            table.Emit(writer);
            writer.AppendLine();

            if (handlerBinding.RequiresCustomDelegate())
            {
                handlerBinding.EmitCustomDelegateDeclaration(writer, names.DelegateName);
                writer.AppendLine();
            }

            var handlerTypeName = handlerBinding.BuildHandlerTypeName(names.DelegateName);
            handlerBinding.EmitChainDispatchPublicMethod(writer, names, handlerTypeName);
            writer.AppendLine();
            handlerBinding.EmitChainDispatchCoreMethods(writer, names, handlerTypeName, context);
        }

        /// <summary>
        /// Emits metadata fields, delegate declaration, and the public extension method.
        /// </summary>
        private static void EmitCanonical(
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
            var context = CodegenContext.ForCanonical(
                names,
                handlerBinding.ReturnShape.HasDefaultItemNTupleElements,
                OptionalFallbackSites.WhenTheyDiffer(handlerBinding, merged.ChainBindings));
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
