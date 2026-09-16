using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CppSharp.AST;
using CppSharp.AST.Extensions;
using AstType = CppSharp.AST.Type;

// Step 1 of the product pipeline: a small, *checked-in*, normalized API model.
//
//   extract : clang/CppSharp AST  ->  model/qtsharp.api.json   (deterministic, sorted)
//   codegen : qtsharp.api.json    ->  C# bindings + C++ wrapper + .def (pure function)
//
// The point of the model is that codegen no longer reads raw Qt headers itself, so it is
// a pure function of a versioned artifact: the same model always produces byte-identical
// output, and CI can enforce "regenerate -> diff must be empty".
public static class ApiModel
{
    public const int FormatVersion = 1;
    public const string QtVersion = "6.11.0";
    public const string Namespace = "QtSharp.Api";

    // First slice: one class, enough to exercise a method, a virtual and a signal.
    static readonly string[] RootClasses = { "QObject" };

    static readonly HashSet<string> VirtualAllow = new()
    {
        "eventFilter", "event", "customEvent",
    };

    // Types the first slice can marshal. Anything else is skipped rather than guessed.
    static readonly HashSet<string> SupportedTypes = new()
    {
        "void", "bool", "int", "QString", "QAnyStringView", "QObject*", "QEvent*",
    };

    // ---------------------------------------------------------------- extraction
    public static void Extract(ASTContext ctx, string path)
    {
        var classes = new List<Class>();
        foreach (var tu in ctx.TranslationUnits)
            Collect(tu, classes);

        classes = classes
            .Where(c => RootClasses.Contains(c.OriginalName))
            .OrderBy(c => c.OriginalName, StringComparer.Ordinal)
            .ToList();

        var signals = Program.ScanSignals()
            .Where(s => RootClasses.Contains(s.ClassName))
            .Where(s => s.Params.Where(p => !p.Private)
                .All(p => Program.MapSignalType(p.CppType, out _, out _, out _, out _, out _)))
            .ToList();
        // Every signal name (supported or not) so callable methods do not include signals.
        var allSignalNames = new HashSet<string>(Program.ScanSignals()
            .Where(s => RootClasses.Contains(s.ClassName))
            .Select(s => s.ClassName + "::" + s.SignalName));

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"formatVersion\": {FormatVersion},\n");
        sb.Append($"  \"qtVersion\": \"{QtVersion}\",\n");
        sb.Append($"  \"namespace\": \"{Namespace}\",\n");
        sb.Append("  \"classes\": [\n");

        for (int ci = 0; ci < classes.Count; ci++)
        {
            var c = classes[ci];
            var baseName = c.Bases.Where(b => b.IsClass && b.Class != null)
                .Select(b => b.Class.OriginalName).FirstOrDefault();

            sb.Append("    {\n");
            sb.Append($"      \"name\": \"{c.OriginalName}\",\n");
            sb.Append($"      \"base\": {Json(baseName)},\n");
            sb.Append($"      \"isAbstract\": {(c.IsAbstract ? "true" : "false")},\n");

            // methods (callable, non virtual)
            // Getter/setter pairs are turned into Property objects by CppSharp, so the
            // accessor methods have to be pulled back out of the properties.
            if (Environment.GetEnvironmentVariable("QTSHARP_MODEL_DEBUG") == "1")
            {
                Console.WriteLine($"[model-debug] {c.OriginalName} methods:");
                foreach (var m in c.Methods)
                    Console.WriteLine($"    {m.OriginalName}({string.Join(",", m.Parameters.Select(p => p.Kind + ":" + Normalize(p.Type)))}) ret={Normalize(ReturnTypeOf(m))} gen={m.IsGenerated} virt={m.IsVirtual} acc={m.Access}");
                Console.WriteLine($"[model-debug] {c.OriginalName} properties:");
                foreach (var p in c.Properties)
                    Console.WriteLine($"    {p.Name} get={p.GetMethod?.OriginalName ?? "null"} set={p.SetMethod?.OriginalName ?? "null"} gen={p.IsGenerated}");
            }

            var callable = new List<Method>();
            var propertyAccessors = new HashSet<Method>();
            callable.AddRange(c.Methods);
            foreach (var p in c.Properties)
            {
                // Properties are explicitly ignored by the legacy pipeline, but their
                // accessors are exactly the methods a binding wants to expose.
                if (p.GetMethod != null) { callable.Add(p.GetMethod); propertyAccessors.Add(p.GetMethod); }
                if (p.SetMethod != null) { callable.Add(p.SetMethod); propertyAccessors.Add(p.SetMethod); }
            }

            var methods = callable
                .Where(m => !m.IsDestructor && !m.IsOperator && !m.IsConstructor)
                .Where(m => !m.IsVirtual)
                .Where(m => m.Access == AccessSpecifier.Public)
                .Where(m => !allSignalNames.Contains(c.OriginalName + "::" + m.OriginalName))
                // Deliberately not gated on IsGenerated: the legacy pipeline prunes methods
                // for its own reasons, and we decide by our own type whitelist instead.
                .Where(m => Signature(ReturnTypeOf(m), VisibleParams(m)))
                .GroupBy(m => DedupeKey(m))
                .Select(g => g.First())
                .OrderBy(m => m.OriginalName, StringComparer.Ordinal)
                .ThenBy(m => m.Parameters.Count)
                .ToList();
            WriteFunctions(sb, "methods", methods);

            // virtuals that a managed subclass may override
            var virtuals = c.Methods
                .Where(m => m.IsGenerated && m.IsVirtual && !m.IsDestructor && !m.IsOperator
                            && VirtualAllow.Contains(m.OriginalName))
                .Where(m => ReturnTypeOf(m) is BuiltinType b &&
                            (b.Type == PrimitiveType.Void || b.Type == PrimitiveType.Bool || b.Type == PrimitiveType.Int))
                .Where(m => Signature(ReturnTypeOf(m), VisibleParams(m)))
                .OrderBy(m => m.OriginalName, StringComparer.Ordinal)
                .ThenBy(m => m.Parameters.Count)
                .ToList();
            WriteFunctions(sb, "virtuals", virtuals);

            // signals (names come from moc; here from the header scan)
            var classSignals = signals.Where(s => s.ClassName == c.OriginalName)
                .OrderBy(s => s.SignalName, StringComparer.Ordinal).ToList();
            sb.Append("      \"signals\": [\n");
            for (int i = 0; i < classSignals.Count; i++)
            {
                var s = classSignals[i];
                sb.Append("        { \"name\": ").Append(Json(s.SignalName)).Append(", \"parameters\": [");
                sb.Append(string.Join(", ", s.Params.Where(p => !p.Private).Select(p =>
                    "{ \"name\": " + Json(CleanName(p.Name)) + ", \"type\": " + Json(Program.MapSignalType(p.CppType, out var cs, out _, out _, out _, out _) ? SignalModelType(cs) : CleanName(p.CppType)) + " }")));
                sb.Append("] }");
                sb.Append(i + 1 < classSignals.Count ? ",\n" : "\n");
            }
            sb.Append("      ]\n");
            sb.Append(ci + 1 < classes.Count ? "    },\n" : "    }\n");
        }

