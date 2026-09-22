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
        DropStaleIncludeEntries();

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

        // The extension points a managed subclass has to be able to implement: publishing them is
        // a *name* closure, because a name published only on the class that declares it would
        // leave concrete subclasses (QStandardItemModel::data) without an implementation to chain
        // to. Done before any class is rendered, so every class sees the same set.
        CollectVirtualExtensionNames(index2);
        Console.WriteLine($"[model2] {VirtualExtensionNames.Count} virtual name(s) published for {VirtualExtensionClasses.Length} extension class(es)");

        var part = new ModelPart();
        Skipped2.Clear();
        ClassBases.Clear();
        extensionTree.Clear();

        // Signals are *identified* by the header scan: moc's Q_SIGNALS leaves no trace clang can
        // see, and QT_ANNOTATE_ACCESS_SPECIFIER expands to nothing in a normal build. Their
        // parameters, however, are read out of the AST like every other member's (see
        // SignalFactsOf) - that is what makes a QModelIndex, a QWidget*, an enum or a container
        // expressible in a signal instead of the scalar-only vocabulary the text scan could guess.
        var rootSignals = ActiveRoots
            .Select(r => (r.Module, r.Header))
            .Distinct()
            .SelectMany(h => Program.ScanSignals(h.Module, h.Header))
            .Where(s => RootClasses.Contains(s.ClassName))
            .ToList();

        // Every signal name (published or not) so the *signal* is never also published as a plain
        // callable method: a signal is emit-only API, even when its parameters are beyond the
        // vocabulary.
        var allSignalNames = new HashSet<string>(rootSignals.Select(s => s.ClassName + "::" + s.SignalName));

        // Q_PROPERTY declarations come from the same header scan; which of them can be published
        // is decided per class, once the accessors they name are known.
        var rootProperties = ActiveRoots
            .Select(r => (r.Module, r.Header))
            .Distinct()
            .SelectMany(h => Program.ScanProperties(h.Module, h.Header))
            .Where(p => RootClasses.Contains(p.ClassName))
            .ToList();
        Console.WriteLine($"[model2] scanned {rootProperties.Count} Q_PROPERTY declaration(s)");

        // One group per header. The grouping only makes the log readable now (the "owned" rule
        // that used to depend on it is gone); the class *order* in the model is sorted anyway.
        foreach (var chunk in ActiveRoots.GroupBy(r => (r.Module, r.Header)))
        {
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
                part.Classes[root.Class] = ClassJson(classRecord, root, index2, rootSignals, allSignalNames, rootProperties, part.Enums);
            }
            Console.WriteLine($"[model2]   {chunk.Key.Module}/{chunk.Key.Header}: {chunk.Count()} class(es)");
        }

        ReportCoverage2();

        // Enums that are part of the published vocabulary but never appear in a signature.
        CollectExtraEnums(index2, part.Enums);

        var text = part.Serialize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(modelPath)));
        File.WriteAllText(modelPath, text);
        Console.WriteLine($"[model2] wrote {modelPath} ({part.Classes.Count} class(es), {part.Enums.Count} enum(s)) in {clock.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    static string HeaderOf(Root r) => Path.Combine(QtLibrary.QtIncludeRoot, r.Module, r.Header);

    // libClangSharp ships no builtin headers of its own (they are what <stddef.h> and friends come
    // from), so gen.csproj copies a clang resource folder next to the executable - the glob under
    // lib\lib\clang - and clang is pointed at it; the MSVC STL and the Windows SDK it finds by
    // itself. Any clang version works, the highest one wins if several are present, and
    // QTSHARP_CLANG_RESOURCE_DIR overrides the search for a tree kept somewhere else.
    static string ResourceDir()
    {
        var configured = Environment.GetEnvironmentVariable("QTSHARP_CLANG_RESOURCE_DIR");
        if (!string.IsNullOrEmpty(configured))
            return configured;

        foreach (var root in new[] { AppContext.BaseDirectory, QtLibrary.RepoRoot })
        {
            if (string.IsNullOrEmpty(root))
                continue;
            var libClang = Path.Combine(root, "lib", "clang");
            if (!Directory.Exists(libClang))
                continue;
            foreach (var version in Directory.GetDirectories(libClang).OrderByDescending(d => d, StringComparer.Ordinal))
                if (File.Exists(Path.Combine(version, "include", "stddef.h")))
                    return version;
        }

        throw new InvalidOperationException(
            "clang's builtin headers are missing: put a clang resource folder at "
            + Path.Combine("lib", "lib", "clang", "<version>", "include") + " (gen.csproj copies it "
            + "next to the generator), or set QTSHARP_CLANG_RESOURCE_DIR. See README.md.");
    }


    // A stale INCLUDE - the usual leftover of a Visual Studio that was uninstalled or moved - is
    // taken by clang *instead of* the toolchain it would otherwise detect, and then every
    // <cstddef>/<type_traits> the Qt headers include goes missing. The damage is silent: with
    // delayed template parsing and MSVC compatibility, unknown types recover as int, so
    // std::size_t stops being 8 bytes and qsizetype comes out as int - a model that differs from
    // the checked-in one in hundreds of places with no error to explain it. Drop the entries that
    // do not exist, and let clang detect the toolchain when none survive.
    static void DropStaleIncludeEntries()
    {
        var include = Environment.GetEnvironmentVariable("INCLUDE");
        if (string.IsNullOrEmpty(include))
            return;

        var entries = include.Split(';').Where(e => e.Length > 0).ToArray();
        var live = entries.Where(Directory.Exists).ToArray();
        if (live.Length == entries.Length)
            return;

        Console.WriteLine($"[model2] INCLUDE: dropped {entries.Length - live.Length} path(s) that do not exist"
            + (live.Length == 0 ? " - clang will detect the MSVC toolchain itself" : ""));
        Environment.SetEnvironmentVariable("INCLUDE", live.Length == 0 ? null : string.Join(";", live));
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

    // The index of the translation unit being extracted. Default-literal translation needs it:
    // `QNativeIpcKey::DefaultTypeForOs` looks exactly like an enumerator but is a static constexpr
    // member of the class, and only the enum's own declaration can tell the two apart.
    static ClangIndex CurrentIndex;

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
        CurrentIndex = index;
        return index;
    }

    // --------------------------------------------------------- clang type -> model type
    // The model's vocabulary, applied to a clang type. Like the CppSharp one it desugars
    // typedefs (qreal -> double) but keeps a *named* QFlags typedef, because that name is what
    // the managed surface publishes as a [Flags] enum.
    // The std::chrono durations Qt uses for its "as a duration" overloads (QTimer::setInterval,
    // QThread::sleep, QDeadlineTimer, QElapsedTimer): they all cross as a managed TimeSpan. The
    // unit stays in the clang spelling, which the code generator uses for the conversion.
    static readonly HashSet<string> ChronoDurations = new(StringComparer.Ordinal)
    {
        "std::chrono::nanoseconds", "std::chrono::microseconds", "std::chrono::milliseconds",
        "std::chrono::seconds", "std::chrono::minutes", "std::chrono::hours",
    };

    static bool IsChronoDuration(CXType type)
        => ChronoDurations.Contains(clang.getTypeSpelling(type).ToString())
           || ChronoDurations.Contains(clang.getTypeSpelling(type.CanonicalType).ToString());

    static string ModelName(CXType type)
    {
        type = Unwrap(type);

        // Checked before the typedef/class branches: the canonical spelling of
        // std::chrono::milliseconds is a duration<...> specialization, which would lose the unit.
        if (IsChronoDuration(type))
            return "TimeSpan";

        if (type.kind == CXTypeKind.CXType_LValueReference ||
            type.kind == CXTypeKind.CXType_RValueReference)
            return ModelName(type.PointeeType);

        // A named QFlags typedef is the one typedef whose own name is published.
        var flags = FlagsName(type);
        if (flags != null)
            return flags;

        // QStringList is QList<QString> and keeps its own name (the signals already use it, and
        // the runtime has had a bridge for it all along). Any other QList<E> whose element type
        // the vocabulary can express becomes "QList<E>"; the rest stay unbound rather than
        // guessed.
        if (IsQStringList(type))
            return "QStringList";
        if (IsQList(type, out var element))
            return "QList<" + element + ">";
        // QMap<K, V> and QHash<K, V> keep both halves of the template; QMultiMap/QMultiHash are
        // different classes and stay unbound.
        if (IsAssoc(type, "QMap", out var mapKey, out var mapValue))
            return "QMap<" + mapKey + "," + mapValue + ">";
        if (IsAssoc(type, "QHash", out var hashKey, out var hashValue))
            return "QHash<" + hashKey + "," + hashValue + ">";
        // QBindable<T> is the handle Qt's property-binding API hands out (bindableFoo()).
        if (IsQBindable(type, out var bound))
            return "QBindable<" + bound + ">";

        if (type.kind == CXTypeKind.CXType_Pointer)
        {
            var pointee = Unwrap(type.PointeeType);
            var canonical = pointee.CanonicalType;
            // Qt's other string convention: a NUL-terminated UTF-8 buffer, always const (a
            // writable char buffer is an out parameter and stays unbound).
            if (IsChar(canonical))
                return pointee.IsConstQualified ? "char*" : null;
            var builtin = BuiltinName(canonical);
            // A byte buffer with the length passed separately (QImage's pixel data and the other
            // raw-data entry points) surfaces as a managed byte[]; Qt only reads it.
            if (builtin == "unsigned char")
                return "uchar*";
            if (builtin == "void")
                // An untyped pointer crosses as a raw handle; there is nothing to dereference.
                return "void*";
            if (builtin != null)
                // A pointer to any other scalar. A const one is an input array; a non-const one
                // is an out parameter (QColor::getRgb(int *r, ...)), which the code generator
                // surfaces as a C# `out`.
                return builtin == "char16_t" || !IsScalarName(builtin) ? null : builtin + "*";
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
            // An enum with no enumerators is a strong integer - `enum class QCborTag : quint64 {}`
            // exists so that a CBOR tag is a type of its own. It is deliberately left out of the
            // vocabulary: mapped to the integer it is built on, every entry point taking one needs
            // an explicit static_cast the code generator cannot know about (QCborValue::tag came
            // back ambiguous), and published as an enum it would be a type with no members that no
            // caller can name a value of. Skipping it costs the four QCbor value facts and keeps
            // the rest of the class.
            if (!Children(declaration).Any(c => clang.getCursorKind(c) == CXCursorKind.CXCursor_EnumConstantDecl))
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

    static bool IsChar(CXType type)
        => type.kind == CXTypeKind.CXType_Char_S || type.kind == CXTypeKind.CXType_Char_U;

    // The type as clang spells it, with runs of whitespace collapsed: the model has to stay
    // byte-reproducible and the spelling carries the declaration's own spacing.
    static string Spelling(CXType type)
        => string.Join(" ", clang.getTypeSpelling(type).ToString()
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

    static bool IsQStringList(CXType type)
    {
        var spelling = clang.getTypeSpelling(type.CanonicalType).ToString();
        return spelling.Replace(" ", "") == "QList<QString>";
    }

    // The element type of a "QList<E>" model type name, or null when it is not a list.
    static string ListElement(string modelType)
        => modelType != null && modelType.StartsWith("QList<", StringComparison.Ordinal) && modelType.EndsWith(">")
            ? modelType.Substring("QList<".Length, modelType.Length - "QList<".Length - 1)
            : null;

    // A QList<E> whose element type the vocabulary can express. The element comes from the
    // template argument of the specialization rather than from parsing the spelling.
    static bool IsQList(CXType type, out string element)
    {
        element = null;
        var canonical = type.CanonicalType;
        var declaration = canonical.Declaration;
        if (clang.getCursorKind(declaration) != CXCursorKind.CXCursor_ClassDecl)
            return false;
        if (clang.getCursorSpelling(declaration).ToString() != "QList")
            return false;
        int count = clang.Cursor_getNumTemplateArguments(declaration);
        if (count != 1)
            return false;
        element = ModelName(clang.Cursor_getTemplateArgumentType(declaration, 0));
        return element != null;
    }

    // A QMap/QHash specialization's two template arguments, references unwrapped. The arguments
    // come from the specialization rather than from parsing the spelling, exactly as they do for
    // QList.
    static bool TryUnwrapAssoc(CXType type, string container, out CXType key, out CXType value)
    {
        key = value = default;
        type = Unwrap(type);
        while (type.kind == CXTypeKind.CXType_LValueReference || type.kind == CXTypeKind.CXType_RValueReference)
            type = Unwrap(type.PointeeType);
        var canonical = type.CanonicalType;
        var declaration = canonical.Declaration;
        if (clang.getCursorKind(declaration) != CXCursorKind.CXCursor_ClassDecl)
            return false;
        if (clang.getCursorSpelling(declaration).ToString() != container)
            return false;
        if (clang.Cursor_getNumTemplateArguments(declaration) != 2)
            return false;
        key = clang.Cursor_getTemplateArgumentType(declaration, 0);
        value = clang.Cursor_getTemplateArgumentType(declaration, 1);
        return true;
    }

    // A QMap/KHash whose key and value types the vocabulary can express.
    static bool IsAssoc(CXType type, string container, out string key, out string value)
    {
        key = value = null;
        if (!TryUnwrapAssoc(type, container, out var keyType, out var valueType))
            return false;
        key = ModelName(keyType);
        value = ModelName(valueType);
        return key != null && value != null;
    }

    // A QBindable<T> specialization, with its single template argument.
    static bool TryUnwrapBindable(CXType type, out CXType element)
    {
        element = default;
        type = Unwrap(type);
        while (type.kind == CXTypeKind.CXType_LValueReference || type.kind == CXTypeKind.CXType_RValueReference)
            type = Unwrap(type.PointeeType);
        var canonical = type.CanonicalType;
        var declaration = canonical.Declaration;
        if (clang.getCursorKind(declaration) != CXCursorKind.CXCursor_ClassDecl)
            return false;
        if (clang.getCursorSpelling(declaration).ToString() != "QBindable")
            return false;
        if (clang.Cursor_getNumTemplateArguments(declaration) != 1)
            return false;
        element = clang.Cursor_getTemplateArgumentType(declaration, 0);
        return true;
    }

    // A QBindable<T> whose T the vocabulary can express.
    static bool IsQBindable(CXType type, out string element)
    {
        element = null;
        return TryUnwrapBindable(type, out var elementType) && (element = ModelName(elementType)) != null;
    }

    static string BuiltinName(CXType type) => type.kind switch
    {
        CXTypeKind.CXType_Void => "void",
        CXTypeKind.CXType_Bool => "bool",
        CXTypeKind.CXType_Int => "int",
        CXTypeKind.CXType_Float => "float",
        CXTypeKind.CXType_Double => "double",
        // Qt spells its 64-bit and unsigned integers as typedefs (qsizetype, qint64, uint,
        // QRgb); the canonical type is the builtin, and these are the ones the model can carry.
        CXTypeKind.CXType_LongLong => "long long",
        CXTypeKind.CXType_ULongLong => "unsigned long long",
        CXTypeKind.CXType_UInt => "unsigned int",
        // The remaining small scalars Qt uses for byte/character work. A UTF-32 code unit is a
        // 32-bit unsigned value, which is how managed code sees it.
        CXTypeKind.CXType_Short => "short",
        CXTypeKind.CXType_UShort => "unsigned short",
        CXTypeKind.CXType_Long => "long",
        CXTypeKind.CXType_ULong => "unsigned long",
        CXTypeKind.CXType_Char_S => "char",
        CXTypeKind.CXType_UChar => "unsigned char",
        CXTypeKind.CXType_Char16 => "char16_t",
        CXTypeKind.CXType_Char32 => "unsigned int",
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

    // The scalar vocabulary, spelled the way the model spells it.
    static readonly HashSet<string> ScalarNames = new(StringComparer.Ordinal)
    {
        "bool", "int", "float", "double",
        "long long", "unsigned long long", "unsigned int",
        "short", "unsigned short", "long", "unsigned long",
    };

    static bool IsScalarName(string t) => ScalarNames.Contains(t);

    // A pointer to a scalar: `int*`'s base is a scalar, `QWidget*`'s is not.
    static bool IsScalarPointer(string t)
        => t.EndsWith("*") && IsScalarName(t.Substring(0, t.Length - 1));

    // A public data member the model publishes as a C# property. Qt's option/style structs are
    // plain aggregates (QStyleOption::rect, QJsonParseError::error), and without their fields a
    // managed QStyledItemDelegate has no way to know where to paint.
    class FieldFact
    {
        public string Name;
        public string Type;
        public CXType ClangType;
    }

    // The public, non-static, non-bitfield data members whose type the vocabulary can express.
    static List<FieldFact> FieldsOf(CXCursor record)
    {
        var fields = new List<FieldFact>();
        foreach (var child in Children(record))
        {
            if (clang.getCursorKind(child) != CXCursorKind.CXCursor_FieldDecl)
                continue;
            if (Access(child) != CX_CXXAccessSpecifier.CX_CXXPublic)
                continue;
            // A bit field has no address to take, and an anonymous member has no name to publish.
            if (clang.Cursor_isBitField(child) != 0)
                continue;
            var name = clang.getCursorSpelling(child).ToString();
            if (name.Length == 0)
                continue;
            var type = clang.getCursorType(child);
            var modelType = ModelName(type);
            if (modelType == null || !SupportedType(type))
                continue;
            fields.Add(new FieldFact { Name = name, Type = modelType, ClangType = type });
        }
        return fields.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
    }

    // Whether a managed subclass of this (abstract) class can exist. The generated native
    // subclass is concrete only if every pure virtual it inherits is one we publish as an
    // override point - otherwise `new QsApi_X` does not compile. QAbstractTableModel qualifies
    // (all of QAbstractItemModel's pure virtuals are published), QLayout does not (itemAt /
    // takeAt are neither published nor implemented).
    static bool CanSubclassFromManaged(string className, ClangIndex index, HashSet<string> seen)
    {
        if (!seen.Add(className))
            return true;                        // already visited on this path
        if (!index.Classes.TryGetValue(className, out var record))
            return true;                        // an unbound base: nothing we can check
        foreach (var child in Children(record))
        {
            if (clang.getCursorKind(child) != CXCursorKind.CXCursor_CXXMethod)
                continue;
            if (clang.CXXMethod_isPureVirtual(child) == 0)
                continue;
            var name = clang.getCursorSpelling(child).ToString();
            if (!PublishAsVirtual(name, className))
                return false;
        }
        if (ClassBases.TryGetValue(className, out var bases))
            foreach (var b in bases)
                if (!CanSubclassFromManaged(b, index, seen))
                    return false;
        return true;
    }

    // Which virtuals a managed subclass may override. The curated list (VirtualAllow) is the
    // event handlers and the layout hooks; `VirtualExtensionNames` adds the extension points a
    // real application needs - without them a custom QAbstractItemModel or QStyledItemDelegate
    // cannot be written from C# at all. The rule is by *name*, not by class: publishing a name
    // only on the class that declares it would leave every concrete subclass without an
    // implementation to chain to (QStandardItemModel::data would fall back to the pure virtual),
    // so every class declaring that name gets it.
    static bool PublishAsVirtual(string name, string className)
        => VirtualAllow.Contains(name)
           || (VirtualExtensionNames.Contains(name) && IsInExtensionTree(className));

    // Whether the class is one of the extension classes or inherits from one. The name closure
    // has to stay inside that subtree: a name like `setData` is also declared, completely
    // unrelated, by QPicture/QPixmap as `setData(const char *, uint)`, and publishing that as an
    // override point would generate a slot for a method no model ever calls.
    static readonly Dictionary<string, bool> extensionTree = new(StringComparer.Ordinal);

    static bool IsInExtensionTree(string className)
    {
        if (extensionTree.TryGetValue(className, out bool known))
            return known;
        extensionTree[className] = false;       // a cycle would otherwise not terminate
        bool result = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(className);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
                continue;
            if (Array.IndexOf(VirtualExtensionClasses, current) >= 0)
            {
                result = true;
                break;
            }
            if (ClassBases.TryGetValue(current, out var bases))
                foreach (var b in bases)
                    queue.Enqueue(b);
        }
        extensionTree[className] = result;
        return result;
    }

    // The union of the virtual names declared by the extension classes, filled once per run
    // (RunExtract2) so `PublishAsVirtual` stays a set lookup.
    static readonly HashSet<string> VirtualExtensionNames = new(StringComparer.Ordinal);

    // The classes whose virtualls a managed subclass must be able to implement: the item model and
    // the item delegate are the two extension points every real Qt application uses.
    static readonly string[] VirtualExtensionClasses =
    {
        "QAbstractItemModel", "QAbstractListModel", "QAbstractTableModel",
        "QAbstractItemDelegate", "QStyledItemDelegate", "QItemDelegate",
        // Work handed to a thread: a managed QRunnable implements run(), and a managed QThread
        // subclass overrides the same name (both are the standard Qt extension points).
        "QRunnable", "QThread",
        // A document object drawn by the application: QTextDocument asks the layout handler for
        // its intrinsic size and to paint itself, which is the textobject example.
        "QTextObjectInterface",
        // Custom filtering and sorting: QSortFilterProxyModel is how a real application narrows a
        // model down, and filterAcceptsRow/filterAcceptsColumn/lessThan are the hooks it overrides.
        "QSortFilterProxyModel",
    };

    // Collect the virtual names those classes declare, by walking their own members.
    static void CollectVirtualExtensionNames(ClangIndex index)
    {
        VirtualExtensionNames.Clear();
        foreach (var className in VirtualExtensionClasses)
        {
            if (!index.Classes.TryGetValue(className, out var record))
                continue;
            foreach (var child in Children(record))
            {
                var kind = clang.getCursorKind(child);
                if (kind != CXCursorKind.CXCursor_CXXMethod && kind != CXCursorKind.CXCursor_FunctionTemplate)
                    continue;
                if (clang.CXXMethod_isVirtual(child) == 0)
                    continue;
                var name = clang.getCursorSpelling(child).ToString();
                // The Q_OBJECT scaffolding is not an API: moc generates metaObject, qt_metacall
                // and qt_metacast in every QObject subclass, and they must not become overridable
                // C# members.
                if (name.Length == 0 || name == "metaObject" || name.StartsWith("qt_", StringComparison.Ordinal))
                    continue;
                VirtualExtensionNames.Add(name);
            }
        }
    }

    // How one parameter crosses the boundary.
    enum ParamMode
    {
        In,         // a plain input
        Out,        // a non-const scalar pointer: Qt writes one value back (QColor::getRgb)
        Ref,        // a non-const scalar reference: in and out (QValidator::validate's int &pos)
        Invalid,    // we cannot express it; the declaration is rejected
    }

    // Qt's convention for "one extra output" is a non-const pointer, and for "in and out" a
    // non-const reference. A non-const reference to a *bound class* is neither: the managed
    // object *is* the native object, which is why Qt's own swap(QBrush &other) keeps working.
    // Anything else (a container, an enum, a string, a pointer to a pointer) is not expressible
    // and poisons the declaration.
    static ParamMode ParamModeOf(CXType type)
    {
        type = Unwrap(type);
        if (type.kind == CXTypeKind.CXType_Pointer)
        {
            if (type.PointeeType.IsConstQualified)
                return ParamMode.In;
            var name = ModelName(type);
            if (name == null)
                return ParamMode.Invalid;
            // uchar* is a byte buffer Qt *reads* (QImage's pixel data), not an output.
            if (name == "uchar*")
                return ParamMode.In;
            return IsScalarPointer(name) ? ParamMode.Out : ParamMode.In;
        }
        if (type.kind == CXTypeKind.CXType_LValueReference && !type.PointeeType.IsConstQualified)
        {
            var pointee = ModelName(type.PointeeType);
            if (pointee == null)
                return ParamMode.Invalid;
            if (RootClasses.Contains(pointee))
                return ParamMode.In;
            // QString is the one non-scalar Qt rewrites in place (QValidator::validate/fixup
            // rewrite the text, QClipboard::text fills in the subtype): the native side gets a
            // local QString and hands the updated copy back. The views are left out - a
            // QStringView local cannot bind to a QString&.
            if (pointee == "QString")
                return ParamMode.Ref;
            return IsScalarName(pointee) ? ParamMode.Ref : ParamMode.Invalid;
        }
        return ParamMode.In;
    }

    // A non-const scalar pointer is a single value *except* where Qt means "a buffer of N":
    // QMatrix4x4::copyDataTo(float *values) writes 16 floats and QMetaMethod::getParameterTypes
    // writes one int per parameter, neither of which an `out int`/`out float` can hold. The
    // pointer carries no length, so the only safe thing is to leave the declaration unbound.
    static bool IsOutBufferMethod(string name)
        => name is "copyDataTo" or "getParameterTypes";

    // The class/enum vocabulary the model is allowed to express.
    static bool SupportedType(CXType type, bool isParameter = false, bool isReturn = false)
    {
        if (isParameter && ParamModeOf(type) == ParamMode.Invalid)
            return false;
        var name = ModelName(type);
        if (name == null)
            return false;
        if (name.EndsWith("*"))
        {
            // A raw scalar pointer in return position has no length and no owner, so managed
            // code could neither bound nor free the buffer (QImage::bits(), QMatrix4x4::data()).
            // The parameter position is fine: it is an input array or an out value.
            if (isReturn && IsScalarPointer(name))
                return false;
            return SupportedTypes.Contains(name);
        }
        // A QList<E> is bindable exactly when its element type is.
        if (ListElement(name) is string element)
            return SupportedTypes.Contains(element);
        // A QMap/QHash is bindable when both halves are, checked as the value types they are
        // (so a pointer half still has to name a bound class, and an enum half a publishable
        // scope).
        if (TryUnwrapAssoc(type, "QMap", out var mapKeyType, out var mapValueType)
            || TryUnwrapAssoc(type, "QHash", out mapKeyType, out mapValueType))
            return SupportedType(mapKeyType) && SupportedType(mapValueType);
        // A QBindable<T> only ever comes *out* of Qt (the bindableFoo() accessors). Handing one
        // back in would need a native QBindable object, which the managed side has no way to
        // make, so a bindable parameter is not bindable.
        if (TryUnwrapBindable(type, out var boundType))
            return !isParameter && SupportedType(boundType);

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
        // The coverage report groups by the container name; the debug log keeps the full
        // spelling, which is what says which element types are still missing.
        if (Environment.GetEnvironmentVariable("QTSHARP_MODEL_DEBUG") == "1")
            Console.WriteLine($"[model2-debug] blocked: {spelling}");
        var open = spelling.IndexOf('<');
        return open < 0 ? spelling : spelling.Substring(0, open) + "<...>";
    }

    // --------------------------------------------------------------- facts and rendering
    class ParamFact
    {
        public string Name;
        public string Type;         // model type, null when the vocabulary cannot express it
        public CXType ClangType;
        // The spelling clang printed for the declaration. Only virtuals publish it: an override
        // has to repeat the base declaration exactly, and the model type has already dropped the
        // pointee const and the reference (const QMimeData * -> QMimeData*).
        public string CppType;
        public ParamMode Mode;      // input, out (non-const scalar pointer) or ref
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
        public bool IsConst;
        public bool Private;
        public bool IsCtor;
        // A protected member. Qt declares the ones a model must call on itself
        // (QAbstractItemModel::createIndex, beginInsertRows, ...) protected, so they are only
        // reachable through the generated subclass - the code generator emits them as such.
        public bool IsProtected;
        public string ReturnType;
        public CXType ReturnClangType;
        public List<ParamFact> Params = new();
        // The longest marshalable prefix of the declaration: what the model emits.
        public List<ParamFact> Visible = new();
    }

    static string ClassJson(CXCursor record, Root root, ClangIndex index,
                            List<Program.SignalInfo> signals, HashSet<string> allSignalNames,
                            List<Program.PropertyInfo> properties,
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

        // The application object is a singleton, and PyQt spells its run loop and its exits on the
        // instance (`app.exec()`), which is what a C# user reaches for too - but C# refuses to call
        // a *static* member through an instance reference (CS0176), so a static exec/quit/exit is
        // unusable in the one spelling that reads naturally. They are published as instance
        // members instead; the native side still calls the real static (self->exec() resolves to
        // the same function - a static member function ignores the object expression).
        foreach (var m in callable)
        {
            if (IsApplicationSingletonMember(className, m.Name))
                m.IsStatic = false;
        }

        // Qt also re-declares exec() in QGuiApplication and QApplication. Those declarations are
        // dropped: keeping them would only hide the QCoreApplication one (CS0108 in the generated
        // binding) and bind a second, identical forwarder.
        callable.RemoveAll(m => IsRedundantApplicationExec(className, m.Name));


        // An abstract class cannot be instantiated: its C++ pure virtuals are not implemented by
        // the generated subclass, so offering constructors would only produce a wrapper that
        // fails at link time (or worse, at run time).
        var ctors = BindsCtors(className) && !isAbstract
            ? callable.Where(m => m.IsCtor)
                .Where(m => Access(m.Cursor) == CX_CXXAccessSpecifier.CX_CXXPublic)
                // CppSharp only marks copy constructors after its own passes, so they have to be
                // recognised by shape here (QColor(const QColor&)).
                .Where(m => m.Visible.Count != 1 || m.Visible[0].Type != className)
                .Where(m => Accept(m, className))
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
            .Where(m => !m.IsCtor && !(m.IsVirtual && PublishAsVirtual(m.Name, className)))
            // Qt's "qt_" members are macro scaffolding (Q_OBJECT), not API, and have no exported
            // symbol either.
            .Where(m => !m.Name.StartsWith("qt_", StringComparison.Ordinal))
            .Where(m => !m.Name.StartsWith("operator", StringComparison.Ordinal))
            // Public members, plus the protected ones a *managed subclass* has to call on itself.
            // A model is the case: QAbstractItemModel declares createIndex/beginInsertRows/... as
            // protected, so without them a C# model can never produce a valid index or announce a
            // structural change. The generated native subclass exposes them (they stay protected
            // in C++, where nobody else can reach them); outside the extension tree protected
            // members are of no use and stay unpublished.
            //
            // Two shapes are left out: a protected *static* (there is no object to hang it on, and
            // QThread::setTerminationEnabled is not worth a shim) and one with a by-reference
            // parameter (the subclass helper takes the flat C ABI parameters, and an out parameter
            // is a statement rather than an expression there).
            .Where(m => Access(m.Cursor) == CX_CXXAccessSpecifier.CX_CXXPublic
                        || (m.IsProtected && subclassable && IsInExtensionTree(className)
                            && !m.IsStatic && m.Visible.All(p => p.Mode == ParamMode.In)))
            .Where(m => !allSignalNames.Contains(className + "::" + m.Name))
            .Where(m => Accept(m, className))
            .GroupBy(DedupeKey)
            .Select(g => g.First())
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Params.Count)
            .ThenBy(SignatureKey, StringComparer.Ordinal)
            .ToList();

        // Only the virtuals this class declares, and only the names a managed override may
        // install. The dispatch chain is merged from the model by the code generator.
        //
        // A *private* virtual is recorded (with `noBase`) but the code generator must not override
        // it: Qt declares the three hooks QAbstractListModel hides (columnCount, hasChildren,
        // parent) as private precisely so that subclasses do not replace them, and a subclass
        // cannot chain to them either, since the generated native wrapper is not a friend.
        // Overriding one with `return default` made QAbstractListModel::columnCount answer 0
        // instead of 1 - and since QAbstractItemModel::index() goes through hasIndex() ->
        // columnCount(), every QModelIndex of a QStringListModel came back invalid (found by the
        // completer example: `model.index(0, 0).isValid()` was false and data() was empty).
        // It has to stay in the list, though, or the chain merge would walk past it and pick up
        // the public pure QAbstractItemModel::columnCount instead.
        var virtuals = callable
            .Where(m => m.IsVirtual && !m.IsCtor)
            .Where(m => PublishAsVirtual(m.Name, className))
            // An override has to repeat the base declaration exactly, so a virtual keeps its
            // whole parameter list - the marshalable-prefix rule is for callable members only.
            .Where(m => { m.Visible = m.Params; return Accept(m, className); })
            .GroupBy(DedupeKey)
            .Select(g => g.First())
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Params.Count)
            .ToList();

        // A Q_PROPERTY is sugar over the accessors: both the READ getter and the (optional)
        // WRITE setter have to be published methods already, and the setter has to take exactly
        // the type the getter returns. Anything else - a QIcon-typed property whose accessors the
        // vocabulary rejects, an echoMode whose accessor returns an enum the class also nests -
        // is skipped, and the methods stay.
        var published = methods.Concat(virtuals).ToList();
        var classProperties = properties
            .Where(p => p.ClassName == className && p.Getter != null)
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(p => (
                Property: p,
                Getter: published.FirstOrDefault(m => m.Name == p.Getter && m.Visible.Count == 0)))
            .Where(x => x.Getter != null && x.Getter.ReturnType != null && x.Getter.ReturnType != "void")
            .Select(x => (
                x.Property,
                x.Getter,
                // The setter is matched by *type*, not by name: Qt has several overloads
                // (QTimer::setInterval(int) and setInterval(std::chrono::milliseconds)) and only
                // the one that takes what the getter returns can be the property's setter.
                Setter: published.FirstOrDefault(m => m.Name == x.Property.Setter && m.Visible.Count == 1
                    && ApiTypes.Managed(m.Visible[0].Type ?? "?", RootClasses.ToArray())
                       == ApiTypes.Managed(x.Getter.ReturnType, RootClasses.ToArray()))))
            .Where(x => x.Property.Setter == null || x.Setter != null)
            .OrderBy(x => x.Property.Name, StringComparer.Ordinal)
            .ToList();

        // The signals this class declares: the header scan says *which* declarations are signals,
        // the AST says what their parameters are. A signal is published only when every parameter
        // is one the vocabulary can marshal; otherwise it is dropped (and stays out of `methods`
        // through allSignalNames - a signal is not a callable member).
        var signalFacts = SignalFactsOf(className, signals, callable);
        // A signal is exposed as a property named after it, so two of the same name cannot
        // coexist.
        var classSignals = signalFacts
            .GroupBy(f => f.Name + "(" + string.Join(",", f.Params.Select(p => p.Type)) + ")")
            .Select(g => g.First())
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => new Program.SignalInfo
            {
                ClassName = className,
                SignalName = f.Name,
                // The model type is also the *normalized* spelling Qt stores in the meta-object
                // (QString, QWidget*, Qt::Orientation), which indexOfSignal matches literally -
                // clang's "const QString &" is not.
                Params = f.Params.Select(p => new Program.SignalParamInfo
                {
                    Name = p.Name,
                    CppType = p.Type,
                    ModelType = p.Type,
                }).ToList(),
            })
            .ToList();

        foreach (var fn in ctors.Concat(methods).Concat(virtuals))
            CollectEnums2(fn.ReturnClangType, fn.Visible, index, enums);
        // A signal's parameter types have to reach the enum table too: QAbstractItemDelegate's
        // commitData(QWidget*) and the signals carrying an enum are the only place that enum
        // appears.
        foreach (var f in signalFacts)
            CollectEnums2(f.Fn.ReturnClangType, f.Params, index, enums);
        // A published data member can be the only place an enum appears (QStyleOption::state is
        // QStyle::State), so its type has to contribute enums too.
        foreach (var field in FieldsOf(record))
            CollectEnums2(field.ClangType, new List<ParamFact>(), index, enums);

        var body = new StringBuilder();
        body.Append("    {\n");
        body.Append($"      \"name\": \"{className}\",\n");
        body.Append($"      \"header\": \"{root.Module}/{root.Header}\",\n");
        body.Append($"      \"bases\": [{string.Join(", ", bases.Select(Json))}],\n");
        body.Append($"      \"isAbstract\": {(isAbstract ? "true" : "false")},\n");
        body.Append($"      \"subclassable\": {(subclassable ? "true" : "false")},\n");
        // An abstract class a managed subclass can still derive from: the native subclass is
        // concrete because every pure virtual it inherits is published as an override point, so
        // the code generator can give it a constructor (QAbstractItemModel/QAbstractTableModel).
        bool subclassCtor = isAbstract && subclassable && virtuals.Count > 0
                            && CanSubclassFromManaged(className, index, new HashSet<string>(StringComparer.Ordinal));
        if (subclassCtor)
            body.Append("      \"subclassCtor\": true,\n");
        body.Append($"      \"owned\": {(OwnedOf(root) ? "true" : "false")},\n");
        var fields = FieldsOf(record);
        if (fields.Count > 0)
        {
            body.Append("      \"fields\": [\n");
            for (int i = 0; i < fields.Count; i++)
            {
                body.Append($"        {{ \"name\": {Json(fields[i].Name)}, \"type\": {Json(fields[i].Type)} }}");
                body.Append(i + 1 < fields.Count ? ",\n" : "\n");
            }
            body.Append("      ],\n");
        }
        WriteConstructors2(body, ctors);
        WriteFunctions2(body, "methods", methods);
        WriteVirtuals2(body, virtuals);
        // Before the signals: WriteSignals is the last key and relies on having no trailing comma.
        WriteProperties2(body, classProperties);
        WriteSignals(body, classSignals);
        body.Append("    }\n");
        return body.ToString();
    }

    // A signal's parameter list, built from the AST declaration the header scan pointed at.
    // A signal is matched by name and by the number of *public* parameters - the scanner knows
    // the QPrivateSignal marker the AST spells as a nested type.
    static List<(FnFact Fn, string Name, List<ParamFact> Params)> SignalFactsOf(
        string className, List<Program.SignalInfo> signals, List<FnFact> callable)
    {
        var result = new List<(FnFact Fn, string Name, List<ParamFact> Params)>();
        foreach (var info in signals.Where(s => s.ClassName == className))
        {
            int arity = info.Params.Count(p => !p.Private);
            FnFact match = null;
            foreach (var m in callable)
            {
                if (m.IsCtor || m.Name != info.SignalName)
                    continue;
                if (result.Any(r => ReferenceEquals(r.Fn, m)))
                    continue;                       // an overload this run already claimed
                if (m.Params.Count(p => !IsPrivateSignalParam(p)) != arity)
                    continue;
                match = m;
                break;
            }
            if (match == null)
                continue;

            var ps = match.Params.Where(p => !IsPrivateSignalParam(p)).ToList();
            // Every parameter has to be marshalable, and a signal parameter is always an input:
            // there is no such thing as an out parameter on a signal. The string *views* are read
            // but cannot be emitted (a QStringView is not a QString, and the emit side has to
            // build the exact storage type Qt declared).
            if (ps.Any(p => p.Type == null || p.Mode != ParamMode.In
                            || p.Type is "QStringView" or "QAnyStringView"
                            || !SupportedType(p.ClangType, isParameter: true)))
                continue;
            result.Add((match, info.SignalName, ps));
        }
        return result;
    }

    // moc puts a QPrivateSignal marker parameter on a "private" signal (QTimer::timeout). It
    // carries no data and is not part of the managed surface.
    static bool IsPrivateSignalParam(ParamFact p)
        => p.CppType != null && p.CppType.Contains("QPrivateSignal");

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

    // Whether the binding publishes a release path for the class: Dispose, a finalizer and the
    // native Destroy_<X> entry point.
    //
    // It used to be "the Roots flag, unless the class derives from QObject" - and that rule was
    // decided per *chunk* (a class whose primary base sat in another header looked like a plain
    // value type). The result was backwards: QSharedMemory/QTimer/QThread/QEventLoop, which a
    // program creates and should be able to release, had no Dispose at all, while QPushButton did.
    //
    // "Does Qt own this?" is a question about the *object*, not about the class, so the model no
    // longer answers it: the Roots flag says whether the managed side may ever release an instance
    // (true for everything a program can construct; false for the events Qt builds on the stack,
    // the item classes a view or a scene holds, and the few values Qt adopts by a route that
    // leaves no parent). The generated destructor answers the runtime half by asking Qt first -
    // `if (object->parent()) return;` - and the wrapper only ever releases what it created
    // (__ownsNative).
    static bool OwnedOf(Root root) => root.Owned;

    // The application classes. Their exec()/quit()/exit() act on the process's one application
    // object, so the binding publishes them as *instance* members: C# cannot call a static member
    // through an instance reference, and `app.Exec()` is the spelling PyQt (and every reader) uses.
    static bool IsApplicationClassName(string className) =>
        className is "QCoreApplication" or "QGuiApplication" or "QApplication";

    static bool IsApplicationSingletonMember(string className, string name) =>
        IsApplicationClassName(className) && name is "exec" or "quit" or "exit";

    // QGuiApplication::exec and QApplication::exec are re-declarations of QCoreApplication::exec.
    // The model keeps the one that can be inherited.
    static bool IsRedundantApplicationExec(string className, string name) =>
        className is "QGuiApplication" or "QApplication" && name == "exec";


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
        // A member whose name equals its class's is a constructor, whatever libclang calls it:
        // Q_WEAK_OVERLOAD wraps a constructor in the `template <typename = void>` trick
        // (QVersionNumber(const QList<int> &)), and the cursor then reads as a function template.
        // Without this the constructor is published as a method named after the class, and the
        // generated forwarder (`self->QVersionNumber(...)`) does not compile.
        if (!isCtor)
        {
            var parent = clang.getCursorSemanticParent(handle);
            if (clang.getCursorKind(parent) == CXCursorKind.CXCursor_ClassDecl ||
                clang.getCursorKind(parent) == CXCursorKind.CXCursor_StructDecl)
                isCtor = clang.getCursorSpelling(handle).ToString() == clang.getCursorSpelling(parent).ToString();
        }
        if (!isCtor && !IsCopyableResult(clang.getCursorResultType(handle)))
            return null;
        // `= delete` removes a member from the API rather than declaring one. Qt uses it to turn a
        // stray argument into a compile error (QCborValue(const void *) = delete), and taking the
        // declaration at face value publishes an untyped-pointer constructor that cannot compile.
        if (clang.CXXMethod_isDeleted(handle) != 0)
            return null;
        // A byte buffer only makes sense as an argument: a returned one would have no length.
        if (!isCtor && ModelName(clang.getCursorResultType(handle)) == "uchar*")
            return null;

        var fn = new FnFact
        {
            Cursor = handle,
            Name = clang.getCursorSpelling(handle).ToString(),
            IsCtor = isCtor,
            IsStatic = clang.CXXMethod_isStatic(handle) != 0,
            IsVirtual = clang.CXXMethod_isVirtual(handle) != 0,
            IsPure = clang.CXXMethod_isPureVirtual(handle) != 0,
            IsConst = clang.CXXMethod_isConst(handle) != 0,
            // A *private* virtual cannot be called from the generated subclass at all, so a
            // managed override has no base implementation to chain to (Qt declares
            // QAbstractListModel::columnCount private).
            Private = !isCtor && Access(handle) == CX_CXXAccessSpecifier.CX_CXXPrivate,
            IsProtected = !isCtor && Access(handle) == CX_CXXAccessSpecifier.CX_CXXProtected,
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
                    CppType = Spelling(type),
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
                    CppType = Spelling(type),
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

        foreach (var p in fn.Params)
            p.Mode = ParamModeOf(p.ClangType);
        fn.Visible = VisibleParamsOf(fn);
        return fn;
    }

    static CX_CXXAccessSpecifier Access(CXCursor cursor) => clang.getCXXAccessSpecifier(cursor);

    // The generated glue hands a returned value to managed code by copying it, so a class whose
    // copy constructor is deleted (QDataStream, returned by its own fluent writeBytes) cannot be
    // a return type at all.
    static bool IsCopyableResult(CXType type)
    {
        type = Unwrap(type);
        if (type.kind == CXTypeKind.CXType_LValueReference ||
            type.kind == CXTypeKind.CXType_RValueReference)
            type = type.PointeeType;
        var declaration = type.CanonicalType.Declaration;
        if (clang.getCursorKind(declaration) != CXCursorKind.CXCursor_ClassDecl &&
            clang.getCursorKind(declaration) != CXCursorKind.CXCursor_StructDecl)
            return true;
        foreach (var child in Children(declaration))
        {
            if (clang.getCursorKind(child) != CXCursorKind.CXCursor_Constructor)
                continue;
            if (clang.CXXConstructor_isCopyConstructor(child) != 0)
                return clang.CXXMethod_isDeleted(child) == 0;
        }
        return true;    // an implicitly declared copy constructor
    }

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
                // A by-reference parameter cannot carry a C# default (`out int a = 0` is not
                // legal), so an optional one is dropped and the C++ default applies - which is
                // exactly what QColor::getRgb(int *r, int *g, int *b, int *a = nullptr) means.
                if (p.Mode != ParamMode.In && p.Optional)
                    break;
                // A boxed value parameter whose optional default we cannot express (QRect(),
                // QVariant(), QSize(-1, -1)) would reach the generated glue as a null handle and
                // be dereferenced. Leaving it out makes the C++ default apply, which is what the
                // declaration meant. The same goes for an enum whose default is not an enumerator
                // (QNativeIpcKey::DefaultTypeForOs): the C# default would be 0, and 0 is the one
                // value Qt reserved for "invalid".
                if (p.Optional && p.Default == null
                    && (RootClasses.Contains(p.Type) || p.Type.Contains("::")))
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

    static bool Accept(FnFact fn, string className)
    {
        var blocker = SupportedType(fn.ReturnClangType, isReturn: true) ? null : DescribeBlocked(fn.ReturnClangType);
        if (blocker == null && fn.Visible.Any(p => p.Mode != ParamMode.In))
        {
            // A by-reference parameter is only supported on a plain method: the native wrapper
            // the virtual dispatch path builds has no way to hand an out value back through the
            // argv slot convention. No whitelisted virtual declares one today; this keeps it
            // that way.
            if (fn.IsVirtual && PublishAsVirtual(fn.Name, className))
                blocker = "out parameter on an overridable virtual";
            else if (fn.IsCtor)
                // The generated constructor forwarders take the flat C ABI types and have no
                // room for the extra out slot a by-reference parameter needs.
                blocker = "a by-reference constructor parameter";
            else if (IsOutBufferMethod(fn.Name))
                blocker = "a buffer written through a non-const pointer";
        }
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
        // Which declaration the type blocks is what says whether the gap is worth closing, so
        // the debug log names the function and the blocker together.
        if (Environment.GetEnvironmentVariable("QTSHARP_MODEL_DEBUG") == "1")
            Console.WriteLine($"[model2-debug] skipped {fn.Name}({string.Join(",", fn.Visible.Select(p => p.Type ?? "?"))}) blocked by {blocker}");
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
        // The slice starts at the `=` of the assignment (clang reports the range from there), so
        // `= {}` is what a value-initialised default looks like.
        if (text.StartsWith("=", StringComparison.Ordinal))
            text = text.Substring(1).Trim();
        if (text.Length == 0)
            return null;
        if (text is "nullptr" or "NULL" or "0")
            return type != null && (type.EndsWith("*") || type == "QString" || RootClasses.Contains(type)) ? "null" : "0";
        if (text is "true" or "false")
            return text;
        // A value-initialised default: `{}`, `Type()` or `Type{}`. An empty pair of braces is the
        // zero of the type - a flag set with no bits for the flags and enum types Qt writes this
        // for (QUrl::AceProcessingOptions, Qt::WindowFlags), and "let the C++ default stand in"
        // for a class-typed one (QRect()).
        if (text is "{}" or "()")
            return IsFlagLike(type) ? "0" : null;
        // Qt also writes a flag set it builds: `StandardButtons(Yes | No)`, `Qt::WindowFlags(...)`.
        // Only the argument list matters.
        if (type != null)
        {
            int open = text.IndexOfAny(new[] { '(', '{' });
            char close = open >= 0 && text[open] == '(' ? ')' : '}';
            if (open > 0 && text[text.Length - 1] == close)
            {
                string head = text.Substring(0, open).Trim();
                string inner = text.Substring(open + 1, text.Length - open - 2);
                if (head == type || head == ItemOf(type))
                {
                    if (inner.Trim().Length == 0)
                        return IsFlagLike(type) ? "0" : null;
                    return DefaultLiteral2(inner, type);
                }
            }
        }
        // An enumerator, or a set of them joined with `|`. Two spellings have to work: the
        // qualified one Qt writes for a plain enum (Qt::AutoColor) and the bare one it writes for
        // a QFlags enumerator (QDir::NoFilter, spelled `NoFilter` because the parameter's type is
        // already the flag type). Both fold to the same C# member reference. Getting this wrong is
        // silent: `default` is 0, and 0 is not what Qt declared (QDir's NoFilter is -1, so a
        // zero-valued filter returns no entries at all).
        if (type != null && type.Contains("::"))
        {
            var parts = text.Split('|').Select(p => p.Trim()).ToList();
            if (parts.Count > 0 && parts.All(IsEnumeratorName) && parts.All(p => IsEnumeratorOf(type, p)))
                return string.Join(" | ", parts.Select(p =>
                    ApiTypes.Managed(type, RootClasses.ToArray()) + "." + ItemOf(p)));
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

    // The model types whose zero is `0` rather than a null handle: an enum, or a QFlags typedef
    // (both are published as a managed enum). A class is the other case.
    static bool IsFlagLike(string type)
        => type != null && type.Contains("::") && !RootClasses.Contains(type);


    // The enumerator out of `Qt::AutoColor` or a bare `NoFilter` (Qt writes the bare spelling when
    // the parameter's type is already the flag type).
    static string ItemOf(string qualified)
    {
        int at = qualified.LastIndexOf("::", StringComparison.Ordinal);
        return at < 0 ? qualified : qualified.Substring(at + 2);
    }

    // Whether the default argument really names an enumerator of something the parameter's type
    // could mean. `Qt::AutoColor` and the bare `QDir::NoFilter` do; `QNativeIpcKey::DefaultTypeForOs`
    // only looks like it does - it is a static constexpr member of the class - and translating it
    // into `QNativeIpcKey.Type.DefaultTypeForOs` names a member that does not exist. The spelling
    // alone cannot tell the two apart, so the parse index decides.
    static bool IsEnumeratorOf(string type, string qualified)
    {
        string item = ItemOf(qualified);
        // `Qt::AutoColor` names the namespace its enum lives in, while `CheckIndexOption::NoOption`
        // names the enum itself (the parameter's type is the *flags* typedef around it). Both are
        // accepted as long as some enum with that name contains the enumerator.
        string head = qualified.Contains("::")
            ? qualified.Substring(0, qualified.LastIndexOf("::", StringComparison.Ordinal))
            : null;
        // A bare enumerator (`NoFilter`) is looked up in the type's own scope (QDir::Filters -> QDir::).
        string scope = head == null && type.LastIndexOf("::", StringComparison.Ordinal) is int at && at >= 0
            ? type.Substring(0, at + 2)
            : null;
        foreach (var kv in CurrentIndex.Enums)
        {
            if (head != null)
            {
                if (kv.Key != head
                    && !kv.Key.StartsWith(head + "::", StringComparison.Ordinal)
                    && !kv.Key.EndsWith("::" + head, StringComparison.Ordinal))
                    continue;
            }
            else if (kv.Key != type && (scope == null || !kv.Key.StartsWith(scope, StringComparison.Ordinal)))
            {
                continue;
            }
            if (Children(kv.Value).Any(c => clang.getCursorKind(c) == CXCursorKind.CXCursor_EnumConstantDecl
                                            && clang.getCursorSpelling(c).ToString() == item))
                return true;
        }
        return false;
    }

    // `NoFilter`, `Qt::AutoColor`: a single qualified name with no operators of any kind. Anything
    // that mentions a function call, a template or arithmetic stays out.
    static bool IsEnumeratorName(string text)
    {
        bool expectLetter = true;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (expectLetter)
            {
                if (!char.IsLetter(c) && c != '_')
                    return false;
                expectLetter = false;
            }
            else if (c == ':')
            {
                // Only ever as the "::" separator.
                if (i + 1 >= text.Length || text[i + 1] != ':')
                    return false;
                i++;
                expectLetter = true;
            }
            else if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }
        return !expectLetter;
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

    // Enums an application needs but no bound *signature* mentions. QKeyEvent::key() returns a
    // plain int, so Qt::Key - the table every key handler compares against - would otherwise be
    // missing from the model entirely, and the keyboard modifiers only reach it through the
    // QFlags spelling (Qt::KeyboardModifiers, which carries the same items). Both are published
    // as enums of their own so managed code can name the values.
    static readonly string[] ExtraEnums =
    {
        "Qt::Key", "Qt::KeyboardModifier", "QTextFormat::FormatType",
        // Item roles are plain ints in every signature (QAbstractItemModel::data), so the table
        // every model implementation switches on would otherwise be missing.
        "Qt::ItemDataRole",
    };

    static void CollectExtraEnums(ClangIndex index, Dictionary<string, EnumEntry> enums)
    {
        foreach (var qualified in ExtraEnums)
        {
            if (!index.Enums.TryGetValue(qualified, out var cursor))
                continue;
            enums[qualified] = new EnumEntry
            {
                Name = qualified.Substring(qualified.LastIndexOf("::", StringComparison.Ordinal) + 2),
                Qualified = qualified,
                Items = EnumItems(cursor),
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
            sb.Append(string.Join(", ", ctors[i].Visible.Select(p => ParamJson2(p))));
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
            if (m.IsProtected)
                sb.Append(", \"protected\": true");
            sb.Append(", \"returnType\": ").Append(Json(m.ReturnType));
            sb.Append(", \"parameters\": [");
            sb.Append(string.Join(", ", m.Visible.Select(p => ParamJson2(p))));
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
            // A const virtual has to be declared const in the native override or it does not
            // override anything (QWidget::sizeHint() const).
            if (v.IsConst)
                sb.Append(", \"const\": true");
            // A private virtual has no accessible base implementation. The code generator must
            // leave the slot alone (Qt's own implementation is the only correct one) - overriding
            // it with a do-nothing default is what broke QStringListModel's indices.
            if (v.Private)
                sb.Append(", \"noBase\": true");
            sb.Append(", \"returnType\": ").Append(Json(v.ReturnType));
            sb.Append(", \"parameters\": [");
            sb.Append(string.Join(", ", v.Visible.Select(p => ParamJson2(p, withCpp: true))));
            sb.Append("] }");
            sb.Append(i + 1 < virtuals.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    // The properties a class publishes: the Qt name, the model type (the getter's return type)
    // and the accessors the generated C# property calls.
    static void WriteProperties2(StringBuilder sb, List<(Program.PropertyInfo Property, FnFact Getter, FnFact Setter)> properties)
    {
        if (properties.Count == 0)
            return;
        sb.Append("      \"properties\": [\n");
        for (int i = 0; i < properties.Count; i++)
        {
            var (property, getter, setter) = properties[i];
            sb.Append($"        {{ \"name\": {Json(property.Name)}, \"type\": {Json(getter.ReturnType)}, \"getter\": {Json(getter.Name)}");
            if (setter != null)
                sb.Append($", \"setter\": {Json(setter.Name)}");
            sb.Append(" }");
            sb.Append(i + 1 < properties.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    static string ParamJson2(ParamFact p, bool withCpp = false)
    {
        var sb = new StringBuilder();
        sb.Append("{ \"name\": ").Append(Json(p.Name));
        sb.Append(", \"type\": ").Append(Json(p.Type));
        // A virtual override has to repeat Qt's declaration exactly, so its parameters carry the
        // spelling clang printed. A duration needs it too: which unit it is decides the C++
        // conversion (std::chrono::duration_cast<std::chrono::milliseconds>).
        if ((withCpp || p.Type == "TimeSpan") && p.CppType != null)
            sb.Append(", \"cpp\": ").Append(Json(p.CppType));
        if (p.Mode == ParamMode.Out)
            sb.Append(", \"out\": true");
        else if (p.Mode == ParamMode.Ref)
            sb.Append(", \"ref\": true");
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
