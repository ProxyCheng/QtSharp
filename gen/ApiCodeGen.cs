using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

// Deterministic code generator: model/qtsharp.api.json -> C# bindings + C++ wrapper.
// Pure function of the model: same input file, byte-identical output.
//
// Type mapping (one row per kind of model type):
//   string            C#                        P/Invoke            C++
//   int/bool/double   int/bool/double           same                same
//   QString           string                    marshalled string   const char16_t* -> QString::fromUtf16
//   X::Y (enum)       X.Y (enum)                int                 int, cast to X::Y
//   int*              int[]                     int[]               const int*
//   Foo* (bound)      Foo                       IntPtr              Foo*
//   Foo* (opaque)     IntPtr                    IntPtr              Foo*
//   Foo (by value)    Foo, owned wrapper        IntPtr              Foo* (boxed, dereferenced)
public static partial class ApiCodeGen
{
    // The identity the generated classes carry in [GeneratedCode]. The generator's own assembly
    // version is stable for a build, so the output stays deterministic.
    static readonly string GeneratedByVersion =
        typeof(ApiCodeGen).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    // The generated types the hand-written runtime extends with a second part. Rather than keeping
    // a second list in the generator, this is read from runtime/*.cs itself: a `partial class` or
    // `partial struct` there is the definition of "this type needs to be partial". Everything else
    // is emitted as one whole declaration.
    static HashSet<string> handWrittenParts;

