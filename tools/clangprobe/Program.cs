using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ClangSharp;
using ClangSharp.Interop;

// Feasibility probe for "route A": replace CppSharp's AST projection with ClangSharp/libclang.
//
// It parses one Qt header with the exact configuration CppSharp's ParserOptions builds up (see
// ApiModel.DumpParseOptions), then prints the facts the API model extracts - base classes,
// public methods with their spelled types/parameter names/default arguments, own virtuals and
// nested enums - in the same canonical shape as model/qtsharp.api.json, so the two can be
// diffed directly.
static class Program
{
    // The same two locations the generator uses: this repository's Qt kit and clang resource folder
    // by default (found relative to the repository root, so a clone can live anywhere), overridable
    // with QTSHARP_QT_INCLUDE / QTSHARP_CLANG_RESOURCE_DIR.
    static readonly string RepoRoot = FindRepoRoot();
    static readonly string QtRoot = Environment.GetEnvironmentVariable("QTSHARP_QT_INCLUDE")
        ?? Path.Combine(RepoRoot, "Qt", "6.12.0", "msvc2022_64", "include");
    static readonly string ResourceDir = Environment.GetEnvironmentVariable("QTSHARP_CLANG_RESOURCE_DIR")
        ?? (Directory.GetDirectories(Path.Combine(RepoRoot, "lib", "lib", "clang"))
                .OrderByDescending(d => d, StringComparer.Ordinal)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "include", "stddef.h")))
            ?? throw new InvalidOperationException("clang's builtin headers are missing - see README.md"));

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "QtSharp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    // Depth-first walk over a cursor tree through the raw visitor.
    sealed class Walker
    {
        public Action<CXCursor> OnCursor;

        public unsafe CXChildVisitResult Visit(CXCursor cursor, CXCursor parent, void* data)
        {
            OnCursor(cursor);
            cursor.VisitChildren(Visit, default);
            return CXChildVisitResult.CXChildVisit_Continue;
        }

        public unsafe void Walk(CXCursor root) => root.VisitChildren(Visit, default);
    }

    sealed class Counter
    {
        public int Count;

        public unsafe CXChildVisitResult Visit(CXCursor cursor, CXCursor parent, void* data)
        {
            Count++;
            return CXChildVisitResult.CXChildVisit_Continue;
        }
    }

    static unsafe int ChildCount(CXCursor cursor)
    {
        var counter = new Counter();
        cursor.VisitChildren(counter.Visit, default);
        return counter.Count;
    }

    // Finds the templated declaration inside a CXCursor_FunctionTemplate. The high-level
    // CursorChildren view is not dependable here, so this walks the raw cursor.
    sealed class InnerDecl
    {
        public CXCursor Cursor;
        public bool Found;

        public unsafe CXChildVisitResult Visit(CXCursor cursor, CXCursor parent, void* data)
        {
            if (!Found)
            {
                var kind = clang.getCursorKind(cursor);
                if (kind == CXCursorKind.CXCursor_CXXMethod ||
                    kind == CXCursorKind.CXCursor_FunctionDecl ||
                    kind == CXCursorKind.CXCursor_Constructor)
                {
                    Cursor = cursor;
                    Found = true;
                }
            }
            return CXChildVisitResult.CXChildVisit_Continue;
        }
    }

    sealed class FirstChildExtent
    {
        public CXSourceRange Range;
        public bool HasRange;

        public unsafe CXChildVisitResult Visit(CXCursor cursor, CXCursor parent, void* data)
        {
            // A ParmDecl's first children are the type references of its own type; the default
            // argument is the expression child.
            var kind = clang.getCursorKind(cursor);
            if (!HasRange && kind >= CXCursorKind.CXCursor_FirstExpr && kind <= CXCursorKind.CXCursor_LastExpr)
            {
                Range = clang.getCursorExtent(cursor);
                HasRange = true;
            }
            return CXChildVisitResult.CXChildVisit_Continue;
        }
    }

    static unsafe int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--dump")
        {
            foreach (var asm in new[] { typeof(CXIndex).Assembly, typeof(TranslationUnit).Assembly })
                foreach (var name in args.Skip(1))
                    foreach (var t in asm.GetTypes().Where(x => x.Name == name && (x.IsPublic || x.IsNestedPublic)))
                    {
                        Console.WriteLine($"--- {t.FullName}");
                        foreach (var m in t.GetMethods()
                                          .Where(m => m.Name is "Invoke" or "BeginInvoke" || m.IsStatic)
                                          .OrderBy(m => m.Name, StringComparer.Ordinal))
                            Console.WriteLine($"    {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
                        foreach (var f in t.GetFields().Where(f => f.Name.Contains("Visit")))
                            Console.WriteLine($"    field {f.Name}");
                    }
            return 0;
        }

        var header = Path.Combine(QtRoot, "QtWidgets", "qlineedit.h");
        var className = "QLineEdit";
        string outPath = null;
        var emitRaw = false;
        var all = false;
        var tree = false;
        var indexMode = false;
        var rootsSource = Path.Combine(RepoRoot, "gen", "ApiModel.cs");

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--header": header = args[++i]; break;
                case "--class": className = args[++i]; break;
                case "--out": outPath = args[++i]; break;
                case "--raw": emitRaw = true; break;
                case "--all": all = true; break;
                case "--roots": rootsSource = args[++i]; break;
                case "--tree": tree = true; break;
                case "--index": indexMode = true; header = args[++i]; break;
            }
        }

        // --index: one parse over a whole module, then "class <tab> defining header" for every
        // exported class that could become a Root. Choosing Roots rows needs this.
        if (indexMode)
        {
            var headers = new List<string> { header };
            headers.AddRange(Directory.GetFiles(Path.Combine(QtRoot, "QtWidgets"), "q*.h"));
            headers.AddRange(Directory.GetFiles(Path.Combine(QtRoot, "QtGui"), "q*.h"));
            headers = headers.Distinct().ToList();
            header = headers[0];

            using var indexHandle = CXIndex.Create(false, false);
            var indexClock = Stopwatch.StartNew();
            var err = CXTranslationUnit.TryParse(indexHandle, header, BuildArgs(headers), default,
                CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies |
                CXTranslationUnit_Flags.CXTranslationUnit_KeepGoing,
                out var indexTu);
            if (err != CXErrorCode.CXError_Success)
            {
                Console.Error.WriteLine($"index parse failed: {err}");
                return 2;
            }
            Console.Error.WriteLine($"indexed {headers.Count} headers in {indexClock.Elapsed.TotalSeconds:F1}s");

            var seen = new Dictionary<string, (string File, int Members)>(StringComparer.Ordinal);
            var walker = new Walker
            {
                OnCursor = cursor =>
                {
                    var kind = clang.getCursorKind(cursor);
                    if (kind != CXCursorKind.CXCursor_ClassDecl && kind != CXCursorKind.CXCursor_StructDecl)
                        return;
                    var name = clang.getCursorSpelling(cursor).ToString();
                    if (name.Length == 0 || !name.StartsWith("Q", StringComparison.Ordinal))
                        return;
                    int members = ChildCount(cursor);
                    if (seen.TryGetValue(name, out var previous) && previous.Members >= members)
                        return;
                    clang.getCursorLocation(cursor).GetSpellingLocation(out var file, out _, out _, out _);
                    var path = file.Handle == IntPtr.Zero ? "?" : Path.GetFileName(file.Name.ToString());
                    seen[name] = (path, members);
                },
            };
            walker.Walk(indexTu.Cursor);
            foreach (var kv in seen.OrderBy(k => k.Key, StringComparer.Ordinal))
                if (kv.Value.Members > 0)
                    Console.WriteLine($"{kv.Key}\t{kv.Value.File}");
            return 0;
        }

        var log = new List<string>();
        void Say(string s)
        {
            Console.WriteLine(s);
            log.Add(s);
        }

        // --all: every root header of the checked-in model, parsed as one translation unit.
        // This is the configuration CppSharp could not do reproducibly - the whole reason the
        // model is extracted in chunks with a double-parse gate.
        var extraIncludes = new List<string>();
        var classNames = new List<string>();
        if (all)
        {
            foreach (var (cls, module, hdr) in ReadRoots(rootsSource))
            {
                classNames.Add(cls);
                var path = Path.Combine(QtRoot, module, hdr);
                if (!extraIncludes.Contains(path))
                    extraIncludes.Add(path);
            }
            header = extraIncludes[0];
            Say($"[probe] mode         all roots ({classNames.Count} classes, {extraIncludes.Count} headers, one TU)");
        }

        Say($"[probe] clang        {clang.getClangVersion().ToString()}");
        Say($"[probe] header       {header}");

        using var index = CXIndex.Create(false, false);

        var sw = Stopwatch.StartNew();
        var error = CXTranslationUnit.TryParse(index, header, BuildArgs(extraIncludes), default,
            CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies |
            CXTranslationUnit_Flags.CXTranslationUnit_KeepGoing,
            out var translationUnit);
        var parseMs = sw.ElapsedMilliseconds;

        if (error != CXErrorCode.CXError_Success)
        {
            Say($"[probe] FAILED to parse: {error}");
            return 2;
        }

        Say($"[probe] parsed in {parseMs} ms");
        Say($"[probe] diagnostics: {translationUnit.NumDiagnostics}");
        for (uint i = 0; i < translationUnit.NumDiagnostics; i++)
        {
            using var d = translationUnit.GetDiagnostic(i);
            using var text = d.Format(CXDiagnosticDisplayOptions.CXDiagnostic_DisplaySourceLocation |
                                      CXDiagnosticDisplayOptions.CXDiagnostic_DisplayColumn |
                                      CXDiagnosticDisplayOptions.CXDiagnostic_DisplayOption);
            var line = text.ToString();
            if (d.Severity >= CXDiagnosticSeverity.CXDiagnostic_Error)
                Say($"[probe]   {d.Severity}: {line}");
            else if (d.Severity == CXDiagnosticSeverity.CXDiagnostic_Warning)
                Say($"[probe]   warning: {line}");
        }

        var unit = TranslationUnit.GetOrCreate(translationUnit);
        var total = Stopwatch.StartNew();

        var lines = new List<string>();
        if (all)
        {
            var byName = CollectClasses(unit);
            var missing = 0;
            int facts = 0;
            foreach (var name in classNames.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!byName.TryGetValue(name, out var record))
                {
                    lines.Add($"CLASS {name}");
                    lines.Add("  NOT FOUND");
                    missing++;
                    continue;
                }
                lines.Add($"CLASS {name}");
                foreach (var l in Describe(translationUnit, record, name, false))
                {
                    lines.Add("  " + l);
                    facts++;
                }
            }
            Say($"[probe] found {classNames.Count - missing}/{classNames.Count} classes, {facts} facts in {total.ElapsedMilliseconds} ms");
            Console.WriteLine($"CLASSES {classNames.Count} MISSING {missing} FACTS {facts}");
        }
        else
        {
            var record = FindClass(unit, className);
            if (record == null)
            {
                Say($"[probe] FAILED: {className} not found");
                return 3;
            }
            lines = Describe(translationUnit, record, className, emitRaw);
            Say($"[probe] extracted {lines.Count} facts in {total.ElapsedMilliseconds} ms");
            if (tree)
            {
                int mi = 0;
                foreach (var m in record.CursorChildren)
                {
                    var h = m.Handle;
                    Console.WriteLine($"TREE[{mi}] {m.CursorKindSpelling} '{m.Spelling}' args={clang.Cursor_getNumArguments(h)} acc={clang.getCXXAccessSpecifier(h)} ret={clang.getTypeSpelling(clang.getCursorResultType(h))}");
                    int ci = 0;
                    foreach (var child in m.CursorChildren)
                        Console.WriteLine($"TREE[{mi}].c[{ci++}] {child.CursorKindSpelling} '{child.Spelling}' args={clang.Cursor_getNumArguments(child.Handle)} acc={clang.getCXXAccessSpecifier(child.Handle)} ret={clang.getTypeSpelling(clang.getCursorResultType(child.Handle))}");
                    mi++;
                }
            }
        }

        foreach (var l in lines)
            Console.WriteLine(l);
        if (outPath != null)
            File.WriteAllText(outPath, string.Join("\n", lines) + "\n");
        return 0;
    }

    // The root table is the model's input description, so the probe reads it straight out of
    // the extractor instead of keeping a second copy in sync.
    static IEnumerable<(string Class, string Module, string Header)> ReadRoots(string sourcePath)
    {
        var text = File.ReadAllText(sourcePath);
        var start = text.IndexOf("static readonly Root[] Roots");
        var end = text.IndexOf("};", start);
        foreach (var line in text.Substring(start, end - start).Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, "R\\(\"([^\"]+)\",\\s*\"([^\"]+)\",\\s*\"([^\"]+)\"");
            if (m.Success)
                yield return (m.Groups[1].Value, m.Groups[3].Value, m.Groups[2].Value);
        }
    }

    static string[] BuildArgs(List<string> extraIncludes = null)
    {
        var args = new List<string>
        {
        "-xc++",
        "-std=c++17",
        "-fno-rtti",
        // Exactly what ParserOptions.SetupMSVC adds on Windows.
        "-fms-extensions",
        "-fms-compatibility",
        "-fms-compatibility-version=19.30",
        "-fdelayed-template-parsing",
        "-D_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH",
        // What ModelLibrary.Setup adds for the Qt modules.
        "-DQT_NO_DEBUG",
        "-DQT_CORE_LIB",
        "-DQT_GUI_LIB",
        "-DQT_WIDGETS_LIB",
        "-Wno-everything",
        "-I" + QtRoot,
        "-I" + Path.Combine(QtRoot, "QtCore"),
        "-I" + Path.Combine(QtRoot, "QtGui"),
        "-I" + Path.Combine(QtRoot, "QtWidgets"),
        // libClangSharp ships no builtin headers.
        "-resource-dir", ResourceDir,
        };

        // One TU, several roots: the first header is the source file, the rest are pre-included.
        foreach (var include in extraIncludes?.Skip(1) ?? Enumerable.Empty<string>())
        {
            args.Add("-include");
            args.Add(include);
        }
        return args.ToArray();
    }

    static Dictionary<string, Cursor> CollectClasses(TranslationUnit unit)
    {
        var byName = new Dictionary<string, Cursor>(StringComparer.Ordinal);
        void Walk(Cursor c)
        {
            if (c.CursorKind == CXCursorKind.CXCursor_ClassDecl)
            {
                // A forward declaration has no members; the definition has the most.
                if (!byName.TryGetValue(c.Spelling, out var existing) ||
                    c.CursorChildren.Count > existing.CursorChildren.Count)
                    byName[c.Spelling] = c;
            }
            foreach (var child in c.CursorChildren)
                Walk(child);
        }
        Walk(unit.TranslationUnitDecl);
        return byName;
    }

    static Cursor FindClass(TranslationUnit unit, string name)
        => CollectClasses(unit).TryGetValue(name, out var c) ? c : null;

    static string FileOf(CXTranslationUnit tu, CXSourceLocation loc)
    {
        loc.GetExpansionLocation(out var file, out _, out _, out _);
        if (file.Handle == IntPtr.Zero)
            return null;
        return file.Name.ToString();
    }

    static string TextOf(CXTranslationUnit tu, CXSourceRange range)
    {
        // Spelling locations, not expansion ones: declarations that come out of a macro
        // (Q_OBJECT's tr/metaObject/...) otherwise resolve to the macro invocation token.
        var start = clang.getRangeStart(range);
        var end = clang.getRangeEnd(range);
        start.GetSpellingLocation(out var file, out _, out _, out var off);
        end.GetSpellingLocation(out var file2, out _, out _, out var off2);
        if (file.Handle != file2.Handle || file.Handle == IntPtr.Zero)
            return null;
        var contents = tu.GetFileContents(file, out var size);
        if (off2 <= off || off2 > (ulong)size)
            return null;
        return Encoding.UTF8.GetString(contents.Slice((int)off, (int)(off2 - off))).Trim();
    }

    static unsafe List<string> Describe(CXTranslationUnit tu, Cursor record, string className, bool emitRaw)
    {
        var lines = new List<string>();
        var canonical = new List<string>();
        var raw = new List<string>();

        var bases = record.CursorChildren
            .Where(c => c.CursorKind == CXCursorKind.CXCursor_CXXBaseSpecifier)
            .Select(c => Normalize(clang.getTypeSpelling(clang.getCursorType(c.Handle)).ToString()))
            .Where(b => !b.Contains("<"))
            .ToList();
        canonical.Add("BASES: " + string.Join(", ", bases));
        canonical.Add("ABSTRACT: " + (clang.CXXRecord_isAbstract(record.Handle) != 0 ? "true" : "false"));

        // Materialized: CursorChildren is a lazily-built view, and enumerating a child's
        // children while walking the parent's would invalidate the outer enumeration.
        foreach (var m in record.CursorChildren.ToList())
        {
            var kind = m.CursorKind;
            if (kind == CXCursorKind.CXCursor_EnumDecl)
            {
                var items = m.CursorChildren.ToList()
                    .Where(e => e.CursorKind == CXCursorKind.CXCursor_EnumConstantDecl)
                    .Select(e => $"{e.Spelling}={clang.getEnumConstantDeclValue(e.Handle)}");
                canonical.Add($"ENUM {m.Spelling} {{ {string.Join(", ", items)} }}");
                continue;
            }

            if (kind != CXCursorKind.CXCursor_Constructor &&
                kind != CXCursorKind.CXCursor_CXXMethod &&
                kind != CXCursorKind.CXCursor_ConversionFunction &&
                kind != CXCursorKind.CXCursor_FunctionTemplate)
                continue;
            if (kind == CXCursorKind.CXCursor_ConversionFunction)
                continue;

            // Qt's Q_WEAK_OVERLOAD is "template <typename = void>", so a plain-looking member
            // can come back as a function template with no arguments on the template cursor.
            // The real signature lives on the templated declaration underneath it.
            var handle = m.Handle;
            var isTemplate = kind == CXCursorKind.CXCursor_FunctionTemplate;
            if (isTemplate)
            {
                var inner = new InnerDecl();
                m.Handle.VisitChildren(inner.Visit, default);
                if (inner.Found)
                    handle = inner.Cursor;
                // libclang reports no arguments for a function-template cursor and does not
                // always expose the templated declaration; the function type still has the
                // parameter types (only the names and defaults are lost).
            }

            // Qt declares its event handlers protected; the model keeps those as overridable
            // virtuals, so protected members have to stay visible (and tagged).
            var access = clang.getCXXAccessSpecifier(handle);
            if (access == CX_CXXAccessSpecifier.CX_CXXPrivate)
                continue;
            if (kind != CXCursorKind.CXCursor_Constructor &&
                clang.getCursorSpelling(handle).ToString().StartsWith("operator"))
                continue;

            var name = clang.getCursorSpelling(handle).ToString();
            if (Environment.GetEnvironmentVariable("QTSHARP_PROBE_DEBUG") == "1")
                Console.Error.WriteLine($"  member {m.CursorKindSpelling} '{name}' kind={kind} access={access} args={clang.Cursor_getNumArguments(handle)}");
            var isCtor = kind == CXCursorKind.CXCursor_Constructor;
            var pars = new List<string>();
            var rawPars = new List<string>();

            int n = clang.Cursor_getNumArguments(handle);
            if (n < 0)
            {
                // Function template: take the parameter types off the function type.
                var signature = clang.getCursorType(handle);
                for (uint i = 0; i < (uint)signature.NumArgTypes; i++)
                {
                    var t = Normalize(clang.getTypeSpelling(signature.GetArgType(i)).ToString());
                    pars.Add($"{t} arg{i}");
                    rawPars.Add($"{clang.getTypeSpelling(signature.GetArgType(i))} arg{i}");
                }
                n = 0;
            }
            for (uint i = 0; i < n; i++)
            {
                var arg = clang.Cursor_getArgument(handle, i);
                var argType = clang.getTypeSpelling(clang.getCursorType(arg)).ToString();
                var argName = clang.getCursorSpelling(arg).ToString();
                if (argName.Length == 0)
                    argName = "arg" + i;

                // libclang exposes no "has default argument" query; the default shows up as a
                // child expression of the ParmDecl cursor. (A lambda cannot sit in an unsafe
                // context, and the visitor delegate mentions a pointer type, hence the class.)
                string def = "";
                var probe = new FirstChildExtent();
                arg.VisitChildren(probe.Visit, default);
                if (probe.HasRange)
                    def = "=" + (TextOf(tu, probe.Range) ?? "<expr>");

                pars.Add($"{Normalize(argType)} {argName}{def}");
                rawPars.Add($"{argType} {argName}{def}");
            }

            var ret = Normalize(clang.getTypeSpelling(clang.getCursorResultType(handle)).ToString());
            var flags = "";
            if (access == CX_CXXAccessSpecifier.CX_CXXProtected) flags += " [protected]";
            if (clang.CXXMethod_isStatic(handle) != 0) flags += " [static]";
            if (clang.CXXMethod_isVirtual(handle) != 0) flags += " [virtual]";
            if (clang.CXXMethod_isPureVirtual(handle) != 0) flags += " [pure]";
            if (clang.CXXMethod_isDeleted(handle) != 0) flags += " [deleted]";
            if (isTemplate) flags += " [template]";

            if (Environment.GetEnvironmentVariable("QTSHARP_PROBE_DEBUG") == "1")
                Console.Error.WriteLine($"  emit {(isCtor ? "CTOR" : "METHOD")} ret='{ret}' name='{name}' pars=[{string.Join(" | ", pars)}] flags='{flags}'");
            canonical.Add(isCtor
                ? $"CTOR({string.Join(", ", pars)}){flags}"
                : $"METHOD {ret} {name}({string.Join(", ", pars)}){flags}");
            if (emitRaw)
                raw.Add($"    raw: {clang.getTypeSpelling(clang.getCursorResultType(handle))} {name}({string.Join(", ", rawPars)})");
        }

        canonical.Sort(StringComparer.Ordinal);
        lines.AddRange(canonical);
        lines.AddRange(raw);
        return lines;
    }

    // The model's type vocabulary, applied to clang's spelled type. Clang returns the *written*
    // type, so typedefs like Qt::Alignment and QFlags names survive without any desugaring.
    static string Normalize(string spelled)
    {
        var t = spelled.Trim();
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var q in new[] { "const ", "volatile ", "struct ", "class ", "enum " })
                if (t.StartsWith(q))
                {
                    t = t.Substring(q.Length).Trim();
                    changed = true;
                }
            if (t.EndsWith(" const"))
            {
                t = t.Substring(0, t.Length - 6).Trim();
                changed = true;
            }
        }
        if (t.EndsWith("&"))
            return Normalize(t.Substring(0, t.Length - 1));
        if (t.EndsWith("*"))
            return Normalize(t.Substring(0, t.Length - 1)) + "*";
        // Qt's scalar typedefs have to be desugared to the primitive the model's vocabulary
        // is written in (CppSharp's AST desugars them; the spelled type does not).
        return t switch
        {
            "qreal" => "double",
            "unsigned int" => "int",
            "unsigned long" => "int",
            "long long" => "int",
            "unsigned long long" => "int",
            "signed int" => "int",
            _ => t,
        };
    }
}
