using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits all members for a chain-dispatched merged station schema.
    /// </summary>
    internal static class ChainAwareEmission
    {
        /// <summary>
        /// Emits chain binding tables, delegate declaration, and core adapter overloads.
        /// </summary>
        internal static void Emit(CodegenWriter writer, MergedStationSchema merged, EmissionState emissionState)
        {
            ChainBindingTable.EmitStructOnce(writer, emissionState);

            var handlerBinding = merged.CanonicalBinding;
            var names = NamingScope.ForDelegate(merged.DelegateTypeId, handlerBinding, merged.ReturnMembers);
            var context = CodegenContext.ForChain(names);
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
    }
}
