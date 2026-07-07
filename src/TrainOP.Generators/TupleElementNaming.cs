using System;

namespace TrainOP.Generators
{
    /// <summary>
    /// Normalizes value-tuple element names to the named strategy (Item1, Item2, ...).
    /// </summary>
    internal static class TupleElementNaming
    {
        /// <summary>
        /// Returns the default tuple element name for the given zero-based index.
        /// </summary>
        public static string DefaultName(int index) => "Item" + (index + 1);

        /// <summary>
        /// Builds default ItemN names for a tuple of the given arity.
        /// </summary>
        public static string[] BuildDefaultNames(int count)
        {
            var names = new string[count];
            for (var i = 0; i < count; i++)
            {
                names[i] = DefaultName(i);
            }

            return names;
        }

        /// <summary>
        /// Determines whether a tuple element name is the default ItemN name for its index.
        /// </summary>
        public static bool IsDefaultName(string name, int index)
        {
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            if (!name.StartsWith("Item", StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(name.Substring(4), out var parsed)
                && parsed == index + 1;
        }
    }
}
