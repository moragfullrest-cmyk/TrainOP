using System.Security.Cryptography;
using System.Text;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds stable caller chain keys for generated dispatch tables.
    /// Must stay aligned with runtime <c>TrainOP.CallerChainKeyFormat</c>.
    /// </summary>
    internal static class CallerChainKeyHasher
    {
        /// <summary>
        /// Builds a stable chain key from caller identity components.
        /// </summary>
        public static string Build(string filePath, int lineNumber, string memberName)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                filePath = "unknown";
            }

            return HashCanonicalPayload(BuildCanonicalPayload(filePath, lineNumber, memberName));
        }

        internal static string BuildCanonicalPayload(string filePath, int lineNumber, string memberName)
        {
            filePath = StringHelpers.NormalizeFilePath(filePath);
            if (filePath.Length == 0)
            {
                filePath = "unknown";
            }

            if (string.IsNullOrEmpty(memberName))
            {
                memberName = "global";
            }

            if (lineNumber <= 0)
            {
                lineNumber = 1;
            }

            return filePath + ":" + lineNumber + ":" + memberName;
        }

        internal static string HashCanonicalPayload(string canonicalPayload)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(canonicalPayload);
                var hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(16);
                for (var i = 0; i < 8; i++)
                {
                    result.Append(hash[i].ToString("x2"));
                }

                return result.ToString();
            }
        }
    }
}
