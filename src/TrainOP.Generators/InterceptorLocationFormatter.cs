using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace TrainOP.Generators
{
    /// <summary>
    /// Formats source locations for Roslyn interceptor attributes.
    /// </summary>
    internal static class InterceptorLocationFormatter
    {
        /// <summary>
        /// Normalizes a file path for use in <c>InterceptsLocation</c> attributes.
        /// </summary>
        public static string NormalizeFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return string.Empty;
            }

            return filePath.Replace('\\', '/');
        }

        /// <summary>
        /// Formats an <c>InterceptsLocation</c> attribute for a station invocation.
        /// </summary>
        public static bool TryAppendAttribute(
            StringBuilder source,
            Compilation compilation,
            InvocationExpressionSyntax invocation,
            out string displayLocation)
        {
            displayLocation = null;
            if (source == null || compilation == null || invocation == null)
            {
                return false;
            }

            var semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
            var interceptableLocation = semanticModel.GetInterceptableLocation(invocation);
            if (interceptableLocation == null)
            {
                return false;
            }

            displayLocation = interceptableLocation.GetDisplayLocation();
            source.Append("[global::System.Runtime.CompilerServices.InterceptsLocation(")
                .Append(interceptableLocation.Version)
                .Append(", \"")
                .Append(Escape(interceptableLocation.Data))
                .AppendLine("\")]");
            return true;
        }

        private static string Escape(string value)
        {
            return GeneratedSourceEscape.Escape(value);
        }
    }
}
