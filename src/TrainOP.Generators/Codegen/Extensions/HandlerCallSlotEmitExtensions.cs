using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    internal static class HandlerCallSlotEmitExtensions
    {
        /// <summary>
        /// Emits one handler call argument for this call slot.
        /// </summary>
        internal static void EmitArgument(this HandlerCallSlot slot, CodegenWriter writer, CallArgumentContext context)
        {
            writer.Append(slot.Kind switch
            {
                HandlerInputKind.Wagon => slot.Wagon.ArgumentModifier
                    + (context.UseNeutralWagonNames
                        ? "wagon" + slot.WagonIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : slot.Wagon.Name),
                HandlerInputKind.RedSignal => context.RedVariable ?? "red",
                HandlerInputKind.SignalIssue => context.SignalIssueExpression,
                HandlerInputKind.SignalIssues => context.SignalIssuesExpression,
                HandlerInputKind.CargoManifest => "manifest",
                HandlerInputKind.CancellationToken => context.TokenVariable ?? "default",
                _ => string.Empty
            });
        }
    }
}
