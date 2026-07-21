using System;
using System.Text;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    internal static class HandlerInputParametersEmitExtensions
    {
        /// <summary>
        /// Emits handler call arguments in <see cref="HandlerInputParameters.CallOrder"/>.
        /// </summary>
        internal static void EmitCallArguments(
            this HandlerInputParameters input,
            StringBuilder source,
            CallArgumentContext context)
        {
            var needsComma = false;
            var callOrder = input.CallOrder;
            for (var i = 0; i < callOrder.Length; i++)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                callOrder[i].EmitArgument(source, context);
                needsComma = true;
            }
        }

        /// <summary>
        /// Emits a <c>string[]</c> literal of wagon names into generated source.
        /// </summary>
        internal static void EmitWagonNamesArrayLiteral(
            this HandlerInputParameters input,
            StringBuilder source,
            Func<string, string> escape)
        {
            source.Append("new string[] { ");
            for (var i = 0; i < input.Wagons.Length; i++)
            {
                source.Append("\"").Append(escape(input.Wagons[i].Name)).Append("\"");
                if (i < input.Wagons.Length - 1)
                {
                    source.Append(", ");
                }
            }

            source.Append(" }");
        }

        /// <summary>
        /// Emits a <c>bool[]</c> literal of wagon ref flags into generated source.
        /// </summary>
        internal static void EmitRefFlagsArrayLiteral(this HandlerInputParameters input, StringBuilder source)
        {
            source.Append("new bool[] { ");
            for (var i = 0; i < input.Wagons.Length; i++)
            {
                source.Append(input.Wagons[i].IsByReference ? "true" : "false");
                if (i < input.Wagons.Length - 1)
                {
                    source.Append(", ");
                }
            }

            source.Append(" }");
        }

        /// <summary>
        /// Emits static metadata fields for one delegate signature group.
        /// </summary>
        internal static void EmitMetadataFields(
            this HandlerInputParameters input,
            StringBuilder source,
            NamingScope names,
            string[] returnMembers)
        {
            if (names.RefFlagsField != null)
            {
                source.Append("        private static readonly bool[] ").Append(names.RefFlagsField).Append(" = ");
                input.EmitRefFlagsArrayLiteral(source);
                source.AppendLine(";");
            }

            source.Append("        private static readonly string[] ").Append(names.WagonNamesField).Append(" = ");
            input.EmitWagonNamesArrayLiteral(source, StringHelpers.Escape);
            source.AppendLine(";");

            if (names.ReturnMembersField != null && returnMembers != null)
            {
                source.Append("        private static readonly string[] ").Append(names.ReturnMembersField).Append(" = new string[] { ");
                for (var i = 0; i < returnMembers.Length; i++)
                {
                    source.Append("\"").Append(StringHelpers.Escape(returnMembers[i])).Append("\"");
                    if (i < returnMembers.Length - 1)
                    {
                        source.Append(", ");
                    }
                }

                source.AppendLine(" };");
            }
        }
    }
}
