using System.Text;
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
        internal static void Emit(StringBuilder source, MergedStationSchema merged, EmissionState emissionState)
        {
            ChainBindingTable.EmitStructOnce(source, emissionState);

            var handlerBinding = merged.CanonicalBinding;
            var names = NamingScope.ForDelegate(merged.DelegateTypeId, handlerBinding, merged.ReturnMembers);
            var context = CodegenContext.ForChain(names);
            var table = new ChainBindingTable(names, merged.ChainBindings, handlerBinding, merged.ReturnMembers);
            table.Emit(source);
            source.AppendLine();

            if (handlerBinding.RequiresCustomDelegate())
            {
                handlerBinding.EmitCustomDelegateDeclaration(source, names.DelegateName);
                source.AppendLine();
            }

            var handlerTypeName = handlerBinding.BuildHandlerTypeName(names.DelegateName);
            handlerBinding.EmitChainDispatchPublicMethod(source, names, handlerTypeName);
            source.AppendLine();
            handlerBinding.EmitChainDispatchCoreMethods(source, names, handlerTypeName, context);
        }
    }
}
