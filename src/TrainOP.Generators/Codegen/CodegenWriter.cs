using System;
using System.Text;

namespace TrainOP.Generators
{
    /// <summary>
    /// Indent-aware wrapper over <see cref="StringBuilder"/> for generated C# source.
    /// </summary>
    internal sealed class CodegenWriter
    {
        public const int IndentUnit = 4;

        private readonly StringBuilder _builder;
        private int _indentLevel;

        /// <summary>
        /// Creates a writer with an empty source buffer.
        /// </summary>
        public CodegenWriter()
        {
            _builder = new StringBuilder();
        }

        /// <summary>Current indent depth (each level is <see cref="IndentUnit"/> spaces).</summary>
        public int IndentLevel => _indentLevel;

        /// <summary>Appends text without indent or a trailing newline.</summary>
        public CodegenWriter Append(string text)
        {
            _builder.Append(text);
            return this;
        }

        /// <summary>Appends one character without indent or a trailing newline.</summary>
        public CodegenWriter Append(char value)
        {
            _builder.Append(value);
            return this;
        }

        /// <summary>Appends a formatted integer without indent or a trailing newline.</summary>
        public CodegenWriter Append(int value)
        {
            _builder.Append(value);
            return this;
        }

        /// <summary>Appends current indent followed by text without a trailing newline.</summary>
        public CodegenWriter AppendIndented(string text)
        {
            _builder.Append(GetIndent());
            _builder.Append(text);
            return this;
        }

        /// <summary>Appends a blank line without indent.</summary>
        public void AppendLine()
        {
            _builder.AppendLine();
        }

        /// <summary>Appends one indented line with a trailing newline.</summary>
        public void AppendLine(string line)
        {
            _builder.Append(GetIndent());
            _builder.AppendLine(line);
        }

        /// <summary>Ends the current line without adding indent to a continuation fragment.</summary>
        public void EndLine()
        {
            _builder.AppendLine();
        }

        /// <summary>Appends one line without indent (usings, namespace, auto-generated header).</summary>
        public void AppendRawLine(string line)
        {
            _builder.AppendLine(line);
        }

        /// <summary>Increases indent for a lexical scope; restore via <see cref="IDisposable.Dispose"/>.</summary>
        public IDisposable PushIndent(int levels = 1)
        {
            _indentLevel += levels;
            return new IndentScope(this, levels);
        }

        internal void PopIndent(int levels)
        {
            _indentLevel -= levels;
            if (_indentLevel < 0)
            {
                throw new InvalidOperationException("Indent level underflow.");
            }
        }

        /// <summary>Opens a braced block; restores indent and writes <c>}</c> on <see cref="IDisposable.Dispose"/>.</summary>
        public IDisposable Block(string headerLine = null, string closeSuffix = null)
        {
            return new BlockScope(this, headerLine, closeSuffix);
        }

        /// <summary>Emits standard usings for generated TrainRoute extension source.</summary>
        public void EmitExtensionFileUsings()
        {
            AppendRawLine("using System;");
            AppendRawLine("using System.Threading;");
            AppendRawLine("using System.Threading.Tasks;");
            AppendLine();
        }

        /// <summary>Emits namespace and extensions class opening braces.</summary>
        public void EmitExtensionFileHeader()
        {
            AppendRawLine("namespace TrainOP");
            AppendRawLine("{");
            _indentLevel = 1;
            AppendLine("public static class TrainRouteStationExtensions");
            AppendLine("{");
            _indentLevel = 2;
        }

        /// <summary>Emits extensions class and namespace closing braces.</summary>
        public void EmitExtensionFileFooter()
        {
            _indentLevel = 1;
            AppendLine("}");
            _indentLevel = 0;
            AppendRawLine("}");
        }

        public override string ToString()
        {
            return _builder.ToString();
        }

        private string GetIndent()
        {
            return _indentLevel <= 0 ? string.Empty : new string(' ', _indentLevel * IndentUnit);
        }

        internal sealed class IndentScope : IDisposable
        {
            private readonly CodegenWriter _writer;
            private readonly int _levels;
            private bool _disposed;

            internal IndentScope(CodegenWriter writer, int levels)
            {
                _writer = writer;
                _levels = levels;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _writer.PopIndent(_levels);
                _disposed = true;
            }
        }

        internal sealed class BlockScope : IDisposable
        {
            private readonly CodegenWriter _writer;
            private readonly string _closeSuffix;
            private readonly IDisposable _indent;
            private bool _disposed;

            internal BlockScope(CodegenWriter writer, string headerLine, string closeSuffix)
            {
                _writer = writer;
                _closeSuffix = closeSuffix;

                if (headerLine != null)
                {
                    writer.AppendLine(headerLine);
                }

                writer.AppendLine("{");
                _indent = writer.PushIndent(1);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _indent.Dispose();

                if (_closeSuffix == null)
                {
                    _writer.AppendLine("}");
                }
                else
                {
                    _writer.AppendIndented("}");
                    _writer.Append(_closeSuffix);
                    _writer.EndLine();
                }

                _disposed = true;
            }
        }
    }
}
