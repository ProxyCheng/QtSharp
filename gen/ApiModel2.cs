using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClangSharp.Interop;

// The ClangSharp front end: the same model, read out of libclang instead of CppSharp's AST
// projection.
//
// Everything downstream of "the AST has been read" is shared with ApiModel.cs - the model shape,
// the ordering rules, the serializer, the signal scanner - so the two extractors can be diffed
// byte for byte while the CppSharp one is retired. What is *not* shared is exactly the layer that
// kept breaking: no Driver, no Module, no pass chain, no Type.Desugar(), no cross-TU declaration
// identity. A libclang cursor is a plain value and one parse of one translation unit is
// reproducible by construction.
public static partial class ApiModel
{
    // ------------------------------------------------------------------ extract2 entry
    public static int RunExtract2(string modelPath)
    {
        var clock = Stopwatch.StartNew();

        foreach (var root in ActiveRoots)
        {
            var header = Path.Combine(QtLibrary.QtIncludeRoot, root.Module, root.Header);
            if (!File.Exists(header))
            {
                Console.WriteLine($"[model2] FAILED: {root.Class} is declared in {root.Module}/{root.Header}, which does not exist");
                return 1;
            }
        }

        // All roots in one translation unit: the first header is the source file, the rest are
        // pre-included. This is the configuration CppSharp could not parse reproducibly; here the
        // parse is a straight function of its inputs.
        var headers = ActiveRoots.Select(HeaderOf).Distinct().ToList();
        Console.WriteLine($"[model2] parsing {headers.Count} header(s), {ActiveRoots.Length} class(es), one translation unit");

        using var index = CXIndex.Create(false, false);
        var parseClock = Stopwatch.StartNew();
        var error = CXTranslationUnit.TryParse(index, headers[0], ClangArgs(headers), default,
            CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies |
            CXTranslationUnit_Flags.CXTranslationUnit_KeepGoing,
            out var tu);
        if (error != CXErrorCode.CXError_Success)
        {
            Console.WriteLine($"[model2] FAILED: {error}");
            ReportDiagnostics(tu);
            return 1;
        }
        using var unit = tu;
        CurrentUnit = tu;
        Console.WriteLine($"[model2] parsed in {parseClock.Elapsed.TotalSeconds:F1}s");
        ReportDiagnostics(tu);

        var index2 = BuildIndex(tu);
        Console.WriteLine($"[model2] indexed {index2.Classes.Count} class(es), {index2.Enums.Count} enum(s)");

        var part = new ModelPart();
        Skipped2.Clear();
        ClassBases.Clear();

        // Signals still come from the header scan: they are metadata the C++ declarations do not
        // carry, and the scanner is the same for both front ends.
        var rootSignals = ActiveRoots
            .Select(r => (r.Module, r.Header))
            .Distinct()
            .SelectMany(h => Program.ScanSignals(h.Module, h.Header))
            .Where(s => RootClasses.Contains(s.ClassName))
            .ToList();

        var signals = rootSignals
            .Where(s => s.Params.Where(p => !p.Private)
                .All(p => Program.MapSignalType(p.CppType, out _, out _, out _, out _, out _)))
            .ToList();
        var allSignalNames = new HashSet<string>(rootSignals.Select(s => s.ClassName + "::" + s.SignalName));

        // A chunk is one header's roots. It matters for the "owned" rule, which is a relation
        // between classes of the same chunk in the CppSharp extractor.
        foreach (var chunk in ActiveRoots.GroupBy(r => (r.Module, r.Header)))
        {
            var chunkClasses = chunk.Select(r => r.Class).ToHashSet(StringComparer.Ordinal);

            // The whole chunk's base lists have to be known before any class is rendered: a chain
            // may be walked in either direction.
            foreach (var root in chunk)
                ClassBases[root.Class] = index2.Classes.TryGetValue(root.Class, out var record)
                    ? BaseNames(record)
                    : new List<string>();

            foreach (var root in chunk)
            {
                if (!index2.Classes.TryGetValue(root.Class, out var classRecord))
                {
                    Console.WriteLine($"[model2] WARNING: root class {root.Class} was not found in the parsed headers");
                    continue;
                }
                part.Classes[root.Class] = ClassJson(classRecord, root, index2, chunkClasses, signals, allSignalNames, part.Enums);
            }
            Console.WriteLine($"[model2]   {chunk.Key.Module}/{chunk.Key.Header}: {chunk.Count()} class(es)");
        }

        ReportCoverage2();

        var text = part.Serialize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(modelPath)));
        File.WriteAllText(modelPath, text);
        Console.WriteLine($"[model2] wrote {modelPath} ({part.Classes.Count} class(es), {part.Enums.Count} enum(s)) in {clock.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    static string HeaderOf(Root r) => Path.Combine(QtLibrary.QtIncludeRoot, r.Module, r.Header);

    // libClangSharp ships no builtin headers of its own; the build copies CppSharp's clang-19
    // resource folder next to the executable, and clang finds the MSVC STL by itself.
    static string ResourceDir()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "lib", "clang", "19");
        return Directory.Exists(Path.Combine(local, "include")) ? local : @"D:\QtSharp\lib\lib\clang\19";
    }

    static string[] ClangArgs(List<string> headers)
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
            "-I" + QtLibrary.QtIncludeRoot,
            "-I" + Path.Combine(QtLibrary.QtIncludeRoot, "QtCore"),
            "-I" + Path.Combine(QtLibrary.QtIncludeRoot, "QtGui"),
            "-I" + Path.Combine(QtLibrary.QtIncludeRoot, "QtWidgets"),
            "-resource-dir", ResourceDir(),
        };
        foreach (var header in headers.Skip(1))
        {
            args.Add("-include");
            args.Add(header);
        }
        return args.ToArray();
    }

    static void ReportDiagnostics(CXTranslationUnit tu)
    {
        for (uint i = 0; i < tu.NumDiagnostics; i++)
        {
            using var d = tu.GetDiagnostic(i);
            using var text = d.Format(CXDiagnosticDisplayOptions.CXDiagnostic_DisplaySourceLocation |
                                      CXDiagnosticDisplayOptions.CXDiagnostic_DisplayColumn);
            Console.WriteLine($"[model2]   {d.Severity}: {text}");
        }
    }

    // ------------------------------------------------------------------ cursor plumbing
    class ClangIndex
    {
        public Dictionary<string, CXCursor> Classes = new(StringComparer.Ordinal);
        // Enum declarations by qualified name (Qt::AlignmentFlag, QLineEdit::EchoMode, ...).
        public Dictionary<string, CXCursor> Enums = new(StringComparer.Ordinal);
    }

    // The translation unit currently being walked; one extraction at a time.
    static CXTranslationUnit CurrentUnit;

    // Depth-first walk over the translation unit using the raw visitor. (A lambda cannot live in
    // an unsafe context and the visitor delegate mentions a pointer type, hence the class.)
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

    sealed class Collector
    {
        public List<CXCursor> List;

        public unsafe CXChildVisitResult Visit(CXCursor cursor, CXCursor parent, void* data)
        {
            List.Add(cursor);
            return CXChildVisitResult.CXChildVisit_Continue;
        }
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

    // CursorChildren is a lazily built view, and walking a child's children while walking the
    // parent's invalidates it - always materialize first.
    static unsafe List<CXCursor> Children(CXCursor cursor)
    {
        var list = new List<CXCursor>();
        cursor.VisitChildren(new Collector { List = list }.Visit, default);
        return list;
    }

    static int ChildCount(CXCursor cursor) => Children(cursor).Count;

    static ClangIndex BuildIndex(CXTranslationUnit tu)
    {
        var index = new ClangIndex();
        var walker = new Walker
        {
            OnCursor = cursor =>
            {
                switch (clang.getCursorKind(cursor))
                {
                    case CXCursorKind.CXCursor_ClassDecl:
                    case CXCursorKind.CXCursor_StructDecl:
                    {
                        var name = clang.getCursorSpelling(cursor).ToString();
                        if (name.Length == 0)
                            break;
                        // The parse sees forward declarations of classes defined elsewhere; the
                        // definition is the one with members.
                        if (!index.Classes.TryGetValue(name, out var previous) ||
                            ChildCount(cursor) > ChildCount(previous))
                            index.Classes[name] = cursor;
                        break;
                    }
                    case CXCursorKind.CXCursor_EnumDecl:
                    {
                        if (clang.getCursorSpelling(cursor).ToString().Length == 0)
                            break;      // anonymous: not addressable from managed code
                        index.Enums[EnumTypeName(cursor)] = cursor;
                        break;
                    }
                }
            },
        };
        walker.Walk(tu.Cursor);
        return index;
    }

    // --------------------------------------------------------- clang type -> model type
    // The model's vocabulary, applied to a clang type. Like the CppSharp one it desugars
    // typedefs (qreal -> double) but keeps a *named* QFlags typedef, because that name is what
    // the managed surface publishes as a [Flags] enum.
    static string ModelName(CXType type)
    {
        type = Unwrap(type);

        if (type.kind == CXTypeKind.CXType_LValueReference ||
            type.kind == CXTypeKind.CXType_RValueReference)
            return ModelName(type.PointeeType);

        // A named QFlags typedef is the one typedef whose own name is published.
        var flags = FlagsName(type);
        if (flags != null)
            return flags;

        if (type.kind == CXTypeKind.CXType_Pointer)
        {
            var pointee = Unwrap(type.PointeeType);
            var builtin = BuiltinName(pointee.CanonicalType);
            if (builtin != null)
                // Only const input arrays; a non-const pointer means an out parameter whose
                // write-back managed code could not see, so it must not be bound.
                return builtin == "int" && pointee.IsConstQualified ? "int*" : null;
            var inner = ModelName(pointee);
            return inner == null ? null : inner + "*";
        }

        // Every other typedef resolves to what it names (qreal -> double, and a container's
        // const_iterator -> QRect*), which is what the CppSharp side sees through Desugar().
        if (type.kind == CXTypeKind.CXType_Typedef)
            return ModelName(type.CanonicalType);

        var primitive = BuiltinName(type);
        if (primitive != null)
            return primitive;

        var declaration = type.Declaration;
        var kind = clang.getCursorKind(declaration);
        if (kind == CXCursorKind.CXCursor_EnumDecl)
        {
            if (clang.getCursorSpelling(declaration).ToString().Length == 0)
                return null;
            return EnumTypeName(declaration);
        }
        if (kind == CXCursorKind.CXCursor_ClassDecl || kind == CXCursorKind.CXCursor_StructDecl)
        {
            // A template specialization has no place in the vocabulary: QList<int> is not a
            // bound type, so every method taking one is skipped rather than guessed.
            if (clang.getTypeSpelling(type).ToString().Contains("<"))
                return null;
            return clang.getCursorSpelling(declaration).ToString();
        }
        return null;
    }

    static CXType Unwrap(CXType type)
    {
        while (type.kind == CXTypeKind.CXType_Elaborated)
        {
            var named = type.NamedType;
            if (named.kind == CXTypeKind.CXType_Invalid)
                break;
            type = named;
        }
        return type;
    }

    static string BuiltinName(CXType type) => type.kind switch
    {
        CXTypeKind.CXType_Void => "void",
        CXTypeKind.CXType_Bool => "bool",
        CXTypeKind.CXType_Int => "int",
        CXTypeKind.CXType_Float => "float",
        CXTypeKind.CXType_Double => "double",
        _ => null,
    };

    // A QFlags<E> typedef publishes its own name (Qt::Alignment), not the template.
    static string FlagsName(CXType type)
    {
        var enumName = FlagsEnumName(type);
        return enumName == null ? null : QualifiedName(type.Declaration);
    }

    // The underlying enum of a QFlags<E> typedef, by qualified name.
    static string FlagsEnumName(CXType type)
    {
        type = Unwrap(type);
        if (type.kind != CXTypeKind.CXType_Typedef)
            return null;
        var spelling = clang.getTypeSpelling(type.CanonicalType).ToString();
        if (!spelling.StartsWith("QFlags<", StringComparison.Ordinal) || !spelling.EndsWith(">"))
            return null;
        return spelling.Substring("QFlags<".Length, spelling.Length - "QFlags<".Length - 1);
    }

    // The fully qualified name of an enum, as the model publishes it: QLineEdit::EchoMode,
    // Qt::AlignmentFlag, or just the name for a global enum.
    static string EnumTypeName(CXCursor enumCursor)
    {
        var name = clang.getCursorSpelling(enumCursor).ToString();
        var scope = ScopeOf(enumCursor);
        return scope.Length == 0 ? name : scope + "::" + name;
    }

    static string ScopeOf(CXCursor declaration)
    {
        var parent = clang.getCursorSemanticParent(declaration);
        return clang.getCursorKind(parent) switch
        {
            CXCursorKind.CXCursor_Namespace => clang.getCursorSpelling(parent).ToString(),
            CXCursorKind.CXCursor_ClassDecl => clang.getCursorSpelling(parent).ToString(),
            CXCursorKind.CXCursor_StructDecl => clang.getCursorSpelling(parent).ToString(),
            CXCursorKind.CXCursor_ClassTemplate => clang.getCursorSpelling(parent).ToString(),
            _ => string.Empty,
        };
    }

    static string QualifiedName(CXCursor declaration)
    {
        var name = clang.getCursorSpelling(declaration).ToString();
        var scope = ScopeOf(declaration);
        return scope.Length == 0 ? name : scope + "::" + name;
    }

    // A non-const reference to something that is not a bound class is an out parameter
    // (QClipboard::text(QString &subtype)): managed code would pass a temporary and never see the
    // write-back, so it must not be bound - the same rule the non-const int* case follows. A
    // reference to a bound class is fine: the managed object *is* the native object, which is why
    // Qt's own swap(QBrush &other) keeps working.
    static bool OutParameter(CXType type)
    {
        type = Unwrap(type);
        if (type.kind != CXTypeKind.CXType_LValueReference || type.PointeeType.IsConstQualified)
            return false;
        var pointee = ModelName(type.PointeeType);
        return pointee == null || !RootClasses.Contains(pointee);
    }

    // The class/enum vocabulary the model is allowed to express.
    static bool SupportedType(CXType type, bool isParameter = false)
    {
        if (isParameter && OutParameter(type))
            return false;
        var name = ModelName(type);
        if (name == null)
            return false;
        if (name.EndsWith("*"))
            return SupportedTypes.Contains(name);

        var flags = FlagsName(type);
        if (flags != null)
            return ScopeIsPublishable(ScopeOfQualified(flags));

        if (SupportedTypes.Contains(name))
            return true;

        var declaration = type.CanonicalType.Declaration;
        if (clang.getCursorKind(declaration) != CXCursorKind.CXCursor_EnumDecl)
            return false;
        return ScopeIsPublishable(ScopeOfQualified(EnumTypeName(declaration)));
    }

    static string DescribeBlocked(CXType type)
    {
        var name = ModelName(type);
        if (name != null)
            return name;
        var spelling = clang.getTypeSpelling(type).ToString();
        if (spelling.Length == 0)
            return clang.getTypeKindSpelling(type.kind).ToString();
        var open = spelling.IndexOf('<');
        return open < 0 ? spelling : spelling.Substring(0, open) + "<...>";
    }

    // --------------------------------------------------------------- facts and rendering
    class ParamFact
    {
        public string Name;
        public string Type;         // model type, null when the vocabulary cannot express it
        public CXType ClangType;
        public bool Optional;       // the C++ declaration has a default here
        public string Default;      // a literal the code generator can emit, or null
    }

    class FnFact
    {
        public CXCursor Cursor;
        public string Name;
        public bool IsStatic;
        public bool IsVirtual;
        public bool IsPure;
        public bool IsCtor;
        public string ReturnType;
        public CXType ReturnClangType;
        public List<ParamFact> Params = new();
        // The longest marshalable prefix of the declaration: what the model emits.
        public List<ParamFact> Visible = new();
    }

    static string ClassJson(CXCursor record, Root root, ClangIndex index, HashSet<string> chunkClasses,
                            List<Program.SignalInfo> signals, HashSet<string> allSignalNames,
                            Dictionary<string, EnumEntry> enums)
    {
        var className = clang.getCursorSpelling(record).ToString();
        var isAbstract = clang.CXXRecord_isAbstract(record) != 0;
        var bases = ClassBases[className];
        // A class whose destructor is private cannot be derived from in C++ at all (QClipboard,
        // QStyleHints): there is no way to declare the native subclass that installs the vtable
        // hooks, so the class is published without one.
        var subclassable = IsSubclassable(record);

        var callable = Children(record).Select(Member).Where(m => m != null).ToList();

        // An abstract class cannot be instantiated: its C++ pure virtuals are not implemented by
        // the generated subclass, so offering constructors would only produce a wrapper that
        // fails at link time (or worse, at run time).
        var ctors = BindsCtors(className) && !isAbstract
            ? callable.Where(m => m.IsCtor)
                .Where(m => Access(m.Cursor) == CX_CXXAccessSpecifier.CX_CXXPublic)
                // CppSharp only marks copy constructors after its own passes, so they have to be
                // recognised by shape here (QColor(const QColor&)).
                .Where(m => m.Visible.Count != 1 || m.Visible[0].Type != className)
                .Where(Accept)
                .GroupBy(CtorDedupeKey)
                .Select(g => g.First())
                .OrderBy(m => m.Params.Count)
                .ThenBy(SignatureKey, StringComparer.Ordinal)
                .ToList()
            : new List<FnFact>();

        var methods = callable
            // A virtual the model does not offer as an override point is still part of the API:
            // it belongs here as a plain callable member (QAbstractItemView::setModel,
            // QDialog::accept, QWidget::sizeHint). Only the whitelisted ones are published as
            // overridable C# virtuals instead.
            .Where(m => !m.IsCtor && !(m.IsVirtual && VirtualAllow.Contains(m.Name)))
            // Qt's "qt_" members are macro scaffolding (Q_OBJECT), not API, and have no exported
            // symbol either.
            .Where(m => !m.Name.StartsWith("qt_", StringComparison.Ordinal))
            .Where(m => !m.Name.StartsWith("operator", StringComparison.Ordinal))
            .Where(m => Access(m.Cursor) == CX_CXXAccessSpecifier.CX_CXXPublic)
            .Where(m => !allSignalNames.Contains(className + "::" + m.Name))
            .Where(Accept)
            .GroupBy(DedupeKey)
            .Select(g => g.First())
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Params.Count)
            .ThenBy(SignatureKey, StringComparer.Ordinal)
            .ToList();

        // Only the virtuals this class declares, and only the names a managed override may
        // install. The dispatch chain is merged from the model by the code generator.
        var virtuals = callable
            .Where(m => m.IsVirtual && !m.IsCtor)
            .Where(m => VirtualAllow.Contains(m.Name))
            .Where(Accept)
            .GroupBy(DedupeKey)
            .Select(g => g.First())
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Params.Count)
            .ToList();

        // A signal is exposed as a property named after it, so two of the same name cannot
        // coexist.
        var classSignals = signals
            .Where(s => s.ClassName == className)
            .GroupBy(s => s.SignalName + "(" + string.Join(",", s.Params.Where(p => !p.Private).Select(p => p.CppType)) + ")")
            .Select(g => g.First())
            .OrderBy(s => s.SignalName, StringComparer.Ordinal)
            .ToList();

        foreach (var fn in ctors.Concat(methods).Concat(virtuals))
            CollectEnums2(fn.ReturnClangType, fn.Visible, index, enums);

        var body = new StringBuilder();
        body.Append("    {\n");
        body.Append($"      \"name\": \"{className}\",\n");
        body.Append($"      \"header\": \"{root.Module}/{root.Header}\",\n");
        body.Append($"      \"bases\": [{string.Join(", ", bases.Select(Json))}],\n");
        body.Append($"      \"isAbstract\": {(isAbstract ? "true" : "false")},\n");
        body.Append($"      \"subclassable\": {(subclassable ? "true" : "false")},\n");
        body.Append($"      \"owned\": {(OwnedOf(root, chunkClasses) ? "true" : "false")},\n");
        WriteConstructors2(body, ctors);
        WriteFunctions2(body, "methods", methods);
        WriteVirtuals2(body, virtuals);
        WriteSignals(body, classSignals);
        body.Append("    }\n");
        return body.ToString();
    }

    // Whether C++ lets us derive from the class at all: a private (or deleted) destructor makes
    // every subclass undestructible, which MSVC rejects outright.
    static bool IsSubclassable(CXCursor record)
    {
        foreach (var child in Children(record))
        {
            if (clang.getCursorKind(child) != CXCursorKind.CXCursor_Destructor)
                continue;
            if (Access(child) == CX_CXXAccessSpecifier.CX_CXXPrivate)
                return false;
            if (clang.CXXMethod_isDeleted(child) != 0)
                return false;
        }
        return true;
    }

    // The base classes that are bound types, in declaration order. Template bases
    // (QPolygon : QList<QPoint>) are not modelled.
    static List<string> BaseNames(CXCursor record)
    {
        var bases = new List<string>();
        foreach (var child in Children(record))
        {
            if (clang.getCursorKind(child) != CXCursorKind.CXCursor_CXXBaseSpecifier)
                continue;
            var name = ModelName(clang.getCursorType(child));
            if (name == null || name.EndsWith("*") || !RootClasses.Contains(name))
                continue;
            bases.Add(name);
        }
        return bases;
    }

    // Filled for a whole chunk before any of its classes is rendered, because the walk can go
    // either way along the chain.
    static readonly Dictionary<string, List<string>> ClassBases = new(StringComparer.Ordinal);

    // Qt owns everything under QObject; a class the model marked "owned" and that is not in
    // QObject's chunk may be deleted by the managed side.
    static bool OwnedOf(Root root, HashSet<string> chunkClasses)
        => root.Owned && !DerivesFromQObject(root.Class, chunkClasses);

    static bool DerivesFromQObject(string className, HashSet<string> chunkClasses)
    {
        if (className == "QObject")
            return true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = className; current != null && seen.Add(current);)
        {
            if (!ClassBases.TryGetValue(current, out var bases) || bases.Count == 0)
                return false;
            if (bases[0] == "QObject")
                return true;
            if (!chunkClasses.Contains(bases[0]))
                return false;       // the chain crosses a chunk boundary
            current = bases[0];
        }
        return false;
    }

    // One class-body child that is a function: methods, constructors, operators and templates.
    static unsafe FnFact Member(CXCursor cursor)
    {
        var kind = clang.getCursorKind(cursor);
        if (kind != CXCursorKind.CXCursor_Constructor &&
            kind != CXCursorKind.CXCursor_CXXMethod &&
            kind != CXCursorKind.CXCursor_FunctionTemplate)
            return null;

        // Qt's Q_WEAK_OVERLOAD is "template <typename = void>", so a plain-looking member comes
        // back as a function template: libclang reports no arguments for the template cursor
        // itself, and the signature lives in the templated declaration underneath it - or, when
        // there is none, in the function type (which loses only the names and defaults).
        var handle = cursor;
        if (kind == CXCursorKind.CXCursor_FunctionTemplate)
        {
            if (!CallableTemplate(PrettyPrinted(cursor)))
                return null;
            var inner = new InnerDecl();
            cursor.VisitChildren(inner.Visit, default);
            if (inner.Found)
                handle = inner.Cursor;
        }

        var isCtor = clang.getCursorKind(handle) == CXCursorKind.CXCursor_Constructor;
        var fn = new FnFact
        {
            Cursor = handle,
            Name = clang.getCursorSpelling(handle).ToString(),
            IsCtor = isCtor,
            IsStatic = clang.CXXMethod_isStatic(handle) != 0,
            IsVirtual = clang.CXXMethod_isVirtual(handle) != 0,
            IsPure = clang.CXXMethod_isPureVirtual(handle) != 0,
            ReturnType = isCtor ? "void" : ModelName(clang.getCursorResultType(handle)),
            ReturnClangType = clang.getCursorResultType(handle),
        };
        if (fn.Name.Length == 0)
            fn.Name = clang.getCursorSpelling(cursor).ToString();

        int count = clang.Cursor_getNumArguments(handle);
        if (count < 0)
        {
            var signature = clang.getCursorType(handle);
            var names = TemplateParamNames(cursor);
            for (uint i = 0; i < (uint)signature.NumArgTypes; i++)
            {
                var type = signature.GetArgType(i);
                var spelled = names != null && i < names.Count ? names[(int)i] : null;
                fn.Params.Add(new ParamFact
                {
                    Name = spelled ?? "arg" + i,
                    Type = ModelName(type),
                    ClangType = type,
                });
            }
        }
        else
        {
            for (uint i = 0; i < count; i++)
            {
                var arg = clang.Cursor_getArgument(handle, i);
                var spelled = clang.getCursorSpelling(arg).ToString();
                var type = clang.getCursorType(arg);
                var fact = new ParamFact
                {
                    Name = spelled.Length > 0 ? spelled : "arg" + i,
                    Type = ModelName(type),
                    ClangType = type,
                };
                // libclang has no "has default argument" query: the default argument shows up as
                // the expression child of the ParmDecl cursor.
                var probe = new FirstExpr();
                arg.VisitChildren(probe.Visit, default);
                if (probe.HasRange)
                {
                    fact.Optional = true;
                    var text = SourceText(probe.Range);
                    fact.Default = DefaultLiteral2(text, fact.Type);
                    if (Environment.GetEnvironmentVariable("QTSHARP_MODEL_DEBUG") == "1")
                        Console.WriteLine($"[model2-debug] {fn.Name}({fact.Name}) default: '{text}' kind={clang.getCursorKind(probe.Cursor).ToString().Replace("CXCursor_", "")} type={fact.Type} literal={fact.Default}");
                }
                fn.Params.Add(fact);
            }
        }

        fn.Visible = VisibleParamsOf(fn);
        return fn;
    }

    static CX_CXXAccessSpecifier Access(CXCursor cursor) => clang.getCXXAccessSpecifier(cursor);

    static readonly HashSet<string> TypeWords = new(StringComparer.Ordinal)
    {
        "void", "bool", "char", "short", "int", "long", "float", "double", "signed", "unsigned",
        "const", "volatile", "auto", "wchar_t", "char8_t", "char16_t", "char32_t", "size_t",
        "qreal", "qsizetype", "qint8", "quint8", "qint16", "quint16", "qint32", "quint32",
        "qint64", "quint64", "qlonglong", "qulonglong", "uint", "ulong", "ushort", "uchar",
    };

    static unsafe string PrettyPrinted(CXCursor cursor)
    {
        var policy = clang.getCursorPrintingPolicy(cursor);
        try
        {
            using var printed = clang.getCursorPrettyPrinted(cursor, policy);
            return printed.ToString();
        }
        finally
        {
            clang.PrintingPolicy_dispose(policy);
        }
    }

    // A function template can only be called without an explicit template argument when every
    // template parameter has a default. Qt's Q_WEAK_OVERLOAD is "template <typename = void>", so
    // it is callable; QVariant::canConvert<T>() is not, and there is nothing to bind.
    static bool CallableTemplate(string pretty)
    {
        int open = pretty.IndexOf('<');
        if (open < 0)
            return true;
        int depth = 0, close = -1;
        for (int i = open; i < pretty.Length; i++)
        {
            if (pretty[i] == '<') depth++;
            else if (pretty[i] == '>' && --depth == 0) { close = i; break; }
        }
        if (close < 0)
            return false;
        foreach (var parameter in SplitAtTopLevel(pretty.Substring(open + 1, close - open - 1)))
            if (!parameter.Contains('='))
                return false;
        return true;
    }

    // libclang does not expose the declaration inside a function template, so the parameter names
    // are recovered from clang's pretty-printed declaration. Only the names are missing; the
    // types come from the function type.
    static List<string> TemplateParamNames(CXCursor cursor)
    {
        var text = PrettyPrinted(cursor);
        int open = text.IndexOf('(');
        if (open < 0)
            return null;
        int depth = 0, close = -1;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) { close = i; break; }
        }
        if (close < 0)
            return null;

        var names = new List<string>();
        foreach (var part in SplitAtTopLevel(text.Substring(open + 1, close - open - 1)))
        {
            // Drop a default argument, then the name is the last identifier of what is left.
            var declaration = part;
            int equals = IndexAtTopLevel(declaration, '=');
            if (equals >= 0)
                declaration = declaration.Substring(0, equals);
            var tokens = declaration.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var last = tokens.Length == 0 ? null : tokens[tokens.Length - 1].Trim('*', '&');
            names.Add(last != null && IsIdentifier(last) && !TypeWords.Contains(last) ? last : null);
        }
        return names;
    }

    static bool IsIdentifier(string text)
    {
        if (text.Length == 0 || (!char.IsLetter(text[0]) && text[0] != '_'))
            return false;
        return text.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    static IEnumerable<string> SplitAtTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(' || c == '<' || c == '[') depth++;
            else if (c == ')' || c == '>' || c == ']') depth--;
            else if (c == ',' && depth == 0) { parts.Add(text.Substring(start, i - start)); start = i + 1; }
        }
        if (start < text.Length)
            parts.Add(text.Substring(start));
        return parts.Where(p => p.Trim().Length > 0);
    }

    static int IndexAtTopLevel(string text, char needle)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(' || c == '<' || c == '[') depth++;
            else if (c == ')' || c == '>' || c == ']') depth--;
            else if (c == needle && depth == 0) return i;
        }
        return -1;
    }

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

    sealed class FirstExpr
    {
        public CXCursor Cursor;
        public CXSourceRange Range;
        public bool HasRange;

        public unsafe CXChildVisitResult Visit(CXCursor cursor, CXCursor parent, void* data)
        {
            // A ParmDecl's first children are the type references of its own type; the default
            // argument is the expression child.
            var kind = clang.getCursorKind(cursor);
            if (!HasRange && kind >= CXCursorKind.CXCursor_FirstExpr && kind <= CXCursorKind.CXCursor_LastExpr)
            {
                Cursor = cursor;
                Range = clang.getCursorExtent(cursor);
                HasRange = true;

                // A cast or a parenthesised default is rendered as its operand, which is what the
                // CppSharp side sees: static_cast<Qt::MatchFlags>(Qt::A|Qt::B) -> Qt::A|Qt::B.
                if (IsTransparent(kind))
                {
                    var inner = new FirstExpr();
                    cursor.VisitChildren(inner.Visit, default);
                    if (inner.HasRange)
                    {
                        Cursor = inner.Cursor;
                        Range = inner.Range;
                    }
                }
            }
            return CXChildVisitResult.CXChildVisit_Continue;
        }

        static bool IsTransparent(CXCursorKind kind) =>
            kind == CXCursorKind.CXCursor_CXXStaticCastExpr ||
            kind == CXCursorKind.CXCursor_CXXFunctionalCastExpr ||
            kind == CXCursorKind.CXCursor_CStyleCastExpr ||
            kind == CXCursorKind.CXCursor_CXXConstCastExpr ||
            kind == CXCursorKind.CXCursor_CXXReinterpretCastExpr ||
            kind == CXCursorKind.CXCursor_CXXDynamicCastExpr ||
            kind == CXCursorKind.CXCursor_ParenExpr;
    }

    // The source text of a range, using spelling locations: a declaration that came out of a
    // macro (Q_OBJECT's tr/metaObject/...) otherwise resolves to the macro invocation token.
    static string SourceText(CXSourceRange range)
    {
        var start = clang.getRangeStart(range);
        var end = clang.getRangeEnd(range);
        start.GetSpellingLocation(out var file, out _, out _, out var from);
        end.GetSpellingLocation(out var toFile, out _, out _, out var to);
        if (file.Handle == IntPtr.Zero || file.Handle != toFile.Handle || to <= from)
            return null;
        var contents = CurrentUnit.GetFileContents(file, out var size);
        if (to > (ulong)size)
            return null;
        var text = Encoding.UTF8.GetString(contents.Slice((int)from, (int)(to - from)));
        return string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }

    // --------------------------------------------------------------- model rules
    // The parameters we can actually marshal: the *longest prefix* of the declaration.
    //
    // Dropping an unmarshalable parameter is only safe if it is defaulted, because C++ then
    // guarantees that everything after it is defaulted too. A required parameter we cannot
    // marshal rejects the whole declaration instead (it is kept here so the signature check
    // fails).
    static List<ParamFact> VisibleParamsOf(FnFact fn)
    {
        var result = new List<ParamFact>();
        foreach (var p in fn.Params)
        {
            if (p.Type != null && SupportedType(p.ClangType, isParameter: true))
            {
                // A boxed value parameter whose optional default we cannot express (QRect(),
                // QVariant(), QSize(-1, -1)) would reach the generated glue as a null handle and
                // be dereferenced. Leaving it out makes the C++ default apply, which is what the
                // declaration meant.
                if (p.Optional && p.Default == null && RootClasses.Contains(p.Type))
                    break;
                result.Add(p);
                continue;
            }
            if (!p.Optional)
                result.Add(p);      // not droppable: poison the signature so the method is rejected
            break;
        }
        return result;
    }

    static bool Accept(FnFact fn)
    {
        var blocker = SupportedType(fn.ReturnClangType) ? null : DescribeBlocked(fn.ReturnClangType);
        if (blocker == null)
        {
            foreach (var p in fn.Visible)
            {
                if (p.Type != null && SupportedType(p.ClangType, isParameter: true))
                    continue;
                blocker = DescribeBlocked(p.ClangType);
                break;
            }
        }
        if (blocker == null)
            return true;

        Skipped2.TryGetValue(blocker, out int n);
        Skipped2[blocker] = n + 1;
        return false;
    }

    // Overloads are keyed by the *managed* signature: two C++ overloads that surface as the same
    // C# method cannot both be generated. Qt regularly declares a view overload next to the
    // owning type (QObject::setObjectName(QString) / (QAnyStringView)); both become
    // `SetObjectName(string)`, so only the first declaration is kept.
    static string DedupeKey(FnFact fn)
        => fn.Name + "(" + string.Join(",", fn.Visible.Select(p => ApiTypes.Managed(p.Type ?? "?", RootClasses.ToArray()))) + ")";

    // Constructors all surface as the same C# name, so overloads that only differed by a dropped
    // parameter have to be folded by shape alone.
    static string CtorDedupeKey(FnFact fn)
        => "(" + string.Join(",", fn.Visible.Select(p => ApiTypes.Managed(p.Type ?? "?", RootClasses.ToArray()))) + ")";

    static string SignatureKey(FnFact fn)
        => fn.Name + "(" + string.Join(",", fn.Visible.Select(p => p.Type ?? "?")) + ")";

    // A default argument is only handed to C# when it is a plain literal; anything else becomes a
    // type default. Enumerators matter here: `default` is the zero member, which is not the same
    // as the declared default (Qt::BrushStyle's zero is NoBrush, while QBrush::QBrush declares
    // SolidPattern), so a named enumerator is carried over as the matching C# enum member.
    static string DefaultLiteral2(string text, string type)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (text is "nullptr" or "NULL" or "0")
            return type != null && (type.EndsWith("*") || type == "QString" || RootClasses.Contains(type)) ? "null" : "0";
        if (text is "true" or "false")
            return text;
        if (type != null && type.Contains("::") && text.Contains("::") && !text.Contains("("))
        {
            var item = text.Substring(text.LastIndexOf("::", StringComparison.Ordinal) + 2);
            if (item.Length > 0 && item.All(char.IsLetterOrDigit))
                return ApiTypes.Managed(type, RootClasses.ToArray()) + "." + item;
        }
        var isHex = text.StartsWith("0x") || text.StartsWith("0X");
        var digits = isHex ? text.Substring(2) : text;
        try
        {
            return Convert.ToInt64(digits, isHex ? 16 : 10).ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    // Any enum that reaches an accepted signature is part of the model; enums are open (plain
    // integers with no further reachability), but their scope has to be somewhere managed code
    // can put them.
    static void CollectEnums2(CXType returnType, List<ParamFact> parameters, ClangIndex index,
                              Dictionary<string, EnumEntry> enums)
    {
        Visit(returnType);
        foreach (var p in parameters)
            Visit(p.ClangType);

        void Visit(CXType type)
        {
            var flags = FlagsName(type);
            if (flags != null)
            {
                var underlying = FlagsEnumName(type);
                if (underlying != null && index.Enums.TryGetValue(underlying, out var enumCursor))
                    enums[flags] = new EnumEntry
                    {
                        Name = flags.Substring(flags.LastIndexOf("::", StringComparison.Ordinal) + 2),
                        Qualified = flags,
                        Items = EnumItems(enumCursor),
                        Flags = true,
                    };
                return;
            }
            var declaration = type.CanonicalType.Declaration;
            if (clang.getCursorKind(declaration) != CXCursorKind.CXCursor_EnumDecl)
                return;
            if (clang.getCursorSpelling(declaration).ToString().Length == 0)
                return;
            var name = EnumTypeName(declaration);
            enums[name] = new EnumEntry
            {
                Name = clang.getCursorSpelling(declaration).ToString(),
                Qualified = name,
                Items = EnumItems(declaration),
                Flags = false,
            };
        }
    }

    static List<(string Name, long Value)> EnumItems(CXCursor enumCursor)
    {
        var items = new List<(string, long)>();
        foreach (var child in Children(enumCursor))
        {
            if (clang.getCursorKind(child) != CXCursorKind.CXCursor_EnumConstantDecl)
                continue;
            items.Add((clang.getCursorSpelling(child).ToString(), clang.getEnumConstantDeclValue(child)));
        }
        return items;
    }

    // ------------------------------------------------------------------ rendering
    static void WriteConstructors2(StringBuilder sb, List<FnFact> ctors)
    {
        sb.Append("      \"constructors\": [\n");
        for (int i = 0; i < ctors.Count; i++)
        {
            sb.Append("        { \"parameters\": [");
            sb.Append(string.Join(", ", ctors[i].Visible.Select(ParamJson2)));
            sb.Append("] }");
            sb.Append(i + 1 < ctors.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    static void WriteFunctions2(StringBuilder sb, string key, List<FnFact> methods)
    {
        sb.Append($"      \"{key}\": [\n");
        for (int i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            sb.Append("        { \"name\": ").Append(Json(m.Name));
            if (m.IsStatic)
                sb.Append(", \"static\": true");
            sb.Append(", \"returnType\": ").Append(Json(m.ReturnType));
            sb.Append(", \"parameters\": [");
            sb.Append(string.Join(", ", m.Visible.Select(ParamJson2)));
            sb.Append("] }");
            sb.Append(i + 1 < methods.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    static void WriteVirtuals2(StringBuilder sb, List<FnFact> virtuals)
    {
        sb.Append("      \"virtuals\": [\n");
        for (int i = 0; i < virtuals.Count; i++)
        {
            var v = virtuals[i];
            sb.Append("        { \"name\": ").Append(Json(v.Name));
            // A pure virtual has no C++ implementation to chain to (QAbstractButton::paintEvent
            // is declared "= 0"), so the code generator must not emit a base call for it.
            if (v.IsPure)
                sb.Append(", \"pure\": true");
            sb.Append(", \"returnType\": ").Append(Json(v.ReturnType));
            sb.Append(", \"parameters\": [");
            sb.Append(string.Join(", ", v.Visible.Select(ParamJson2)));
            sb.Append("] }");
            sb.Append(i + 1 < virtuals.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    static string ParamJson2(ParamFact p)
    {
        var sb = new StringBuilder();
        sb.Append("{ \"name\": ").Append(Json(p.Name));
        sb.Append(", \"type\": ").Append(Json(p.Type));
        if (p.Optional)
        {
            sb.Append(", \"optional\": true");
            if (p.Default != null)
                sb.Append(", \"default\": ").Append(Json(p.Default));
        }
        sb.Append(" }");
        return sb.ToString();
    }

    // What is *not* in the model and why: growing the model is a matter of widening the type
    // vocabulary, so the interesting number is which type blocks the most declarations.
    static readonly Dictionary<string, int> Skipped2 = new(StringComparer.Ordinal);

    static void ReportCoverage2()
    {
        foreach (var kv in Skipped2.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(20))
            Console.WriteLine($"[model2]   blocked by {kv.Key} x{kv.Value}");
    }
}
