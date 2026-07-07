using System;
using System.Collections.Immutable;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds terminal report metadata used when decomposing RouteReport into tuples.
    /// </summary>
    internal static class TerminalReportMetadataBuilder
    {
        /// <summary>
        /// Extracts manifest wagon names from a terminal schema in production order.
        /// </summary>
        public static string[] BuildWagonNames(TerminalWagonSchema schema)
        {
            return BuildWagonNames(schema.Wagons);
        }

        /// <summary>
        /// Extracts manifest wagon names from wagon bindings in declaration order.
        /// </summary>
        public static string[] BuildWagonNames(ImmutableArray<WagonBinding> wagons)
        {
            if (wagons.IsDefaultOrEmpty)
            {
                return Array.Empty<string>();
            }

            var names = new string[wagons.Length];
            for (var i = 0; i < wagons.Length; i++)
            {
                names[i] = wagons[i].Name;
            }

            return names;
        }

        /// <summary>
        /// Builds positional tuple member names (Item1, Item2, ...) for a terminal schema.
        /// Wagon order comes from <see cref="TerminalDeconstructSchemaBuilder"/>.
        /// </summary>
        public static string[] BuildTupleElementNames(TerminalWagonSchema schema)
        {
            return TupleElementNaming.BuildDefaultNames(schema.Wagons.Length);
        }
    }
}
