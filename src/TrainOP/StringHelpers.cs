namespace TrainOP
{
    /// <summary>
    /// Shared string helpers for runtime route keys.
    /// Must stay aligned with compile-time <c>TrainOP.Generators.StringHelpers.NormalizeFilePath</c>.
    /// </summary>
    internal static class StringHelpers
    {
        /// <summary>
        /// Normalizes path separators to forward slashes; empty paths become <see cref="string.Empty"/>.
        /// </summary>
        public static string NormalizeFilePath(string filePath)
        {
            return string.IsNullOrEmpty(filePath) ? string.Empty : filePath.Replace('\\', '/');
        }
    }
}