    static HashSet<string> ReadHandWrittenParts()
    {
        var parts = new HashSet<string>(StringComparer.Ordinal);
        string runtime = Path.Combine(QtLibrary.RepoRoot, "runtime");
        if (!Directory.Exists(runtime))
            return parts;
        var declaration = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:(?:public|internal|protected|private|unsafe|static|sealed|abstract|readonly|new)\s+)*partial\s+(?:class|struct)\s+([A-Za-z_][A-Za-z0-9_]*)",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (string file in Directory.EnumerateFiles(runtime, "*.cs", SearchOption.AllDirectories))
            foreach (System.Text.RegularExpressions.Match match in declaration.Matches(File.ReadAllText(file)))
                parts.Add(match.Groups[1].Value);
        return parts;
    }

    static List<JsonElement> Classes;

    // One C# file per Qt header (the model's `header`), mirroring how the C++ headers are split.
    // The writers below append to the file a declaration belongs to; GenerateCSharp closes the
    // namespaces and hands the set to Run, which writes them under <out>/generated/.
    static Dictionary<string, StringBuilder> csharpFiles;
    static string csharpNamespace;
    // The P/Invoke entry names for the free functions, computed once over the whole list so two
    // headers cannot invent the same name for two different overloads.
    static List<(JsonElement Function, string Entry)> freeFunctionEntries;

    /// <summary>
    /// The classes this run's own model declares, before the runtime structs are filtered out of
    /// <see cref="Classes"/>. The structs' members are emitted from the model as well, but only when
    /// the model is the one that declares them - an extension module inherits them from the core
    /// binding's assembly instead of generating a second copy.
    /// </summary>
    internal static HashSet<string> ModelClassNames = new(StringComparer.Ordinal);
    // The class rows whose members this run emits: ordinary classes plus the runtime structs
    // (QMetaEnum, QDate, ...). The latter are filtered out of Classes but their methods still
    // use containers, pairs and optionals, so SignatureTypes walks this list.
    static List<JsonElement> EmittedClassRows = new();
    // The runtime structs this run's own model declares (un-namespaced core classes only).
    internal static HashSet<string> ModelRuntimeStructNames = new(StringComparer.Ordinal);
    static List<JsonElement> Enums;
    // Namespace-level functions (qCompress, qVersion, ...) - the QtCore/QtGui/QtWidgets module
    // functions, published as a static Qt class.
    static List<JsonElement> Functions = new();
    static Dictionary<string, JsonElement> ByName;
    // Keyed by the C++ reference name (qualified for a namespaced extension class, simple for a
    // core class). ByName is keyed by the simple name the emitters use; a simple name is
    // ambiguous when an extension class shadows a core one, so type resolution goes through
    // this table first.
    static Dictionary<string, JsonElement> ByRef;
    // The owning extension module of every known class/enum (null for the core binding). A class
    // from another extension module is emitted in that module's assembly and namespace, so its
    // C# spelling is qualified with that namespace.
    static Dictionary<string, ApiModel.ExtensionModule> ClassModules = new(StringComparer.Ordinal);
    static Dictionary<string, ApiModel.ExtensionModule> EnumModules = new(StringComparer.Ordinal);
    static HashSet<string> ClassNames;
    static HashSet<string> EnumNames;
    static HashSet<string> FlagsEnums;
    // The extension classes whose C++ name is namespace-qualified: simple name -> Qt3DCore::QEntity.
    // The generated C++ (base specifiers, casts, static calls) has to spell the qualified name.
    static Dictionary<string, string> CppNames = new(StringComparer.Ordinal);

    // The arity classes the runtime publishes. Beyond this a signal is not worth growing them
    // for (no Qt signal in the bound modules comes close).
    const int MaxSignalArity = 8;
    // The same for enums whose published name dropped a module namespace: ChangeFlag ->
    // Qt3DCore::ChangeFlag, API -> Qt3DRender::API.
    static Dictionary<string, string> CppEnumNames = new(StringComparer.Ordinal);

    // Emitters are instances, one per output concern. The context (model tables and output
    // files) is still owned by this run for now; moving it into BindingContext is the next step.
    abstract class Emitter { }

    static string Ref(JsonElement cls)
        => cls.TryGetProperty("cppName", out var cpp) ? cpp.GetString() : Name(cls);

    static string SimpleName(string modelType) => ApiTypes.SimpleName(modelType);

    // A C# string literal for a generated attribute. The model's names are identifiers and Qt
    // types (QList<...>), so only the escapes a compiler would need are handled.
    static string CsString(string text)
        => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // A hand-written type that lives in the QtCore assembly. QFunctionPointer is the one such
    // type the model spells: the struct is not a bound class, so it has no entry in ClassModules
    // and needs the owning namespace spelled out for every dependent module.
    const string QtCoreNamespace = "QtSharp.QtCore";

    static string CoreTypeName(string name)
        => csharpNamespace == QtCoreNamespace ? name : QtCoreNamespace + "." + name;

    // The C# spelling of a model type. A type owned by this module is unqualified; one owned by
    // another module (base or addon) is qualified with that module's namespace.
    static string CsTypeName(string t)
    {
        // QUuid is erased to the BCL type; its Qt class is not emitted (see IsUuid).
        if (IsUuid(t))
            return "Guid";
        // QChar is erased to char (see IsCharT).
        if (IsCharT(t))
            return "char";
        if (IsCharPtrT(t))
            return "string";
        // QVariant::Type is the deprecated Qt5 enum. Its values are metatype ids, and the class it
        // is nested in is not part of the API, so every signature spells it as int.
        if (t == "QVariant::Type")
            return "int";
        // The enums of an erased class cannot stay nested; they are published at namespace level.
        // A dependent module (QtGui seeing QChar::Script) has to qualify the owning module.
        if (HoistedEnumNames.TryGetValue(t, out var hoisted))
            return EnumModules.TryGetValue(t, out var hoistedOwner) && hoistedOwner != null
                   && !ReferenceEquals(hoistedOwner, ApiModel.CurrentModule)
                ? hoistedOwner.Namespace + "." + hoisted
                : hoisted;
        if (ClassModules.TryGetValue(t, out var owner) && owner != null)
            return ReferenceEquals(owner, ApiModel.CurrentModule)
                ? SimpleName(t)
                : owner.Namespace + "." + SimpleName(t);
        if (EnumModules.TryGetValue(t, out var enumOwner))
            return EnumCSharpName(t, enumOwner);
        int i = t.LastIndexOf("::", StringComparison.Ordinal);
        return i >= 0 ? t.Replace("::", ".") : t;
    }

    // The full C++ name of an enum owned by module. A model keeps the owner-relative spelling
    // (QNode::Aspect, API), while a dependent module's signature spells the namespace too
    // (Qt3DCore::QNode::Aspect).
    static string EnumFullName(ApiModel.ExtensionModule owner, string qualified)
    {
        // A base module's model already spells every enum the way a dependent module will
        // (QObject::ConnectionType, Qt::Alignment); the addon extractor keeps the short form in
        // its own model and records the C++ namespace, so only that case needs the prefix.
        if (owner == null || owner.IsCore)
            return qualified;
        foreach (var ns in owner.CppNamespaces)
            if (qualified.StartsWith(ns + "::", StringComparison.Ordinal))
                return qualified;
        return owner.CppNamespaces.Length == 0
            ? qualified
            : owner.CppNamespaces[0] + "::" + qualified;
    }

    // The enum's name inside its owner's namespace (Qt.Alignment, QImage.Format).
    static string EnumManagedName(string t, ApiModel.ExtensionModule owner)
    {
        string managed = t;
        foreach (var ns in owner.CppNamespaces)
        {
            if (managed.StartsWith(ns + "::", StringComparison.Ordinal))
            {
                managed = managed.Substring(ns.Length + 2);
                break;
            }
        }
        // QtCore's Qt:: enums are emitted inside the static Qt class.
        bool qtNamespace = owner.Name == "qtcore" && t.StartsWith("Qt::", StringComparison.Ordinal);
        return qtNamespace ? "Qt." + managed.Replace("::", ".") : managed.Replace("::", ".");
    }

    static string EnumCSharpName(string t, ApiModel.ExtensionModule owner)
    {
        if (owner == null)
            return t.Replace("::", ".");
        string managed = EnumManagedName(t, owner);
        return ReferenceEquals(owner, ApiModel.CurrentModule)
            ? managed
            : owner.Namespace + "." + managed;
    }

    // An enum literal in a default argument is recorded as C# calls would spell it in the
    // declaring assembly (Qt.ImageConversionFlags.AutoColor). When the enum belongs to another
    // module, the prefix has to become that module's qualified name.
    static string QualifyEnumDefault(string literal, string modelType)
    {
        if (string.IsNullOrEmpty(literal))
            return literal;
        string t = RefValueType(modelType);
        if (!EnumModules.TryGetValue(t, out var owner) || owner == null)
            return literal;
        string local = EnumManagedName(t, owner);
        string full = ReferenceEquals(owner, ApiModel.CurrentModule)
            ? local
            : owner.Namespace + "." + local;
        // A flags default combines items with |, so every occurrence is rewritten, not just the
        // leading one (Qt.MatchExactly | Qt.MatchCaseSensitive).
        if (ReferenceEquals(full, local) || literal.Contains(full + "."))
            return literal;
        // The extractor writes a default from the C++ spelling, which may still carry the module
        // namespace (Qt3DRender.API.RHI); the local C# name drops it (API). Try the longer,
        // namespace-carrying spelling first so replacing "API." cannot corrupt that prefix.
        foreach (string candidate in new[] { t.Replace("::", ".") + ".", local + "." })
            if (literal.Contains(candidate))
                return literal.Replace(candidate, full + ".");
        return literal;
    }

    // A *model type*: an extension class is spelled by its qualified C++ name, an unqualified
    // name is a core class (that is what an unqualified name means in an extension model). This
    // is what keeps `QKeyEvent` in QWindow::keyPressEvent resolving to the *core* QKeyEvent
    // rather than Qt3DInput's, which is what decides event-vs-object wrapping.
    static bool TryLookup(string className, out JsonElement cls)
        => ByRef.TryGetValue(className, out cls) || ByName.TryGetValue(className, out cls);

    // A class-table name used by the emitters: a simple name is the class this run emits (an
    // extension class shadows the core one), a qualified name is a base/type reference.
    static bool TryLookupInternal(string className, out JsonElement cls)
        => className.Contains("::", StringComparison.Ordinal)
            ? TryLookup(className, out cls)
            : ByName.TryGetValue(className, out cls);

    static string CppClass(string simpleName)
        => CppNames.TryGetValue(simpleName, out var qualified) ? qualified : simpleName;

    // A C# string literal for generated source (the model's names never contain control
    // characters, but a backslash-quote round trip keeps this safe if one ever does).
    static string Quoted(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // A model type spelled in C++. The model drops the C++ namespace of a Qt3D class from the
    // class table itself but keeps it in signatures (Qt3DCore::QEntity*); an enum nested in one
    // of those classes is still written QBuffer::AccessType, so the owning class has to be
    // qualified here. A core namespace enum (Qt::Alignment) is left alone.
    static string CppTypeName(string t)
    {
        if (string.IsNullOrEmpty(t))
            return t;
        if (CppEnumNames.TryGetValue(t, out var cppEnum))
            return cppEnum;
        if (t.EndsWith("*", StringComparison.Ordinal))
            return CppTypeName(t.Substring(0, t.Length - 1)) + "*";
        // QPair is a using-alias of std::pair; spell the real type in the glue.
        if (t.StartsWith("QPair<", StringComparison.Ordinal) && t.EndsWith(">", StringComparison.Ordinal))
        {
            var (first, second) = ApiTypes.SplitPair(t.Substring("QPair<".Length, t.Length - "QPair<".Length - 1));
            return "std::pair<" + CppTypeName(first) + "," + CppTypeName(second) + ">";
        }
        if (t.StartsWith("std::optional<", StringComparison.Ordinal) && t.EndsWith(">", StringComparison.Ordinal))
            return "std::optional<" + CppTypeName(OptionalElem(t)) + ">";
        foreach (var container in new[] { "QList", "QSet", "QMap", "QHash" })
        {
            if (!t.StartsWith(container + "<", StringComparison.Ordinal) || !t.EndsWith(">", StringComparison.Ordinal))
                continue;
            var inner = t.Substring(container.Length + 1, t.Length - container.Length - 2);
            if (container is "QList" or "QSet")
                return container + "<" + CppTypeName(inner) + ">";
            var (key, value) = ApiTypes.SplitPair(inner);
            return container + "<" + CppTypeName(key) + "," + CppTypeName(value) + ">";
        }
        int i = t.LastIndexOf("::", StringComparison.Ordinal);
        if (i >= 0)
            return CppClass(t.Substring(0, i)) + "::" + t.Substring(i + 2);
        // An unqualified model type is a core class (an extension model spells its own classes
        // qualified), so it must not be mapped through the extension's simple-name table - that
        // would turn a core QKeyEvent into Qt3DInput::QKeyEvent.
        return t;
    }

    // Whether a value of this class returned by value is ours to free (a boxed value type) or
    // belongs to Qt (an event, which Qt builds on the stack).
    // "Owned" only means anything when the managed side can actually create the object: the
    // release path (FromOwnedHandle/Dispose/Destroy_*) is generated together with the
    // constructors, so a boxed class without published constructors has to be wrapped the
    // transient way (QItemSelection, whose constructors take an unbound QModelIndex).
    static bool IsOwned(string className)
        => TryLookup(className, out var cls)
           && HasClass(cls, ApiModel.ClassFlags.Owned)
           && (cls.GetProperty("constructors").GetArrayLength() > 0 || SubclassCtor(cls));

    // An abstract class a managed subclass can derive from: it has no constructor of its own, but
    // the generated native subclass is concrete, so one can be built for it (QAbstractItemModel,
    // QAbstractTableModel). Only an abstract class is ever marked this way.
    static bool SubclassCtor(JsonElement cls)
        => HasClass(cls, ApiModel.ClassFlags.SubclassCtor);

    // The public data members the model published for a class.
    static List<JsonElement> FieldList(JsonElement cls)
        => cls.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().ToList()
            : new List<JsonElement>();

    // The Q_PROPERTY declarations the model published for a class.
    static List<JsonElement> PropertyList(JsonElement cls)
        => cls.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().ToList()
            : new List<JsonElement>();

    // Whether a bound class is a QObject (directly or through a bound base). A QObject's owner can
    // move to Qt at runtime - the parent in setParent, or a layout adopting a widget - so the
    // generated destructor has to ask Qt before deleting anything (see WriteNativeClass).
    static readonly Dictionary<string, bool> derivesFromQObject = new(StringComparer.Ordinal);

    static bool DerivesFromQObject(string className)
    {
        if (className == "QObject")
            return true;
        if (derivesFromQObject.TryGetValue(className, out bool known))
            return known;
        derivesFromQObject[className] = false;      // a cycle would otherwise not terminate
        bool result = false;
        if (TryLookup(className, out var cls))
        {
            foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()))
            {
                if (DerivesFromQObject(b))
                {
                    result = true;
                    break;
                }
            }
        }
        derivesFromQObject[className] = result;
        return result;
    }

    // Whether a bound class is a QEvent (directly or through a bound base). An event is the one
    // object Qt builds on the caller's stack and hands to a handler for the duration of the call,
    // with the address reused by the next event - which may well be of another type.
    static readonly Dictionary<string, bool> derivesFromEvent = new(StringComparer.Ordinal);

    static bool DerivesFromEvent(string className)
    {
        if (className == "QEvent")
            return true;
        if (derivesFromEvent.TryGetValue(className, out bool known))
            return known;
        derivesFromEvent[className] = false;        // a cycle would otherwise not terminate
        bool result = false;
        if (TryLookup(className, out var cls))
        {
            foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()))
            {
                if (DerivesFromEvent(b))
                {
                    result = true;
                    break;
                }
            }
        }
        derivesFromEvent[className] = result;
        return result;
    }

    // Wrapping the handle of a bound class that Qt passes to managed code (a virtual's argument or
    // a signal's).
    //
    // An event must *not* enter the identity cache: it lives on the caller's stack, the next event
    // reuses the address, and the cache would hand that next event the previous one's wrapper (a
    // QMouseEvent wrapper for what is now a QPaintEvent). Everything else is an object with a life
    // of its own, and identity is exactly what the C# side expects of it - the editor a delegate
    // created has to come back as the very QKeySequenceEdit it built, or the cast to it fails
    // (that is what a real double-click in the shortcut editor exposed).
    static string ClassPtrRead(string cls, string expression)
        => DerivesFromEvent(cls)
            ? $"{CsTypeName(cls)}.FromTransientHandle({expression})"
            : $"{CsTypeName(cls)}.FromHandle({expression})";

    /// <summary>
    /// The native library the generated P/Invokes call. The core binding points at QtSharp.Api; an
    /// extension module (QtSharp.Charts and whatever follows it) points at its own DLL. That is what
    /// lets a module be packaged on its own, against the core binding, without recompiling the core
    /// - the module's generated file carries the name it needs instead of borrowing the runtime's.
    /// </summary>
    internal static string NativeLibraryName = "QtSharp.Api";

    /// <summary>The model file this run generated from, for the banner comment.</summary>
    internal static string ModelFileName = "model/qtsharp.api.json";

    /// <summary>
    /// The model file as it was passed in. The hand-written parts of the binding (runtime/QtStructs.cs)
    /// are found relative to it, so the generator can tell which of a value type's members are already
    /// spelled out by hand and leave them alone.
    /// </summary>
    internal static string ModelPath = "";

    /// <summary>
    /// Every class this run knows - the ones it emits, plus (for an extension module) the core's.
    /// Used where a *reference* to a class matters rather than its declaration: the native side has
    /// to include the header of every type it mentions, and a module's signatures mention core types
    /// (QDateTime, QEasingCurve, QColor) whose headers only the core's classes bring in.
    /// </summary>
    internal static List<JsonElement> KnownClasses = new();

    /// <summary>
    /// The model of an extension module this one builds on (Qt3DCore for Qt3DRender, ...). The
    /// sibling of the module model is tried first, then QTSHARP_MODEL_DIR, then the checkout.
    /// </summary>
    static string DependencyModelPath(ApiModel.ExtensionModule dependency, string modelPath)
    {
        string name = $"qtsharp.{dependency.Name}.json";
        var sibling = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modelPath)), name);
        if (File.Exists(sibling))
            return sibling;
        var configured = Environment.GetEnvironmentVariable("QTSHARP_MODEL_DIR");
        if (!string.IsNullOrEmpty(configured))
        {
            var candidate = Path.Combine(configured, name);
            if (File.Exists(candidate))
                return candidate;
        }
        if (QtLibrary.RepoRoot != null)
        {
            var inRepo = Path.Combine(QtLibrary.RepoRoot, "model", name);
            if (File.Exists(inRepo))
                return inRepo;
        }
        throw new FileNotFoundException(
            $"'{dependency.Name}' is a dependency of this module, but model/{name} was not found beside " +
            $"'{modelPath}'. Put it in the same model directory or set QTSHARP_MODEL_DIR.");
    }

    /// <summary>
    /// Generates a binding from a model. With <paramref name="module"/> null that is the core
    /// binding; with one it is that extension module's binding, which is a separate assembly and a
    /// separate package - it references the core one rather than containing it.
    /// </summary>
    public static void Run(string modelPath, string outDir, ApiModel.ExtensionModule module = null,
                           bool nativeOnly = false)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(modelPath));

        var root = document.RootElement;
        string ns = root.GetProperty("namespace").GetString();
        ModelFileName = Path.GetFileName(modelPath);
        ModelPath = Path.GetFullPath(modelPath);
        NativeLibraryName = module?.NativeLibrary ?? "QtSharp.Api";
        ApiModel.CurrentModule = module;

        // Every module (base or addon) reads the models of the modules it builds on. There is no
        // special "core model": QtCore/QtGui/... are modules like Qt3DCore/Qt3DRender, and the
        // combined qtsharp.api.json is only the extraction gate and the input of the splitter.
        var dependencyDocuments = new List<(ApiModel.ExtensionModule Module, JsonDocument Document)>();
        if (module != null)
        {
            foreach (var dependency in ApiModel.ModuleClosure(module))
            {
                if (ReferenceEquals(dependency, module))
                    continue;
                dependencyDocuments.Add((dependency, JsonDocument.Parse(
                    File.ReadAllText(DependencyModelPath(dependency, modelPath)))));
            }
        }

        // What this run *emits*. Runtime value types (QPoint, QColor, ...) are generated as
        // structs, not classes, so they are filtered out of Classes but stay known.
        var modelClasses = root.GetProperty("classes").EnumerateArray().ToList();
        var modelEnums = root.GetProperty("enums").EnumerateArray().ToList();
        Classes = modelClasses.Where(c => !IsRuntimeStructClass(c) && !IsUuid(Name(c)) && !IsCharT(Name(c))).ToList();
        EmittedClassRows = modelClasses.Where(c => !IsUuid(Name(c)) && !IsCharT(Name(c))).ToList();
        // The core classes this model itself declares as runtime structs. A simple name is not
        // enough: an extension has its own namespaced Qt3DCore::QTransform, which must not be
        // mistaken for QtGui's QTransform struct.
        ModelRuntimeStructNames = new HashSet<string>(modelClasses
            .Where(IsRuntimeStructClass)
            .Select(Name), StringComparer.Ordinal);
        ModelClassNames = modelClasses.Select(c => Name(c)).ToHashSet(StringComparer.Ordinal);
        Enums = module == null
            ? modelEnums
            : modelEnums.Where(e => !e.TryGetProperty("module", out var owner)
                                    || owner.GetString() == module.Name).ToList();
        Functions = root.TryGetProperty("functions", out var fns) && fns.ValueKind == JsonValueKind.Array
            ? fns.EnumerateArray().ToList()
            : new List<JsonElement>();

        ClassModules = new Dictionary<string, ApiModel.ExtensionModule>(StringComparer.Ordinal);
        EnumModules = new Dictionary<string, ApiModel.ExtensionModule>(StringComparer.Ordinal);

        void RegisterEnum(JsonElement e, ApiModel.ExtensionModule fallback)
        {
            string qualified = e.GetProperty("qualified").GetString();
            // An extension model carries its dependencies' enums too; the entry's `module` is the
            // owner that emits it, the fallback only applies to an older model without the field.
            var owner = fallback;
            if (e.TryGetProperty("module", out var ownerName) && ownerName.ValueKind == JsonValueKind.String)
                owner = ApiModel.FindExtensionModule(ownerName.GetString()) ?? fallback;
            EnumModules[qualified] = owner;
            // A dependent module's signature spells the enum with the owner's C++ namespace
            // (Qt3DCore::QNode::Aspect); the owner model keeps the short form (QNode::Aspect).
            // Register both, so either spelling resolves to the owning assembly.
            EnumModules[EnumFullName(owner, qualified)] = owner;
        }

        var dependencyClasses = new List<JsonElement>();
        var dependencyEnums = new List<JsonElement>();
        foreach (var (dependency, dependencyDocument) in dependencyDocuments)
        {
            foreach (var c in dependencyDocument.RootElement.GetProperty("classes").EnumerateArray())
            {
                dependencyClasses.Add(c);
                ClassModules[Ref(c)] = dependency;
            }
            foreach (var e in dependencyDocument.RootElement.GetProperty("enums").EnumerateArray())
            {
                dependencyEnums.Add(e);
                RegisterEnum(e, dependency);
            }
        }

        foreach (var c in Classes)
            ClassModules[Ref(c)] = module;
        foreach (var e in modelEnums)
            RegisterEnum(e, module);

        // includes the runtime structs (QPoint, QColor) even though they are not emitted as
        // classes: their rows carry the header and the members the struct partial is written from.
        var knownClasses = dependencyClasses.Concat(modelClasses).ToList();
        var knownEnums = dependencyEnums.Concat(modelEnums).ToList();
        KnownClasses = knownClasses;

        // Keyed by the name the model uses in signatures: simple for a core module, qualified
        // for a namespaced module (Qt3DCore::QEntity). ByRef resolves a model type, ByName is
        // what the emitters use for the class they are writing.
        ByName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        ByRef = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        CppNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in knownClasses)
        {
            ByName[Name(c)] = c;
            ByRef[Ref(c)] = c;
            if (c.TryGetProperty("cppName", out var cpp))
                CppNames[Name(c)] = cpp.GetString();
        }
        ClassNames = new HashSet<string>(knownClasses.Select(c => Ref(c)), StringComparer.Ordinal);
        foreach (var c in knownClasses)
            ClassNames.Add(Name(c));

        EnumNames = new HashSet<string>(StringComparer.Ordinal);
        CppEnumNames = new Dictionary<string, string>(StringComparer.Ordinal);
        FlagsEnums = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in knownEnums)
        {
            string qualified = e.GetProperty("qualified").GetString();
            string full = EnumFullName(EnumModules.TryGetValue(qualified, out var owner) ? owner : null, qualified);
            string cpp = e.TryGetProperty("cppQualified", out var cppQualified)
                ? cppQualified.GetString()
                : full;
            EnumNames.Add(qualified);
            EnumNames.Add(full);
            CppEnumNames[qualified] = cpp;
            CppEnumNames[full] = cpp;
            if (e.TryGetProperty("flags", out var f) && f.ValueKind == JsonValueKind.True)
            {
                FlagsEnums.Add(qualified);
                FlagsEnums.Add(full);
            }
        }
        PrepareNames();
        derivesFromQObject.Clear();
        // Handler selection is a function of this run's class/enum tables.
        typeHandlerCache.Clear();
        freeFunctionEntries = null;
        handWrittenParts = ReadHandWrittenParts();

        string assemblyName = module?.Namespace ?? ApiModel.CoreNamespace;
        Directory.CreateDirectory(outDir);
        string generatedDir = Path.Combine(outDir, "generated");
        int written = 0;
        if (!nativeOnly)
        {
            var files = GenerateCSharp(ns);
            // Keep the generated tree in sync without rewriting files whose bytes did not change: an
            // unchanged tree keeps its timestamps, so the module assemblies stay up to date and a
            // `dotnet build` after a regen is a no-op. Files that are no longer generated are
            // removed, so a type that moved to a different header cannot leave a stale file
            // behind; the old single-file output goes as well.
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
                expected.Add(Path.GetFullPath(Path.Combine(outDir, file.Key.Replace('/', Path.DirectorySeparatorChar))));
            if (Directory.Exists(generatedDir))
            {
                foreach (var existing in Directory.GetFiles(generatedDir, "*", SearchOption.AllDirectories))
                    if (!expected.Contains(Path.GetFullPath(existing)))
                        File.Delete(existing);
            }
            foreach (var file in files)
            {
                string path = Path.Combine(outDir, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                WriteIfChanged(path, file.Value);
            }
            string staleSingleFile = Path.Combine(outDir, assemblyName + ".cs");
            if (File.Exists(staleSingleFile))
                File.Delete(staleSingleFile);
            written = files.Count;
        }
        else if (Directory.Exists(generatedDir))
        {
            // The combined base model is only the extraction gate and the native glue's input now;
            // the assemblies are generated from the per-module split models, so a combined C# tree
            // here would just be a second, uncompiled copy of the binding.
            Directory.Delete(generatedDir, true);
        }

        WriteIfChanged(Path.Combine(outDir, assemblyName + ".native.cpp"), GenerateNative());
        foreach (var (_, dependencyDocument) in dependencyDocuments)
            dependencyDocument.Dispose();
        Console.WriteLine(nativeOnly
            ? $"[codegen] wrote {assemblyName}.native.cpp (native glue only)"
            : $"[codegen] wrote {outDir}/generated/ ({written} files) + {assemblyName}.native.cpp");
    }

    // Writes only when the bytes differ. File.WriteAllText uses UTF-8 without a BOM, and so does
    // this, so the comparison is against exactly what a previous run wrote.
    static void WriteIfChanged(string path, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            return;
        File.WriteAllBytes(path, bytes);
    }

    // ------------------------------------------------------------------ model queries
    static string Name(JsonElement e) => e.GetProperty("name").GetString();

    // Members Qt declares only when building for Windows. The model is extracted from the MSVC
    // headers, so they are in it, but a Linux build has no such symbol - the generated C# and the
    // native glue both skip them, and the .NET equivalent (ProcessStartInfo.Arguments) covers the
    // use case. Keep this to APIs qprocess.h puts under #if defined(Q_OS_WIN).
    static readonly HashSet<string> WindowsOnlyMembers = new(StringComparer.Ordinal)
    {
        "QProcess.nativeArguments",
        "QProcess.setNativeArguments",
    };

    static IEnumerable<JsonElement> Methods(JsonElement cls)
        => cls.GetProperty("methods").EnumerateArray()
            .Where(m => !WindowsOnlyMembers.Contains(Name(cls) + "." + Name(m)));
    static string ReturnType(JsonElement m) => m.GetProperty("returnType").GetString();
    static List<JsonElement> Params(JsonElement m) => m.GetProperty("parameters").EnumerateArray().ToList();

    // The constructors the binding publishes. One that takes a const QChar* is dropped: Qt keeps
    // the pointer (QTextBoundaryFinder::QTextBoundaryFinder does not copy the string), while the
    // UTF-16 buffer behind a managed string only lives for the call - the object would hold a
    // dangling pointer. Its other entry points are still published. Every emitter walks this list,
    // so the constructor ordinals in C# and in the glue stay in step.
    static List<JsonElement> PublishedCtors(JsonElement cls)
    {
        // A constructor that takes a const QChar* is dropped: the managed string's buffer only
        // lives for the call, so an object that kept the pointer (QTextBoundaryFinder) would hold
        // a dangling one. Qt's QString/QStringView constructors of the same class have the same
        // hazard, but they predate this erasure and keep their published shape.
        return cls.GetProperty("constructors").EnumerateArray()
            .Where(c => !Params(c).Any(p => IsCharPtrT(PType(p))))
            // QVariant(QVariant::Type) is the deprecated "a default value of this metatype" ctor.
            // QVariant::Type is spelled int in signatures, which would collide with QVariant(int),
            // and the class is internal anyway; the value it builds is not needed by the bridge.
            .Where(c => !(Name(cls) == "QVariant" && Params(c).Any(p => PType(p) == "QVariant::Type")))
            .ToList();
    }
    static string PType(JsonElement p) => p.GetProperty("type").GetString();
    static string PName(JsonElement p) => p.GetProperty("name").GetString();

    static bool IsStatic(JsonElement m)
        => HasMethod(m, ApiModel.MethodFlags.Static);

    // C++ lets us derive from the class, so a native subclass can install the vtable hooks.
    static bool Subclassable(JsonElement cls)
        => HasClass(cls, ApiModel.ClassFlags.Subclassable);

    // The value types Qt itself converts into QVariant (`operator QVariant()`): QColor, QBrush,
    // QIcon and friends. They get the IQtVariantValue marker and a conversion registration.
    static bool ConvertsToVariant(JsonElement cls)
        => HasClass(cls, ApiModel.ClassFlags.ToVariant);

    // QVariant is the one Qt type the managed surface does not name. Like PySide6 (where the class
    // is not exposed at all), a function that takes or returns one takes or returns `object`: the
    // generated wrapper converts at the boundary through VariantMarshalling, so callers hand over
    // and receive ordinary C# values, null is the invalid variant, and no temporary QVariant
    // wrapper - and no Dispose - leaks into a call site. The native ABI is unchanged (a pointer to
    // the QVariant, as any boxed value), so all of IsBoxed's machinery (assign/clone helpers, the
    // virtual trampoline slots) still applies.
    const string VariantMarshalling = "QtSharp.QtCore.VariantMarshalling";

    // The value types qvariant.h itself declares a constructor for (QPoint, QUrl, QDate,
    // QModelIndex, ...): one constructor parameter each, and QVariant has the matching ToXxx()
    // accessor. Both directions are already generated, so registering the pair here is what lets
    // QVariant's object entry points follow the header instead of a hand-kept list in
    // BclInterop.Variant.cs. Only the module that declares a type emits its registration.
    static List<(string Managed, string Cpp, string Unwrap)> VariantCtorRegistrations()
    {
        var registrations = new List<(string, string, string)>();
        if (!TryLookup("QVariant", out var qvariant)
            || !qvariant.TryGetProperty("constructors", out var constructors))
            return registrations;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ctor in constructors.EnumerateArray())
        {
            if (!ctor.TryGetProperty("parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Array
                || parameters.GetArrayLength() != 1)
                continue;
            string type = parameters[0].TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            // Named types only: QString/QStringList are the managed string forms, the containers
            // are QList/QHash, and QVariant::Type is an enum.
            if (string.IsNullOrEmpty(type) || type.Contains('<') || type.Contains('*'))
                continue;
            if (!ByName.TryGetValue(ApiTypes.SimpleName(type), out var cls))
                continue;
            // An extension class that shares the simple name (Qt3DCore::QTransform) is not the
            // core type the QVariant constructor means.
            if (cls.TryGetProperty("cppName", out _))
                continue;
            // Runtime structs (QPoint, QDate, ...) are filtered out of Classes but are still
            // declared by the model that owns them, which is the test that matters here.
            if (!ModelClassNames.Contains(Name(cls)))
                continue;
            string managed = CsTypeName(type);
            if (!seen.Add(managed))
                continue;
            // The unwrap direction is the generated P/Invoke the accessor itself calls, so it
            // needs no QVariant instance (the class is internal to this assembly): the lambda
            // takes the native handle and hands back the managed value directly.
            string accessor = VariantAccessor(qvariant, type);
            string unwrap = accessor == null
                ? null
                : RetToManaged(type,
                    $"ApiNative.QVariant_{char.ToLowerInvariant(accessor[0])}{accessor.Substring(1)}(h)");
            registrations.Add((managed, CppClass(Name(cls)), unwrap));
        }
        return registrations;
    }

    // The QVariant method that gives the value back (toPoint -> ToPoint); null when the header has
    // no accessor, in which case only the create direction is registered.
    static string VariantAccessor(JsonElement qvariant, string type)
    {
        if (!qvariant.TryGetProperty("methods", out var methods))
            return null;
        string simple = ApiTypes.SimpleName(type);
        string stripped = simple.Length > 1 && simple[0] == 'Q' ? simple.Substring(1) : simple;
        foreach (var method in methods.EnumerateArray())
        {
            if (!method.TryGetProperty("parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Array
                || parameters.GetArrayLength() != 0)
                continue;
            if (!method.TryGetProperty("returnType", out var returnType)
                || returnType.GetString() != type)
                continue;
            string name = Pascal(method.GetProperty("name").GetString());
            if (name == "To" + stripped || name == "To" + simple)
                return name;
        }
        return null;
    }

    // The generated half of the C# -> Qt type-name map the runtime's MetaType class publishes.
    // Only the types this run emits are registered: the dependencies' types belong to their own
    // assemblies and their own module initializers. The exact C++ spelling matters - a Qt3D class
    // is "Qt3DCore::QEntity", not the simple C# name - and the kind is what decides whether
    // MetaType appends the "*" a QObject pointer metatype carries.
    static void WriteTypeTable(string ns)
    {
        var table = CSharpFile(PlainFileKey(ns + ".TypeTable"));
        table.AppendLine("    /// <summary>The Qt type names of the types this assembly publishes.</summary>");
        table.AppendLine("    internal static class BoundTypeTable");
        table.AppendLine("    {");
        table.AppendLine("        [ModuleInitializer]");
        table.AppendLine("        internal static void Register()");
        table.AppendLine("        {");
        foreach (var cls in Classes)
        {
            string name = Name(cls);
            // QVariant is the internal bridge type: no declared member can name it, and the
            // marshaler handles its metatype separately.
            if (name == "QVariant")
                continue;
            // The QObject subtree is the pointer metatype (QWidget*, Qt3DCore::QEntity*); the
            // owned classes that do not derive from QObject (QFont, QByteArray) cross by value.
            string kind = DerivesFromQObject(Ref(cls)) ? "ObjectPointer" : "ValueClass";
            table.AppendLine(
                $"            QtSharp.Runtime.MetaType.Register(typeof({CsTypeName(Ref(cls))}), {Quoted(CppClass(name))}, QtSharp.Runtime.QtTypeKind.{kind});");
        }
        // The runtime structs (QColor, QDate, QNodeId) are values with no handle.
        foreach (var s in ApiTypes.RuntimeStructs)
        {
            if (!ModelRuntimeStructNames.Contains(s.Name))
                continue;
            table.AppendLine(
                $"            QtSharp.Runtime.MetaType.Register(typeof({s.Name}), {Quoted(ApiTypes.CppStructName(s.Name))}, QtSharp.Runtime.QtTypeKind.Pod);");
        }
        // Enums keep their bare name too; cppQualified is what keeps a Qt3D enum's namespace.
        foreach (var e in Enums)
        {
            // An enum whose owning class was erased (QChar, QUuid) has no managed type to name
            // unless it was hoisted to the namespace; QVariant::Type became int.
            if (!EnumIsEmitted(e))
                continue;
            string qualified = e.GetProperty("qualified").GetString();
            string cpp = CppEnumNames.TryGetValue(qualified, out var spelled) ? spelled : qualified;
            table.AppendLine(
                $"            QtSharp.Runtime.MetaType.Register(typeof({CsTypeName(qualified)}), {Quoted(cpp)}, QtSharp.Runtime.QtTypeKind.Enum);");
        }
        table.AppendLine("        }");
        table.AppendLine("    }");
        table.AppendLine();
    }

    // Whether the generated C# for an enum exists: a namespace-level or Qt-namespace one, one
    // hoisted out of an erased class, or one nested in a class/struct this run emits.
    static bool EnumIsEmitted(JsonElement e)
    {
        string qualified = e.GetProperty("qualified").GetString();
        string scope = ScopeOf(e);
        if (scope.Length == 0 || scope == "Qt")
            return true;
        if (HoistedEnumNames.ContainsKey(qualified))
            return true;
        // QVariant::Type is the deprecated Qt5 enum of metatype ids: signatures spell it as int,
        // and the class itself is internal to the bridge, so there is no managed type to name.
        if (scope == "QVariant")
            return false;
        if (ModelRuntimeStructNames.Contains(scope))
            return true;
        return Classes.Any(c => Name(c) == scope);
    }

    // Every declaration kind carries one flag list; these read it once and the callers test bits.
    static int FlagBits(JsonElement e, Func<string, int> bit)
    {
        int bits = 0;
        if (!e.TryGetProperty("flags", out var list) || list.ValueKind != JsonValueKind.Array)
            return bits;
        foreach (var entry in list.EnumerateArray())
            bits |= bit(entry.GetString());
        return bits;
    }

    static ApiModel.ClassFlags ClassFlagsOf(JsonElement cls)
        => (ApiModel.ClassFlags)FlagBits(cls, name => name switch
        {
            "abstract" => (int)ApiModel.ClassFlags.Abstract,
            "subclassable" => (int)ApiModel.ClassFlags.Subclassable,
            "subclassCtor" => (int)ApiModel.ClassFlags.SubclassCtor,
            "owned" => (int)ApiModel.ClassFlags.Owned,
            "nonCopyable" => (int)ApiModel.ClassFlags.NonCopyable,
            "toVariant" => (int)ApiModel.ClassFlags.ToVariant,
            _ => 0,
        });

    static ApiModel.MethodFlags MethodFlagsOf(JsonElement m)
        => (ApiModel.MethodFlags)FlagBits(m, name => name switch
        {
            "static" => (int)ApiModel.MethodFlags.Static,
            "protected" => (int)ApiModel.MethodFlags.Protected,
            "const" => (int)ApiModel.MethodFlags.Const,
            "iterator" => (int)ApiModel.MethodFlags.Iterator,
            "ownedReturn" => (int)ApiModel.MethodFlags.OwnedReturn,
            "slot" => (int)ApiModel.MethodFlags.Slot,
            "invokable" => (int)ApiModel.MethodFlags.Invokable,
            _ => 0,
        });

    static ApiModel.ParamFlags ParamFlagsOf(JsonElement p)
        => (ApiModel.ParamFlags)FlagBits(p, name => name switch
        {
            "buffer" => (int)ApiModel.ParamFlags.Buffer,
            "optional" => (int)ApiModel.ParamFlags.Optional,
            _ => 0,
        });

    static bool HasClass(JsonElement cls, ApiModel.ClassFlags flag)
        => (ClassFlagsOf(cls) & flag) != 0;
    static bool HasMethod(JsonElement m, ApiModel.MethodFlags flag)
        => (MethodFlagsOf(m) & flag) != 0;
    static bool HasParam(JsonElement p, ApiModel.ParamFlags flag)
        => (ParamFlagsOf(p) & flag) != 0;

    // The traits a virtual declaration carries, read from the model's one flag list.
    static ApiModel.VirtualFlags VirtualFlagsOf(JsonElement v)
        => (ApiModel.VirtualFlags)FlagBits(v, name => name switch
        {
            "pure" => (int)ApiModel.VirtualFlags.Pure,
            "const" => (int)ApiModel.VirtualFlags.Const,
            "noBase" => (int)ApiModel.VirtualFlags.NoBase,
            "final" => (int)ApiModel.VirtualFlags.Final,
            _ => 0,
        });

    static bool Has(JsonElement v, ApiModel.VirtualFlags flag)
        => (VirtualFlagsOf(v) & flag) != 0;

    // A virtual with nothing to chain to: the managed default implementation does nothing.
    static bool HasNoBase(JsonElement v)
        => Has(v, ApiModel.VirtualFlags.Pure | ApiModel.VirtualFlags.NoBase);

    // A const virtual must be declared const to override anything (QWidget::sizeHint() const).
    static string ConstSuffix(JsonElement v)
        => Has(v, ApiModel.VirtualFlags.Const) ? " const" : "";

    // Whether a value type can be default-constructed in C++ (needed when a managed null is
    // passed for a boxed parameter).
    static bool HasDefaultCtor(string className)
        => TryLookup(className, out var cls)
           && cls.GetProperty("constructors").EnumerateArray().Any(c => Params(c).Count == 0);

    static bool IsInheritedVirtual(JsonElement v)
        => v.TryGetProperty("inherited", out var i) && i.ValueKind == JsonValueKind.True;

    static string DeclaringClass(JsonElement v) => v.GetProperty("declaring").GetString();

    static bool IsOptional(JsonElement p)
        => HasParam(p, ApiModel.ParamFlags.Optional);

    // A protected member of an extension class (QAbstractItemModel::createIndex, beginInsertRows,
    // ...). C++ only lets a *member* call it, so the generated subclass exposes a static helper
    // the native forwarders go through - the same shape as the CallBase_* forwarders.
    static bool IsProtected(JsonElement m)
        => HasMethod(m, ApiModel.MethodFlags.Protected);

    // How a parameter crosses: "out" (Qt writes one value through a non-const pointer), "ref"
    // (in and out through a non-const reference), or null for a plain input.
    static string ParamModeName(JsonElement p)
        => p.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : null;

    // The model spells the mode as a string; the codegen works with the exclusive enum, so the
    // illegal "both out and ref" state cannot be built by accident.
    static ApiModel.ParamMode ParamModeOf(JsonElement p)
        => ParamModeName(p) switch
        {
            "out" => ApiModel.ParamMode.Out,
            "ref" => ApiModel.ParamMode.Ref,
            _ => ApiModel.ParamMode.In,
        };

    static bool IsOut(JsonElement p) => ParamModeOf(p) == ApiModel.ParamMode.Out;
    static bool IsRef(JsonElement p) => ParamModeOf(p) == ApiModel.ParamMode.Ref;
    static bool IsByRef(JsonElement p) => ParamModeOf(p) != ApiModel.ParamMode.In;

    // The value a by-reference parameter carries: an out pointer's model type is the scalar
    // pointer the C++ declaration has (int*), a ref's is the scalar itself (int).
    static string RefValueType(string t) => t.EndsWith("*") ? t.Substring(0, t.Length - 1) : t;

    static string ParamManagedType(JsonElement p) => ParamFor(p).ManagedType(p);

    // A container parameter accepts the read-only interface for its kind, so a List<T>, an array,
    // a HashSet<T> or a Dictionary all work without an overload. Returns stay concrete.
    static string ParamValueManagedType(JsonElement p)
    {
        string t = PType(p);
        if (IsStringListT(t))
            return "IReadOnlyList<string>";
        var container = ContainerOf(t);
        if (container?.Family == "list")
            return $"IReadOnlyList<{ManagedElement(container.Element)}>";
        if (container?.Family == "set")
            return $"IReadOnlySet<{ManagedElement(container.Element)}>";
        if (container?.Family is "map" or "hash")
            return $"IReadOnlyDictionary<{ManagedElement(container.Key)}, {ManagedElement(container.Value)}>";
        return ManagedType(t);
    }

    // A non-const QString& is Qt's in-out string (QValidator::validate/fixup, QClipboard::text).
    // The C ABI cannot carry a reference, so one model parameter becomes two native ones: the
    // string going in, and a pointer the updated copy comes back through.
    static bool IsStringByRef(JsonElement p) => IsByRef(p) && PType(p) == "QString";
    static string StringOutLocal(JsonElement p) => "__out_" + PName(p);
    static string StringInLocal(string nativeName) => "qs_in_" + nativeName;

    // Qt's headers are full of default arguments; carrying them into C# keeps navigation
    // like `new QObject()` or `SetRenderHint(hint)` working.
    static string ParamSig(JsonElement p) => ParamFor(p).Signature(p);

    // CSharpDefault lives in TypeHandlers.cs with the other per-kind mappings.

    static bool IsOpaqueStructPtr(JsonElement p) => IsOptional(p) && IsStructPtr(PType(p));

    /// <summary>
    /// An optional value-type parameter whose declaration default the model kept as C++ (QWidget::grab's
    /// `QRect rectangle = QRect(QPoint(0, 0), QSize(-1, -1))` - "the whole widget"). A value type has no
    /// way to say "not given" - that is what a null handle used to carry, and the glue is what put the
    /// declaration's own default in its place. Spelling the parameter as the *nullable* struct gives
    /// that state back: the marshaller hands the glue a null pointer and the glue applies the default.
    /// </summary>
    /// <summary>
    /// Whether a C++ default is a plain constructor expression, the only shape that can be pasted
    /// into the glue's ternary. A braced initialiser, a scope-qualified enumerator or a reference
    /// would not parse there, so those parameters keep the older `= default` fallback.
    /// </summary>
    static bool IsPlainConstructorDefault(string expression)
        // A braced initialiser or a reference cannot be pasted into the ternary; a scope-qualified
        // enumerator (Qt::white for QPixmap::fill's QColor) can, and the glue wraps it in the
        // value type below.
        => expression.IndexOf('{') < 0 && expression.IndexOf('&') < 0;

    static bool IsOptionalStructDefault(JsonElement p)
        => IsOptional(p) && IsStruct(PType(p)) && !IsByRef(p)
           && p.TryGetProperty("defaultCpp", out var cpp) && cpp.ValueKind == JsonValueKind.String
           && IsPlainConstructorDefault(cpp.GetString())
           // A default that *is* the type's zero value needs no "not given" state: the managed
           // `default` is the same value and the ABI can carry it directly. QModelIndex is the
           // case this was added for - its zero value is the invalid index, which is exactly
           // QModelIndex(). QTime is deliberately absent: its null value has mds == -1, so a
           // default(QTime) would be midnight, not Qt's null time.
           && !(SelfDefaultStructs.Contains(PType(p))
                && IsSelfDefaultConstruction(PType(p), cpp.GetString()));

    // The runtime structs whose zero value is identical to Qt's default construction, so an
    // optional parameter defaulting to `T()` can surface as `T param = default` instead of a
    // nullable struct (QTime is not one of them: its null value is not its zero value).
    static readonly HashSet<string> SelfDefaultStructs = new(StringComparer.Ordinal)
    {
        "QModelIndex",
    };

    // The structs whose zero value is not Qt's default construction, so the parameterless
    // constructor is published instead of being folded into C#'s `default`. QCalendar's default
    // constructor is the Gregorian calendar (a zeroed QCalendar is the null backend), and a
    // QKeyFrame's is BezierInterpolation with zeroed vectors (zero is ConstantInterpolation).
    static readonly HashSet<string> ZeroCtorStructs = new(StringComparer.Ordinal)
    {
        "QCalendar",
        "QKeyFrame",
    };

    static bool IsSelfDefaultConstruction(string type, string expression)
        => expression.Replace(" ", "").TrimEnd() == type + "()";

    /// <summary>The C name of the "was it given" flag an optional value type travels with.</summary>
    static string GivenName(JsonElement p) => "qs_given_" + NativeName(PName(p));

    /// <summary>
    /// One parameter handed on to another ABI-shaped function (a virtual's `_vt` twin, a protected
    /// helper) as the arguments that call takes: its name, plus the flag when the type is an
    /// optional value type.
    /// </summary>
    static IEnumerable<string> AbiArgs(JsonElement p) => ParamFor(p).AbiArgs(p);

    static string DefaultCppOf(JsonElement p)
        => p.GetProperty("defaultCpp").GetString();

    // The C++ expression for an optional struct parameter's declared default. `{}` is value
    // initialisation (T()); a constructor call is already a T; anything else (Qt::white for
    // QPixmap::fill's QColor) is converted through T's own constructor so the ternary has one type.
    static string StructDefaultExpr(string type, string defaultCpp)
    {
        string cppType = ApiTypes.CppStructName(type);
        if (defaultCpp.Replace(" ", "") == "{}")
            return $"{cppType}()";
        string trimmed = defaultCpp.Trim();
        if (trimmed.StartsWith(cppType + "(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            return defaultCpp;
        return $"{cppType}({defaultCpp})";
    }

    // The enums of an erased class cannot stay nested in a type that no longer exists, so they are
    // published at namespace level with a prefix that keeps them recognisable. QChar::Category is
    // absent on purpose: Category() returns the BCL's System.Globalization.UnicodeCategory.
    static readonly Dictionary<string, string> HoistedEnumNames = new(StringComparer.Ordinal)
    {
        ["QChar::Decomposition"] = "CharDecomposition",
        ["QChar::Direction"] = "CharDirection",
        ["QChar::JoiningType"] = "CharJoiningType",
        ["QChar::Script"] = "CharScript",
        ["QChar::UnicodeVersion"] = "UnicodeVersion",
    };

    // The model was extracted from the MSVC headers, where `long` is 32 bits and the managed side is
    // int/uint. On LP64 (Linux x86-64) the same spelling is 64 bits, which would disagree with the
    // P/Invoke declaration, so the C ABI pins it to the 32-bit type the managed side uses and lets
    // the wrapper convert at the Qt call instead.
    static string CppAbiScalar(string t) => t switch
    {
        "long" => "int",
        "unsigned long" => "unsigned int",
        _ => t,
    };
    static bool IsPointerElem(string e) => e.EndsWith("*");

    // Reading a pointer element. A QObject goes through FromHandle, so a QList<QObject*> element
    // (QObject::children()) is the object's actual bound class and keeps its identity, exactly
    // like a QObject* return value. Any other pointer keeps the transient wrapper: a
    // QGraphicsItem has no meta-object and no lifetime the identity cache could track.
    static string PointerElemRead(string e, string expr)
    {
        string pointee = e.Substring(0, e.Length - 1);
        return $"{CsTypeName(pointee)}.{(DerivesFromQObject(pointee) ? "FromHandle" : "FromTransientHandle")}({expr})";
    }
    // A C identifier for one element type: used for the native exports, the generated helper
    // class and the DllImport method names.
    static string ListStem(string elem)
    {
        var stem = elem.Replace("*", "Ptr").Replace(" ", "").Replace("::", "_").Replace("unsigned", "u");
        // A nested container keeps its inner spelling, and `QHash<int, QHash<T, QVariant>>` would
        // otherwise become a C# identifier with angle brackets in it. These stems are used as
        // identifiers and as native export names, so nothing outside [A-Za-z0-9_] may survive.
        var sb = new StringBuilder(stem.Length);
        foreach (char c in stem)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        // A nested spelling ends in '>' (QList<QPair<...>>), which became a trailing '_';
        // trimming it keeps the generated helper names readable and unchanged for flat types.
        return sb.ToString().TrimEnd('_');
    }
    static string MapStem(string key, string value) => ListStem(key) + "_" + ListStem(value);
    // A boxed class with no accessible copy constructor (Q_DISABLE_COPY: QDataStream). A virtual
    // takes such a value as a transient handle over Qt's object instead of a copy.
    static bool IsNonCopyable(string t)
        => ByName.TryGetValue(t, out var c)
           && HasClass(c, ApiModel.ClassFlags.NonCopyable);
    static string CsEnum(string t) => CsTypeName(t);

    // ------------------------------------------------------------------ C#
    // The C# binding is split the way Qt's C++ headers are: every class, value-type struct and
    // free function goes into the file named after the header the model records for it
    // (QtWidgets/qwidget.h -> generated/QtWidgets/qwidget.cs). Namespace-level enums have no
    // header in the model and live in <namespace>.Enums.cs; the generated list/assoc
    // helpers and the shared P/Invokes have their own files too.
    static string HeaderFileKey(string header)
    {
        string path = (header ?? string.Empty).Replace('\\', '/');
        if (path.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
            path = path.Substring(0, path.Length - 2);
        if (path.Length == 0)
            path = "misc";
        return "generated/" + path + ".cs";
    }

    static string PlainFileKey(string name) => "generated/" + name + ".cs";

    static StringBuilder CSharpFile(string key)
    {
        if (csharpFiles.TryGetValue(key, out var existing))
            return existing;
        var sb = new StringBuilder();
        sb.AppendLine($"// <auto-generated> generated from {ModelFileName} - do not edit");
        sb.AppendLine("using System;");
        sb.AppendLine("using QtSharp.Runtime;   // handles, signals and the marshalling runtime");
        sb.AppendLine("using System.CodeDom.Compiler;   // [GeneratedCode] on every wrapper type");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using System.Security;");
        // A wrapper's == is a liveness guard - a Qt object Qt has destroyed reads as null, the way a
        // destroyed UnityEngine.Object does - so it is deliberately not an equality relation.
        // Equals/GetHashCode keep their reference semantics (a wrapper must not change its hash when
        // its object dies), and the compiler's "you defined == without Equals" warnings are off for
        // every generated file. See IsAlive and operator == on each root class.
        sb.AppendLine("#pragma warning disable 660, 661, 0649   // generated fields a hierarchy may not use");
        sb.AppendLine();
        sb.AppendLine($"namespace {csharpNamespace}");
        sb.AppendLine("{");
        csharpFiles[key] = sb;
        return sb;
    }

    static SortedDictionary<string, string> GenerateCSharp(string ns)
    {
        csharpNamespace = ns;
        csharpFiles = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);

        // Namespace-level enums do not record a header in the model; one file holds them all. A
        // module whose enums are all class-scoped (Qt3DAnimation) has no such file at all, rather
        // than an empty namespace in the tree.
        if (Enums.Any(e => ScopeOf(e).Length == 0 || ScopeOf(e) == "Qt"))
        {
            var enums = CSharpFile(PlainFileKey(ns + ".Enums"));
            WriteEnums(enums, "", 1);
            WriteEnums(enums, "Qt", 1);
        }

        // An erased class (QChar) cannot nest its enums in a type that no longer exists, so they
        // are published at namespace level under the names CsTypeName resolves.
        if (Enums.Any(e => HoistedEnumNames.ContainsKey(e.GetProperty("qualified").GetString())))
        {
            var enums = CSharpFile(PlainFileKey(ns + ".ErasedEnums"));
            foreach (var e in Enums)
            {
                string qualified = e.GetProperty("qualified").GetString();
                if (HoistedEnumNames.TryGetValue(qualified, out var hoisted))
                    WriteEnum(enums, e, 1, name: hoisted);
            }
        }

        // Free functions are grouped by the header that declares them; the entry names were
        // computed over the whole list, so two headers cannot collide.
        foreach (var group in FreeFunctionEntries()
                     .GroupBy(f => f.Function.GetProperty("header").GetString(), StringComparer.Ordinal))
        {
            var functions = group.ToList();
            var file = CSharpFile(HeaderFileKey(group.Key));
            WriteFreeFunctions(file, functions);
            file.AppendLine("    internal static partial class ApiNative");
            file.AppendLine("    {");
            WriteFreeFunctionNativeDeclarations(file, functions);
            file.AppendLine("    }");
            file.AppendLine();
        }

        foreach (var cls in Classes)
        {
            var file = CSharpFile(HeaderFileKey(cls.GetProperty("header").GetString()));
            CSharpClasses.WriteClass(file, cls);
            file.AppendLine("    internal static partial class ApiNative");
            file.AppendLine("    {");
            Native.WriteNativeDeclarations(file, cls);
            file.AppendLine("    }");
            file.AppendLine();
        }

        // The managed thunks for every function type in a published signature. Qt keeps only the
        // address, so one bridge class per spelling turns the C ABI values back into Action/Func;
        // the runtime registry roots both the thunk and the caller's delegate.
        var callbackSpellings = Callbacks.CallbackSpellings();
        if (callbackSpellings.Count > 0)
            Callbacks.WriteCallbackThunks(CSharpFile(PlainFileKey(ns + ".Callbacks")), callbackSpellings);

        // The QObject type table: every wrapper this module publishes registers itself at assembly
        // load, so resolving an object's actual type never depends on which class the program
        // happened to touch first. It is a generated initializer rather than a reflection scan: that
        // is trimming-safe, and `Classes` order keeps the emitted registrations deterministic.
        var qobjectClasses = Classes.Where(c => DerivesFromQObject(Ref(c))).ToList();
        if (qobjectClasses.Count > 0)
        {
            var registrations = CSharpFile(PlainFileKey(ns + ".QObjectRegistrations"));
            registrations.AppendLine("    internal static class QObjectRegistrations");
            registrations.AppendLine("    {");
            registrations.AppendLine("        [ModuleInitializer]");
            registrations.AppendLine("        internal static void Register()");
            registrations.AppendLine("        {");
            foreach (var cls in qobjectClasses)
            {
                string name = Name(cls);
                // The key is Qt's own name, which for an addon class is qualified
                // (Qt3DCore::QEntity). The simple alias covers a meta-object that spells only
                // the unqualified name.
                string cpp = Ref(cls);
                string alias = cpp == name ? "null" : Quoted(name);
                registrations.AppendLine(
                    $"            QtSharp.QtCore.QObjectTypeRegistry.Register(typeof({name}), {Quoted(cpp)}, {alias}, {name}.__WrapBorrowed_{name}, {name}.__WrapOwned_{name}, {name}.__WrapTransient_{name});");
            }
            registrations.AppendLine("        }");
            registrations.AppendLine("    }");
            registrations.AppendLine();
        }

        // The QVariant bridge's type table. Two kinds of value type register here:
        //   * the ones Qt converts with `operator QVariant()` (QColor, QBrush, ...): the metatype
        //     id plus the factory that owns the copy QMetaType::create made;
        //   * the ones qvariant.h has a constructor and accessor for (QPoint, QUrl, QDate, ...):
        //     the generated constructor and accessor, so QVariant(object) and ToObject() stay in
        //     step with the header instead of a hand-kept list in BclInterop.Variant.cs.
        // The module initializer runs when this assembly loads, so a QVariant that Qt hands back
        // can be unwrapped without the caller having touched the wrapper type first.
        var variantClasses = Classes
            .Where(c => ConvertsToVariant(c) && HasClass(c, ApiModel.ClassFlags.Owned))
            .ToList();
        var variantCtorTypes = VariantCtorRegistrations();
        // Trivially copyable value types with `operator QVariant()` (QVector2D, QQuaternion, ...):
        // they have no handle for QMetaType, so the glue carries the conversion itself.
        var variantStructs = ApiTypes.RuntimeStructs
            .Where(s => ModelRuntimeStructNames.Contains(s.Name) && StructHasVariant(s.Name))
            .Select(s => s.Name)
            .ToList();
        if (variantClasses.Count > 0 || variantCtorTypes.Count > 0 || variantStructs.Count > 0)
        {
            var conversions = CSharpFile(PlainFileKey(ns + ".VariantConversions"));
            conversions.AppendLine("    internal static class VariantConversions");
            conversions.AppendLine("    {");
            conversions.AppendLine("        [ModuleInitializer]");
            conversions.AppendLine("        internal static void Register()");
            conversions.AppendLine("        {");
            foreach (var cls in variantClasses)
            {
                string name = Name(cls);
                conversions.AppendLine(
                    $"            QtSharp.QtCore.QtVariantConversion.Register(typeof({name}), {Quoted(CppClass(name))}, static handle => {name}.FromOwnedHandle(handle));");
            }
            foreach (var (managed, cpp, unwrapBody) in variantCtorTypes)
            {
                string unwrap = unwrapBody == null ? "null" : $"static h => {unwrapBody}";
                conversions.AppendLine(
                    $"            QtSharp.QtCore.QtVariantConversion.RegisterFactory(typeof({managed}), {Quoted(cpp)}, static v => new QtSharp.QtCore.QVariant(({managed})v).DetachHandle(), {unwrap});");
            }
            foreach (string name in variantStructs)
            {
                conversions.AppendLine(
                    $"            QtSharp.QtCore.QtVariantConversion.RegisterFactory(typeof({name}), {Quoted(CppClass(name))}, static v => ApiNative.{name}_toQVariant(({name})v), static h => ApiNative.{name}_fromQVariant(h));");
            }
            conversions.AppendLine("        }");
            conversions.AppendLine("    }");
            conversions.AppendLine();
        }

        // The generated half of the C# -> Qt type-name map (MetaType, in the runtime assembly).
        // The runtime is Qt-type-free, so every module registers the types it declares when its
        // assembly loads; QMetaType.FromType<T> and the declared-member mapper then read the same
        // table, including the exact C++ spelling of a namespaced class (Qt3DCore::QEntity).
        WriteTypeTable(ns);

        // The value types' members come from the same model, into the partial structs the
        // hand-written core extends (see WriteStructMembers); they carry their P/Invokes too.
        foreach (var s in ApiTypes.RuntimeStructs)
        {
            // Only the run that declares the struct emits it: an extension module gets the core's
            // value types from the core assembly, not as empty files of its own.
            if (!ModelRuntimeStructNames.Contains(s.Name))
                continue;
            var row = CoreClassOf(s.Name);
            if (row == null)
                continue;
            Structs.WriteStructMembers(CSharpFile(HeaderFileKey(row.Value.GetProperty("header").GetString())), s.Name);
        }

        // Shared P/Invokes: the library name and the boxed-value assign/clone helpers.
        var shared = CSharpFile(PlainFileKey(ns + ".Shared"));
        shared.AppendLine("    internal static partial class ApiNative");
        shared.AppendLine("    {");
        shared.AppendLine("        // Where every P/Invoke below goes. The core binding calls the core glue; an extension");
        shared.AppendLine("        // module calls its own DLL, which is how it stays a separate package.");
        shared.AppendLine($"        internal const string Library = \"{NativeLibraryName}\";");
        foreach (var boxed in VirtualBoxedReturns())
        {
            string stem = ListStem(boxed);
            shared.AppendLine();
            shared.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_assign_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
            shared.AppendLine($"        internal static extern void Assign_{stem}(IntPtr destination, IntPtr source);");
        }
        foreach (var boxed in VirtualBoxedParams())
        {
            string stem = ListStem(boxed);
            shared.AppendLine();
            shared.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_clone_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
            shared.AppendLine($"        internal static extern IntPtr Clone_{stem}(IntPtr source);");
        }
        shared.AppendLine("    }");

        // The generated helpers for the container types used by the signatures, including the
        // ones nested inside another container or a pair.
        var containerTypes = ContainerTypes();
        if (containerTypes.Count > 0)
        {
            var helpers = CSharpFile(PlainFileKey(ns + ".Helpers"));
            var apiNative = new CodeWriter(helpers, 1);
            apiNative.Open("internal static partial class ApiNative");
            var declarationWriter = new CodeWriter(helpers, 2);
            foreach (var type in containerTypes.Where(IsSequenceT))
                ContainerGlue.WriteSequenceNativeDeclarations(declarationWriter, type);
            foreach (var type in containerTypes.Where(IsAssocT))
                ContainerGlue.WriteAssocNativeDeclarations(declarationWriter, type);
            foreach (var type in containerTypes.Where(IsPairT))
                ContainerGlue.WritePairNativeDeclarations(declarationWriter, type);
            apiNative.Close();
            var helperWriter = new CodeWriter(helpers, 1);
            foreach (var type in containerTypes.Where(IsSequenceT))
                ContainerGlue.WriteSequenceHelper(helperWriter, type);
            foreach (var type in containerTypes.Where(IsAssocT))
                ContainerGlue.WriteAssocHelper(helperWriter, type);
            foreach (var type in containerTypes.Where(IsPairT))
                ContainerGlue.WritePairHelper(helperWriter, type);
        }

        // std::optional helpers live in the same file, in their own partial ApiNative block.
        var optionalTypes = OptionalTypes();
        if (optionalTypes.Count > 0)
        {
            var helpers = CSharpFile(PlainFileKey(ns + ".Helpers"));
            var apiNative = new CodeWriter(helpers, 1);
            apiNative.Open("internal static partial class ApiNative");
            var declarationWriter = new CodeWriter(helpers, 2);
            foreach (var type in optionalTypes)
                ContainerGlue.WriteOptionalNativeDeclarations(declarationWriter, type);
            apiNative.Close();
            var helperWriter = new CodeWriter(helpers, 1);
            foreach (var type in optionalTypes)
                ContainerGlue.WriteOptionalHelper(helperWriter, type);
        }

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in csharpFiles)
        {
            pair.Value.AppendLine("}");
            result[pair.Key] = pair.Value.ToString();
        }
        return result;
    }

    // Every model type that appears in an emitted signature, in a stable order. Signals count:
    // the connect/emit lambdas built for them use the same container helpers as a method, and a
    // signal may be the only place a container appears (QListView::indexesMoved, QList<QModelIndex>).
    static IEnumerable<string> SignatureTypes()
    {
        foreach (var cls in EmittedClassRows)
        {
            foreach (var fn in Methods(cls)
                         .Concat(cls.GetProperty("virtuals").EnumerateArray())
                         .Concat(cls.GetProperty("constructors").EnumerateArray()))
            {
                if (fn.TryGetProperty("returnType", out var r))
                    yield return r.GetString();
                foreach (var p in fn.GetProperty("parameters").EnumerateArray())
                    yield return PType(p);
            }
            if (cls.TryGetProperty("signals", out var signals))
                foreach (var signal in signals.EnumerateArray())
                    foreach (var p in signal.GetProperty("parameters").EnumerateArray())
                        yield return PType(p);
            if (cls.TryGetProperty("fields", out var fields))
                foreach (var field in fields.EnumerateArray())
                    yield return field.GetProperty("type").GetString();

            // The dispatch switch forwards every virtual in the chain, including the ones the class
            // only inherits: QSqlQueryModel dispatches QAbstractItemModel::itemData/mimeData even
            // though it never declares them. Those signatures use the same container helpers, and
            // the helpers are internal to the assembly that declares them, so this module has to
            // emit its own copy.
            foreach (var slot in ChainOf(Name(cls)))
            {
                if (slot.Virtual.TryGetProperty("returnType", out var virtualReturn))
                    yield return virtualReturn.GetString();
                foreach (var p in slot.Virtual.GetProperty("parameters").EnumerateArray())
                    yield return PType(p);
            }
        }
        foreach (var fn in Functions)
        {
            if (fn.TryGetProperty("returnType", out var r))
                yield return r.GetString();
            foreach (var p in fn.GetProperty("parameters").EnumerateArray())
                yield return PType(p);
        }
    }

    // Every container/pair spelling that appears anywhere in the model, in a stable order. The
    // helper is per spelling, and the nested ones need their own helper too: QList<QMap<...>>
    // is built from the list helper and the map helper.
    static List<string> ContainerTypes()
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in SignatureTypes())
            foreach (var container in ContainerTypesIn(t))
                types.Add(container);
        return types.ToList();
    }

    // Every std::optional<T> spelling that appears in a signature. Optionals are top-level
    // values only (never a container element or a pair half).
    static List<string> OptionalTypes()
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in SignatureTypes())
            if (t != null && IsOptionalT(t))
                types.Add(t);
        return types.ToList();
    }


    static void WriteEnums(StringBuilder sb, string scope, int indent, Members inherited = null)
    {
        // Enums live either in the Qt namespace or nested inside the class that declares
        // them, mirroring how they are written in C++.
        var scoped = Enums.Where(e => ScopeOf(e) == scope).ToList();
        if (scoped.Count == 0)
            return;
        var pad = new string(' ', indent * 4);
        if (scope != "Qt")
        {
            // Namespace level, or nested inside the class that declares them. A nested enum that
            // shares a name with an inherited member has to say `new` (CS0108).
            foreach (var e in scoped)
                WriteEnum(sb, e, indent, inherited != null && NewName(inherited, Name(e)) != "");
            return;
        }
        sb.AppendLine($"{pad}public static partial class Qt");
        sb.AppendLine($"{pad}{{");
        foreach (var e in scoped)
            WriteEnum(sb, e, indent + 1);
        sb.AppendLine($"{pad}}}");
        sb.AppendLine();
    }

    static string ScopeOf(JsonElement e)
    {
        var qualified = e.GetProperty("qualified").GetString();
        int i = qualified.LastIndexOf("::", StringComparison.Ordinal);
        return i < 0 ? string.Empty : qualified.Substring(0, i);
    }

    static void WriteEnum(StringBuilder sb, JsonElement e, int indent, bool hide = false, string name = null)
    {
        var pad = new string(' ', indent * 4);
        var items = e.GetProperty("items").EnumerateArray().ToList();
        var values = items.Select(i => i.GetProperty("value").GetInt64()).ToList();
        var underlying = values.Any(v => v < int.MinValue || v > int.MaxValue) ? "long" : "int";
        if (IsFlags(e.GetProperty("qualified").GetString()))
            sb.AppendLine($"{pad}[Flags]");
        sb.AppendLine($"{pad}public {(hide ? "new " : "")}enum {name ?? Name(e)} : {underlying}");
        sb.AppendLine($"{pad}{{");
        foreach (var item in items)
            sb.AppendLine($"{pad}    {SafeName(Name(item))} = {item.GetProperty("value").GetInt64()},");
        sb.AppendLine($"{pad}}}");
        sb.AppendLine();
    }

    // Qt's namespace-level functions (qCompress, qVersion, qRegisterMetaType, qFuzzyCompare, the
    // qDraw* helpers) live next to the classes in the module; PyQt publishes them the same way.
    // They become static methods on the same `Qt` class the namespace enums use, so the C# surface
    // is `Qt.QCompress(...)`.
    // The free function's P/Invoke entry name, computed once over the whole model so that two
    // headers (or two files) cannot give two different overloads the same entry.
    static List<(JsonElement Function, string Entry)> FreeFunctionEntries()
    {
        if (freeFunctionEntries != null)
            return freeFunctionEntries;
        var entries = new List<(JsonElement, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fn in Functions)
        {
            string entry = FreeFunctionEntry(fn);
            if (!seen.Add(entry))
                throw new InvalidOperationException(
                    $"two free functions share the entry {entry}; a signature is not unique: {Name(fn)}");
            entries.Add((fn, entry));
        }
        return freeFunctionEntries = entries;
    }

    // The export name of a free function. A positional suffix (qHash, qHash2, ...) is not stable
    // across models: the module models drop the overloads that belong to other modules, so the
    // qtcore model's qHash(Guid) was entry 13 while the combined model's was 18 - and the C# side
    // called the QUrl overload with a Guid, which crashed. The signature hash is model-independent:
    // a function gets the same entry in every model that carries it.
    static string FreeFunctionEntry(JsonElement fn)
    {
        var signature = new StringBuilder();
        signature.Append(fn.GetProperty("returnType").GetString()).Append(' ').Append(Name(fn)).Append('(');
        foreach (var p in fn.GetProperty("parameters").EnumerateArray())
        {
            signature.Append(p.GetProperty("type").GetString());
            if (p.TryGetProperty("cpp", out var cpp) && cpp.ValueKind == JsonValueKind.String)
                signature.Append('|').Append(cpp.GetString());
            if (p.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String)
                signature.Append('|').Append(mode.GetString());
            signature.Append(';');
        }
        signature.Append(')');
        if (fn.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array)
            signature.Append('|').Append(string.Join(",", flags.EnumerateArray().Select(f => f.GetString())));
        return "fn_" + Name(fn) + "_" + StableHash(signature.ToString());
    }

    // FNV-1a, 64-bit: deterministic across processes and platforms, and only used to keep two
    // exports apart, so 48 bits is plenty.
    static string StableHash(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return (hash & 0xFFFFFFFFFFFFUL).ToString("x12");
    }

    // The static class that carries a module's namespace-level surface. Only the core module owns
    // the name `Qt`: a widget application imports QtCore and QtGui together, and two types called
    // `Qt` make every `Qt.Alignment` reference ambiguous (CS0104). The other modules name their
    // free-function class after the module (`QtGui.QHash(...)`), which is also what the module
    // assembly itself is called.
    static string NamespaceClassName
        => ApiModel.CurrentModule == null || ApiModel.CurrentModule.Name == "qtcore"
            ? "Qt"
            : ApiModel.CurrentModule.QtModules[0];

    static void WriteFreeFunctions(StringBuilder sb, IReadOnlyList<(JsonElement Function, string Entry)> functions)
    {
        if (functions.Count == 0)
            return;
        sb.AppendLine("    /// <summary>Qt's namespace-level functions.</summary>");
        sb.AppendLine($"    public static partial class {NamespaceClassName}");
        sb.AppendLine("    {");
        foreach (var (fn, entry) in functions)
        {
            string fname = Name(fn);
            var ps = Params(fn);
            string sig = string.Join(", ", ps.Select(ParamSig));
            string ret = ReturnType(fn);
            sb.AppendLine($"        /// <summary>Calls <c>{fname}</c>.</summary>");
            sb.AppendLine($"        public static {ManagedRetType(ret)} {Pascal(fname)}({sig})");
            sb.AppendLine("        {");
            var marshal = MarshalParams(ps, null);
            foreach (string line in marshal.Prelude)
                sb.AppendLine("            " + line);
            string call = $"ApiNative.{entry}({string.Join(", ", marshal.Args)})";
            string body;
            if (marshal.Postlude.Count == 0)
                body = ret == "void" ? $"{call};" : $"return {RetToManaged(ret, call)};";
            else
            {
                string writeBack = string.Join("\n", marshal.Postlude);
                body = ret == "void"
                    ? $"{call};\n{writeBack}"
                    : $"var __result = {RetToManaged(ret, call)};\n{writeBack}\nreturn __result;";
            }
            EmitGuarded(sb, "            ", body, marshal.Cleanup);
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void WriteFreeFunctionNativeDeclarations(
        StringBuilder sb, IReadOnlyList<(JsonElement Function, string Entry)> functions)
    {
        foreach (var (fn, entry) in functions)
        {
            var ps = Params(fn);
            string sig = string.Join(", ", ps.SelectMany(NativeParamsFor));
            string ret = ReturnType(fn);
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{entry}\", CallingConvention = CallingConvention.Cdecl)]");
            if (ret == "bool")
                sb.AppendLine("        [return: MarshalAs(UnmanagedType.I1)]");
            sb.AppendLine($"        internal static extern {NativeRetType(ret)} {entry}({sig});");
        }
    }

    static void WriteFreeFunctionNative(StringBuilder sb)
    {
        foreach (var (fn, entry) in FreeFunctionEntries())
        {
            string fname = Name(fn);
            var ps = Params(fn);
            string sig = string.Join(", ", ps.SelectMany(CppParamsFor));
            string ret = ReturnType(fn);
            string args = string.Join(", ", ps.SelectMany(CppArgsFor));
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {NativeRetCpp(ret)} qs_api_{entry}({sig})");
            sb.AppendLine("{");
            foreach (string line in NativeBody(fn, $"{fname}({args})"))
                sb.AppendLine("    " + line);
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }



    // derived class dispatches into an inherited virtual whose name the base already chose.
    static Dictionary<string, HashSet<string>> reservedByClass;

    // Names a generated property must not take: the class itself, the members the code generator
    // owns, a nested type, a data member. A property that would collide is skipped and its
    // accessors stay the only way in (QLineEdit::echoMode, whose type is the nested EchoMode).
    static readonly Dictionary<string, HashSet<string>> hardReservedByClass = new(StringComparer.Ordinal);

    // The C# names a class's data members take: a Q_PROPERTY cannot claim one of them either.
    static readonly Dictionary<string, HashSet<string>> fieldNamesByClass = new(StringComparer.Ordinal);

    static void PrepareNames()
    {
        reservedByClass = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        hardReservedByClass.Clear();
        fieldNamesByClass.Clear();
        // Every class this run *knows*, not just the ones it emits: an extension module's classes
        // derive from core ones, and the inherited-member analysis asks about those too (a missing
        // QObject entry is a KeyNotFoundException the moment a QChart is written).
        foreach (var cls in ByName.Values)
        {
            var hard = new HashSet<string>(StringComparer.Ordinal)
            {
                Name(cls), "Handle", "FromHandle", "FromOwnedHandle", "Dispose", "Dispatch",
            };
            foreach (var e in Enums.Where(e => ScopeOf(e) == Name(cls)))
                hard.Add(Name(e));
            foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
                hard.Add("As" + SimpleName(b));
            hardReservedByClass[Name(cls)] = hard;

            // A data member keeps its own name (it is published as the property), so its names
            // are only reserved against everything else.
            var fields = new HashSet<string>(
                FieldList(cls).Select(f => Pascal(Name(f))), StringComparer.Ordinal);

            // A property reserves its C# name even when a virtual has the same name: the property
            // takes the Pascal spelling and the accessor keeps the C++ one (QXYSeries::color is a
            // virtual, the Q_PROPERTY is Color, and the getter stays color()). The virtual's name
            // is the base-most declarer's (VirtualCsName), so the override chain stays consistent.
            var reserved = new HashSet<string>(hard, StringComparer.Ordinal);
            reserved.UnionWith(fields);
            foreach (var p in PropertyList(cls))
            {
                string pascal = Pascal(Name(p));
                if (!hard.Contains(pascal) && !fields.Contains(pascal))
                    reserved.Add(pascal);
            }
            reservedByClass[Name(cls)] = reserved;
            fieldNamesByClass[Name(cls)] = fields;
        }
    }

    static string MemberName(string className, string member)
        // The fallback keeps the C++ name, and some of those *are* C# keywords: QLogValueAxis::base()
        // is a real accessor (its Q_PROPERTY took the `Base` name), and `public double base()` does
        // not compile. Escaping it is what SafeName is for.
        => reservedByClass.TryGetValue(className, out var reserved) && reserved.Contains(Pascal(member))
            ? SafeName(member)
            : Pascal(member);

    // The C# members a class declares, so that a derived class can tell which of its own members
    // would otherwise hide an inherited one (CS0108) and mark it `new`. Three sets, because C#
    // hides by *signature* for methods (an overload of a different signature does not hide) but
    // by *name* for everything else - and a method named like a base property hides it too, so a
    // method has to be checked against the base's non-methods as well.
    class Members
    {
        public readonly HashSet<string> AllNames = new(StringComparer.Ordinal);
        public readonly HashSet<string> NonMethodNames = new(StringComparer.Ordinal);
        public readonly HashSet<string> MethodKeys = new(StringComparer.Ordinal);
    }

    static readonly Dictionary<string, Members> declaredMembers = new(StringComparer.Ordinal);
    static readonly Dictionary<string, Members> inheritedMembers = new(StringComparer.Ordinal);

    // The parameter spelling that makes up a method's signature (without the name or the default).
    static string ParamTypeKey(JsonElement p) => ParamFor(p).TypeKey(p);

    static string MethodKey(string name, IEnumerable<JsonElement> ps)
        => name + "(" + string.Join(",", ps.Select(ParamTypeKey)) + ")";

    static void MergeInto(Members target, Members source)
    {
        target.AllNames.UnionWith(source.AllNames);
        target.NonMethodNames.UnionWith(source.NonMethodNames);
        target.MethodKeys.UnionWith(source.MethodKeys);
    }

    // Whether this run emits the class, i.e. whether it lands in the same assembly as the classes
    // being written. An extension module emits only its own classes; everything else it knows comes
    // from the core assembly.
    static bool Emits(string className)
        => Classes.Any(c => Name(c) == className);

    /// <summary>
    /// The members a generated class declares that are <c>internal</c>, so they only exist within
    /// their own assembly. <c>Dispatch</c> is the one: every class declares its own vtable
    /// dispatcher as <c>internal static</c>, which means a class in *another* assembly cannot hide
    /// it - writing <c>new</c> across that boundary is exactly what CS0109 complains about.
    /// </summary>
    static readonly string[] AssemblyLocalNames = { "Dispatch" };

    static Members WithoutAssemblyLocal(Members source)
    {
        var copy = new Members();
        foreach (var name in source.AllNames)
            if (Array.IndexOf(AssemblyLocalNames, name) < 0)
                copy.AllNames.Add(name);
        foreach (var name in source.NonMethodNames)
            if (Array.IndexOf(AssemblyLocalNames, name) < 0)
                copy.NonMethodNames.Add(name);
        foreach (var key in source.MethodKeys)
        {
            int paren = key.IndexOf('(');
            var name = paren < 0 ? key : key.Substring(0, paren);
            if (Array.IndexOf(AssemblyLocalNames, name) < 0)
                copy.MethodKeys.Add(key);
        }
        return copy;
    }

    // The members visible to a class that inherits from `className`: everything `className`
    // declares, plus everything it inherits. This is what WriteClass unions over a class's base.
    static Members InheritedMembers(string className)
    {
        // A base is recorded by its qualified C++ name (Qt3DCore::QNode); the caches and the
        // member tables use the class's own simple name.
        if (TryLookupInternal(className, out var resolved))
            className = Name(resolved);
        if (inheritedMembers.TryGetValue(className, out var cached))
            return cached;
        var members = new Members();
        inheritedMembers[className] = members;      // a cycle must not recurse forever
        if (ByName.TryGetValue(className, out var cls))
        {
            MergeInto(members, Emits(className) ? DeclaredMembers(className) : WithoutAssemblyLocal(DeclaredMembers(className)));
            string baseName = cls.GetProperty("bases").EnumerateArray()
                .Select(b => b.GetString()).FirstOrDefault();
            if (baseName != null)
                MergeInto(members, InheritedMembers(baseName));
        }
        return members;
    }

    static Members DeclaredMembers(string className)
    {
        if (TryLookupInternal(className, out var resolved))
            className = Name(resolved);
        if (declaredMembers.TryGetValue(className, out var cached))
            return cached;
        var members = new Members();
        declaredMembers[className] = members;
        if (!ByName.TryGetValue(className, out var cls))
            return members;

        var bases = cls.GetProperty("bases").EnumerateArray().Select(b => b.GetString()).ToList();
        string baseName = bases.Count > 0 ? bases[0] : null;
        bool boxed = HasClass(cls, ApiModel.ClassFlags.Owned);
        var ctors = PublishedCtors(cls);
        bool owned = boxed && (ctors.Count > 0 || SubclassCtor(cls));

        void Property(string n)
        {
            members.AllNames.Add(n);
            members.NonMethodNames.Add(n);
        }
        void Method(string key)
        {
            int paren = key.IndexOf('(');
            members.AllNames.Add(paren < 0 ? key : key.Substring(0, paren));
            members.MethodKeys.Add(key);
        }

        // A nested enum is a member of the class in C#'s eyes: a method or property with the same
        // name must say `new`, exactly as it would for an inherited method (QPrinter.PdfVersion()
        // versus QPagedPaintDevice.PdfVersion). EnumNames is the whole known vocabulary, so a
        // dependency's enum counts too - the class being written may not declare it itself.
        foreach (string enumName in EnumNames)
        {
            if (!enumName.StartsWith(className + "::", StringComparison.Ordinal))
                continue;
            string nested = enumName.Substring(className.Length + 2);
            if (nested.Contains("::"))
                continue;
            members.AllNames.Add(nested);
            members.NonMethodNames.Add(nested);
        }

        // The handle and liveness members are declared by the root only; every class declares its
        // own factories (FromHandle/FromTransientHandle, and the release pair when it is owned).
        if (baseName == null)
        {
            Property("Handle");
            Property("IsAlive");
        }
        Method("FromHandle(IntPtr)");
        Method("FromTransientHandle(IntPtr)");
        if (owned)
        {
            Method("FromOwnedHandle(IntPtr)");
            Method("Dispose()");
        }
        // The vtable dispatcher (already emitted with `new` when the class has a base).
        if (ChainOf(className).Count > 0)
            Method("Dispatch(IntPtr,int,IntPtr)");
        foreach (var b in bases.Skip(1))
            Method("As" + SimpleName(b) + "()");
        foreach (var e in Enums.Where(e => ScopeOf(e) == className))
            Property(Name(e));

        // A Q_PROPERTY that collides with a nested type, a data member or a virtual is skipped.
        var virtualNames = new HashSet<string>(
            ChainOf(className).Select(s => Pascal(Name(s.Virtual))), StringComparer.Ordinal);
        foreach (var property in PropertyList(cls))
        {
            string propName = Pascal(Name(property));
            if (hardReservedByClass[className].Contains(propName)
                || fieldNamesByClass[className].Contains(propName)
                || virtualNames.Contains(propName))
                continue;
            Property(propName);
        }

        // Data members: a property, or Get/Set accessors when the name is reserved.
        foreach (var field in FieldList(cls))
        {
            string prop = SafeName(Pascal(Name(field)));
            if (hardReservedByClass[className].Contains(prop))
            {
                Method("Get" + prop + "()");
                Method("Set" + prop + "(" + ManagedType(field.GetProperty("type").GetString()) + ")");
            }
            else
            {
                Property(prop);
            }
        }

        foreach (var m in Methods(cls))
            Method(MethodKey(MemberName(className, Name(m)), Params(m)));
        foreach (var slot in ChainOf(className).Where(s => s.DeclaredHere && !s.Blocked))
            Method(MethodKey(MemberName(className, Name(slot.Virtual)), Params(slot.Virtual)));

        var signalOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in cls.GetProperty("signals").EnumerateArray())
        {
            string sname = Name(s);
            if (Params(s).Count > MaxSignalArity)
                continue;
            signalOverloads.TryGetValue(sname, out int seen);
            signalOverloads[sname] = seen + 1;
            string suffix = seen == 0 ? "" : (seen + 1).ToString();
            Property(Pascal(sname) + suffix + "Signal");
            string eventName = Pascal(sname) + suffix;
            if (reservedByClass.TryGetValue(className, out var reserved) && !reserved.Contains(eventName))
                Property(eventName);
        }

        return members;
    }

    // The `new` a member needs: a method hides an inherited method of the same signature, or an
    // inherited property/field/type/nested type of the same name.
    static string NewMethod(Members inherited, string name, string paramKeys)
        => inherited.NonMethodNames.Contains(name)
           || inherited.MethodKeys.Contains(name + "(" + paramKeys + ")")
            ? "new "
            : "";
    static string NewMethod(Members inherited, string name, IEnumerable<JsonElement> ps)
        => NewMethod(inherited, name, string.Join(",", ps.Select(ParamTypeKey)));
    static string NewName(Members inherited, string name)
        => inherited.AllNames.Contains(name) ? "new " : "";

    // Whether any class in this hierarchy (the root or one of its descendants, following the
    // primary base) can be constructed - the only place the release flags are ever assigned.
    static readonly Dictionary<string, bool> subtreeCtorCache = new(StringComparer.Ordinal);
    static bool SubtreeHasCtor(string className)
    {
        if (subtreeCtorCache.TryGetValue(className, out var cached))
            return cached;
        subtreeCtorCache[className] = false;        // a cycle must not recurse forever
        bool result = false;
        if (TryLookupInternal(className, out var cls))
            result = cls.GetProperty("constructors").GetArrayLength() > 0 || SubclassCtor(cls);
        if (!result)
            result = Children(className).Any(c => SubtreeHasCtor(Name(c)));
        subtreeCtorCache[className] = result;
        return result;
    }

    // The classes that inherit from className through their primary base.
    static IEnumerable<JsonElement> Children(string className)
        => Classes.Where(c => c.GetProperty("bases").EnumerateArray()
            .Select(b => b.GetString()).Select(SimpleName).FirstOrDefault() == className);

    // Whether any class here declares a virtual whose stub reads `__managedSubclass`.
    static readonly Dictionary<string, bool> subtreeVirtualCache = new(StringComparer.Ordinal);
    static bool SubtreeDeclaresVirtual(string className)
    {
        if (subtreeVirtualCache.TryGetValue(className, out var cached))
            return cached;
        subtreeVirtualCache[className] = false;     // a cycle must not recurse forever
        bool result = ChainOf(className).Any(s => s.DeclaredHere && !s.Blocked)
                      || Children(className).Any(c => SubtreeDeclaresVirtual(Name(c)));
        subtreeVirtualCache[className] = result;
        return result;
    }

    // One vtable slot a generated native subclass has to install: the method to forward, and
    // the class whose declaration of it we chain to.
    class VirtualSlot
    {
        public JsonElement Virtual;
        public string Declaring;
        public bool DeclaredHere;
        // A slot whose nearest C++ implementation is private. Qt hides the three QAbstractListModel
        // hooks (columnCount, hasChildren, parent) that way, so a subclass can neither call them nor
        // should it replace them: the slot stays in the chain - it has to shadow the public pure
        // QAbstractItemModel declaration further up - but nothing is emitted for it. Overriding one
        // with `return default` made QAbstractListModel::columnCount answer 0 instead of 1, and
        // since QAbstractItemModel::index() goes through hasIndex() -> columnCount(), every
        // QModelIndex of a QStringListModel came back invalid (the completer example found this).
        public bool Blocked;
    }

    static readonly Dictionary<string, List<VirtualSlot>> virtualChains = new(StringComparer.Ordinal);

    // Every virtual in the class's inheritance chain, base-first, one slot per signature. A
    // class that redeclares an inherited virtual keeps the slot where the base put it, which
    // is what makes the generated native override table and the managed dispatch switch agree.
    static List<VirtualSlot> ChainOf(string className)
    {
        if (virtualChains.TryGetValue(className, out var cached))
            return cached;

        var slots = new List<VirtualSlot>();
        if (TryLookupInternal(className, out var cls))
        {
            var bases = cls.GetProperty("bases").EnumerateArray().Select(b => b.GetString()).ToList();
            if (bases.Count > 0)
                foreach (var inherited in ChainOf(bases[0]))
                    slots.Add(new VirtualSlot
                    {
                        Virtual = inherited.Virtual,
                        Declaring = inherited.Declaring,
                        DeclaredHere = false,
                        Blocked = inherited.Blocked,
                    });

            // A class C++ will not let us derive from (private destructor) has no native subclass
            // to install its hooks, so its slots cannot be part of the chain at all - the managed
            // dispatch of a *derived* class would otherwise point at a member that class never
            // declared.
            if (Subclassable(cls))
                foreach (var v in cls.GetProperty("virtuals").EnumerateArray())
                {
                    var key = VirtualKey(v);
                    var index = slots.FindIndex(s => VirtualKey(s.Virtual) == key);
                    var slot = new VirtualSlot
                    {
                        Virtual = v, Declaring = className, DeclaredHere = true,
                        // A private or final declaration gets no hook: C++ forbids overriding a
                        // final one, and a private one has no base implementation to chain to.
                        Blocked = Has(v, ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final),
                    };
                    if (index >= 0)
                        slots[index] = slot;
                    else
                        slots.Add(slot);
                }
        }

        // A bridge may keep its published virtuals on the class itself (the QIODevice bridge
        // does); the registry says so, so this stays free of per-class rules. The class that
        // declares the slot keeps it either way.
        slots.RemoveAll(s => s.Declaring != className && !VirtualBridges.PropagatesToSubclasses(s.Declaring));

        virtualChains[className] = slots;
        return slots;
    }

    static string VirtualKey(JsonElement v)
        => Name(v) + "(" + string.Join(",", Params(v).Select(PType)) + ")" + ConstSuffix(v);

    // The C++ type a virtual's native override has to return, verbatim from clang. An override
    // repeats the declaration exactly - `const QMetaObject *`, `qreal`, `QValidator::State` - and
    // the model's returnType is the normalized managed type instead.
    static string VirtualReturnCpp(JsonElement v)
        => v.TryGetProperty("returnCpp", out var cpp) && cpp.ValueKind == JsonValueKind.String
            ? cpp.GetString()
            : ReturnType(v);

    // The C# name of a virtual: the one its base-most declaring class published. A class that
    // redeclares an inherited virtual cannot choose its own spelling - C# overrides keep the
    // base member's name - and the base-most declarer is where that name was decided (QStyle's
    // PixelMetric enum forces pixelMetric() on QCommonStyle and QProxyStyle).
    static string VirtualCsName(string className, JsonElement v)
    {
        string key = VirtualKey(v);
        string declarer = null;
        string current = className;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (current != null && seen.Add(current) && ByName.TryGetValue(current, out var cls))
        {
            if (cls.GetProperty("virtuals").EnumerateArray().Any(x => VirtualKey(x) == key))
                declarer = current;
            var bases = cls.GetProperty("bases").EnumerateArray().Select(b => b.GetString()).ToList();
            current = bases.Count > 0 ? bases[0] : null;
        }
        return MemberName(declarer ?? className, Name(v));
    }

    // ------------------------------------------------------------------ the runtime structs
    //
    // Qt's geometry value types are structs here rather than classes, but their *members* are still
    // Qt's - and the model already has them, because the extractor parsed qpoint.h, qsize.h and the
    // rest. So they are generated the way a class's are, with one difference: the receiver is the
    // value itself instead of a handle, and the glue rebuilds Qt's own type from the POD for the call
    // (QSize qs_self(qs_pod.width, qs_pod.height); qs_self.transposed()). That is what PyQt does when
    // SIP emits the forwarders its .sip file declares, and what shiboken does from the headers.
    //
    // What stays hand-written (runtime/QtStructs.cs) is the storage and the members worth spelling
    // out: the fields, the accessors that want to be a field read rather than a P/Invoke, and the
    // operators, equality and ToString that C# needs and Python gets from its type system. The
    // generator reads that file, so a member written by hand wins over the generated one and the two
    // never collide.
    // A model class that is the runtime struct: the core (un-namespaced) class, never an extension
    // class that merely shares its simple name. The exceptions are the Qt3D value types, whose
    // model is namespaced and whose row says so (NamespacedStructs): only that exact C++ class may
    // claim the struct, so Qt3DCore::QTransform stays a class even though QtGui's QTransform is a
    // struct.
    static bool IsRuntimeStructClass(JsonElement c)
        => ApiTypes.IsStruct(Name(c))
           && (!c.TryGetProperty("cppName", out var cpp)
               || ApiTypes.NamespacedStructs.TryGetValue(Name(c), out var expect)
                  && expect == cpp.GetString());

    static JsonElement? CoreClassOf(string name)
    {
        bool namespaced = ApiTypes.NamespacedStructs.TryGetValue(name, out var cppName);
        foreach (var c in KnownClasses)
            if (Name(c) == name
                && (namespaced
                    ? c.TryGetProperty("cppName", out var cpp) && cpp.GetString() == cppName
                    : !c.TryGetProperty("cppName", out _)))
                return c;
        return null;
    }

    // The base classes of a model class, transitively, in the order Qt declares them.
    static IEnumerable<JsonElement> BaseChainOf(JsonElement cls)
    {
        var result = new List<JsonElement>();
        var pending = new Queue<string>();
        if (cls.TryGetProperty("bases", out var bases) && bases.ValueKind == JsonValueKind.Array)
            foreach (var b in bases.EnumerateArray())
                pending.Enqueue(b.GetString());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            string b = pending.Dequeue();
            if (b == null || !seen.Add(b) || !ByName.TryGetValue(b, out var baseCls))
                continue;
            result.Add(baseCls);
            if (baseCls.TryGetProperty("bases", out var more) && more.ValueKind == JsonValueKind.Array)
                foreach (var x in more.EnumerateArray())
                    pending.Enqueue(x.GetString());
        }
        return result;
    }

    static bool IsIdentifier(string n)
    {
        if (string.IsNullOrEmpty(n) || !(char.IsLetter(n[0]) || n[0] == '_'))
            return false;
        foreach (char c in n)
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        return true;
    }

    /// <summary>
    /// The member names runtime/QtStructs.cs declares for one struct. Its own braces are counted to
    /// find the struct's end; the file holds no braces inside strings or interpolations (ToString
    /// concatenates), which is what makes that enough.
    /// </summary>
    static HashSet<string> HandWrittenMembers(string name)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        string root = Path.GetDirectoryName(Path.GetDirectoryName(ModelPath));
        string path = Path.Combine(root, "runtime", "QtStructs.cs");
        if (!File.Exists(path))
            return names;
        string text = File.ReadAllText(path);
        int at = text.IndexOf("struct " + name + "\n", StringComparison.Ordinal);
        if (at < 0)
            at = text.IndexOf("struct " + name + " ", StringComparison.Ordinal);
        if (at < 0)
            return names;
        int open = text.IndexOf('{', at);
        if (open < 0)
            return names;
        int depth = 0, end = open;
        for (; end < text.Length; end++)
        {
            if (text[end] == '{') depth++;
            else if (text[end] == '}' && --depth == 0) break;
        }
        string body = text.Substring(open, Math.Min(end, text.Length) - open);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(body, @"public\s+[\w<>\[\],\.\? ]+?\s+(\w+)\s*\("))
            names.Add(m.Groups[1].Value);
        return names;
    }

    /// <summary>The struct's members, as a partial the hand-written core extends.</summary>
    // A struct that declares `operator QVariant()` (QVector2D, QQuaternion, ...). It has no handle
    // to hand QMetaType, so the glue carries both directions: Qt builds the variant from the POD,
    // and QVariant::value<T>() gives the value back.
    static bool StructHasVariant(string name)
        => CoreClassOf(name) is JsonElement cls && ConvertsToVariant(cls);


    static string CtorSuffix(int index) => index == 0 ? "" : (index + 1).ToString();

    // Stores the value a managed override produced into the slot the native caller provided.
    // Scalars go through the runtime's typed writers; a boxed value needs a per-type assignment
    // (the caller's slot holds the value itself, not a pointer to it). `slot` is the argv index:
    // 0 is the return value, and a by-reference parameter's own slot is the variable Qt pointed at.
    // The boxed types a virtual may take *by value*: the override receives a copy, because the
    // address Qt passes points at a temporary that the next call reuses (and, worse, is an object
    // Qt owns - a wrapper over it would delete it at finalization). A class the binding cannot
    // copy has no clone to call, so it is not in this set; DispatchRead hands such a value over
    // as a transient handle instead.
    // Both walk the *chain*, not just the class's own virtuals: the native wrapper overrides every
    // slot the chain declares, so a class whose ancestor returns a QSizeF needs the helper for it
    // even though it declares no such virtual itself. (An extension module is all ancestors - its
    // classes declare almost nothing and inherit the rest from the core binding.)
    static List<string> VirtualBoxedParams()
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var cls in Classes)
            foreach (var slot in ChainOf(Name(cls)))
                foreach (var p in slot.Virtual.GetProperty("parameters").EnumerateArray())
                {
                    var t = PType(p);
                    if (IsBoxed(t) && !IsNonCopyable(t))
                        types.Add(t);
                }
        return types.ToList();
    }

    // The boxed types a virtual may return: each needs the assignment helper above.
    static List<string> VirtualBoxedReturns()
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var cls in Classes)
            foreach (var slot in ChainOf(Name(cls)))
            {
                var ret = ReturnType(slot.Virtual);
                if (IsBoxed(ret))
                    types.Add(ret);
            }
        return types.ToList();
    }

    // Methods that hand an object we do not own to an owner that does: a scene creates an item
    // (addRect/addEllipse/... return it) or adopts one (addItem takes it). A non-QObject item has
    // no destroyed signal of its own, so its owner's destruction is the only lifetime signal - the
    // managed side remembers the association and invalidates the item with its owner.
    static readonly HashSet<string> OwnershipTransfers = new(StringComparer.Ordinal)
    {
        "addItem", "addToGroup", "addRect", "addEllipse", "addLine", "addPath", "addPixmap",
        "addPolygon", "addSimpleText", "addText",
    };

    // Whether the object this call hands over is already tracked by the runtime, and how the
    // generated body names it: as the return value, or as the argument that was adopted.
    static (bool IsItem, bool IsArgument, string Argument) OwnershipTransferOf(
        string methodName, List<JsonElement> ps, string ret)
    {
        if (!OwnershipTransfers.Contains(methodName))
            return (false, false, null);
        // An item Qt keeps alive on our behalf: a bound class the model does not own.
        if (IsClassPtr(ret) && !IsOwned(ret.Substring(0, ret.Length - 1)))
            return (true, false, null);
        if (ret != "void")
            return (false, false, null);
        foreach (var p in ps)
        {
            string t = PType(p);
            if (IsClassPtr(t) && !IsOwned(t.Substring(0, t.Length - 1)))
                return (false, true, ArgToNative(t, SafeName(PName(p))));
        }
        return (false, false, null);
    }

    // A parameter that is not a plain scalar/pointer/handle needs a temporary native object for
    // the duration of the call: the managed array becomes a QStringList here, and the cleanup
    // runs in a finally so an exception cannot leak it.
    // The postlude runs after the call and before the return: an in-out string has to be written
    // back into the caller's variable there.
    // Qt's buffer-plus-cleanup constructors (QImage's uchar* family) hand Qt a buffer it keeps
    // reading until it calls the cleanup function. That callback is what says so: a uchar* array
    // without one is borrowed for the length of the call, while with one the callee owns the
    // pointer until its last copy dies. Such a buffer is pinned for that window and the cleanup
    // releases the pin (QtSharp.Runtime.PinnedBuffer); nothing is copied.
    static bool TryPinnedBuffer(IReadOnlyList<JsonElement> ps, out int dataIndex, out int cleanupIndex,
        out string cleanupName)
    {
        dataIndex = -1;
        cleanupIndex = -1;
        cleanupName = null;
        for (int i = 0; i < ps.Count; i++)
        {
            string t = PType(ps[i]);
            if (dataIndex < 0)
            {
                if (IsByteBufferT(t))
                    dataIndex = i;
            }
            else if (cleanupIndex < 0 && IsCleanupCallback(t))
            {
                cleanupIndex = i;
            }
        }
        if (dataIndex < 0 || cleanupIndex < 0)
            return false;
        cleanupName = SafeName(PName(ps[cleanupIndex]));
        return true;
    }

    static bool IsCleanupCallback(string t)
        => ApiTypes.TryParseCallback(t, out string ret, out string[] arguments)
           && ret == "void" && arguments.Length == 1 && arguments[0] == "void*";

    static (List<string> Prelude, List<string> Args, List<string> Postlude, List<string> Cleanup) MarshalParams(
        IEnumerable<JsonElement> ps, string self, (int Data, int Cleanup, string CleanupName)? pinned = null)
    {
        var prelude = new List<string>();
        var args = new List<string>();
        var postlude = new List<string>();
        var cleanup = new List<string>();
        if (self != null)
            args.Add(self);
        var parameters = ps.ToList();
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            string t = PType(p), n = SafeName(PName(p));
            if (pinned is { } pin && i == pin.Data)
            {
                // The callee keeps the pointer, so the array is pinned now and released by the
                // cleanup Qt calls - or by the catch in the constructor when the native call fails.
                prelude.Add($"var __buffer = PinnedBuffer.Pin({n}, {pin.CleanupName});");
                args.Add("__buffer.Pointer");
                continue;
            }
            if (pinned is { } pinnedCleanup && i == pinnedCleanup.Cleanup)
            {
                // The address of the composed cleanup (the caller's callback, then the pin release).
                args.Add($"{CallbackThunkName(t)}.Get(__buffer.Cleanup)");
                continue;
            }
            var (paramPrelude, paramArgs, paramPostlude, paramCleanup) = ParamFor(p).Marshal(p, n);
            prelude.AddRange(paramPrelude);
            args.AddRange(paramArgs);
            postlude.AddRange(paramPostlude);
            cleanup.AddRange(paramCleanup);
        }
        return (prelude, args, postlude, cleanup);
    }

    static void EmitGuarded(StringBuilder sb, string indent, string body, List<string> cleanup,
        string catchCleanup = null)
    {
        if (cleanup.Count == 0 && catchCleanup == null)
        {
            foreach (var line in body.Split('\n'))
                sb.AppendLine(indent + line);
            return;
        }
        sb.AppendLine(indent + "try");
        sb.AppendLine(indent + "{");
        foreach (var line in body.Split('\n'))
            sb.AppendLine(indent + "    " + line);
        sb.AppendLine(indent + "}");
        // A pinned buffer must only be released when the native call failed: on success Qt owns
        // the pointer and calls the cleanup itself.
        if (catchCleanup != null)
        {
            sb.AppendLine(indent + "catch");
            sb.AppendLine(indent + "{");
            sb.AppendLine(indent + "    " + catchCleanup);
            sb.AppendLine(indent + "    throw;");
            sb.AppendLine(indent + "}");
        }
        if (cleanup.Count > 0)
        {
            sb.AppendLine(indent + "finally");
            sb.AppendLine(indent + "{");
            foreach (var line in cleanup)
                sb.AppendLine(indent + "    " + line);
            sb.AppendLine(indent + "}");
        }
    }


    // The type the meta-object signature has to spell. Qt stores the *normalized* type
    // (QString, QWidget*, QList<int>, Qt::Orientation) and indexOfSignal matches it literally -
    // "const QString &" would not be found. The model type is already that spelling, which is
    // what makes a signal's parameters readable from the model.
    // The type as Qt's meta-object spells it. Qt normalizes an enum to its full C++ name
    // (Qt3DCore::QBuffer::AccessType) even though moc's own signature prints just AccessType;
    // indexOfSignal accepts the qualified form and rejects the class-scoped QBuffer::AccessType.
    static string SignalMocType(JsonElement p) => CppTypeName(PType(p));

    // Reading one signal argument out of the argv array moc builds. Unlike a virtual's argv (which
    // carries the argument itself), a signal's argv[i] is the *address* of the argument storage:
    // moc hands the signal function's own parameters over by address. So a pointer parameter has
    // to be dereferenced, while a boxed value is already the address of the value.
    // The native argument storage a signal's emit needs, in the shape the moc-generated signal
    // function would have built on its own stack: scalars/enums/durations by value, a pointer as
    // a pointer variable (whose address goes into argv), a boxed value as the object itself, and
    // a string/container as a native object built for the call.
    static (List<string> Prelude, List<string> Pointers, List<string> Cleanup) EmitParams(List<JsonElement> ps)
    {
        var prelude = new List<string>();
        var pointers = new List<string>();
        var cleanup = new List<string>();
        for (int i = 0; i < ps.Count; i++)
        {
            string t = PType(ps[i]), name = "a" + (i + 1), local = "__e" + i;
            var (paramPrelude, paramPointers, paramCleanup) = EmitParam(t, name, local);
            prelude.AddRange(paramPrelude);
            pointers.AddRange(paramPointers);
            cleanup.AddRange(paramCleanup);
        }
        return (prelude, pointers, cleanup);
    }

    // The emit half of a signal property: an arity `Action<...>` that turns the managed values
    // into native argument storage and activates the signal.
    static List<string> EmitLambda(List<JsonElement> ps, string mocSig)
    {
        var (prelude, pointers, cleanup) = EmitParams(ps);
        var lines = new List<string>();
        lines.Add("(" + string.Join(", ", Enumerable.Range(1, ps.Count).Select(i => "a" + i)) + ") =>");
        lines.Add("{");
        // The prelude can declare pointer locals (a pointer parameter becomes a void* variable), so
        // the whole body is unsafe; the class itself no longer carries the modifier.
        lines.Add("    unsafe");
        lines.Add("    {");
        foreach (string line in prelude)
            lines.Add("        " + line);
        if (cleanup.Count > 0)
            lines.Add("        try");
        if (cleanup.Count > 0)
            lines.Add("        {");
        string ind = cleanup.Count > 0 ? "            " : "        ";
        if (ps.Count > 0)
        {
            lines.Add($"{ind}void** __argv = stackalloc void*[{ps.Count}];");
            for (int i = 0; i < ps.Count; i++)
                lines.Add($"{ind}__argv[{i}] = {pointers[i]};");
        }
        string args = ps.Count == 0 ? "IntPtr.Zero, 0" : $"(IntPtr)__argv, {ps.Count}";
        lines.Add($"{ind}ApiRuntime.Emit(Handle, \"{mocSig}\", {args});");
        if (cleanup.Count > 0)
        {
            lines.Add("        }");
            lines.Add("        finally");
            lines.Add("        {");
            foreach (string line in cleanup)
                lines.Add("            " + line);
            lines.Add("        }");
        }
        lines.Add("    }");
        lines.Add("}");
        return lines;
    }

    // ------------------------------------------------------------------ type maps
    // The per-kind mappings live in Types/*.cs. The registry selects a handler by its [TypeHandler]
    // regex, so adding a type is one small class instead of another branch in this file's parallel
    // mapping functions.

    // The managed type of a *return* value: a pointer to a runtime struct comes back as the address
    // it is (a view into a container - QRegion's iterator), which is the one place a struct pointer
    // does not read as an array. A concrete function type comes back as the matching delegate: Qt
    // declared the signature, so the wrapper can trust it (and Wrap first looks the address up in
    // the registry, so a callback the binding installed comes back as the same instance).
    // QFunctionPointer has no such declaration, so it comes back as the carrier struct over the
    // address and the caller supplies the signature with As<TDelegate>.
    static string ManagedRetType(string t)
        => IsStructPtr(t) ? "IntPtr" : ManagedType(t);

    // The delegate the managed caller hands over for a function type. Action/Func are the BCL
    // shapes PySide's PyObject* and PyQt's Callable both stand for, and they keep the callback a
    // first-class C# value (nothing custom to learn).
    static string CallbackDelegateType(string t)
    {
        if (!ApiTypes.TryParseCallback(t, out string ret, out string[] arguments))
            return "IntPtr";
        var parts = new List<string>(arguments.Select(CallbackManagedType));
        if (ret == "void")
            return parts.Count == 0 ? "Action" : $"Action<{string.Join(", ", parts)}>";
        parts.Add(CallbackManagedType(ret));
        return $"Func<{string.Join(", ", parts)}>";
    }

    // What one callback argument looks like on the managed side. A pointer is only an address Qt
    // lends the callback for the duration of the call, never a wrapper the managed side owns, so
    // it stays IntPtr; QString is read out of Qt's reference; enums keep their generated type.
    static string CallbackManagedType(string t)
    {
        if (t == "QString") return "string";
        if (t.EndsWith("*", StringComparison.Ordinal)) return "IntPtr";
        if (IsEnum(t)) return CsEnum(t);
        return CSharpScalar(t) ?? t;
    }

    // The same values over the C ABI: an enum is an int, a pointer/QString reference is an
    // address, and the scalar spellings are the C# ones (long -> int on the MSVC target).
    static string CallbackAbiType(string t)
    {
        if (t == "void") return "void";
        if (IsEnum(t)) return "int";
        // A QString argument is Qt's const QString&: the ABI carries the address of the object.
        if (t == "QString" || t.EndsWith("*", StringComparison.Ordinal)) return "IntPtr";
        return CSharpScalar(t) ?? t;
    }

    static string CallbackAbiParam(string t, int index)
    {
        // A C++ bool is one byte; the default delegate marshalling would make it a 4-byte BOOL.
        string type = t == "bool" ? "[MarshalAs(UnmanagedType.I1)] bool" : CallbackAbiType(t);
        return type + " arg" + index;
    }

    static string CallbackParamToManaged(string t, string argument)
    {
        if (t == "QString")
            // Borrowed: the QString belongs to Qt for the duration of the callback and must not
            // be freed by the managed side (ToManaged would release it).
            return $"ApiRuntime.ToManagedTransient({argument})";
        if (t.EndsWith("*", StringComparison.Ordinal))
            return argument;
        if (IsEnum(t))
            return $"({CsEnum(t)}){argument}";
        return argument;
    }

    static string CallbackReturnToAbi(string t, string expression)
        => IsEnum(t) ? $"(int){expression}" : expression;

    // The same conversion in the other direction, used when the binding calls a callback that
    // native code installed. A QString is the caller's job (it has to build and free the native
    // object), so this covers the values that cross as themselves or as an int.
    static string CallbackManagedToAbi(string t, string argument)
        => IsEnum(t) ? $"(int){argument}" : argument;

    static string CallbackAbiToManagedReturn(string t, string expression)
        => IsEnum(t) ? $"({CsEnum(t)}){expression}" : expression;

    static string CallbackThunkName(string t) => "CallbackThunk_" + StableHash(t);


    // NativeParamType/NativeRetType/ArgToNative/RetToManaged are dispatched in TypeHandlers.cs.

    // A by-reference parameter: the marshaller writes straight into the caller's variable, so
    // the P/Invoke declaration carries the C# keyword. A bool is one byte in C++ and four in the
    // default Win32 marshalling, hence the explicit I1.

    // One model parameter is not always one native parameter: an in-out string contributes the
    // string it goes in as, plus the out slot the updated copy comes back through. A value stays one
    // parameter - the struct's layout is the C++ one, so it crosses as itself.
    static List<string> NativeParamsFor(JsonElement p) => ParamFor(p).NativeParams(p).ToList();

    static List<string> CppParamsFor(JsonElement p) => ParamFor(p).CppParams(p).ToList();

    // The call takes the local QString; the out pointer is filled after the call returns, and a
    // value hands over Qt's own type rebuilt from the POD.
    static List<string> CppArgsFor(JsonElement p) => ParamFor(p).CppArgs(p).ToList();

    // ---------------------------------------------------------------------------------------------
    // Qt's value types (QPointF, QPoint). C++ passes them by value, every member is arithmetic, and
    // PyQt and PySide both model them as value types - so no wrapper exists per point, and
    // `series.replace(points)` costs one temporary list rather than one heap object plus one
    // finalizer each. The managed side is a struct (runtime/QtStructs.cs) laid out exactly like
    // the C++ one (two doubles, two ints), so it crosses the boundary as a plain POD: the glue
    // declares the matching type from QtSharpApiRuntime.h and rebuilds Qt's value from it.

    /// <summary>The POD the glue declares for a value type (QPointF -> QsApiPointF).</summary>
    static string StructPod(string t)
    {
        // A Qt3D value type reaches here qualified (Qt3DAnimation::QKeyFrame); the POD's name is
        // the simple one, as is the C# struct's.
        string name = ApiTypes.StructName(t) ?? t;
        return "QsApi" + (name.StartsWith("Q", StringComparison.Ordinal) ? name.Substring(1) : name);
    }

    /// <summary>Qt's value rebuilt from the POD, for a call: QPointF(p.x, p.y).</summary>
    static string StructFromPod(string t, string name)
    {
        var entry = ApiTypes.RuntimeStructOf(t).Value;
        // A type whose memory layout is not its accessor order carries its own conversion (QRect).
        if (entry.FromPod != null)
            return entry.FromPod.Replace("@pod", name);
        return $"{ApiTypes.CppStructName(t)}({string.Join(", ", entry.Fields.Select(f => $"{name}.{f}"))})";
    }

    /// <summary>The POD the glue returns, built from Qt's value.</summary>
    static string StructToPod(string t, string value)
    {
        var entry = ApiTypes.RuntimeStructOf(t).Value;
        if (entry.ToPod != null)
            return entry.ToPod.Replace("@qt", value);
        return $"{StructPod(t)}{{ {string.Join(", ", entry.Fields.Select(f => $"{value}.{f}()"))} }}";
    }

    // The body of a native helper that calls a virtual through its flat ABI parameters. Most
    // parameters need nothing beyond the conversion CppArgsFor already spells; a QString& is the
    // exception, because the ABI carries it as the input text plus an out slot (the same shape a
    // normal method forwarder uses - see NativeBody). `finish` turns the call (or its result) into
    // the helper's return statement.
    static List<string> VirtualCallBody(JsonElement v, string call, Func<string, string> finish)
    {
        var refs = Params(v).Where(IsStringByRef).ToList();
        if (refs.Count == 0)
            return new List<string> { finish(call) };
        var lines = new List<string>();
        foreach (var p in refs)
        {
            string n = NativeName(PName(p));
            lines.Add($"QString {StringInLocal(n)} = {n} ? QString::fromUtf16({n}) : QString();");
        }
        string ret = ReturnType(v);
        if (ret == "void")
            lines.Add(call + ";");
        else
            lines.Add($"auto qs_api_result = {call};");
        foreach (var p in refs)
        {
            string n = NativeName(PName(p));
            lines.Add($"*{n}_out = new QString({StringInLocal(n)});");
        }
        if (ret != "void")
            lines.Add(finish("qs_api_result"));
        return lines;
    }

    // The body of an exported forwarder. Most are a single expression; an in-out string needs a
    // local to pass by reference and a statement that publishes the result.
    static List<string> NativeBody(JsonElement m, string call)
    {
        string ret = ReturnType(m);
        // An insert whose C++ return is the container's iterator publishes the value it points at;
        // the glue spells that as `.value()` on the call.
        if (HasMethod(m, ApiModel.MethodFlags.Iterator))
            call += ".value()";
        var refs = Params(m).Where(IsStringByRef).ToList();
        if (refs.Count == 0)
            return new List<string> { NativeRetExpr(ret, call) };

        var lines = new List<string>();
        foreach (var p in refs)
        {
            string name = NativeName(PName(p));
            lines.Add($"QString {StringInLocal(name)} = {name} ? QString::fromUtf16({name}) : QString();");
        }
        lines.Add(ret == "void" ? $"{call};" : $"auto qs_result = {call};");
        foreach (var p in refs)
        {
            string name = NativeName(PName(p));
            lines.Add($"*{name}_out = new QString({StringInLocal(name)});");
        }
        if (ret != "void")
            lines.Add(NativeRetExpr(ret, "qs_result"));
        return lines;
    }

    // NativeRetType is dispatched in TypeHandlers.cs.

    // The model's scalar types whose C# spelling is not the C++ one.
    static string CSharpScalar(string t) => t switch
    {
        "long long" => "long",
        "unsigned long long" => "ulong",
        "unsigned int" => "uint",
        "short" => "short",
        "unsigned short" => "ushort",
        "long" => "int",            // 32-bit on the MSVC target we bind
        "unsigned long" => "uint",
        "char" => "sbyte",
        "uchar" => "byte",
        "unsigned char" => "byte",
        "char16_t" => "char",
        _ => null,
    };

    // The C# field spelling inside a runtime struct. The scalar map covers the model's spellings;
    // a pointer field (QModelIndex's model pointer) is only stored, never dereferenced by the
    // struct, so IntPtr is the right representation.
    static string ManagedStructFieldType(string t)
        => CSharpScalar(t) ?? (t.EndsWith("*", StringComparison.Ordinal) ? "IntPtr" : t);

    // ArgToNative and RetToManaged are dispatched in TypeHandlers.cs.

    // The parameter that makes an owned return conditional (`ownedWhenNull` in the model): the
    // caller owns the object only when this parameter was null. Absent means the return is always
    // borrowed, exactly like a plain method.
    static string OwnedWhenNull(JsonElement m)
        => m.TryGetProperty("ownedWhenNull", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    // A method that hands a new object to the caller. Conditional ownership counts: the model's
    // `ownedWhenNull` is the explicit predicate, and the generated body chooses the factory.
    static bool ReturnsOwned(JsonElement m)
        => HasMethod(m, ApiModel.MethodFlags.OwnedReturn) || OwnedWhenNull(m) != null;

    // The managed expression for an owned return. Only a pointer to a class the binding can
    // release is expressible: the release path (FromOwnedHandle/Dispose/Destroy_<X>) is generated
    // together with the class's constructors, and a value or an opaque pointer has nothing to
    // own. A model entry that names anything else is a generator error, not a silent borrow -
    // otherwise the caller would be handed an object it can never release.
    static string OwnedRetToManaged(string t, string call)
    {
        if (IsClassPtr(t) && IsOwned(t.Substring(0, t.Length - 1)))
            return $"{CsTypeName(t.Substring(0, t.Length - 1))}.FromOwnedHandle({call})";
        throw new InvalidOperationException(
            $"A method return is marked owned, but '{t}' has no release path " +
            "(a bound class pointer with published constructors is required).");
    }

    // ------------------------------------------------------------------ C++
    static string GenerateNative()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// <auto-generated> generated from {ModelFileName} - do not edit");
        foreach (var header in (KnownClasses.Count > 0 ? KnownClasses : Classes).Select(c => c.GetProperty("header").GetString())
            .Concat(Functions.Select(f => f.GetProperty("header").GetString()))
                     .Distinct().OrderBy(h => h, StringComparer.Ordinal))
            sb.AppendLine($"#include <{header}>");
        sb.AppendLine("#include \"QtSharpApiRuntime.h\"");
        // QPair is std::pair and the optional glue spells std::optional; Qt's headers usually
        // pull both in, but the generated translation unit should not depend on that.
        sb.AppendLine("#include <utility>");
        sb.AppendLine("#include <optional>");
        // qHashBits, used by the hash of a struct whose equality ignores padding/flag bits.
        sb.AppendLine("#include <qhashfunctions.h>");
        sb.AppendLine("#include \"QtSharpApiExport.h\"");
        sb.AppendLine();
        // The mirrors of Qt's value types. Generated, not written into the shared runtime header: the
        // table in ApiModel.cs is their single source of truth, so a binding - the core or a module -
        // carries exactly the ones its model has, and growing the table cannot leave a module
        // compiling against a copy of the header that predates it.
        foreach (var s in ApiTypes.RuntimeStructs)
        {
            // The POD's size is asserted against the real Qt type, so the typedef is only emitted
            // where that type is known: the run's own model or a dependency it was built against
            // (KnownClasses carries both, and the header loop above included them).
            if (!KnownClasses.Any(c => Name(c) == s.Name))
                continue;
            string fields = string.Join(" ", Enumerable.Range(0, s.Fields.Length)
                .Select(i => $"{s.Types[i]} {s.Fields[i]};"));
            sb.AppendLine($"typedef struct {{ {fields} }} {StructPod(s.Name)};");
            // A bitwise conversion (QModelIndex's FromPod/ToPod) is only sound while the two layouts
            // agree; this catches a Qt layout change at compile time instead of at runtime.
            sb.AppendLine($"static_assert(sizeof({StructPod(s.Name)}) == sizeof({ApiTypes.CppStructName(s.Name)}), \"{s.Name} POD layout\");");
        }
        sb.AppendLine();
        // A managed null for a boxed parameter means "nothing was passed". Most parameters can
        // take a default-constructed value, but a reference parameter needs an lvalue, so it
        // gets a process-wide instance instead.
        sb.AppendLine("template <class T> T& QsApiDefaultValue() { static T value{}; return value; }");
        sb.AppendLine();

        foreach (var cls in Classes)
            Native.WriteNativeClass(sb, cls);

        foreach (var s in ApiTypes.RuntimeStructs)
            if (ModelRuntimeStructNames.Contains(s.Name))
                Structs.WriteStructNative(sb, s.Name);

        WriteFreeFunctionNative(sb);

        var containerTypes = ContainerTypes();
        foreach (var type in containerTypes.Where(IsSequenceT))
            ContainerGlue.WriteSequenceNativeCpp(sb, type);

        foreach (var type in containerTypes.Where(IsAssocT))
            ContainerGlue.WriteAssocNativeCpp(sb, type);

        foreach (var type in containerTypes.Where(IsPairT))
            ContainerGlue.WritePairNativeCpp(sb, type);

        foreach (var type in OptionalTypes())
            ContainerGlue.WriteOptionalNativeCpp(sb, type);

        // A managed override that returns a value type hands the value over by copying it into
        // the caller's slot.
        foreach (var boxed in VirtualBoxedReturns())
        {
            string stem = ListStem(boxed);
            sb.AppendLine();
            sb.AppendLine($"// {boxed}: the value a managed override returned, copied into the caller's slot.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_assign_{stem}(void* destination, void* source)");
            sb.AppendLine("{");
            // A managed override that did not produce a value hands over nullptr; the caller's slot
            // is already value-initialised, so nothing has to happen (it used to be dereferenced).
            sb.AppendLine("    if (source)");
            sb.AppendLine($"        *static_cast<{boxed}*>(destination) = *static_cast<const {boxed}*>(source);");
            sb.AppendLine("}");
        }
        foreach (var boxed in VirtualBoxedParams())
        {
            string stem = ListStem(boxed);
            sb.AppendLine();
            sb.AppendLine($"// {boxed}: the argument a virtual override receives, copied so the wrapper owns it.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_clone_{stem}(const void* source)");
            sb.AppendLine("{");
            sb.AppendLine($"    return source ? new {boxed}(*static_cast<const {boxed}*>(source)) : nullptr;");
            sb.AppendLine("}");
        }
        return sb.ToString();
    }


    static string CppCtorParamTypeFor(JsonElement p) => ParamFor(p).CppCtorParamType(p);


    // The C++ default expression a boxed parameter carries when its default is not expressible as a
    // C# default (QColor(Qt::white), QHostAddress(QHostAddress::Any)); null when there is none.
    static string DefaultCpp(JsonElement p)
        => p.TryGetProperty("defaultCpp", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

    // The generated subclass's constructor takes the *real* C++ parameter types, so it is called
    // with the C ABI argument converted straight to that type - which for everything but a
    // by-reference parameter is the name itself.
    static string CppCtorArgFor(JsonElement p) => ParamFor(p).CppCtorArg(p);

    static string CppParamType(string t) => CppAbiType(t);

    // An array of the runtime structs crosses as the POD array: the layouts are Qt's own, so the
    // glue only has to restate the type (and the call casts it back - see CppArg). The pointer is
    // mutable because the model does not record const and Qt declares both directions: a buffer Qt
    // only reads (QPainter::drawPoints) and one it fills in (QRawFont::advancesForGlyphIndexes).
    // A mutable pointer converts to const implicitly, which is what makes one spelling do for both.
    static string StructArrayAbiType(string t) => StructPod(RefValueType(t)) + "*";

    // The real C++ types, used where the generated code has to speak to Qt directly.
    static string CppCtorParamType(string t) => HandlerFor(t).CppCtorParamType(t);

    // Overrides must repeat the base declaration exactly, so these are the real C++ types
    // rather than the void* used by the exported C entry points.
    //
    // The model carries the spelling clang printed for a virtual's parameters, and that is what
    // an override has to repeat: the normalized model type has already lost the pointee const
    // (const QMimeData * becomes QMimeData*) and the reference (const QList<int> & becomes a
    // by-value QList<int>), and a mismatch is a silent C3668 that leaves the base implementation
    // in place.
    static string CppVirtualParamType(JsonElement p)
    {
        if (p.TryGetProperty("cpp", out var cpp) && cpp.ValueKind == JsonValueKind.String)
            return cpp.GetString();
        return CppVirtualParamTypeFor(PType(p));
    }

    // ------------------------------------------------------------------ identifiers
    static string Pascal(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    static string NativeName(string s) => s == "this" ? "self_" : s;

    static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "event", "base", "this", "object", "string", "params", "ref", "out", "in", "lock",
        "fixed", "default", "class", "namespace", "using", "static", "public", "private",
        "protected", "internal", "virtual", "override", "abstract", "sealed", "return",
        "void", "new", "null", "true", "false", "if", "else", "for", "foreach", "while",
        "do", "switch", "case", "break", "continue", "try", "catch", "finally", "throw",
        "is", "as", "sizeof", "typeof", "delegate", "enum", "operator", "extern", "volatile",
        "unsafe", "checked", "unchecked", "readonly", "const", "goto", "interface", "struct",
        "bool", "int", "long", "short", "byte", "char", "double", "float", "decimal", "uint",
        "ulong", "ushort", "sbyte",
    };

    static string SafeName(string s) => CSharpKeywords.Contains(s) ? "@" + s : s;
}
