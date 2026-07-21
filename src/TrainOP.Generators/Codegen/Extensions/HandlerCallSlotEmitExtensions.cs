using System.Text;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    internal static class HandlerCallSlotEmitExtensions
    {
        /// <summary>
        /// Emits one handler call argument for this call slot.
        /// </summary>
        internal static void EmitArgument(this HandlerCallSlot slot, StringBuilder source, CallArgumentContext context)
        {
            switch (slot.Kind)
            {
                case HandlerInputKind.Wagon:
                    if (slot.Wagon.IsByReference)
                    {
                        source.Append("ref ");
                    }

                    if (context.UseNeutralWagonNames)
                    {
                        source.Append("wagon").Append(slot.WagonIndex);
                    }
                    else
                    {
                        source.Append(slot.Wagon.Name);
                    }

                    break;
                case HandlerInputKind.RedSignal:
                    source.Append(context.RedVariable ?? "red");
                    break;
                case HandlerInputKind.SignalIssue:
                    source.Append(context.SignalIssueExpression);
                    break;
                case HandlerInputKind.CargoManifest:
                    source.Append("manifest");
                    break;
                case HandlerInputKind.CancellationToken:
                    source.Append(context.TokenVariable ?? "default");
                    break;
            }
        }
    }
}