        sb.Append("  ]\n");
        sb.Append("}\n");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"[model] wrote {path} ({classes.Count} class(es))");
    }

    static void Collect(DeclarationContext dc, List<Class> classes)
    {
        foreach (var c in dc.Classes)
        {
            classes.Add(c);
            Collect(c, classes);
        }
        foreach (var ns in dc.Namespaces)
            Collect(ns, classes);
    }

    static void WriteFunctions(StringBuilder sb, string key, List<Method> methods)
    {
        sb.Append($"      \"{key}\": [\n");
        for (int i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            sb.Append("        { \"name\": ").Append(Json(m.OriginalName));
            sb.Append(", \"returnType\": ").Append(Json(Normalize(ReturnTypeOf(m))));
            sb.Append(", \"parameters\": [");
            sb.Append(string.Join(", ", VisibleParams(m).Select(p =>
                "{ \"name\": " + Json(CleanName(p.Name.Length > 0 ? p.Name : "arg" + p.Index)) + ", \"type\": " + Json(Normalize(p.Type)) + " }")));
            sb.Append("] }");
            sb.Append(i + 1 < methods.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    // Value returns are represented in the AST as a hidden "indirect return" parameter;
    // the real type lives in OriginalReturnType.
    static AstType ReturnTypeOf(Method m) => m.OriginalReturnType.Type ?? m.ReturnType.Type;

    static IEnumerable<Parameter> VisibleParams(Method m)
        => m.Parameters.Where(p => p.Kind != ParameterKind.IndirectReturnType);

    static string DedupeKey(Method m)
        => m.OriginalName + "(" + string.Join(",", VisibleParams(m).Select(p => Normalize(p.Type))) + ")";

    static bool Signature(AstType returnType, IEnumerable<Parameter> parameters)
    {
        var ret = Normalize(returnType);
        if (ret == null || !SupportedTypes.Contains(ret))
            return false;
        foreach (var p in parameters)
        {
            var t = Normalize(p.Type);
            if (t == null || !SupportedTypes.Contains(t))
                return false;
        }
        return true;
    }

    public static string Normalize(AstType type)
    {
        if (type == null)
            return "void";
        var d = type.Desugar();
        if (d is PointerType pointer)
        {
            // References are modelled as pointer types with an LV/RV modifier.
            if (pointer.Modifier != PointerType.TypeModifier.Pointer)
                return Normalize(pointer.QualifiedPointee.Type);
            var name = DeclName(pointer.QualifiedPointee.Type.Desugar());
            return name == null ? null : name + "*";
        }
        if (d is BuiltinType builtin)
            return builtin.Type switch
            {
                PrimitiveType.Void => "void",
                PrimitiveType.Bool => "bool",
                PrimitiveType.Int => "int",
                _ => null,
            };
        return DeclName(d);
    }

    static string DeclName(AstType d) => d switch
    {
        TagType tag => tag.Declaration?.OriginalName,
        TypedefType td => td.Declaration?.OriginalName,
        _ => null,
    };

    static string Json(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // CppSharp escapes C# keywords in names ("event" -> "@event"); the model stores plain
    // C++ names and the code generator escapes again for C# only.
    static string CleanName(string s) => string.IsNullOrEmpty(s) ? s : (s[0] == '@' ? s.Substring(1) : s);

    static string SignalModelType(string csharpType) => csharpType switch
    {
        "string" => "QString",
        "string[]" => "QStringList",
        _ => csharpType,
    };
}
