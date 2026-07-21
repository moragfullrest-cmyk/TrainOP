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
            switch (slot.Kind)
            {
                case HandlerInputKind.Wagon:
                    if (slot.Wagon.IsByReference)
                    {
                        writer.Append("ref ");
                    }

                    if (context.UseNeutralWagonNames)
                    {
                        writer.Append("wagon").Append(slot.WagonIndex);
                    }
                    else
                    {
                        writer.Append(slot.Wagon.Name);
                    }

                    break;
                case HandlerInputKind.RedSignal:
                    writer.Append(context.RedVariable ?? "red");
                    break;
                case HandlerInputKind.SignalIssue:
                    writer.Append(context.SignalIssueExpression);
                    break;
                case HandlerInputKind.CargoManifest:
                    writer.Append("manifest");
                    break;
                case HandlerInputKind.CancellationToken:
                    writer.Append(context.TokenVariable ?? "default");
                    break;
            }
        }
    }
}
