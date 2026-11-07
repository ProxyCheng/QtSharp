using System;
using System.Text;

namespace QtSharp.Generator
{
    /// <summary>
    /// An indentation-aware writer for generated source. The emitter supplies statements; the
    /// writer owns braces and indentation, so the emitters never count spaces by hand.
    /// </summary>
    public sealed class CodeWriter
    {
        private readonly StringBuilder text;
        private readonly string indentUnit;
        private int indent;

        public CodeWriter(StringBuilder text, int indent = 0, string indentUnit = "    ")
        {
            this.text = text ?? throw new ArgumentNullException(nameof(text));
            this.indentUnit = indentUnit;
            this.indent = indent;
        }

        public CodeWriter Line(string line = "")
        {
            if (line.Length != 0)
            {
                for (int i = 0; i < indent; i++)
                    text.Append(indentUnit);
            }
            text.AppendLine(line);
            return this;
        }

        public CodeWriter Lines(params string[] lines)
        {
            foreach (string line in lines)
                Line(line);
            return this;
        }

        /// <summary>
        /// Writes a template block, preserving its relative indentation. The block's own line
        /// endings are ignored; every line goes through <see cref="Line"/>.
        /// </summary>
        public CodeWriter Block(string block)
        {
            int start = 0;
            for (int i = 0; i <= block.Length; i++)
            {
                if (i != block.Length && block[i] != '\n')
                    continue;
                int length = i - start;
                if (length > 0 && block[start + length - 1] == '\r')
                    length--;
                if (length > 0)
                    Line(block.Substring(start, length));
                start = i + 1;
            }
            return this;
        }

        public CodeWriter Blank() => Line();

        public CodeWriter Open(string header)
        {
            Line(header);
            Line("{");
            return Indent();
        }

        public CodeWriter Close(string suffix = "}")
        {
            Dedent();
            return Line(suffix);
        }

        public CodeWriter Indent()
        {
            indent++;
            return this;
        }

        public CodeWriter Dedent()
        {
            if (indent == 0)
                throw new InvalidOperationException("Cannot dedent below the writer's starting level.");
            indent--;
            return this;
        }
    }
}
