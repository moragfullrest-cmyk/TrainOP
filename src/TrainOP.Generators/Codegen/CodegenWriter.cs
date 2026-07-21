using System.Text;

namespace TrainOP.Generators
{
    /// <summary>
    /// Indent-aware wrapper over <see cref="StringBuilder"/> for generated C# source.
    /// </summary>
    internal sealed class CodegenWriter
    {
        private readonly StringBuilder _builder;

        /// <summary>
        /// Creates a writer over an existing builder (typically shared with legacy emit helpers).
        /// </summary>
        public CodegenWriter(StringBuilder builder)
        {
            _builder = builder ?? new StringBuilder();
        }

        /// <summary>Underlying builder passed to existing static codegen helpers.</summary>
        public StringBuilder Builder => _builder;

        /// <summary>Default indent for class members (four spaces).</summary>
        public int MemberIndent { get; set; } = 4;

        /// <summary>Appends text without a trailing newline.</summary>
        public void Append(string text)
        {
            _builder.Append(text);
        }

        /// <summary>Appends a blank line.</summary>
        public void AppendLine()
        {
            _builder.AppendLine();
        }

        /// <summary>Appends one line with a trailing newline.</summary>
        public void AppendLine(string line)
        {
            _builder.AppendLine(line);
        }

        /// <summary>Appends one indented line with a trailing newline.</summary>
        public void AppendLine(int indent, string line)
        {
            _builder.Append(new string(' ', indent));
            _builder.AppendLine(line);
        }

        /// <summary>Appends one member-level indented line.</summary>
        public void AppendMemberLine(string line)
        {
            AppendLine(MemberIndent, line);
        }

        /// <summary>Emits standard usings for generated TrainRoute extension source.</summary>
        public void EmitExtensionFileUsings()
        {
            AppendLine("using System;");
            AppendLine("using System.Threading;");
            AppendLine("using System.Threading.Tasks;");
            AppendLine();
        }

        /// <summary>Emits namespace and extensions class opening braces.</summary>
        public void EmitExtensionFileHeader()
        {
            AppendLine("namespace TrainOP");
            AppendLine("{");
            AppendLine("    public static class TrainRouteStationExtensions");
            AppendLine("    {");
        }

        /// <summary>Emits extensions class and namespace closing braces.</summary>
        public void EmitExtensionFileFooter()
        {
            AppendLine("    }");
            AppendLine("}");
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
