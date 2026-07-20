using System;

namespace TrainOP.Generators
{
    /// <summary>
    /// Normalizes source file paths for location comparisons.
    /// </summary>
    internal static class InterceptorLocationFormatter
    {
        /// <summary>
        /// Normalizes a file path for stable comparison (slashes only).
        /// </summary>
        public static string NormalizeFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return string.Empty;
            }

            return filePath.Replace('\\', '/');
        }
    }
}
