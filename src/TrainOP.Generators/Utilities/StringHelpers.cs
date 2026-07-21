using System;
using System.Text;

namespace TrainOP.Generators
{
    /// <summary>
    /// Shared string helpers for source generation and route analysis.
    /// </summary>
    internal static class StringHelpers
    {
        /// <summary>
        /// Escapes backslashes and double quotes for a C# string literal body.
        /// </summary>
        public static string Escape(string value)
        {
            if (value == null)
            {
                return null;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Normalizes path separators to forward slashes; empty paths become <see cref="string.Empty"/>.
        /// </summary>
        public static string NormalizeFilePath(string filePath)
        {
            return string.IsNullOrEmpty(filePath) ? string.Empty : filePath.Replace('\\', '/');
        }

        /// <summary>
        /// Returns the default tuple element name for the given zero-based index.
        /// </summary>
        public static string DefaultTupleElementName(int index) => "Item" + (index + 1);

        /// <summary>
        /// Determines whether a tuple element name is the default ItemN name for its index.
        /// </summary>
        public static bool IsDefaultTupleElementName(string name, int index)
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

        /// <summary>
        /// Keeps letters and digits; replaces all other characters with underscores.
        /// </summary>
        public static string SanitizeIdentifier(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }

            return builder.ToString();
        }
    }
}
