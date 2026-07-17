namespace TrainOP.Generators
{
    /// <summary>
    /// Escapes string literals for inclusion in generated C# source.
    /// </summary>
    internal static class GeneratedSourceEscape
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
    }
}
