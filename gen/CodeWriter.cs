using System;
using System.Text;

// An indentation-aware writer for the generated source. The generator is a compiler-like emitter,
// not a data-driven template: the type logic stays in C#, and this class removes the hand-counted
// spaces that made the larger AppendLine blocks hard to read and easy to break.
//
//   w.Open("internal static class QsApiList_int");
//   w.Open("public static IntPtr New(int[] items)");
//   w.Line("return ApiNative.List_int_new();");
//   w.Close();
//   w.Close();
//
// emits the braces and the four-space indentation itself. A writer is created with an initial
// indent level when it appends to a file whose surrounding block is already open (a helper class
// sits at level 1, the DllImports inside ApiNative at level 2).
sealed class CodeWriter
{
    readonly StringBuilder sb;
    readonly string indentUnit;
    int indent;

    public CodeWriter(StringBuilder sb, int indent = 0, string indentUnit = "    ")
    {
        this.sb = sb ?? throw new ArgumentNullException(nameof(sb));
        this.indentUnit = indentUnit;
        this.indent = indent;
    }

    public CodeWriter Line(string text = "")
    {
        if (text.Length != 0)
            for (int i = 0; i < indent; i++)
                sb.Append(indentUnit);
        sb.AppendLine(text);
        return this;
    }

    public CodeWriter Lines(params string[] lines)
    {
        foreach (var line in lines)
            Line(line);
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
