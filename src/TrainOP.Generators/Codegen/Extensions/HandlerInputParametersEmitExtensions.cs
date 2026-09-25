using System;
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
            CodegenWriter writer,
            CallArgumentContext context)
        {
            var needsComma = false;
            var callOrder = input.CallOrder;
            for (var i = 0; i < callOrder.Length; i++)
            {
                if (needsComma)
                {
                    writer.Append(", ");
                }

                callOrder[i].EmitArgument(writer, context);
                needsComma = true;
            }
        }

        /// <summary>
        /// Emits a <c>string[]</c> literal of wagon names into generated source.
        /// </summary>
        internal static void EmitWagonNamesArrayLiteral(
            this HandlerInputParameters input,
            CodegenWriter writer,
            Func<string, string> escape)
        {
            writer.Append("new string[] { ");
            for (var i = 0; i < input.Wagons.Length; i++)
            {
                writer.Append("\"").Append(escape(input.Wagons[i].Name)).Append("\"");
                if (i < input.Wagons.Length - 1)
                {
                    writer.Append(", ");
                }
            }

            writer.Append(" }");
        }

        /// <summary>
        /// Emits a <c>bool[]</c> literal of wagon ref flags into generated source.
        /// </summary>
        internal static void EmitRefFlagsArrayLiteral(this HandlerInputParameters input, CodegenWriter writer)
        {
            EmitBoolFlags(input, writer, retainedOnly: false);
        }

        /// <summary>
        /// Emits a <c>bool[]</c> literal that is true for <c>in</c> and <c>ref readonly</c> wagons.
        /// </summary>
        internal static void EmitRetainedFlagsArrayLiteral(this HandlerInputParameters input, CodegenWriter writer)
        {
            EmitBoolFlags(input, writer, retainedOnly: true);
        }

        private static void EmitBoolFlags(HandlerInputParameters input, CodegenWriter writer, bool retainedOnly)
        {
            writer.Append("new bool[] { ");
            for (var i = 0; i < input.Wagons.Length; i++)
            {
                var flag = retainedOnly ? input.Wagons[i].IsReadOnlyPass : input.Wagons[i].WritesBack;
                writer.Append(flag ? "true" : "false");
                if (i < input.Wagons.Length - 1)
                {
                    writer.Append(", ");
                }
            }

            writer.Append(" }");
        }

        /// <summary>
        /// Emits static metadata fields for one delegate signature group.
        /// </summary>
        internal static void EmitMetadataFields(
            this HandlerInputParameters input,
            CodegenWriter writer,
            NamingScope names,
            string[] returnMembers)
        {
            if (names.RefFlagsField != null)
            {
                writer.AppendIndented("private static readonly bool[] ").Append(names.RefFlagsField).Append(" = ");
                input.EmitRefFlagsArrayLiteral(writer);
                writer.Append(";");
                writer.EndLine();
            }

            writer.AppendIndented("private static readonly string[] ").Append(names.WagonNamesField).Append(" = ");
            input.EmitWagonNamesArrayLiteral(writer, StringHelpers.Escape);
            writer.Append(";");
            writer.EndLine();

            if (names.ReturnMembersField != null && returnMembers != null)
            {
                writer.AppendIndented("private static readonly string[] ").Append(names.ReturnMembersField).Append(" = new string[] { ");
                for (var i = 0; i < returnMembers.Length; i++)
                {
                    writer.Append("\"").Append(StringHelpers.Escape(returnMembers[i])).Append("\"");
                    if (i < returnMembers.Length - 1)
                    {
                        writer.Append(", ");
                    }
                }

                writer.Append(" };");
                writer.EndLine();
            }
        }
    }
}
