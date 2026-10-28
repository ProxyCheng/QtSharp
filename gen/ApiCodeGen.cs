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
public static class ApiCodeGen
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
    // The same for enums whose published name dropped a module namespace: ChangeFlag ->
    // Qt3DCore::ChangeFlag, API -> Qt3DRender::API.
    static Dictionary<string, string> CppEnumNames = new(StringComparer.Ordinal);

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
        foreach (var container in new[] { "QList", "QMap", "QHash" })
        {
            if (!t.StartsWith(container + "<", StringComparison.Ordinal) || !t.EndsWith(">", StringComparison.Ordinal))
                continue;
            var inner = t.Substring(container.Length + 1, t.Length - container.Length - 2);
            if (container == "QList")
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
        // The type classification is a function of this run's class/enum tables.
        typeKinds.Clear();
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
    static bool IsQVariantType(string t) => Kind(t) == TypeKind.QVariant;
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

    static string ParamManagedType(JsonElement p)
        => IsOut(p) ? ManagedType(RefValueType(PType(p))) : ManagedType(PType(p));

    // A non-const QString& is Qt's in-out string (QValidator::validate/fixup, QClipboard::text).
    // The C ABI cannot carry a reference, so one model parameter becomes two native ones: the
    // string going in, and a pointer the updated copy comes back through.
    static bool IsStringByRef(JsonElement p) => IsByRef(p) && PType(p) == "QString";
    static string StringOutLocal(JsonElement p) => "__out_" + PName(p);
    static string StringInLocal(string nativeName) => "qs_in_" + nativeName;

    // Qt's headers are full of default arguments; carrying them into C# keeps navigation
    // like `new QObject()` or `SetRenderHint(hint)` working.
    static string ParamSig(JsonElement p)
    {
        string type = PType(p);
        // The caller supplies the variable Qt writes into, so the managed spelling is the value
        // type with the C# keyword in front.
        if (IsByRef(p))
            return $"{(IsOut(p) ? "out" : "ref")} {ParamManagedType(p)} {SafeName(PName(p))}";
        // The slot Qt writes one value through is the caller's own struct, addressed by hand.
        string managed = IsOpaqueStructPtr(p) ? "IntPtr" : ManagedType(type);
        string sig = $"{managed} {SafeName(PName(p))}";
        if (!IsOptional(p))
            return sig;
        string literal = p.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;
        literal = QualifyEnumDefault(literal, type);
        // An optional value type with the declaration's own default behind it: the managed spelling is
        // the nullable struct, so "not given" is a state the ABI can carry (`null` is a constant, which
        // is what a C# default argument has to be - the C++ expression itself could not go there).
        if (IsOptionalStructDefault(p))
            return $"{CsTypeName(type)}? {SafeName(PName(p))} = null";
        // An opaque handle is a value type: IntPtr.Zero is not a compile-time constant, so the
        // only usable default is `default`. A Qt value type is one too, and null is not convertible
        // to it - the model's `nullptr` default for `QPointF *` lands here (QLineF::intersects'
        // intersection point, which Qt writes back through; see the note in CONTEXT).
        if (literal == "null" && (ManagedType(type) == "IntPtr" || IsStruct(RefValueType(type))))
            literal = "default";
        return sig + " = " + (literal ?? CSharpDefault(type));
    }

    static string CSharpDefault(string type)
    {
        // A Guid is a value type, and Guid.Empty is exactly the null QUuid the declaration's
        // `= {}` default means (QCborValue::toUuid).
        if (IsUuid(type))
            return "default";
        // A QChar's default is the null character, which is what Qt's QChar() is.
        if (IsCharT(type))
            return "default";
        // A value type's default is `default` (QPointF() is (0, 0)); null is not convertible to it.
        if (IsStruct(RefValueType(type)))
            return "default";
        if (ManagedType(type) == "IntPtr")
            return "default";       // a value type: IntPtr.Zero is not a constant
        // A null Action/Func is Qt's own nullptr default (the declaration's "cpp" spelling is
        // what the glue otherwise substitutes, and null is the only C# constant a delegate has).
        if (IsCallbackType(type))
            return "null";
        if (IsArray(type) || IsPtr(type) || IsBoxed(type) || IsStringT(type) || type == "char*"
            || IsStringListT(type) || IsListT(type) || IsAssocT(type) || type == "QByteArrayView")
            return "null";
        return "default";
    }

    // The classification every IsXxx type predicate reads. A model type string is exactly one of
    // these - a pointer to a runtime struct is an array, a flags enum is still an enum - so the
    // predicates become one comparison each instead of a dozen string prefix tests, and a new
    // special type (QVariant, the erased QUuid/QChar) is one row in Classify rather than a new
    // predicate sprinkled through the marshalling chains. ClassNames/EnumNames are per run, so the
    // cache is cleared at the start of one.
    enum TypeKind
    {
        Unknown,
        Void,
        Scalar,          // int, double, bool, char16_t, uchar, ...
        String,          // QString / QStringView / QAnyStringView
        CString,         // char* (NUL-terminated)
        Char,            // QChar (erased to char)
        CharPtr,         // QChar* (a UTF-16 buffer)
        Uuid,            // QUuid (erased to Guid)
        QVariant,
        Struct,          // a runtime struct (QPoint, QDate)
        ScalarArray,     // int*, uchar*
        StructArray,     // QPoint* (a POD array, or one out slot)
        ClassPtr,        // a pointer to a bound class
        OpaquePtr,       // void*, or a pointer to a type the binding does not publish
        Enum,
        FlagsEnum,
        List,            // QList<E>
        StringList,      // QStringList
        Map,             // QMap<K,V>
        Hash,            // QHash<K,V>
        ByteArrayView,   // QByteArrayView
        TimeSpan,        // a std::chrono duration
        BoxedClass,      // a bound class crossing by value (a handle)
        Callback,        // a C function type: ret(*)(T1,T2)
        FunctionPointer, // QFunctionPointer: a signature-less function address (its carrier struct)
    }

    static readonly Dictionary<string, TypeKind> typeKinds = new(StringComparer.Ordinal);

    static TypeKind Kind(string t)
    {
        if (string.IsNullOrEmpty(t))
            return TypeKind.Unknown;
        if (typeKinds.TryGetValue(t, out var kind))
            return kind;
        kind = Classify(t);
        typeKinds[t] = kind;
        return kind;
    }

    // Only the raw building blocks may be consulted here: the IsXxx predicates below read Kind, so
    // calling one would recurse.
    static TypeKind Classify(string t)
    {
        // The erased types first: they are model classes, so ClassNames would claim them otherwise.
        if (t == "QVariant") return TypeKind.QVariant;
        if (t == "QUuid") return TypeKind.Uuid;
        if (t == "QChar") return TypeKind.Char;
        if (t == "QChar*") return TypeKind.CharPtr;
        if (t == "QString" || t == "QStringView" || t == "QAnyStringView") return TypeKind.String;
        if (t == "QStringList") return TypeKind.StringList;
        if (t == "char*") return TypeKind.CString;
        if (t == "QByteArrayView") return TypeKind.ByteArrayView;
        if (t == "TimeSpan") return TypeKind.TimeSpan;
        // QFunctionPointer is not a concrete signature: Qt promises only "a pointer to a
        // function", so the managed side is the QtCore carrier struct, holding the address.
        if (t == "QFunctionPointer") return TypeKind.FunctionPointer;
        if (ApiTypes.IsCallback(t)) return TypeKind.Callback;
        if (t == "void") return TypeKind.Void;
        if (ApiTypes.IsStruct(t)) return TypeKind.Struct;
        // The null-conditional is for the few calls the setup makes before the tables exist (the
        // erased-type filter on Classes); the cache is reset once they are built.
        if (EnumNames?.Contains(t) == true)
            return FlagsEnums?.Contains(t) == true ? TypeKind.FlagsEnum : TypeKind.Enum;
        if (t.StartsWith("QList<", StringComparison.Ordinal) && t.EndsWith(">")) return TypeKind.List;
        if (t.StartsWith("QMap<", StringComparison.Ordinal) && t.EndsWith(">")) return TypeKind.Map;
        if (t.StartsWith("QHash<", StringComparison.Ordinal) && t.EndsWith(">")) return TypeKind.Hash;
        if (t.EndsWith("*", StringComparison.Ordinal))
        {
            string pointee = t.Substring(0, t.Length - 1);
            if (ApiTypes.IsStruct(pointee)) return TypeKind.StructArray;
            if (IsScalarType(pointee)) return TypeKind.ScalarArray;
            if (ClassNames?.Contains(pointee) == true) return TypeKind.ClassPtr;
            return TypeKind.OpaquePtr;
        }
        if (IsScalarType(t)) return TypeKind.Scalar;
        if (ClassNames?.Contains(t) == true) return TypeKind.BoxedClass;
        return TypeKind.Unknown;
    }

    // The scalar vocabulary CSharpScalar knows, plus the four C#-native ones. void is not a value.
    static bool IsScalarType(string t)
        => t != "void" && (CSharpScalar(t) != null || t is "int" or "float" or "double" or "bool");

    // A pointer to a scalar is an array on the managed side (QPolygon's const int* argument, a
    // byte buffer, and the out parameters Qt writes back through). char* (a string) and void*
    // (a raw handle) have their own meaning and are not arrays.
    static bool IsScalarBase(string t) => Kind(t) == TypeKind.Scalar;
    // A pointer to a scalar is an array on the managed side (QPolygon's const int* argument and the
    // out parameters Qt writes back through), and so is a pointer to a runtime struct
    // (QPainter::drawPoints' `const QPointF *points, int pointCount`, and the advances
    // QRawFont::advancesForGlyphIndexes fills in): the managed array of the struct *is* the memory
    // Qt reads, and writes for the entry points that fill a buffer in. char* (a string) and void*
    // (a raw handle) have their own meaning and are not arrays.
    static bool IsArray(string t) => Kind(t) is TypeKind.ScalarArray or TypeKind.StructArray;

    // A pointer to one of the runtime structs. It crosses as an array of the struct, except where
    // the model marks the parameter omittable - then it is the single slot Qt writes through
    // (QLineF::intersects' intersection point), which only a raw address can express.
    static bool IsStructPtr(string t) => Kind(t) == TypeKind.StructArray;
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
    static IEnumerable<string> AbiArgs(JsonElement p)
    {
        if (IsOptionalStructDefault(p))
            return new[] { NativeName(PName(p)), GivenName(p) };
        // A QString& is two ABI parameters: the text and the slot the updated copy comes back
        // through (see CppParamsFor / NativeParamsFor).
        if (IsStringByRef(p))
            return new[] { NativeName(PName(p)), $"{NativeName(PName(p))}_out" };
        return new[] { NativeName(PName(p)) };
    }

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

    // Everything that surfaces as a managed string: QString and the views Qt declares next to it.
    // (char* is its own kind; callers that treat it as a string test CString explicitly.)
    static bool IsStringT(string t) => Kind(t) == TypeKind.String;

    // QUuid is 16 bytes of data and copies own nothing, so it is erased to System.Guid instead of
    // being wrapped: Guid is the same 128-bit value, and Qt's own operations on it (v3/v5 creation,
    // version/variant, RFC 4122 bytes) are extensions on Guid in runtime/QtCore/Uuid.cs. QUuid is
    // not a type in the binding, the same way QString is not one. The ABI is a plain 16-byte value
    // whose layout matches Guid's (data1, data2, data3, data4[8]); the glue converts it with
    // QsApiGuidToQuuid/QsApiQuuidToGuid.
    static bool IsUuid(string t) => Kind(t) == TypeKind.Uuid;

    // QChar is one UTF-16 code unit and copies own nothing, so it is erased to System.Char exactly
    // as QUuid is erased to Guid: there is no QChar type in the binding, and Qt's Unicode
    // operations on it are extensions on char in runtime/QtCore/Char.cs. The ABI is a uint16_t
    // (the managed side is ushort, because P/Invoke marshals char as one ANSI byte by default).
    static bool IsCharT(string t) => Kind(t) == TypeKind.Char;

    // A const QChar* buffer - a UTF-16 string. Qt's APIs that take one also take a length
    // (QCollator::compare, QRawFont::glyphIndexesForChars), which stays an explicit parameter.
    static bool IsCharPtrT(string t) => Kind(t) == TypeKind.CharPtr;

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

    // The container the model carries; the runtime builds a QStringList around the managed
    // array for the duration of the call.
    static bool IsStringListT(string t) => Kind(t) == TypeKind.StringList;

    // A generic container: QList<E> is marshalled element by element through a generated helper.
    static bool IsListT(string t) => Kind(t) == TypeKind.List;
    static string ListElem(string t) => t.Substring("QList<".Length, t.Length - "QList<".Length - 1);

    // The associative containers, marshalled pair by pair. QMap is ordered and QHash is not, but
    // the model publishes both as a managed Dictionary and both are walked with
    // constBegin()/key()/value().
    static bool IsMapT(string t) => Kind(t) == TypeKind.Map;
    static bool IsHashT(string t) => Kind(t) == TypeKind.Hash;
    static bool IsAssocT(string t) => Kind(t) is TypeKind.Map or TypeKind.Hash;
    static string AssocClass(string t) => IsHashT(t) ? "QHash" : "QMap";
    static string AssocPrefix(string t) => IsHashT(t) ? "Hash" : "Map";
    static string AssocTag(string t) => IsHashT(t) ? "hash" : "map";
    static (string Key, string Value) AssocElems(string t)
        => ApiTypes.SplitPair(t.Substring(AssocClass(t).Length + 1, t.Length - AssocClass(t).Length - 2));

    // The element kinds the generated converters distinguish: a scalar crosses the C ABI as
    // itself, a bound class as its handle, and a boxed value as a pointer to a copy.
    static bool IsScalarElem(string e) => Kind(e) == TypeKind.Scalar;
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

    // One container element crossing the C ABI, in the four directions the generated wrapper
    // needs. A QString goes as the UTF-16 pointer every other string entry point uses, and a
    // container handing an element *back* returns a heap copy, so its ABI type is void*.
    // An enum crosses the C ABI as the int it is.
    static bool IsEnumElem(string e) => IsEnum(e);
    static string ElemAbiCppParam(string e) => IsScalarElem(e) ? CppAbiScalar(e) : IsEnumElem(e) ? "int" : IsStringT(e) ? "const char16_t*" : IsUuid(e) ? "QsApiGuid" : IsCharT(e) ? "char16_t" : IsStruct(e) ? StructPod(e) : "void*";
    static string ElemAbiCppRet(string e) => IsScalarElem(e) ? CppAbiScalar(e) : IsEnumElem(e) ? "int" : IsUuid(e) ? "QsApiGuid" : IsCharT(e) ? "char16_t" : IsStruct(e) ? StructPod(e) : "void*";
    static string ElemAbiCsParam(string e) => IsScalarElem(e) ? ManagedType(e) : IsEnumElem(e) ? "int"
        : IsStringT(e) ? "[MarshalAs(UnmanagedType.LPWStr)] string" : IsUuid(e) ? "Guid" : IsCharT(e) ? "ushort" : IsStruct(e) ? ManagedType(e) : "IntPtr";
    static string ElemAbiCsRet(string e) => IsScalarElem(e) ? ManagedType(e) : IsEnumElem(e) ? "int" : IsUuid(e) ? "Guid" : IsCharT(e) ? "ushort" : IsStruct(e) ? ManagedType(e) : "IntPtr";
    // ABI -> real value (building the native container from managed data). A struct is rebuilt from
    // the POD, whose layout is Qt's own.
    static string ElemToCpp(string e, string expr) => IsScalarElem(e) ? expr
        : IsEnumElem(e) ? $"static_cast<{e}>({expr})"
        : IsStringT(e) ? $"({expr} ? QString::fromUtf16({expr}) : QString())"
        : IsUuid(e) ? $"QsApiGuidToQuuid({expr})"
        : IsCharT(e) ? $"QChar({expr})"
        : IsStruct(e) ? StructFromPod(e, expr)
        : IsPointerElem(e) ? $"static_cast<{e.Substring(0, e.Length - 1)}*>({expr})"
        : $"*static_cast<{e}*>({expr})";
    // Real value -> ABI (handing one element back to managed code; a QString has to be a copy).
    static string ElemFromCpp(string e, string expr) => IsScalarElem(e) ? expr
        : IsEnumElem(e) ? $"static_cast<int>({expr})"
        : IsStringT(e) ? $"new QString({expr})"
        : IsUuid(e) ? $"QsApiQuuidToGuid({expr})"
        : IsCharT(e) ? $"{expr}.unicode()"
        : IsStruct(e) ? StructToPod(e, expr)
        : IsPointerElem(e) ? $"static_cast<void*>({expr})"
        : $"new {e}({expr})";
    // ABI -> managed value. A struct *is* the ABI value: the POD comes back by value and the managed
    // struct has the same layout, so nothing is built and nothing is wrapped.
    static string ElemRead(string e, string expr) => IsScalarElem(e) ? expr
        : IsEnumElem(e) ? $"({CsEnum(e)}){expr}"
        : IsStringT(e) ? $"ApiRuntime.ToManaged({expr})"
        : IsUuid(e) ? expr
        : IsCharT(e) ? $"(char){expr}"
        : IsStruct(e) ? expr
        : IsQVariantType(e) ? $"{VariantMarshalling}.ToManaged({expr})"
        : IsPointerElem(e) ? PointerElemRead(e, expr)
        : IsOwned(e) ? $"{CsTypeName(e)}.FromOwnedHandle({expr})" : $"{CsTypeName(e)}.FromTransientHandle({expr})";
    // Reading a pointer element. A QObject goes through FromHandle, so a QList<QObject*> element
    // (QObject::children()) is the object's actual bound class and keeps its identity, exactly
    // like a QObject* return value. Any other pointer keeps the transient wrapper: a
    // QGraphicsItem has no meta-object and no lifetime the identity cache could track.
    static string PointerElemRead(string e, string expr)
    {
        string pointee = e.Substring(0, e.Length - 1);
        return $"{CsTypeName(pointee)}.{(DerivesFromQObject(pointee) ? "FromHandle" : "FromTransientHandle")}({expr})";
    }
    // Managed value -> ABI. A QVariant has no one-line spelling: converting an object makes a
    // temporary that has to be disposed, which the callers that can emit statements do themselves.
    static string ElemArg(string e, string expr) => IsScalarElem(e) || IsStringT(e) || IsStruct(e) || IsUuid(e) ? expr
        : IsCharT(e) ? $"(ushort){expr}"
        : IsEnumElem(e) ? $"(int){expr}"
        : $"{expr}?.Handle ?? IntPtr.Zero";

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
        return sb.ToString();
    }
    static string ListHelper(string elem) => "QsApiList_" + ListStem(elem);
    static string MapStem(string key, string value) => ListStem(key) + "_" + ListStem(value);
    static string AssocStem(string t)
    {
        var (key, value) = AssocElems(t);
        return MapStem(key, value);
    }
    static string AssocHelper(string t) => $"QsApi{AssocPrefix(t)}_{AssocStem(t)}";

    // Every pointer that is not an array: a C string, a UTF-16 buffer, a bound class, or an
    // opaque handle (void*, QEvent*).
    static bool IsPtr(string t) => Kind(t) is TypeKind.CString or TypeKind.CharPtr
                                            or TypeKind.ClassPtr or TypeKind.OpaquePtr;
    static bool IsCallbackType(string t) => Kind(t) == TypeKind.Callback;

    // Both of Qt's function-pointer spellings: a concrete type to build a thunk for, and the
    // signature-less QFunctionPointer that is only ever an address.
    static bool IsFunctionType(string t)
        => Kind(t) == TypeKind.Callback || Kind(t) == TypeKind.FunctionPointer;
    static bool IsClassPtr(string t) => IsPtr(t) && ClassNames.Contains(t.Substring(0, t.Length - 1));
    static bool IsEnum(string t) => Kind(t) is TypeKind.Enum or TypeKind.FlagsEnum;
    static bool IsFlags(string t) => Kind(t) == TypeKind.FlagsEnum;
    // A handle wrapper. QUuid and QChar are model classes but not wrappers: they are erased to
    // BCL values (Guid, char).
    static bool IsBoxed(string t) => Kind(t) is not (TypeKind.Uuid or TypeKind.Char) && ClassNames.Contains(t);
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
            WriteClass(file, cls);
            file.AppendLine("    internal static partial class ApiNative");
            file.AppendLine("    {");
            WriteNativeDeclarations(file, cls);
            file.AppendLine("    }");
            file.AppendLine();
        }

        // The managed thunks for every function type in a published signature. Qt keeps only the
        // address, so one bridge class per spelling turns the C ABI values back into Action/Func;
        // the runtime registry roots both the thunk and the caller's delegate.
        var callbackSpellings = CallbackSpellings();
        if (callbackSpellings.Count > 0)
            WriteCallbackThunks(CSharpFile(PlainFileKey(ns + ".Callbacks")), callbackSpellings);

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
            WriteStructMembers(CSharpFile(HeaderFileKey(row.Value.GetProperty("header").GetString())), s.Name);
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

        // The generated helpers for the container types used by the signatures.
        var listElements = ListElements();
        var assocElements = AssocElements();
        if (listElements.Count > 0 || assocElements.Count > 0)
        {
            var helpers = CSharpFile(PlainFileKey(ns + ".Helpers"));
            helpers.AppendLine("    internal static partial class ApiNative");
            helpers.AppendLine("    {");
            foreach (var elem in listElements)
                WriteListNativeDeclarations(helpers, elem);
            foreach (var assoc in assocElements)
                WriteAssocNativeDeclarations(helpers, assoc);
            helpers.AppendLine("    }");
            foreach (var elem in listElements)
                WriteListHelper(helpers, elem);
            foreach (var assoc in assocElements)
                WriteAssocHelper(helpers, assoc);
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
        foreach (var cls in Classes)
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

    // Every element type that appears in a QList<...> anywhere in the model, in a stable order.
    static List<string> ListElements()
    {
        var elements = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in SignatureTypes())
            if (t != null && IsListT(t))
                elements.Add(ListElem(t));
        return elements.ToList();
    }

    // The C# side of the container conversion: New/Free around a call, Read for a return.
    static void WriteListHelper(StringBuilder sb, string elem)
    {
        string helper = ListHelper(elem);
        string stem = ListStem(elem);
        string managed = ManagedElement(elem);
        // The element is the loop variable of the generated New(): a scalar and a runtime struct both
        // cross as themselves, a handle (class pointer or boxed value) as the raw pointer it wraps.
        string argConv = IsScalarElem(elem) || IsStruct(elem) ? "item" : "item?.Handle ?? IntPtr.Zero";

        sb.AppendLine();
        sb.AppendLine($"    internal static class {helper}");
        sb.AppendLine("    {");
        sb.AppendLine($"        public static IntPtr New({managed}[] items)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var list = ApiNative.List_{stem}_new();");
        if (IsQVariantType(elem))
        {
            // Every element becomes a QVariant of its own, released as soon as the append copied it.
            sb.AppendLine("            if (items != null)");
            sb.AppendLine("                foreach (var item in items)");
            sb.AppendLine($"                    using (var __variant = {VariantMarshalling}.Value(item))");
            sb.AppendLine($"                        ApiNative.List_{stem}_append(list, __variant.Handle);");
        }
        else
        {
            sb.AppendLine("            if (items != null)");
            sb.AppendLine($"                foreach (var item in items)");
            sb.AppendLine($"                    ApiNative.List_{stem}_append(list, {argConv});");
        }
        sb.AppendLine("            return list;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public static void Free(IntPtr list) => ApiNative.List_{stem}_free(list);");
        sb.AppendLine();
        // Read copies the container out and releases it (it was allocated for us); ReadTransient
        // is for the container a virtual override is handed, which belongs to Qt.
        sb.AppendLine($"        public static {managed}[] Read(IntPtr list)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var result = ReadTransient(list);");
        sb.AppendLine($"            if (list != IntPtr.Zero)");
        sb.AppendLine($"                ApiNative.List_{stem}_free(list);");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        internal static {managed}[] ReadTransient(IntPtr list)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (list == IntPtr.Zero)");
        sb.AppendLine("                return null;");
        sb.AppendLine($"            int count = ApiNative.List_{stem}_count(list);");
        sb.AppendLine($"            var result = new {managed}[count];");
        sb.AppendLine("            for (int i = 0; i < count; i++)");
        sb.AppendLine($"                result[i] = {ReadElement(elem)};");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine();
        // Write is the virtual-dispatch direction: the managed override produced the array and it
        // has to land in the caller's slot.
        sb.AppendLine($"        internal static void Write(IntPtr destination, {managed}[] items)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var list = New(items);");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                ApiNative.Assign_List_{stem}(destination, list);");
        sb.AppendLine("            }");
        sb.AppendLine("            finally");
        sb.AppendLine("            {");
        sb.AppendLine($"                Free(list);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    // Every associative container that appears anywhere in the model, in a stable order.
    static List<string> AssocElements()
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in SignatureTypes())
            if (t != null && IsAssocT(t))
                types.Add(t);
        return types.ToList();
    }

    // The C# side of the container conversion: New/Free around a call, Read for a return.
    static void WriteAssocHelper(StringBuilder sb, string t)
    {
        var (key, value) = AssocElems(t);
        string helper = AssocHelper(t);
        string prefix = AssocPrefix(t);
        string stem = AssocStem(t);
        string managedKey = ManagedElement(key);
        string managedValue = ManagedElement(value);

        sb.AppendLine();
        sb.AppendLine($"    internal static class {helper}");
        sb.AppendLine("    {");
        sb.AppendLine($"        public static IntPtr New(Dictionary<{managedKey}, {managedValue}> items)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var map = ApiNative.{prefix}_{stem}_new();");
        bool keyVariant = IsQVariantType(key), valueVariant = IsQVariantType(value);
        if (keyVariant || valueVariant)
        {
            // A QVariant key or value is built per entry and released as soon as the insert copied it.
            sb.AppendLine("            if (items != null)");
            sb.AppendLine("                foreach (var pair in items)");
            sb.AppendLine("                {");
            if (keyVariant)
                sb.AppendLine($"                    using var __key = {VariantMarshalling}.Value(pair.Key);");
            if (valueVariant)
                sb.AppendLine($"                    using var __value = {VariantMarshalling}.Value(pair.Value);");
            string keyArg = keyVariant ? "__key.Handle" : ElemArg(key, "pair.Key");
            string valueArg = valueVariant ? "__value.Handle" : ElemArg(value, "pair.Value");
            sb.AppendLine($"                    ApiNative.{prefix}_{stem}_insert(map, {keyArg}, {valueArg});");
            sb.AppendLine("                }");
        }
        else
        {
            sb.AppendLine("            if (items != null)");
            sb.AppendLine("                foreach (var pair in items)");
            sb.AppendLine($"                    ApiNative.{prefix}_{stem}_insert(map, {ElemArg(key, "pair.Key")}, {ElemArg(value, "pair.Value")});");
        }
        sb.AppendLine("            return map;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public static void Free(IntPtr map) => ApiNative.{prefix}_{stem}_free(map);");
        sb.AppendLine();
        sb.AppendLine($"        public static Dictionary<{managedKey}, {managedValue}> Read(IntPtr map)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var result = ReadTransient(map);");
        sb.AppendLine("            if (map != IntPtr.Zero)");
        sb.AppendLine($"                ApiNative.{prefix}_{stem}_free(map);");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        internal static Dictionary<{managedKey}, {managedValue}> ReadTransient(IntPtr map)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (map == IntPtr.Zero)");
        sb.AppendLine("                return null;");
        sb.AppendLine($"            int count = ApiNative.{prefix}_{stem}_count(map);");
        sb.AppendLine($"            var result = new Dictionary<{managedKey}, {managedValue}>(count);");
        sb.AppendLine("            for (int i = 0; i < count; i++)");
        sb.AppendLine("            {");
        sb.AppendLine($"                var itemKey = {ElemRead(key, $"ApiNative.{prefix}_{stem}_key(map, i)")};");
        sb.AppendLine($"                var itemValue = {ElemRead(value, $"ApiNative.{prefix}_{stem}_value(map, i)")};");
        sb.AppendLine("                result[itemKey] = itemValue;");
        sb.AppendLine("            }");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        internal static void Write(IntPtr destination, Dictionary<{managedKey}, {managedValue}> items)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var map = New(items);");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                ApiNative.Assign_{prefix}_{stem}(destination, map);");
        sb.AppendLine("            }");
        sb.AppendLine("            finally");
        sb.AppendLine("            {");
        sb.AppendLine("                Free(map);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    // The DllImports the generated container helper class calls.
    static void WriteAssocNativeDeclarations(StringBuilder sb, string t)
    {
        var (key, value) = AssocElems(t);
        string prefix = AssocPrefix(t);
        string stem = AssocStem(t);
        string abiKey = ElemAbiCsParam(key);
        string abiValue = ElemAbiCsParam(value);
        string retKey = ElemAbiCsRet(key);
        string retValue = ElemAbiCsRet(value);
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_new\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern IntPtr {prefix}_{stem}_new();");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_insert\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void {prefix}_{stem}_insert(IntPtr map, {abiKey} key, {abiValue} value);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_count\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern int {prefix}_{stem}_count(IntPtr map);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_key\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern {retKey} {prefix}_{stem}_key(IntPtr map, int index);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_value\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern {retValue} {prefix}_{stem}_value(IntPtr map, int index);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void {prefix}_{stem}_free(IntPtr map);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_assign_{AssocTag(t)}_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void Assign_{prefix}_{stem}(IntPtr destination, IntPtr source);");
    }

    // The C++ side. Neither container has random access, so the pair at an index is reached by
    // walking the iterator; the containers Qt hands us here are small.
    static void WriteAssocNativeCpp(StringBuilder sb, string t)
    {
        var (key, value) = AssocElems(t);
        string tag = AssocTag(t);
        string stem = AssocStem(t);
        string container = $"{AssocClass(t)}<{key},{value}>";
        string abiKey = ElemAbiCppParam(key);
        string abiValue = ElemAbiCppParam(value);
        string retKey = ElemAbiCppRet(key);
        string retValue = ElemAbiCppRet(value);

        sb.AppendLine();
        sb.AppendLine($"// {container}: pair-by-pair conversion for the managed side.");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{tag}_{stem}_new()");
        sb.AppendLine("{");
        sb.AppendLine($"    return new {container}();");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{tag}_{stem}_insert(void* map, {abiKey} key, {abiValue} value)");
        sb.AppendLine("{");
        sb.AppendLine($"    static_cast<{container}*>(map)->insert({ElemToCpp(key, "key")}, {ElemToCpp(value, "value")});");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT int qs_api_{tag}_{stem}_count(const void* map)");
        sb.AppendLine("{");
        sb.AppendLine($"    return static_cast<int>(static_cast<const {container}*>(map)->size());");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT {retKey} qs_api_{tag}_{stem}_key(const void* map, int index)");
        sb.AppendLine("{");
        sb.AppendLine($"    auto it = static_cast<const {container}*>(map)->constBegin();");
        sb.AppendLine("    for (int i = 0; i < index; ++i)");
        sb.AppendLine("        ++it;");
        sb.AppendLine($"    return {ElemFromCpp(key, "it.key()")};");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT {retValue} qs_api_{tag}_{stem}_value(const void* map, int index)");
        sb.AppendLine("{");
        sb.AppendLine($"    auto it = static_cast<const {container}*>(map)->constBegin();");
        sb.AppendLine("    for (int i = 0; i < index; ++i)");
        sb.AppendLine("        ++it;");
        sb.AppendLine($"    return {ElemFromCpp(value, "it.value()")};");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{tag}_{stem}_free(void* map)");
        sb.AppendLine("{");
        sb.AppendLine($"    delete static_cast<{container}*>(map);");
        sb.AppendLine("}");
        sb.AppendLine($"// A virtual override that returns a {container}: copy it into the caller's slot.");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_assign_{tag}_{stem}(void* destination, void* source)");
        sb.AppendLine("{");
        sb.AppendLine("    if (source)");
        sb.AppendLine($"        *static_cast<{container}*>(destination) = *static_cast<const {container}*>(source);");
        sb.AppendLine("}");
    }

    // The DllImports the generated helper class calls.
    static void WriteListNativeDeclarations(StringBuilder sb, string elem)
    {
        string stem = ListStem(elem);
        // A struct element crosses as itself here too: the entry point takes it by value.
        string abi = IsScalarElem(elem) || IsStruct(elem) ? ManagedType(elem) : "IntPtr";
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_list_{stem}_new\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern IntPtr List_{stem}_new();");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_list_{stem}_append\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void List_{stem}_append(IntPtr list, {abi} item);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_list_{stem}_count\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern int List_{stem}_count(IntPtr list);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_list_{stem}_at\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern {abi} List_{stem}_at(IntPtr list, int index);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_list_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void List_{stem}_free(IntPtr list);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_assign_list_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void Assign_List_{stem}(IntPtr destination, IntPtr source);");
    }

    // The C++ side of the container conversion: one append/count/at per element type, with the
    // element crossing the C ABI as itself, as a handle, or as a pointer to a copy.
    static void WriteListNativeCpp(StringBuilder sb, string elem)
    {
        string stem = ListStem(elem);
        string list = $"QList<{elem}>";
        // A struct element crosses as the POD by value, the same way the managed declaration passes it.
        string abi = IsScalarElem(elem) ? elem : IsStruct(elem) ? StructPod(elem) : "void*";

        string appendConv = IsScalarElem(elem) ? "item"
            : IsStruct(elem) ? StructFromPod(elem, "item")
            : IsPointerElem(elem) ? $"static_cast<{elem.Substring(0, elem.Length - 1)}*>(item)"
            : $"*static_cast<{elem}*>(item)";
        // A struct element is handed back as the POD whose layout Qt's type has, the same way the
        // managed declaration reads it.
        string atValue = $"static_cast<const {list}*>(list)->at(index)";
        string atConv = IsScalarElem(elem)
            ? $"return {atValue};"
            : IsStruct(elem)
                ? $"return {StructToPod(elem, atValue)};"
                : IsPointerElem(elem)
                    ? $"return static_cast<void*>({atValue});"
                    : $"return new {elem}({atValue});";

        sb.AppendLine();
        sb.AppendLine($"// QList<{elem}>: element-by-element conversion for the managed side.");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_list_{stem}_new()");
        sb.AppendLine("{");
        sb.AppendLine($"    return new {list}();");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_list_{stem}_append(void* list, {abi} item)");
        sb.AppendLine("{");
        sb.AppendLine($"    static_cast<{list}*>(list)->append({appendConv});");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT int qs_api_list_{stem}_count(const void* list)");
        sb.AppendLine("{");
        sb.AppendLine($"    return static_cast<int>(static_cast<const {list}*>(list)->size());");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT {abi} qs_api_list_{stem}_at(const void* list, int index)");
        sb.AppendLine("{");
        sb.AppendLine($"    {atConv}");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_list_{stem}_free(void* list)");
        sb.AppendLine("{");
        sb.AppendLine($"    delete static_cast<{list}*>(list);");
        sb.AppendLine("}");
        sb.AppendLine($"// A virtual override that returns a {list}: copy it into the caller's slot.");
        sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_assign_list_{stem}(void* destination, void* source)");
        sb.AppendLine("{");
        sb.AppendLine("    if (source)");
        sb.AppendLine($"        *static_cast<{list}*>(destination) = *static_cast<const {list}*>(source);");
        sb.AppendLine("}");
    }

    static string ToCSharpElement(string elem) => ManagedElement(elem);

    static string ReadElement(string elem)
    {
        string stem = ListStem(elem);
        string call = $"ApiNative.List_{stem}_at(list, i)";
        if (IsScalarElem(elem) || IsStruct(elem))
            return call;
        if (IsQVariantType(elem))
            return $"{VariantMarshalling}.ToManaged({call})";
        if (IsPointerElem(elem))
            return PointerElemRead(elem, call);
        // A boxed value comes back as a fresh copy that the wrapper owns.
        return IsOwned(elem) ? $"{CsTypeName(elem)}.FromOwnedHandle({call})" : $"{CsTypeName(elem)}.FromTransientHandle({call})";
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

    // Every function-type spelling that appears in a published signature. One thunk class per
    // distinct spelling: the ABI shape is a function of the spelling alone, so two APIs taking the
    // same function type share the conversion code. Both positions count - a returned callback is
    // wrapped by the same class that installs one.
    static SortedSet<string> CallbackSpellings()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        void Add(string t)
        {
            if (IsCallbackType(t))
                found.Add(t);
        }

        foreach (var (fn, _) in FreeFunctionEntries())
        {
            Add(ReturnType(fn));
            foreach (var p in Params(fn))
                Add(PType(p));
        }
        foreach (var cls in Classes)
        {
            foreach (string list in new[] { "methods", "virtuals", "constructors" })
            {
                if (!cls.TryGetProperty(list, out var functions) || functions.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var fn in functions.EnumerateArray())
                {
                    // Constructors have no return value, and only they lack the property.
                    if (list != "constructors")
                        Add(ReturnType(fn));
                    foreach (var p in Params(fn))
                        Add(PType(p));
                }
            }
        }
        return found;
    }

    // The managed -> native bridge for one function type. Qt stores only the address, so the
    // generated delegate (a static thunk per spelling) does the value conversion and the runtime
    // registry keeps the thunk and the caller's delegate rooted. The delegate type itself is
    // Action/Func, which is what makes a callback an ordinary C# value.
    static void WriteCallbackThunks(StringBuilder sb, IEnumerable<string> spellings)
    {
        foreach (string spelling in spellings)
        {
            if (!ApiTypes.TryParseCallback(spelling, out string ret, out string[] arguments))
                continue;

            string className = CallbackThunkName(spelling);
            string managed = CallbackDelegateType(spelling);
            var declarations = new List<string>();
            var parameters = new List<string>();
            var conversions = new List<string>();
            for (int i = 0; i < arguments.Length; i++)
            {
                declarations.Add(CallbackAbiParam(arguments[i], i));
                parameters.Add("arg" + i);
                conversions.Add(CallbackParamToManaged(arguments[i], "arg" + i));
            }
            string call = $"callback({string.Join(", ", conversions)})";

            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// Converts the C ABI shape of <c>{spelling}</c> to its managed Action/Func. Qt");
            sb.AppendLine("    /// keeps only the address, so the thunk and the callback stay rooted in");
            sb.AppendLine("    /// <see cref=\"QtCallbackRegistry\"/> for the lifetime of the process.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    internal static class {className}");
            sb.AppendLine("    {");
            sb.AppendLine("        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]");
            if (ret == "bool")
                sb.AppendLine("        [return: MarshalAs(UnmanagedType.I1)]");
            sb.AppendLine($"        private delegate {CallbackAbiType(ret)} Native({string.Join(", ", declarations)});");
            sb.AppendLine();
            sb.AppendLine($"        internal static IntPtr Get({managed} callback)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (callback == null)");
            sb.AppendLine("                return IntPtr.Zero;      // Qt's own nullptr: restore the default");
            sb.AppendLine($"            return QtCallbackRegistry.Get(callback, () => new Native(({string.Join(", ", parameters)}) =>");
            sb.AppendLine("            {");
            sb.AppendLine("                try");
            sb.AppendLine("                {");
            if (ret == "void")
                sb.AppendLine($"                    {call};");
            else
                sb.AppendLine($"                    return {CallbackReturnToAbi(ret, call)};");
            sb.AppendLine("                }");
            sb.AppendLine("                catch (Exception exception)");
            sb.AppendLine("                {");
            sb.AppendLine("                    // Qt called this from its own C++ frames; an exception must not");
            sb.AppendLine("                    // unwind through them. Report it and return the type's zero.");
            sb.AppendLine("                    ApiRuntime.ReportCallbackException(\"a native callback\", exception);");
            if (ret != "void")
                sb.AppendLine("                    return default;");
            sb.AppendLine("                }");
            sb.AppendLine("            }));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Qt can also hand a function type back (QEasingCurve::customType,
            // qInstallMessageHandler's previous handler). A concrete signature is wrapped into the
            // same managed delegate type. A QString argument of a *foreign* callback needs a native
            // QString built for the call and freed afterwards.
            var abiArguments = new List<string>();
            var qstringArguments = new List<int>();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == "QString")
                {
                    qstringArguments.Add(i);
                    abiArguments.Add("qsArg" + i);
                }
                else
                {
                    abiArguments.Add(CallbackManagedToAbi(arguments[i], "arg" + i));
                }
            }
            string nativeCall = $"native({string.Join(", ", abiArguments)})";
            string managedParameters = string.Join(", ", parameters);

            sb.AppendLine($"        internal static {managed} Wrap(IntPtr pointer)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (pointer == IntPtr.Zero)");
            sb.AppendLine("                return null;      // Qt's nullptr: no callback installed");
            sb.AppendLine("            return QtCallbackRegistry.Wrap(pointer, () =>");
            sb.AppendLine("            {");
            sb.AppendLine("                // The address was installed by native code, so the declaration's");
            sb.AppendLine("                // signature is the contract; the registry memoizes this wrapper per");
            sb.AppendLine("                // address and delegate type (a repeated read returns the same object).");
            sb.AppendLine("                var native = (Native)Marshal.GetDelegateForFunctionPointer(pointer, typeof(Native));");
            if (qstringArguments.Count == 0)
            {
                string body = ret == "void"
                    ? nativeCall
                    : CallbackAbiToManagedReturn(ret, nativeCall);
                sb.AppendLine($"                return new {managed}(({managedParameters}) => {body});");
            }
            else
            {
                sb.AppendLine($"                return new {managed}(({managedParameters}) =>");
                sb.AppendLine("                {");
                foreach (int i in qstringArguments)
                    sb.AppendLine($"                    var qsArg{i} = ApiRuntime.NewQString(arg{i});");
                sb.AppendLine("                    try");
                sb.AppendLine("                    {");
                if (ret == "void")
                    sb.AppendLine($"                        {nativeCall};");
                else
                    sb.AppendLine($"                        return {CallbackAbiToManagedReturn(ret, nativeCall)};");
                sb.AppendLine("                    }");
                sb.AppendLine("                    finally");
                sb.AppendLine("                    {");
                foreach (int i in qstringArguments)
                    sb.AppendLine($"                        ApiRuntime.FreeQString(qsArg{i});");
                sb.AppendLine("                    }");
                sb.AppendLine("                });");
            }
            sb.AppendLine("            });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
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
    static string ParamTypeKey(JsonElement p)
        => IsByRef(p) ? (IsOut(p) ? "out " : "ref ") + ParamManagedType(p) : ManagedType(PType(p));

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

    /// <summary>
    /// The constructors the model declares for a value type, without the parameterless one: C#'s
    /// `default` already produces the same zero value Qt's own default constructor does.
    /// </summary>
    static List<JsonElement> StructCtors(string name)
    {
        var ctors = new List<JsonElement>();
        if (!ModelClassNames.Contains(name))
            return ctors;
        var cls = CoreClassOf(name);
        if (cls == null || !cls.Value.TryGetProperty("constructors", out var cs))
            return ctors;
        // C#'s `default` is Qt's default construction for almost every value type. QCalendar is
        // the exception: default(QCalendar) is the null backend, while QCalendar() is Gregorian,
        // so its parameterless constructor stays callable.
        bool keepZeroCtor = ZeroCtorStructs.Contains(name);
        foreach (var c in cs.EnumerateArray())
            if (Params(c).Count > 0 || keepZeroCtor)
                ctors.Add(c);
        return ctors;
    }

    /// <summary>
    /// The comparison operators the model declares for a value type. C++ declares them as functions
    /// too, so they arrive here with names like `operator==`, and C# spells them as operators.
    /// </summary>
    static List<JsonElement> StructComparisons(string name)
    {
        var ops = new List<JsonElement>();
        if (!ModelClassNames.Contains(name))
            return ops;
        var cls = CoreClassOf(name);
        if (cls == null || !cls.Value.TryGetProperty("methods", out var ms))
            return ops;
        foreach (var m in ms.EnumerateArray())
            if (ComparisonSymbol(Name(m)) != null && Params(m).Count == 2)
                ops.Add(m);
        return ops;
    }

    /// <summary>The C# spelling of a comparison the model spells the C++ way, or null.</summary>
    static string ComparisonSymbol(string modelName) => modelName switch
    {
        "operator<" => "<",
        "operator<=" => "<=",
        "operator>" => ">",
        "operator>=" => ">=",
        _ => null,
    };

    /// <summary>A C identifier for one of them, used in the export and P/Invoke names.</summary>
    static string ComparisonEntry(JsonElement op) => Name(op) switch
    {
        "operator==" => "eq",
        "operator!=" => "ne",
        "operator<" => "lt",
        "operator<=" => "le",
        "operator>" => "gt",
        "operator>=" => "ge",
        _ => "cmp",
    };

    static List<(JsonElement Method, string Entry, string Cs)> StructMembers(string name)
    {
        var members = new List<(JsonElement, string, string)>();
        if (!ModelClassNames.Contains(name))
            return members;
        var cls = CoreClassOf(name);
        if (cls == null || !cls.Value.TryGetProperty("methods", out var ms))
            return members;
        var written = HandWrittenMembers(name);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        void Add(JsonElement m)
        {
            string n = Name(m);
            // An operator or the destructor is not a member C# can forward under that name.
            if (!IsIdentifier(n) || n == name)
                return;
            // A struct has no inheritance, so the methods it inherits from its base classes have to
            // be folded in. QOperatingSystemVersion's major/minor/micro accessors live in
            // QOperatingSystemVersionBase: as a class they arrived through the base chain, as a
            // struct they would otherwise disappear. Own members win over an inherited overload.
            string signature = n + "(" + string.Join(",", Params(m).Select(PType)) + ")";
            if (!signatures.Add(signature))
                return;
            string cs = MemberName(name, n);
            if (written.Contains(cs))
                return;
            seen.TryGetValue(n, out int count);
            seen[n] = count + 1;
            members.Add((m, n + (count == 0 ? "" : (count + 1).ToString()), cs));
        }
        foreach (var m in ms.EnumerateArray())
            Add(m);
        foreach (var baseCls in BaseChainOf(cls.Value))
            if (baseCls.TryGetProperty("methods", out var baseMethods))
                foreach (var m in baseMethods.EnumerateArray())
                    Add(m);
        return members;
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

    static void WriteStructMembers(StringBuilder sb, string name)
    {
        var members = StructMembers(name);
        var ctors = StructCtors(name);
        var written = HandWrittenMembers(name);
        bool handWrittenCtor = written.Contains(name);
        bool hasVariant = StructHasVariant(name);
        // A type whose whole surface is hand-written still gets its constructors generated (the last
        // ones not to be), so the early return has to account for them.
        if (!hasVariant && members.Count == 0 && (handWrittenCtor || ctors.Count == 0))
            return;
        var row = ApiTypes.RuntimeStructOf(name).Value;
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>{name}: {string.Join(", ", row.Types)} fields, Qt's own members forwarded through them.</summary>");
        sb.AppendLine($"    [GeneratedCode(\"qtgen\", \"{GeneratedByVersion}\")]");
        string partial = handWrittenParts.Contains(name) ? "partial " : "";
        sb.AppendLine($"    public {partial}struct {name} : IEquatable<{name}>");
        sb.AppendLine("    {");
        // The storage. The field names are the model's, and their order is the POD's, which is Qt's
        // memory order - that is the whole of the C# side of a value type.
        // The field's C# spelling, not the model's: "long long" is `long` over here (QDate's
        // julian day), and a pointer field is IntPtr, while the POD keeps the C++ spelling it needs.
        for (int i = 0; i < row.Fields.Length; i++)
            sb.AppendLine($"        {ManagedStructFieldType(row.Types[i])} _{row.Fields[i]};");
        sb.AppendLine();
        // Equality and hashing are Qt's, through the generated glue: operator== for the types that
        // declare one, qHash where it exists, and a field-wise or byte-wise fallback otherwise.
        // ToString is C#'s, and Qt has no spelling for it at all.
        if (!written.Contains("Equals"))
        {
            string show = string.Join(", ", row.Fields.Select(f => $"{{_{f}}}"));
            sb.AppendLine($"        public static bool operator ==({name} a, {name} b) => ApiNative.{name}_valueEq(a, b);");
            sb.AppendLine($"        public static bool operator !=({name} a, {name} b) => !(a == b);");
            sb.AppendLine($"        public bool Equals({name} other) => ApiNative.{name}_valueEq(this, other);");
            sb.AppendLine($"        public override bool Equals(object obj) => obj is {name} other && Equals(other);");
            sb.AppendLine($"        public override int GetHashCode() => ApiNative.{name}_valueHash(this);");
            sb.AppendLine($"        public override string ToString() => FormattableString.Invariant($\"{name}({show})\");");
            sb.AppendLine();
        }
        // The comparisons Qt declares (`operator==` and the ordering operators are functions in the
        // model): forwarded, so the answer is Qt's.
        foreach (var op in StructComparisons(name))
        {
            string symbol = ComparisonSymbol(Name(op));
            sb.AppendLine($"        public static bool operator {symbol}({name} a, {name} b) => ApiNative.{name}_{ComparisonEntry(op)}(a, b);");
        }
        if (StructComparisons(name).Count > 0)
            sb.AppendLine();
        // The nested types Qt declares on it (QLineF::IntersectionType): the class that would have
        // carried them is not generated, so the struct does.
        WriteEnums(sb, name, 2);
        // Constructors, forwarded like every other member: the glue builds Qt's own value from the
        // arguments and hands back its bytes, so no formula is translated by hand - QRect's
        // (x, y, width, height) becoming two corners is Qt's rule, not a copy of it.
        if (!handWrittenCtor)
        {
            int n = 0;
            foreach (var ctor in ctors)
            {
                var cps = Params(ctor);
                // The arguments go over the same way every other call's do: an enum as its int, a
                // bound class as its handle, a nested value type as itself. Passing the managed
                // spelling straight through compiled only while every argument happened to be a
                // scalar (QDate's QCalendar overload and QVector's Qt::Initialization did not).
                var marshal = MarshalParams(cps, null);
                sb.AppendLine($"        public {name}({string.Join(", ", cps.Select(ParamSig))})");
                sb.AppendLine("        {");
                foreach (var line in marshal.Prelude)
                    sb.AppendLine("            " + line);
                sb.AppendLine($"            this = ApiNative.{name}_ctor{n++}({string.Join(", ", marshal.Args)});");
                foreach (var line in marshal.Cleanup)
                    sb.AppendLine("            " + line);
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }
        foreach (var (m, entry, cs) in members)
        {
            var ps = Params(m);
            string ret = ReturnType(m);
            string staticMod = IsStatic(m) ? "static " : "";
            // A member that mutates in place hands over its own storage by reference: the value
            // cannot be changed through a copy, and C# lets a struct method pass `ref this`.
            string self = IsStatic(m) ? null : MutatesInPlace(name, m, ret) ? "ref this" : "this";
            sb.AppendLine($"        public {staticMod}{ManagedRetType(ret)} {cs}({string.Join(", ", ps.Select(ParamSig))})");
            sb.AppendLine("        {");
            var marshal = MarshalParams(ps, self);
            foreach (string line in marshal.Prelude)
                sb.AppendLine("            " + line);
            string call = $"ApiNative.{name}_{entry}({string.Join(", ", marshal.Args)})";
            string body = ret == "void" ? call + ";" : "return " + RetToManaged(ret, call) + ";";
            sb.AppendLine("            " + body);
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine($"    internal static partial class ApiNative");
        sb.AppendLine("    {");
        {
            int n = 0;
            foreach (var ctor in ctors)
            {
                if (handWrittenCtor)
                    break;
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_ctor{n}\", CallingConvention = CallingConvention.Cdecl)]");
                sb.AppendLine($"        internal static extern {name} {name}_ctor{n}({string.Join(", ", Params(ctor).SelectMany(NativeParamsFor))});");
                n++;
            }
        }
        foreach (var op in StructComparisons(name))
        {
            string cmp = ComparisonEntry(op);
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_cmp_{cmp}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine("        [return: MarshalAs(UnmanagedType.I1)]");
            sb.AppendLine($"        internal static extern bool {name}_cmp_{cmp}({name} a, {name} b);");
        }
        foreach (var (m, entry, cs) in members)
        {
            var ps = Params(m);
            var nativeParams = new List<string>();
            // The value crosses as itself: the managed struct and the POD are the same bytes. A
            // mutating one crosses by reference, so the write-back reaches the caller's storage.
            if (!IsStatic(m))
                nativeParams.Add(MutatesInPlace(name, m, ReturnType(m)) ? $"ref {name} self" : $"{name} self");
            nativeParams.AddRange(ps.SelectMany(NativeParamsFor));
            string ret = ReturnType(m);
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_{entry}\", CallingConvention = CallingConvention.Cdecl)]");
            if (ret == "bool")
                sb.AppendLine("        [return: MarshalAs(UnmanagedType.I1)]");
            sb.AppendLine($"        internal static extern {NativeRetType(ret)} {name}_{entry}({string.Join(", ", nativeParams)});");
        }
        if (hasVariant)
        {
            // The QVariant bridge's two halves for a struct: the glue builds Qt's value from the
            // POD and runs the conversion operator, and reads it back out of a variant.
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_toQVariant\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr {name}_toQVariant({name} value);");
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_fromQVariant\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern {name} {name}_fromQVariant(IntPtr variant);");
        }
        {
            // Equality and hashing are Qt's for every struct (see WriteStructNative); the trait
            // dispatch lives in QtSharpApiRuntime.h.
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_valueEq\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine("        [return: MarshalAs(UnmanagedType.I1)]");
            sb.AppendLine($"        internal static extern bool {name}_valueEq({name} a, {name} b);");
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_valueHash\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern int {name}_valueHash({name} value);");
        }
        sb.AppendLine("    }");
    }

    /// <summary>
    /// Whether a member changes the value it is called on (QSize::transpose, QRect::adjust). Qt
    /// spells those as a void instance method, and that is the only shape it uses for them.
    /// </summary>
    static bool MutatesInPlace(string name, JsonElement m, string ret)
        // Qt's own mutators either return void (QSize::transpose) or return *this by reference for
        // chaining (QTransform::scale/translate/rotate/shear, QMatrix4x4's). A struct has no handle
        // behind which the mutation could happen, so both shapes have to write back through `ref
        // this` - the old class wrapper hid the second case.
        => !IsStatic(m) && !IsConstMethod(m) && (ret == "void" || ret == name);

    static bool IsConstMethod(JsonElement m)
        => HasMethod(m, ApiModel.MethodFlags.Const);

    static void WriteStructNative(StringBuilder sb, string name)
    {
        if (!HandWrittenMembers(name).Contains(name))
        {
            int n = 0;
            foreach (var ctor in StructCtors(name))
            {
                var cps = Params(ctor);
                sb.AppendLine($"extern \"C\" QS_API_EXPORT {StructPod(name)} qs_api_{name}_ctor{n}({string.Join(", ", cps.SelectMany(CppParamsFor))})");
                sb.AppendLine("{");
                // A parameterless constructor keeps its own line: `QCalendar qs_new();` would
                // declare a function instead of building the value.
                sb.AppendLine(cps.Count == 0
                    ? $"    {ApiTypes.CppStructName(name)} qs_new;"
                    : $"    {ApiTypes.CppStructName(name)} qs_new({string.Join(", ", cps.SelectMany(CppArgsFor))});");
                sb.AppendLine($"    return {StructToPod(name, "qs_new")};");
                sb.AppendLine("}");
                sb.AppendLine();
                n++;
            }
        }
        foreach (var op in StructComparisons(name))
        {
            string cmp = ComparisonEntry(op);
            sb.AppendLine($"extern \"C\" QS_API_EXPORT bool qs_api_{name}_cmp_{cmp}({StructPod(name)} a, {StructPod(name)} b)");
            sb.AppendLine("{");
            sb.AppendLine($"    return {StructFromPod(name, "a")} {ComparisonSymbol(Name(op))} {StructFromPod(name, "b")};");
            sb.AppendLine("}");
            sb.AppendLine();
        }
        {
            // Equality and hashing belong to Qt. The glue asks the traits in QtSharpApiRuntime.h:
            // operator== when the type declares one, otherwise the POD bytes; qHash when the type
            // declares one, otherwise a generated field-wise hash (or, for a type whose opaque
            // words are all there is, the bytes). The few types whose hash Qt cannot provide or
            // whose operator== ignores part of the value keep an expression in NativeHashStructs.
            string pod = StructPod(name);
            string cpp = ApiTypes.CppStructName(name);
            var row = ApiTypes.RuntimeStructOf(name).Value;
            sb.AppendLine($"extern \"C\" QS_API_EXPORT bool qs_api_{name}_valueEq({pod} lhs, {pod} rhs)");
            sb.AppendLine("{");
            sb.AppendLine($"    return QsApiValueEqual<{cpp}>(lhs, rhs);");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"extern \"C\" QS_API_EXPORT int qs_api_{name}_valueHash({pod} value)");
            sb.AppendLine("{");
            if (ApiTypes.NativeHashStructs.TryGetValue(name, out var hash))
            {
                sb.AppendLine($"    {cpp} qs_value = QsApiRawFromPod<{cpp}>(value);");
                sb.AppendLine($"    return int({hash.Replace("@v", "qs_value")});");
            }
            else if (row.FromPod == null && row.Fields.Length > 0)
            {
                // A field-shaped value: hash the same accessors Qt's operator== compares. The
                // fallback receives the POD (whose padding ToPod normalises) and rebuilds Qt's
                // value for the accessors; hashing the value the template already built would
                // hash that copy's uninitialised padding.
                string fields = string.Join(", ", row.Fields.Select(f => $"qs_value.{f}()"));
                sb.AppendLine($"    return int(QsApiValueHash<{cpp}>(value, [](const {pod}& qs_pod) {{ {cpp} qs_value = QsApiRawFromPod<{cpp}>(qs_pod); return qHashMulti(0, {fields}); }}));");
            }
            else
            {
                // Opaque words with no qHash: the POD's bytes are the value (ToPod normalises any
                // padding the type has). Hashing the reconstructed Qt value would hash padding
                // the conversion does not initialise.
                sb.AppendLine($"    return int(QsApiValueHash<{cpp}>(value, [](const {pod}& qs_pod) {{ return qHashBits(&qs_pod, sizeof qs_pod); }}));");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }
        if (StructHasVariant(name))
        {
            // The conversion operator is the only way into a QVariant for a trivially copyable
            // QtGui type: qvariant.h has no constructor for it.
            string pod = StructPod(name);
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{name}_toQVariant({pod} qs_pod)");
            sb.AppendLine("{");
            sb.AppendLine($"    return new QVariant(static_cast<QVariant>({StructFromPod(name, "qs_pod")}));");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {pod} qs_api_{name}_fromQVariant(const void* variant)");
            sb.AppendLine("{");
            sb.AppendLine($"    {ApiTypes.CppStructName(name)} qs_value = static_cast<const QVariant*>(variant)->value<{ApiTypes.CppStructName(name)}>();");
            sb.AppendLine($"    return {StructToPod(name, "qs_value")};");
            sb.AppendLine("}");
            sb.AppendLine();
        }
        foreach (var (m, entry, cs) in StructMembers(name))
        {
            var ps = Params(m);
            string ret = ReturnType(m);
            bool mutates = MutatesInPlace(name, m, ret);
            string pod = StructPod(name);
            var nativeParams = new List<string>();
            if (!IsStatic(m))
                nativeParams.Add(mutates ? $"{pod}* qs_pod" : $"{pod} qs_pod");
            nativeParams.AddRange(ps.SelectMany(CppParamsFor));
            string args = string.Join(", ", ps.SelectMany(CppArgsFor));
            string call = IsStatic(m) ? $"{ApiTypes.CppStructName(name)}::{Name(m)}({args})" : $"qs_self.{Name(m)}({args})";
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {NativeRetCpp(ret)} qs_api_{name}_{entry}({string.Join(", ", nativeParams)})");
            sb.AppendLine("{");
            if (mutates)
            {
                // The call changes qs_self in place, and the caller's storage has to see that; a
                // value-returning mutator (QTransform::scale returns *this for chaining) also hands
                // the result back, so the write-back must happen before the return statement.
                sb.AppendLine($"    {ApiTypes.CppStructName(name)} qs_self = {StructFromPod(name, "(*qs_pod)")};");
                if (ret == "void")
                {
                    foreach (string line in NativeBody(m, call))
                        sb.AppendLine("    " + line);
                    sb.AppendLine($"    *qs_pod = {StructToPod(name, "qs_self")};");
                }
                else
                {
                    sb.AppendLine($"    auto qs_result = {call};");
                    sb.AppendLine($"    *qs_pod = {StructToPod(name, "qs_self")};");
                    sb.AppendLine("    " + NativeRetExpr(ret, "qs_result"));
                }
            }
            else
            {
                if (!IsStatic(m))
                    sb.AppendLine($"    {ApiTypes.CppStructName(name)} qs_self = {StructFromPod(name, "qs_pod")};");
                foreach (string line in NativeBody(m, call))
                    sb.AppendLine("    " + line);
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    static void WriteClass(StringBuilder sb, JsonElement cls)
    {
        string name = Name(cls);
        bool boxed = HasClass(cls, ApiModel.ClassFlags.Owned);
        var bases = cls.GetProperty("bases").EnumerateArray().Select(b => b.GetString()).ToList();
        // The model records a Qt3D base by its qualified C++ name; C# names the class by its
        // simple one, and the inherited-member analysis resolves either.
        string baseRef = bases.Count > 0 ? bases[0] : null;
        string baseName = baseRef != null ? CsTypeName(baseRef) : null;
        var extraBases = bases.Skip(1).ToList();
        var ctors = PublishedCtors(cls);
        var methods = Methods(cls).ToList();
        var virtuals = ChainOf(Name(cls));
        var signals = cls.GetProperty("signals").EnumerateArray().ToList();
        // The members the base chain already publishes: a member of this class that collides has
        // to say `new`, or the compiler warns that it hides the inherited one (CS0108).
        var inherited = baseRef != null ? InheritedMembers(baseRef) : new Members();

        // A class we allocate ourselves needs a release path; Qt-owned objects must not get
        // one, or the GC would free an object the parent still owns (ownership PoC rule 1).
        bool owned = boxed && (ctors.Count > 0 || SubclassCtor(cls));
        var declared = new List<string>();
        if (baseName != null)
            declared.Add(baseName);
        if (owned)
            declared.Add("IDisposable");
        // Only the root class implements them; every descendant inherits the interface and the
        // invalidation flag with it. IQtHandle is what lets a C#-declared signal/property carry a
        // bound class without the runtime knowing the type at compile time.
        if (baseName == null)
        {
            declared.Add("IQtObjectView");
            // QObject is the one root with a meta-object, so it is what the runtime's
            // [QtSignal]/[QtProperty] support can attach to. The other roots (QGraphicsItem,
            // QModelIndex) are plain wrappers and only carry a handle.
            declared.Add(name == "QObject" ? "IQtObject" : "IQtHandle");
        }
        // A value type Qt itself can convert into a QVariant carries the marker the runtime's
        // QVariant dispatch looks for. It is an interface, not a constructor: C# keeps the
        // conversion in QVariant(object), where QMetaType decides how to copy the value.
        if (ConvertsToVariant(cls))
            declared.Add("IQtVariantValue");
        string ifaces = declared.Count > 0 ? " : " + string.Join(", ", declared) : "";

        sb.AppendLine($"    [GeneratedCode(\"qtgen\", \"{GeneratedByVersion}\")]");
        string partial = handWrittenParts.Contains(name) ? "partial " : "";
        // QVariant is the bridge, not API: every signature says object, the type table and the
        // marshaller live in this assembly, and nothing outside it should name the class.
        string visibility = name == "QVariant" ? "internal" : "public";
        sb.AppendLine($"    {visibility} {partial}class {name}{ifaces}");
        sb.AppendLine("    {");
        if (ConvertsToVariant(cls))
        {
            // The C++ type name is what QMetaType::type() resolves; the managed name can differ
            // (Qt3D's namespaced classes), so the model's cppName is the one to publish.
            sb.AppendLine($"        string IQtVariantValue.QtTypeName => {Quoted(CppClass(name))};");
            sb.AppendLine();
        }
        if (baseName == null)
        {
            // The handle is behind a property so that a wrapper whose native object Qt destroyed
            // (or that was disposed) fails loudly instead of calling into freed memory - the same
            // contract PySide has, where a deleted C++ object raises RuntimeError.
            // internal, not private: a derived class's Dispose works on them (a private field is
            // inherited but not reachable).
            // protected internal, not internal: a class generated into a *separate* extension
            // assembly (QtSharp.Charts) derives from this one and needs both fields. Widening is
            // enough - the meaning and the behaviour are unchanged.
            sb.AppendLine("        protected internal IntPtr __handle;");
            sb.AppendLine("        protected internal bool __invalidated;");
            sb.AppendLine();
            sb.AppendLine("        public IntPtr Handle");
            sb.AppendLine("        {");
            sb.AppendLine("            get");
            sb.AppendLine("            {");
            sb.AppendLine("                if (__invalidated)");
            sb.AppendLine("                    throw new ObjectDisposedException(GetType().Name,");
            sb.AppendLine("                        \"The Qt object behind this wrapper has been destroyed, or the wrapper was disposed.\");");
            sb.AppendLine("                return __handle;");
            sb.AppendLine("            }");
            sb.AppendLine("            protected set => __handle = value;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        void IQtObjectView.MarkDestroyed() => __invalidated = true;");
            // The identity cache asks this before handing a cached wrapper back: one Qt already
            // invalidated no longer stands for anything, even while it is still reachable.
            sb.AppendLine("        bool IQtObjectView.IsInvalidated => __invalidated;");
            sb.AppendLine();
            // Whether the Qt object still exists. Qt destroys what it owns (a parent, a layout, a
            // scene, a view) at any moment, and a disposed wrapper is spent, so this is the
            // sip.isValid() of the binding - and the question `== null` answers below.
            sb.AppendLine("        /// <summary>Whether the Qt object behind this wrapper still exists.</summary>");
            sb.AppendLine("        public bool IsAlive => __handle != IntPtr.Zero && !__invalidated;");
            sb.AppendLine();
            // A destroyed object reads as null, the way a destroyed UnityEngine.Object does, so
            // `if (w != null) w.Use();` is the guard that keeps it from throwing. Only the operator
            // is liveness-aware: `w?.Use()`, `w ?? fallback` and `w is null` test the reference
            // itself, exactly as they do in Unity.
            sb.AppendLine("        /// <summary>True when both sides are null or destroyed; reference equality otherwise.</summary>");
            sb.AppendLine($"        public static bool operator ==({name} left, {name} right)");
            sb.AppendLine("        {");
            sb.AppendLine("            bool leftGone = left is null || left.__invalidated;");
            sb.AppendLine("            bool rightGone = right is null || right.__invalidated;");
            sb.AppendLine("            return leftGone || rightGone ? leftGone && rightGone : ReferenceEquals(left, right);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        public static bool operator !=({name} left, {name} right) => !(left == right);");
            sb.AppendLine();
            // Whether this wrapper is the one that has to release the native object. Only a
            // constructor we published, a boxed copy or an owned method return Qt handed over
            // (FromOwnedHandle) sets it: a wrapper that merely adopts an object
            // (FromHandle/FromTransientHandle - the objects a view or a scene owns) must never
            // delete it at finalization, or the GC frees what Qt still points at.
            //
            // An owned return can also be parented (a conditional return such as a window
            // container with a parent): the model then leaves it borrowed, and even a wrapper
            // that does own has the destructor's parent() guard in front of the delete.
            //
            // Always declared: a root class's derived classes may live in *another* module's
            // assembly (QEvent is QtCore's, QGestureEvent is QtWidgets'), and they assign the
            // ownership flags the root's constructors/Dispose define. The file disables CS0649 for
            // a root whose own subtree never builds anything.
            {
                sb.AppendLine("        protected internal bool __ownsNative;");
                sb.AppendLine();
            }
            // Whether the native object is one of the generated subclasses that install the vtable
            // hooks - i.e. whether this wrapper was *made* rather than adopted. Only a constructor
            // sets it. A pure virtual's C# stub answers `default` for a managed subclass (nothing
            // to chain to), but calls the real C++ virtual for an adopted object: Qt's own models
            // (QTableWidget's, QComboBox's, ...) are private C++ classes that are not part of this
            // model, so without this their index()/data()/setData()/rowCount() would answer
            // null/0/false and the model would be silently unreachable.
            // Same reasoning as __ownsNative: a derived class in another module sets it.
            sb.AppendLine("        protected internal bool __managedSubclass;");
            sb.AppendLine();
        }

        // wrapping constructor (never allocates: it adopts an object someone else owns)
        sb.AppendLine($"        protected {name}(IntPtr handle){(baseName != null ? " : base(handle)" : "")}");
        sb.AppendLine("        {");
        if (baseName == null)
        {
            sb.AppendLine("            Handle = handle;");
            // Qt owns the object and will destroy it: the wrapper has to learn about that.
            if (DerivesFromQObject(Ref(cls)))
                sb.AppendLine("            ApiRuntime.Watch(handle);");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        int ctorIndex = 0;
        foreach (var ctor in ctors)
        {
            var ps = Params(ctor);
            string sig = string.Join(", ", ps.Select(ParamSig));
            string dispatch = virtuals.Count > 0
                ? "(IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch"
                : "IntPtr.Zero";
            // Only the constructor that takes the dispatch function pointer needs `unsafe`; the
            // type itself does not carry the modifier.
            string unsafeModifier = dispatch == "IntPtr.Zero" ? "" : "unsafe ";
            // The native subclass is concrete, but the abstract Qt class itself is not: its
            // constructors exist for a managed subclass, so they are protected in C#.
            bool abstractClass = HasClass(cls, ApiModel.ClassFlags.Abstract);
            string access = abstractClass ? "protected" : "public";
            sb.AppendLine($"        {access} {unsafeModifier}{name}({sig}){(baseName != null ? " : base(IntPtr.Zero)" : "")}");
            sb.AppendLine("        {");
            // A class with no virtuals at all needs no context: the native side reads this pointer
            // for one thing only - dispatching a virtual into the managed object - and this one has
            // none, so it would never be read. Passing one anyway costs the object its life: a
            // GCHandle is a strong root, so the wrapper stays reachable, the finalizer that would
            // dispose it never runs, and the native object behind it is never destroyed either.
            // (A class C# *can* derive from, and whose object is derived, still needs it - that is
            // what the subclass constructor below is for.)
            if (dispatch != "IntPtr.Zero")
                sb.AppendLine("            var __context = GCHandle.Alloc(this);");
            var marshal = MarshalParams(ps, null);
            foreach (string line in marshal.Prelude)
                sb.AppendLine("            " + line);
            string context = dispatch == "IntPtr.Zero" ? "IntPtr.Zero" : "GCHandle.ToIntPtr(__context)";
            string args = string.Join(", ", marshal.Args);
            string create = $"Handle = ApiNative.Ctor_{name}{CtorSuffix(ctorIndex++)}({context}, {dispatch}{(args.Length > 0 ? ", " + args : "")});";
            // This wrapper made the object, so it is the one that may release it. A QObject can
            // still be destroyed by Qt later (it gets reparented), so it is watched as well.
            string register = "            ApiRuntime.Register(Handle, this);\n            __ownsNative = true;\n            __managedSubclass = true;";
            if (DerivesFromQObject(Ref(cls)))
            {
                register += "\n            ApiRuntime.Watch(Handle);";
                // The signals and properties the *C#* class declared with [QtSignal]/[QtProperty]:
                // the object's meta-object gets them before any connection can be made.
                register += "\n            ApiRuntime.AttachMetaObject(this);";
            }
            EmitGuarded(sb, "            ", create + "\n" + register, marshal.Cleanup);
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // Q_PROPERTY declarations, as C# properties. These are pure sugar: the body calls the
        // accessor methods that are generated anyway, so no native glue is duplicated and the
        // accessors keep working under their own names. A property whose name a virtual also wants
        // keeps it; the virtual is emitted under its C++ spelling (see MemberName).
        foreach (var property in PropertyList(cls))
        {
            string propName = Pascal(Name(property));
            if (hardReservedByClass[name].Contains(propName) || fieldNamesByClass[name].Contains(propName))
                continue;
            string propType = property.GetProperty("type").GetString();
            string getter = property.GetProperty("getter").GetString();
            string setter = property.TryGetProperty("setter", out var s) ? s.GetString() : null;
            // The Qt name and type are what a reader cannot see from the C# signature (the name is
            // camelCase, the type is the C++ spelling); the accessors are right below.
            string qtName = System.Security.SecurityElement.Escape(Name(property));
            string qtPropType = System.Security.SecurityElement.Escape(propType);
            string access = setter != null ? "read/write" : "read-only";
            sb.AppendLine($"        /// <summary>The <c>{qtName}</c> property (Q_PROPERTY, <c>{qtPropType}</c>, {access}).</summary>");
            sb.AppendLine($"        [QtNativeProperty({CsString(Name(property))}, {CsString(propType)}, {(setter != null ? "true" : "false")})]");
            sb.AppendLine($"        public {NewName(inherited, propName)}{ManagedType(propType)} {propName}");
            sb.AppendLine("        {");
            sb.AppendLine($"            get => {CSharpMemberName(getter)}();");
            if (setter != null)
                sb.AppendLine($"            set => {CSharpMemberName(setter)}(value);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // Public data members, as properties. Qt's option/style structs are plain aggregates
        // (QStyleOption::rect, QJsonParseError::error) and a managed delegate cannot paint
        // without them.
        foreach (var field in FieldList(cls))
        {
            string fieldName = Name(field);
            string fieldType = field.GetProperty("type").GetString();
            string managed = ManagedType(fieldType);
            // A property is always PascalCase; MemberName's "keep the C++ spelling when the
            // Pascal one is taken" rule is for methods, and a data member named `checked` would
            // otherwise be published under a C# keyword.
            string prop = SafeName(Pascal(fieldName));
            string read = ElemRead(fieldType, $"ApiNative.{name}_get_{fieldName}(Handle)");
            string write = $"ApiNative.{name}_set_{fieldName}(Handle, {ElemArg(fieldType, "value")})";
            if (hardReservedByClass[name].Contains(prop))
            {
                // A field whose name is taken by a nested type (QStyleOptionHeader::selectedPosition
                // next to the SelectedPosition enum) stays reachable through accessors instead.
                sb.AppendLine($"        /// <summary>Data member {name}::{fieldName}.</summary>");
                sb.AppendLine($"        public {NewMethod(inherited, "Get" + prop, "")}{managed} Get{prop}() => {read};");
                if (IsQVariantType(fieldType))
                {
                    // The managed value is object; the setter needs a variant to hand over, and it
                    // is disposed as soon as the native copy is made.
                    sb.AppendLine($"        public {NewMethod(inherited, "Set" + prop, managed)}void Set{prop}({managed} value)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            using (var __variant = {VariantMarshalling}.Value(value))");
                    sb.AppendLine($"                ApiNative.{name}_set_{fieldName}(Handle, __variant.Handle);");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine($"        public {NewMethod(inherited, "Set" + prop, managed)}void Set{prop}({managed} value) => {write};");
                }
                sb.AppendLine();
                continue;
            }
            sb.AppendLine($"        /// <summary>Data member {name}::{fieldName}.</summary>");
            sb.AppendLine($"        public {NewName(inherited, prop)}{managed} {prop}");
            sb.AppendLine("        {");
            sb.AppendLine($"            get => {read};");
            if (IsQVariantType(fieldType))
            {
                sb.AppendLine("            set");
                sb.AppendLine("            {");
                sb.AppendLine($"                using (var __variant = {VariantMarshalling}.Value(value))");
                sb.AppendLine($"                    ApiNative.{name}_set_{fieldName}(Handle, __variant.Handle);");
                sb.AppendLine("            }");
            }
            else
            {
                sb.AppendLine($"            set => {write};");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        if (SubclassCtor(cls))
        {
            // The abstract class has no constructor of its own in the model, so this one exists
            // purely so a managed subclass can build the native subclass that installs the vtable
            // hooks. It is protected on purpose: an abstract Qt class is not something to
            // instantiate directly (that would leave its pure virtuals unimplemented).
            string dispatch = virtuals.Count > 0
                ? "(IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch"
                : "IntPtr.Zero";
            string unsafeModifier = dispatch == "IntPtr.Zero" ? "" : "unsafe ";
            sb.AppendLine($"        /// <summary>Builds the native subclass a managed subclass of this abstract class dispatches through.</summary>");
            sb.AppendLine($"        protected {unsafeModifier}{name}(){(baseName != null ? " : base(IntPtr.Zero)" : "")}");
            sb.AppendLine("        {");
            sb.AppendLine("            var __context = GCHandle.Alloc(this);");
            sb.AppendLine($"            Handle = ApiNative.Ctor_{name}(GCHandle.ToIntPtr(__context), {dispatch});");
            sb.AppendLine("            ApiRuntime.Register(Handle, this);");
            sb.AppendLine("            __ownsNative = true;");
            sb.AppendLine("            __managedSubclass = true;");
            sb.AppendLine("            ApiRuntime.AttachMetaObject(this);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // The factories the actual-type registry calls. They live inside the class because the
        // wrapping constructor is protected and __ownsNative is a protected member: nothing outside
        // the class may set the ownership flag. All three are emitted for every QObject-derived
        // class - not only the ones that dispose themselves - because the object's actual class can
        // be any class below this one in the hierarchy, and that is the class whose flag matters.
        bool qobjectClass = DerivesFromQObject(Ref(cls));
        if (qobjectClass)
        {
            sb.AppendLine($"        internal static QtSharp.QtCore.QObject __WrapBorrowed_{name}(IntPtr handle)");
            sb.AppendLine($"            => ApiRuntime.Wrap(handle, static h => new {name}(h));");
            sb.AppendLine();
            sb.AppendLine($"        internal static QtSharp.QtCore.QObject __WrapTransient_{name}(IntPtr handle)");
            sb.AppendLine($"            => new {name}(handle);");
            sb.AppendLine();
            sb.AppendLine($"        internal static QtSharp.QtCore.QObject __WrapOwned_{name}(IntPtr handle)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var wrapper = new {name}(handle);");
            sb.AppendLine("            wrapper.__ownsNative = true;");
            sb.AppendLine("            return wrapper;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // A QObject returned as a base type (parent(), window(), a QList<QObject*> element) is
        // created as the object's actual bound class: the native meta-object is read once, when the
        // wrapper is made. That is what makes `is`/`as` work on a QObject*-typed value. The declared
        // class is still the fallback when the object is an internal Qt class (QComboBox's view) or
        // its module is not loaded.
        sb.AppendLine($"        public {NewMethod(inherited, "FromHandle", "IntPtr")}static {name} FromHandle(IntPtr handle)");
        sb.AppendLine(qobjectClass
            ? $"            => ({name})QtSharp.QtCore.QObjectTypeRegistry.Wrap(handle, typeof({name}), __WrapBorrowed_{name});"
            : $"            => handle == IntPtr.Zero ? null : ApiRuntime.Wrap(handle, h => new {name}(h));");
        // Arguments handed to a virtual come from an object with a very short life (an event
        // lives on the caller's stack), so they must not enter the identity cache: the next
        // event would reuse the same address and get the previous wrapper back. A QObject is not
        // such an object, so it may return the wrapper the program already holds.
        sb.AppendLine();
            sb.AppendLine($"        public {NewMethod(inherited, "FromTransientHandle", "IntPtr")}static {name} FromTransientHandle(IntPtr handle)");
        sb.AppendLine(qobjectClass
            ? $"            => ({name})QtSharp.QtCore.QObjectTypeRegistry.WrapTransient(handle, typeof({name}), __WrapTransient_{name});"
            : $"            => handle == IntPtr.Zero ? null : new {name}(handle);");
        if (owned)
        {
            sb.AppendLine();
            // Qt handed over a copy it allocated for us, so this wrapper owns it. The identity cache
            // is deliberately not consulted: whoever takes ownership must be the only wrapper that
            // can release the object, so an existing borrowed wrapper is never adopted here.
            sb.AppendLine($"        public {NewMethod(inherited, "FromOwnedHandle", "IntPtr")}static {name} FromOwnedHandle(IntPtr handle)");
            if (qobjectClass)
            {
                sb.AppendLine($"            => ({name})QtSharp.QtCore.QObjectTypeRegistry.WrapOwned(handle, typeof({name}), __WrapOwned_{name});");
            }
            else
            {
                sb.AppendLine("        {");
                sb.AppendLine("            if (handle == IntPtr.Zero)");
                sb.AppendLine("                return null;");
                sb.AppendLine($"            var wrapper = new {name}(handle);");
                sb.AppendLine("            wrapper.__ownsNative = true;");
                sb.AppendLine("            return wrapper;");
                sb.AppendLine("        }");
            }
            sb.AppendLine();
            sb.AppendLine($"        public {NewMethod(inherited, "Dispose", "")}void Dispose()");
            sb.AppendLine("        {");
            // The backing field, not the property: a wrapper whose object Qt already destroyed is
            // invalid, and the finalizer still has to run through here without throwing.
            sb.AppendLine("            if (__handle == IntPtr.Zero)");
            sb.AppendLine("                return;");
            // Nothing is released once the process is shutting down: deleting an object then
            // fires Qt's destroyed signal, and native code may not enter managed code any more.
            sb.AppendLine("            if (__ownsNative && !__invalidated && !ApiRuntime.ShuttingDown)");
            sb.AppendLine("            {");
            sb.AppendLine($"                ApiNative.Destroy_{name}(__handle);");
            sb.AppendLine("                __ownsNative = false;");
            sb.AppendLine("            }");
            // A disposed wrapper must leave the identity cache: the native address can be reused,
            // and then the cache would hand a dead wrapper to the next object that lands there.
            sb.AppendLine("            ApiRuntime.Forget(__handle, this);");
            sb.AppendLine("            __handle = IntPtr.Zero;");
            // Using a disposed wrapper fails loudly instead of calling native code with a null
            // handle (the same contract as an object Qt deleted).
            sb.AppendLine("            __invalidated = true;");
            sb.AppendLine("            GC.SuppressFinalize(this);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        ~{name}() => Dispose();");
        }
        sb.AppendLine();

        // A member cannot share a name with a nested type, so something has to give when Qt
        // declares both (QColor::Spec enum and QColor::spec()); see MemberName.
        string CSharpMemberName(string member) => MemberName(name, member);

        // Secondary bases live at a different address inside the object, so the raw handle
        // (which is the complete-object address) cannot simply be reinterpreted.
        foreach (var b in extraBases)
        {
            string baseCs = CsTypeName(b);
            string baseSimple = SimpleName(b);
            sb.AppendLine($"        /// <summary>View of this object as its {baseSimple} base (a separate subobject).</summary>");
            sb.AppendLine($"        public {NewMethod(inherited, "As" + baseSimple, "")}{baseCs} As{baseSimple}() => {baseCs}.FromHandle(ApiNative.{name}__as_{baseSimple}(Handle));");
            sb.AppendLine();
        }

        // methods
        var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in methods)
        {
            string mname = Name(m);
            overloadCounts.TryGetValue(mname, out int seen);
            overloadCounts[mname] = seen + 1;
            string entrySuffix = seen == 0 ? "" : (seen + 1).ToString();

            var ps = Params(m);
            string sig = string.Join(", ", ps.Select(ParamSig));
            string ret = ReturnType(m);
            string staticMod = IsStatic(m) ? "static " : "";
            // Qt's toString() *is* object.ToString(): spelled without `override` it hides the
            // base member, and then string interpolation and Console.WriteLine print the type
            // name instead of the value (the C# call itself is unaffected, which makes it a
            // thoroughly confusing bug to chase).
            string overrideMod = !IsStatic(m) && mname == "toString" && ps.Count == 0 && ret == "QString"
                ? "override "
                : "";
            // A method that shadows an inherited member of the same signature (or an inherited
            // property/field/type) needs `new`; an override must not say both.
            string methodNew = overrideMod == "" ? NewMethod(inherited, CSharpMemberName(mname), ps) : "";
            // A method Qt itself registered in the meta-object: a Q_SLOTS/Q_SLOT slot or a
            // Q_INVOKABLE method. The signature is the normalized Qt spelling of the model types
            // the DllImport is generated from (setText(QString), grab(QRect)), so
            // QMetaObject::indexOfMethod resolves the very entry moc created. The marker is
            // read-side only: the member already exists, and [QtSlot] is the declaration side a
            // C# subclass uses for a method of its own.
            if (HasMethod(m, ApiModel.MethodFlags.Slot) || HasMethod(m, ApiModel.MethodFlags.Invokable))
            {
                string qtSig = mname + "(" + string.Join(",", ps.Select(p => CppTypeName(PType(p)))) + ")";
                bool invokable = HasMethod(m, ApiModel.MethodFlags.Invokable);
                sb.AppendLine($"        [QtNativeSlot({CsString(qtSig)}{(invokable ? ", invokable: true" : "")})]");
            }
            sb.AppendLine($"        public {overrideMod}{methodNew}{staticMod}{ManagedRetType(ret)} {CSharpMemberName(mname)}({sig})");
            sb.AppendLine("        {");
            var marshal = MarshalParams(ps, IsStatic(m) ? null : "Handle");
            foreach (string line in marshal.Prelude)
                sb.AppendLine("            " + line);
            string call = $"ApiNative.{name}_{mname}{entrySuffix}({string.Join(", ", marshal.Args)})";
            // A call that hands an item over to Qt records the association, so the item's wrapper
            // dies with its owner.
            var transfer = OwnershipTransferOf(mname, ps, ret);
            // An owned return is wrapped with FromOwnedHandle, so Dispose deletes it. Conditional
            // ownership (ownedWhenNull) is the model's explicit predicate: the returned object is
            // only ours when that pointer parameter was null - CreateWindowContainer with a parent
            // hands back a child Qt owns, and claiming it would double-delete when the parent dies.
            // The predicate is read before the call, and an absent predicate means "always ours".
            string ownedParam = OwnedWhenNull(m);
            string retExpr = RetToManaged(ret, call);
            string ownedPrelude = null;
            if (ReturnsOwned(m))
            {
                OwnedRetToManaged(ret, call);      // validates the return has a release path
                if (ownedParam == null)
                {
                    retExpr = OwnedRetToManaged(ret, call);
                }
                else
                {
                    JsonElement predicate = default;
                    bool found = false;
                    foreach (var p in ps)
                        if (PName(p) == ownedParam) { predicate = p; found = true; break; }
                    if (!found || !IsClassPtr(PType(predicate)))
                        throw new InvalidOperationException(
                            $"{name}.{mname}: ownedWhenNull names '{ownedParam}', which is not a " +
                            "class-pointer parameter (the only nullable spelling the model has).");
                    ownedPrelude = $"bool __ownsReturn = {SafeName(ownedParam)} == null;\n"
                                 + $"var __ownedHandle = {call};";
                    retExpr = $"__ownedHandle == IntPtr.Zero ? null : (__ownsReturn ? "
                            + $"{OwnedRetToManaged(ret, "__ownedHandle")} : {RetToManaged(ret, "__ownedHandle")})";
                }
            }
            // A postlude has to run between the call and the return, so the return value is kept
            // in a local when there is one.
            string body;
            if (marshal.Postlude.Count == 0 && transfer.IsItem)
                body = $"var __item = {retExpr};\n"
                     + "ApiRuntime.AssociateOwner(Handle, __item?.Handle ?? IntPtr.Zero);\n"
                     + "return __item;";
            else if (marshal.Postlude.Count == 0 && transfer.IsArgument)
                body = $"{call};\nApiRuntime.AssociateOwner(Handle, {transfer.Argument});";
            else if (marshal.Postlude.Count == 0)
                body = ret == "void" ? $"{call};" : $"return {retExpr};";
            else
            {
                string writeBack = string.Join("\n", marshal.Postlude);
                body = ret == "void"
                    ? $"{call};\n{writeBack}"
                    : $"var __result = {retExpr};\n{writeBack}\nreturn __result;";
            }
            if (ownedPrelude != null)
                body = ownedPrelude + "\n" + body;
            EmitGuarded(sb, "            ", body, marshal.Cleanup);
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // virtuals a managed subclass may override. Only the ones this class declares become
        // C# virtuals; inherited slots are dispatched but already declared by their base. The
        // default implementation chains to the C++ base implementation: the native subclass
        // overrides the virtual for every instance we create, so a managed override that
        // ignores the base would silently disable Qt's own handling (QWidget::event
        // dispatches everything).
        var virtualOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in virtuals.Where(s => s.DeclaredHere))
        {
            var v = slot.Virtual;
            string vname = Name(v);
            virtualOverloads.TryGetValue(vname, out int vseen);
            virtualOverloads[vname] = vseen + 1;
            string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
            // A blocked slot gets no C# virtual at all (the suffix counter is advanced above so
            // that it stays aligned with the other three emitters).
            if (slot.Blocked)
                continue;

            var ps = Params(v);
            string sig = string.Join(", ", ps.Select(ParamSig));
            string ret = ReturnType(v);
            // A base class already published this exact signature, so C# has to *override* it: a
            // plain `virtual` would only hide the base member, and a call through a base-typed
            // reference - which is how Qt and most callers see the object - would run the base
            // implementation instead (for QAbstractItemModel's pure virtuals, "no data at all").
            bool overridesBase = baseRef != null && ChainOf(baseRef).Any(s =>
                VirtualKey(s.Virtual) == VirtualKey(v) && ManagedType(ReturnType(s.Virtual)) == ManagedType(ret));
            // A `virtual` that is not an override can still shadow an inherited member (`override`
            // never warns); mark it `new` in that case.
            // An override has to use the C# name the base's declaration received. QStyle keeps
            // pixelMetric() (its own PixelMetric enum reserves the property name); a subclass
            // inheriting that enum would otherwise emit PixelMetric() and override nothing. The
            // name is the base-most declarer's, all the way up the chain.
            string virtualCsName = overridesBase ? VirtualCsName(name, v) : CSharpMemberName(vname);
            string virtualNew = overridesBase ? "" : NewMethod(inherited, CSharpMemberName(vname), ps);
            sb.AppendLine($"        /// <summary>Virtual from {name}; override in a managed subclass.</summary>");
            sb.AppendLine($"        public {virtualNew}{(overridesBase ? "override" : "virtual")} {ManagedRetType(ret)} {virtualCsName}({sig})");
            sb.AppendLine("        {");
            // A wrapper of a *C++* object has to reach the object's own override, not the
            // declaring class's implementation: `view.model()` for a QTableWidget hands back a
            // private C++ model class that is not part of this model, so its C# type is exactly
            // this one and this stub is what runs - and `self->QAbstractItemModel::setData(...)`
            // would answer the base's "return false" instead of the table's own store, while
            // `index`/`data`/`rowCount` (pure there) would answer default and leave the model
            // silently unreachable. For an object we made, the native hooks are what call this
            // method, so it must NOT call the virtual back (that would recurse): a pure virtual
            // then answers default - which is what makes an abstract class subclassable - and a
            // non-pure one chains to the base implementation as before. `__managedSubclass` is
            // what tells the two apart (only the constructors that install the hooks set it).
            if (Subclassable(cls))
            {
                var forward = MarshalParams(ps, "Handle");
                sb.AppendLine("            if (!__managedSubclass)");
                sb.AppendLine("            {");
                foreach (string line in forward.Prelude)
                    sb.AppendLine("                " + line);
                string virtualCall = $"ApiNative.{name}_{vname}{vSuffix}_vt({string.Join(", ", forward.Args)})";
                // An in-out string comes back through an out slot, so the postlude that reads it
                // has to run before the wrapper returns (QValidator::fixup rewrites its input).
                string virtualBody;
                if (forward.Postlude.Count == 0)
                {
                    virtualBody = ret == "void"
                        ? $"{virtualCall}; return;"
                        : $"return {RetToManaged(ret, virtualCall)};";
                }
                else
                {
                    var lines = new List<string>();
                    lines.Add(ret == "void"
                        ? $"{virtualCall};"
                        : $"var __result = {RetToManaged(ret, virtualCall)};");
                    lines.AddRange(forward.Postlude);
                    lines.Add(ret == "void" ? "return;" : "return __result;");
                    virtualBody = string.Join("\n", lines);
                }
                EmitGuarded(sb, "                ", virtualBody, forward.Cleanup);
                sb.AppendLine("            }");
                sb.AppendLine("            else");
                sb.AppendLine("            {");
            }
            // The two halves are inside their own blocks, so the marshalling temporaries a
            // container parameter needs cannot collide with each other.
            string inner = Subclassable(cls) ? "                " : "            ";
            if (HasNoBase(v) || !Subclassable(cls))
            {
                // Nothing to chain to: Qt declared it pure or private, or the class has no
                // generated subclass to reach the vtable through. A managed subclass that does not
                // override this simply does nothing.
                sb.AppendLine(inner + "// no base implementation to chain to");
                if (ret != "void")
                    sb.AppendLine(inner + "return default;");
            }
            else
            {
                // The base call needs the same marshalling a normal call does: a container
                // parameter has to become a native container first.
                var baseMarshal = MarshalParams(ps, "Handle");
                foreach (string line in baseMarshal.Prelude)
                    sb.AppendLine(inner + line);
                string baseCall = $"ApiNative.{name}_{vname}__base{vSuffix}({string.Join(", ", baseMarshal.Args)})";
                string body;
                if (baseMarshal.Postlude.Count == 0)
                {
                    body = ret == "void" ? $"{baseCall};" : $"return {RetToManaged(ret, baseCall)};";
                }
                else
                {
                    var lines = new List<string>();
                    lines.Add(ret == "void"
                        ? $"{baseCall};"
                        : $"var __result = {RetToManaged(ret, baseCall)};");
                    lines.AddRange(baseMarshal.Postlude);
                    if (ret != "void")
                        lines.Add("return __result;");
                    body = string.Join("\n", lines);
                }
                EmitGuarded(sb, inner, body, baseMarshal.Cleanup);
            }
            if (Subclassable(cls))
                sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // signals
        var signalOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in signals)
        {
            string sname = Name(s);
            var ps = Params(s);
            var csTypes = ps.Select(p => ManagedType(PType(p))).ToList();
            // The arity classes stop somewhere, and a Qt signal with more than a handful of
            // parameters is not worth growing them for.
            if (csTypes.Count > MaxSignalArity)
                continue;
            // C# has no overloaded properties, so a signal Qt declares twice (QCompleter has both
            // activated(QString) and activated(QModelIndex)) gets the same 2,3,... suffix the
            // method overloads use.
            signalOverloads.TryGetValue(sname, out int sseen);
            signalOverloads[sname] = sseen + 1;
            string sSuffix = sseen == 0 ? "" : (sseen + 1).ToString();
            string signalType = csTypes.Count == 0
                ? "QSignal"
                : "QSignal<" + string.Join(", ", csTypes) + ">";
            string mocSig = sname + "(" + string.Join(",", ps.Select(SignalMocType)) + ")";
            string mocSigDoc = System.Security.SecurityElement.Escape(mocSig);
            // Connect: the runtime hands the raw argv over and each parameter is read with the
            // same per-type rules the virtual dispatch uses (a bound class pointer is a pointer
            // *slot*, a boxed value is the value itself, a container is Qt's and copied out).
            // The native forwarder already skipped the return slot (moc's argv[0]), so the
            // signal's first argument is argv[0] here - unlike the virtual-dispatch convention,
            // where the generated wrapper builds the whole array.
            //
            // The handler doubles as the connection key, and the closure forwards the context and
            // connection type: `signal.Connect(h)` uses the defaults, while the QtCore extension
            // overloads (QSignalConnections) pass a context QObject and Qt::ConnectionType through
            // to the same native connection.
            string handler = csTypes.Count == 0
                ? "h()"
                : "h(" + string.Join(", ", ps.Select((p, i) => SignalRead(PType(p), i))) + ")";
            sb.AppendLine($"        /// <summary>The <c>{mocSigDoc}</c> signal as a QSignal (`Connect`/`Emit`).</summary>");
            sb.AppendLine($"        [QtNativeSignal({CsString(mocSig)})]");
            sb.AppendLine($"        public {NewName(inherited, Pascal(sname) + sSuffix + "Signal")}{signalType} {Pascal(sname)}{sSuffix}Signal =>");
            sb.AppendLine($"            new {signalType}((h, context, type) => ApiRuntime.Connect(Handle, \"{mocSig}\", (argv, _) => {handler}, h, context?.Handle ?? IntPtr.Zero, type),");
            foreach (string line in EmitLambda(ps, mocSig))
                sb.AppendLine("                " + line);
            sb.AppendLine("                );");
            sb.AppendLine();

            // The same signal as a C# *event*, so the Qt spelling has its C# equivalent:
            //     button.Clicked += handler;    button.Clicked -= handler;
            // This is what a C# user reaches for, and the remove accessor really disconnects -
            // the runtime remembers the handler as the key of the connection, so a long-lived
            // object does not leak. The XxxSignal property stays: Emit lives there, and a shape
            // that never has to remember the key is often what a test wants.
            //
            // A signal whose Pascal name a property, field or nested type already took keeps the
            // QSignal only, the same way a colliding Q_PROPERTY is skipped (the event name is
            // checked against the same reserved set).
            string eventName = Pascal(sname) + sSuffix;
            if (reservedByClass.TryGetValue(name, out var reservedNames) && !reservedNames.Contains(eventName))
            {
                string delegateType = csTypes.Count == 0
                    ? "Action"
                    : "Action<" + string.Join(", ", csTypes) + ">";
                string readArgs = string.Join(", ", ps.Select((p, i) => SignalRead(PType(p), i)));
                string valueCall = csTypes.Count == 0 ? "value()" : "value(" + readArgs + ")";
                sb.AppendLine($"        /// <summary>The <c>{mocSigDoc}</c> signal as a C# event (`+=` handler, `-=` handler disconnects).</summary>");
                sb.AppendLine($"        [QtNativeSignal({CsString(mocSig)})]");
                sb.AppendLine($"        public {NewName(inherited, eventName)}event {delegateType} {eventName}");
                sb.AppendLine("        {");
                sb.AppendLine($"            add => ApiRuntime.Connect(Handle, \"{mocSig}\", (argv, _) => {valueCall}, value);");
                sb.AppendLine($"            remove => ApiRuntime.Disconnect(Handle, \"{mocSig}\", value);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }

        // native subclass forwarding the virtuals into Dispatch
        if (virtuals.Count > 0)
        {
            // Native calls this through a function pointer, so an exception that escapes it would
            // unwind into Qt's C++ frames and kill the process (see ReportCallbackException). Catch
            // it here and report it; the dispatch switch itself stays untouched, in DispatchCore.
            sb.AppendLine("        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
            string modifier = NewMethod(inherited, "Dispatch", "IntPtr,int,IntPtr");
            sb.AppendLine($"        internal {modifier}static void Dispatch(IntPtr userData, int methodId, IntPtr argv)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                DispatchCore(userData, methodId, argv);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception __exception)");
            sb.AppendLine("            {");
            sb.AppendLine("                ApiRuntime.ReportCallbackException(\"a virtual override\", __exception);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        static void DispatchCore(IntPtr userData, int methodId, IntPtr argv)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var target = ({name})GCHandle.FromIntPtr(userData).Target;");
            sb.AppendLine("            switch (methodId)");
            sb.AppendLine("            {");
            for (int i = 0; i < virtuals.Count; i++)
            {
                // A blocked slot has no native hook, so there is nothing to dispatch. The slot id
                // below stays the position in the chain, which keeps the numbering aligned.
                if (virtuals[i].Blocked)
                    continue;
                var v = virtuals[i].Virtual;
                var ps = Params(v);
                // The slot id is the position in the whole chain, and an inherited slot is
                // dispatched into the member the declaring class already published. A slot the
                // class redeclares but a base published first uses the base's C# spelling (an
                // override cannot rename the member), the same rule as the virtual emitter.
                string member = VirtualCsName(name, v);
                sb.AppendLine($"                case {i + 1}: // {Name(v)}");
                sb.AppendLine("                {");
                // A by-reference parameter crosses as the caller's storage address. The override
                // receives it as a C# ref/out and the result is copied back into that storage
                // once it returns - the argv convention has no other way to carry the value out.
                var prelude = new List<string>();
                var args = new List<string>();
                var postlude = new List<string>();
                for (int a = 0; a < ps.Count; a++)
                {
                    var p = ps[a];
                    int slot = a + 1;
                    if (IsByRef(p))
                    {
                        string valueType = RefValueType(PType(p));
                        string local = "__ref" + slot;
                        // A ref reads the value Qt passed; an out has nothing to read.
                        prelude.Add(IsOut(p)
                            ? $"{ManagedType(valueType)} {local};"
                            : $"var {local} = {DispatchRead(valueType, slot)};");
                        args.Add($"{(IsOut(p) ? "out" : "ref")} {local}");
                        postlude.Add(DispatchStore(valueType, local, slot));
                        continue;
                    }
                    // A `const T *items, int count` pair: the managed override gets an array of
                    // the value type. The count is the next parameter (PublishesAsVirtual only
                    // accepts that shape).
                    if (IsStructPtr(PType(p)) && !IsOpaqueStructPtr(p))
                    {
                        string count = a + 1 < ps.Count
                            ? $"Marshal.ReadInt32(Marshal.ReadIntPtr(argv, {slot + 1} * IntPtr.Size))"
                            : "0";
                        string elem = RefValueType(PType(p));
                        args.Add($"ApiRuntime.ReadArray<{CsTypeName(elem)}>(Marshal.ReadIntPtr(argv, {slot} * IntPtr.Size), {count})");
                        continue;
                    }
                    args.Add(DispatchRead(PType(p), slot));
                }
                foreach (string line in prelude)
                    sb.AppendLine("                    " + line);
                string argList = string.Join(", ", args);
                if (ReturnType(v) == "void")
                    sb.AppendLine($"                    target.{member}({argList});");
                else
                {
                    sb.AppendLine($"                    var result = target.{member}({argList});");
                    sb.AppendLine("                    " + DispatchStore(ReturnType(v), "result"));
                }
                foreach (string line in postlude)
                    sb.AppendLine("                    " + line);
                sb.AppendLine("                    break;");
                sb.AppendLine("                }");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        WriteEnums(sb, name, 2, inherited);

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static string CtorSuffix(int index) => index == 0 ? "" : (index + 1).ToString();

    // Stores the value a managed override produced into the slot the native caller provided.
    // Scalars go through the runtime's typed writers; a boxed value needs a per-type assignment
    // (the caller's slot holds the value itself, not a pointer to it). `slot` is the argv index:
    // 0 is the return value, and a by-reference parameter's own slot is the variable Qt pointed at.
    static string DispatchStore(string t, string value, int slot = 0)
    {
        string dest = $"Marshal.ReadIntPtr(argv, {slot} * IntPtr.Size)";
        if (t == "void")
            return "";
        if (IsEnum(t))
            return $"ApiRuntime.WriteResult({dest}, (int){value});";
        if (t == "TimeSpan")
            return $"ApiRuntime.WriteResult({dest}, (long)({value}.Ticks * 100));";
        // A value return lands in the caller's slot as the struct's own bytes: the managed layout is
        // Qt's, so writing it is a copy and no native helper is involved.
        if (IsStruct(t))
            return $"Marshal.StructureToPtr({value}, {dest}, false);";
        // QVariant crosses as object: build the variant the caller's slot expects, and let the
        // native assignment copy it in (the temporary is released right here).
        if (IsQVariantType(t))
            return $"using (var __variant = {VariantMarshalling}.Value({value})) "
                 + $"ApiNative.Assign_{ListStem(t)}({dest}, __variant.Handle);";
        if (IsBoxed(t))
            return $"ApiNative.Assign_{ListStem(t)}({dest}, {value}?.Handle ?? IntPtr.Zero);";
        // A bound-class pointer has a wrapper; an opaque one is already an IntPtr.
        if (IsClassPtr(t))
            return $"Marshal.WriteIntPtr({dest}, {value}?.Handle ?? IntPtr.Zero);";
        if (IsPtr(t))
            return $"Marshal.WriteIntPtr({dest}, {value});";
        if (IsStringT(t) || t == "char*")
            return $"ApiRuntime.Write({dest}, {value});";
        if (IsStringListT(t))
            return $"ApiRuntime.WriteStringList({dest}, {value});";
        if (IsListT(t))
            return $"{ListHelper(ListElem(t))}.Write({dest}, {value});";
        if (IsAssocT(t))
            return $"{AssocHelper(t)}.Write({dest}, {value});";
        return $"ApiRuntime.WriteResult({dest}, {value});";
    }

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
    static (List<string> Prelude, List<string> Args, List<string> Postlude, List<string> Cleanup) MarshalParams(
        IEnumerable<JsonElement> ps, string self)
    {
        var prelude = new List<string>();
        var args = new List<string>();
        var postlude = new List<string>();
        var cleanup = new List<string>();
        if (self != null)
            args.Add(self);
        foreach (var p in ps)
        {
            string t = PType(p), n = SafeName(PName(p));
            if (IsOptionalStructDefault(p))
            {
                // The managed signature is the nullable struct (`QRect? rectangle = null`); the call
                // passes the value and whether it was given, which is how the ABI carries "not given"
                // for a type that has no such state of its own (see NativeParamsFor and the glue's
                // ternary over the declaration's own default).
                args.Add($"{n} ?? default");
                args.Add($"{n}.HasValue");
            }
            else if (IsStringByRef(p))
            {
                // The value goes in as a string and comes back through the out slot.
                args.Add(n);
                args.Add($"out IntPtr {StringOutLocal(p)}");
                postlude.Add($"{n} = ApiRuntime.ToManaged({StringOutLocal(p)});");
            }
            else if (IsByRef(p))
            {
                // The declaration already says out/ref, so the call site just repeats it.
                args.Add((IsOut(p) ? "out " : "ref ") + n);
            }
            else if (IsCallbackType(t))
            {
                // Qt gets an address, not a delegate. The generated thunk converts the C ABI
                // values to the managed ones and the registry keeps the delegate and the thunk
                // alive; the same delegate instance always yields the same address, which is how
                // qRemovePostRoutine finds the routine qAddPostRoutine registered.
                args.Add($"{CallbackThunkName(t)}.Get({n})");
            }
            else if (IsStringListT(t))
            {
                prelude.Add($"var {n}Handle = ApiRuntime.NewStringList({n});");
                cleanup.Add($"ApiRuntime.FreeStringList({n}Handle);");
                args.Add($"{n}Handle");
            }
            else if (IsListT(t))
            {
                string helper = ListHelper(ListElem(t));
                prelude.Add($"var {n}Handle = {helper}.New({n});");
                cleanup.Add($"{helper}.Free({n}Handle);");
                args.Add($"{n}Handle");
            }
            else if (IsAssocT(t))
            {
                string helper = AssocHelper(t);
                prelude.Add($"var {n}Handle = {helper}.New({n});");
                cleanup.Add($"{helper}.Free({n}Handle);");
                args.Add($"{n}Handle");
            }
            else if (t == "QByteArrayView")
            {
                prelude.Add($"var {n}Handle = ApiRuntime.NewByteArray({n});");
                cleanup.Add($"ApiRuntime.FreeByteArray({n}Handle);");
                args.Add($"{n}Handle");
            }
            else if (IsQVariantType(t))
            {
                // The managed parameter is object. null means "not given", so the glue applies the
                // declaration's own default; anything else becomes a real QVariant for the call.
                prelude.Add($"var {n}Lease = {VariantMarshalling}.Parameter({n});");
                cleanup.Add($"{n}Lease.Dispose();");
                args.Add($"{n}Lease.Handle");
            }
            else
            {
                args.Add(ArgToNative(t, n));
            }
        }
        return (prelude, args, postlude, cleanup);
    }

    static void EmitGuarded(StringBuilder sb, string indent, string body, List<string> cleanup)
    {
        if (cleanup.Count == 0)
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
        sb.AppendLine(indent + "finally");
        sb.AppendLine(indent + "{");
        foreach (var line in cleanup)
            sb.AppendLine(indent + "    " + line);
        sb.AppendLine(indent + "}");
    }

    static void WriteNativeDeclarations(StringBuilder sb, JsonElement cls)
    {
        string name = Name(cls);
        var ctors = PublishedCtors(cls);
        var methods = Methods(cls).ToList();
        var virtuals = ChainOf(Name(cls));

        var virtualOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in virtuals.Where(s => s.DeclaredHere))
        {
            var v = slot.Virtual;
            string vname = Name(v);
            virtualOverloads.TryGetValue(vname, out int vseen);
            virtualOverloads[vname] = vseen + 1;
            string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
            // A private virtual has nothing to chain to *and* nothing to call, so no forwarder is
            // declared for it (the suffix counter is still advanced above, to stay aligned with the
            // other three emitters). A pure one has no base implementation either, but the C++
            // object still implements the virtual, so the stub calls it through the vtable.
            // A private or final virtual has no forwarder at all, and neither has any virtual of
            // a class that gets no generated subclass (there is no access class to reach the
            // vtable through - see WriteNativeClass). A final one is published as a plain method
            // through the ordinary forwarder instead.
            if (Has(v, ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final) || !Subclassable(cls))
                continue;
            var ps = Params(v);
            string sig = string.Join(", ", new[] { "IntPtr self" }.Concat(ps.SelectMany(NativeParamsFor)));
            string ret = ReturnType(v);
            // The vtable call: what a wrapper of a *C++* object uses, so the object's own override
            // runs instead of the declaring class's implementation.
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_{vname}{vSuffix}_vt\", CallingConvention = CallingConvention.Cdecl)]");
            if (ret == "bool")
                sb.AppendLine($"        [return: MarshalAs(UnmanagedType.I1)]");
            sb.AppendLine($"        internal static extern {NativeRetType(ret)} {name}_{vname}{vSuffix}_vt({sig});");
            // The declaring class's implementation: what a managed subclass chains to when it does
            // not override a non-pure virtual.
            if (!HasNoBase(v))
            {
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_{vname}__base{vSuffix}\", CallingConvention = CallingConvention.Cdecl)]");
                if (ret == "bool")
                    sb.AppendLine($"        [return: MarshalAs(UnmanagedType.I1)]");
                sb.AppendLine($"        internal static extern {NativeRetType(ret)} {name}_{vname}__base{vSuffix}({sig});");
            }
        }

        for (int i = 0; i < ctors.Count; i++)
        {
            var ps = Params(ctors[i]);
            string sig = string.Join(", ", new[] { "IntPtr userData", "IntPtr dispatch" }
                .Concat(ps.SelectMany(NativeParamsFor)));
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_ctor{CtorSuffix(i)}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr Ctor_{name}{CtorSuffix(i)}({sig});");
        }

        if (SubclassCtor(cls))
        {
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_ctor\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr Ctor_{name}(IntPtr userData, IntPtr dispatch);");
        }

        foreach (var field in FieldList(cls))
        {
            string fieldName = Name(field);
            string fieldType = field.GetProperty("type").GetString();
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_get_{fieldName}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern {ElemAbiCsRet(fieldType)} {name}_get_{fieldName}(IntPtr self);");
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_set_{fieldName}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern void {name}_set_{fieldName}(IntPtr self, {ElemAbiCsParam(fieldType)} value);");
        }

        if (HasClass(cls, ApiModel.ClassFlags.Owned) && (ctors.Count > 0 || SubclassCtor(cls)))
        {
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_dtor\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern void Destroy_{name}(IntPtr self);");
        }

        foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
        {
            string baseName = SimpleName(b);
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}__as_{baseName}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr {name}__as_{baseName}(IntPtr self);");
        }

        var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in methods)
        {
            string mname = Name(m);
            overloadCounts.TryGetValue(mname, out int seen);
            overloadCounts[mname] = seen + 1;
            string entrySuffix = seen == 0 ? "" : (seen + 1).ToString();

            var ps = Params(m);
            var nativeParams = new List<string>();
            if (!IsStatic(m))
                nativeParams.Add("IntPtr self");
            nativeParams.AddRange(ps.SelectMany(NativeParamsFor));
            string sig = string.Join(", ", nativeParams);
            string ret = ReturnType(m);
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_{mname}{entrySuffix}\", CallingConvention = CallingConvention.Cdecl)]");
            if (ret == "bool")
                sb.AppendLine($"        [return: MarshalAs(UnmanagedType.I1)]");
            sb.AppendLine($"        internal static extern {NativeRetType(ret)} {name}_{mname}{entrySuffix}({sig});");
        }
    }

    // argv[i] carries the argument itself: an object pointer is already an address the
    // managed side can use, while a scalar/enum/QString has to be published by address.
    static string DispatchArgStore(string t, string name)
    {
        // The override receives Qt's value types by const reference and its pointers with the
        // const Qt declared (const QMimeData *data), while the slot is a plain void*, so the
        // const has to come off first.
        // A Guid value crosses as 16 bytes: QUuid's own layout *is* System.Guid's on the target
        // (data1, data2, data3, data4[8]), so the address of the argument is what the slot carries.
        if (IsUuid(t))
            return $"const_cast<void*>(static_cast<const void*>(&{name}))";
        if (IsBoxed(t))
            return $"const_cast<{t}*>(&{name})";
        if (IsPtr(t) || IsArray(t))
            return $"const_cast<void*>(static_cast<const void*>({name}))";
        // A string arrives by const reference (const QString &text).
        if (IsStringT(t))
            return $"const_cast<void*>(static_cast<const void*>(&{name}))";
        // A container or a byte view arrives by const reference, so the address of the parameter
        // itself is what goes in the slot.
        if (IsListT(t) || IsAssocT(t) || IsStringListT(t) || t == "QByteArrayView")
            return $"const_cast<void*>(static_cast<const void*>(&{name}))";
        return "&" + name;
    }

    static string DispatchRead(string t, int index)
    {
        string slot = $"Marshal.ReadIntPtr(argv, {index} * IntPtr.Size)";
        // char* has to be checked before the pointer branch: it is a string, not a handle.
        if (t == "char*") return $"ApiRuntime.FromUtf8({slot})";
        if (IsPtr(t))
            return IsClassPtr(t)
                ? ClassPtrRead(t.Substring(0, t.Length - 1), slot)
                : slot;
        if (t == "bool") return $"Marshal.ReadByte({slot}) != 0";
        if (t == "int") return $"Marshal.ReadInt32({slot})";
        if (t == "double") return $"BitConverter.Int64BitsToDouble(Marshal.ReadInt64({slot}))";
        // The QString a virtual override is handed belongs to Qt: reading it must not free it.
        if (t == "QString") return $"ApiRuntime.ToManagedTransient({slot})";
        if (t == "char*") return $"ApiRuntime.FromUtf8({slot})";
        if (t == "long long") return $"Marshal.ReadInt64({slot})";
        if (t == "unsigned long long") return $"(ulong)Marshal.ReadInt64({slot})";
        if (t == "unsigned int" || t == "unsigned long") return $"(uint)Marshal.ReadInt32({slot})";
        if (t == "short") return $"Marshal.ReadInt16({slot})";
        if (t == "unsigned short" || t == "char16_t") return $"({CSharpScalar(t)})Marshal.ReadInt16({slot})";
        if (t == "long") return $"Marshal.ReadInt32({slot})";
        if (t == "unsigned char") return $"Marshal.ReadByte({slot})";
        if (t == "char") return $"(sbyte)Marshal.ReadByte({slot})";
        if (IsEnum(t)) return $"({CsEnum(t)})Marshal.ReadInt32({slot})";
        // A QChar arrives as its 16-bit code unit.
        if (IsCharT(t)) return $"(char)Marshal.ReadInt16({slot})";
        // A duration crosses as nanoseconds.
        if (t == "TimeSpan") return $"TimeSpan.FromTicks(Marshal.ReadInt64({slot}) / 100)";
        if (IsStringListT(t)) return $"ApiRuntime.ToManagedStringListTransient({slot})";
        // A container Qt hands to an override is Qt's: copy it out without freeing it.
        if (IsListT(t)) return $"{ListHelper(ListElem(t))}.ReadTransient({slot})";
        if (IsAssocT(t)) return $"{AssocHelper(t)}.ReadTransient({slot})";
        // A value arrives by value: the slot points at the storage that holds it.
        if (IsStruct(t)) return $"ApiRuntime.ReadStruct<{CsTypeName(t)}>(Marshal.ReadIntPtr(argv, {index} * IntPtr.Size))";
        // QUuid's 16 bytes are laid out exactly like System.Guid's, so the storage the slot points
        // at is the managed value already.
        if (IsUuid(t)) return $"ApiRuntime.ReadStruct<Guid>(Marshal.ReadIntPtr(argv, {index} * IntPtr.Size))";
        // A QVariant argument is a *copy* the native side made for the override; ToManaged turns it
        // into the value it holds and releases the copy.
        if (IsQVariantType(t))
            return $"{VariantMarshalling}.ToManaged(ApiNative.Clone_{ListStem(t)}({slot}))";
        // A boxed value is a *copy*: the slot points at the argument (or at a temporary Qt
        // allocated), and the wrapper must own its own object. A class that cannot be copied
        // (QDataStream) is wrapped over Qt's object for the duration of the call instead - the
        // override still sees the real argument, which is what a stream needs, but must not keep
        // the wrapper.
        if (IsBoxed(t) && IsNonCopyable(t))
            return $"{CsTypeName(t)}.FromTransientHandle({slot})";
        if (IsBoxed(t) && VirtualBoxedParams().Contains(t))
            return $"{CsTypeName(t)}.FromOwnedHandle(ApiNative.Clone_{ListStem(t)}({slot}))";
        if (IsBoxed(t)) return $"{CsTypeName(t)}.FromHandle({slot})";
        return slot;
    }

    // ------------------------------------------------------------------ signals
    // The arity classes the runtime publishes. Beyond this a signal is not worth growing them
    // for (no Qt signal in the bound modules comes close).
    const int MaxSignalArity = 8;

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
    static string SignalRead(string t, int index)
    {
        string slot = $"Marshal.ReadIntPtr(argv, {index} * IntPtr.Size)";
        if (t == "char*") return $"ApiRuntime.FromUtf8(Marshal.ReadIntPtr({slot}))";
        if (IsPtr(t))
            return IsClassPtr(t)
                ? ClassPtrRead(t.Substring(0, t.Length - 1), $"Marshal.ReadIntPtr({slot})")
                : $"(IntPtr)Marshal.ReadIntPtr({slot})";
        if (t == "bool") return $"Marshal.ReadByte({slot}) != 0";
        if (t == "int") return $"Marshal.ReadInt32({slot})";
        if (t == "double") return $"BitConverter.Int64BitsToDouble(Marshal.ReadInt64({slot}))";
        if (t == "float") return $"BitConverter.Int32BitsToSingle(Marshal.ReadInt32({slot}))";
        if (IsStringT(t)) return $"ApiRuntime.ToManagedTransient({slot})";
        if (t == "long long") return $"Marshal.ReadInt64({slot})";
        if (t == "unsigned long long") return $"(ulong)Marshal.ReadInt64({slot})";
        if (t == "unsigned int" || t == "unsigned long") return $"(uint)Marshal.ReadInt32({slot})";
        if (t == "short") return $"Marshal.ReadInt16({slot})";
        if (t == "unsigned short" || t == "char16_t") return $"({CSharpScalar(t)})Marshal.ReadInt16({slot})";
        if (t == "long") return $"Marshal.ReadInt32({slot})";
        if (t == "unsigned char") return $"Marshal.ReadByte({slot})";
        if (t == "char") return $"(sbyte)Marshal.ReadByte({slot})";
        if (IsEnum(t)) return $"({CsEnum(t)})Marshal.ReadInt32({slot})";
        // A QChar arrives as its 16-bit code unit.
        if (IsCharT(t)) return $"(char)Marshal.ReadInt16({slot})";
        if (t == "TimeSpan") return $"TimeSpan.FromTicks(Marshal.ReadInt64({slot}) / 100)";
        if (IsStringListT(t)) return $"ApiRuntime.ToManagedStringListTransient({slot})";
        // A container Qt hands over belongs to Qt: copy it out without freeing it.
        if (IsListT(t)) return $"{ListHelper(ListElem(t))}.ReadTransient({slot})";
        if (IsAssocT(t)) return $"{AssocHelper(t)}.ReadTransient({slot})";
        // A runtime struct is passed by value, so the storage the slot points at *is* the struct.
        if (IsStruct(t)) return $"ApiRuntime.ReadStruct<{CsTypeName(t)}>({slot})";
        // QUuid's 16 bytes are laid out exactly like System.Guid's (data1, data2, data3, data4[8]).
        if (IsUuid(t)) return $"ApiRuntime.ReadStruct<Guid>({slot})";
        // The variant a signal carries belongs to Qt: read the value out without freeing it.
        if (IsQVariantType(t)) return $"{VariantMarshalling}.ToManagedTransient({slot})";
        if (IsBoxed(t)) return $"{CsTypeName(t)}.FromTransientHandle({slot})";
        return slot;
    }

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
            if (t == "char*" || IsStringT(t))
            {
                prelude.Add($"var {local} = ApiRuntime.NewQString({name});");
                cleanup.Add($"ApiRuntime.FreeQString({local});");
                pointers.Add($"(void*){local}");
            }
            else if (IsStringListT(t))
            {
                prelude.Add($"var {local} = ApiRuntime.NewStringList({name});");
                cleanup.Add($"ApiRuntime.FreeStringList({local});");
                pointers.Add($"(void*){local}");
            }
            else if (IsListT(t))
            {
                string helper = ListHelper(ListElem(t));
                prelude.Add($"var {local} = {helper}.New({name});");
                cleanup.Add($"{helper}.Free({local});");
                pointers.Add($"(void*){local}");
            }
            else if (IsAssocT(t))
            {
                string helper = AssocHelper(t);
                prelude.Add($"var {local} = {helper}.New({name});");
                cleanup.Add($"{helper}.Free({local});");
                pointers.Add($"(void*){local}");
            }
            else if (t == "QByteArrayView")
            {
                prelude.Add($"var {local} = ApiRuntime.NewByteArray({name});");
                cleanup.Add($"ApiRuntime.FreeByteArray({local});");
                pointers.Add($"(void*){local}");
            }
            else if (t == "TimeSpan")
            {
                prelude.Add($"long {local} = (long)({name}.Ticks * 100);");
                pointers.Add($"&{local}");
            }
            else if (IsEnum(t))
            {
                prelude.Add($"int {local} = (int){name};");
                pointers.Add($"&{local}");
            }
            else if (IsStruct(t))
            {
                // By value, the way the moc-generated signal function builds it: the struct lives in
                // a local and argv carries that local's address. No allocation, nothing to free.
                prelude.Add($"var {local} = {name};");
                pointers.Add($"&{local}");
            }
            else if (IsQVariantType(t))
            {
                // The argv entry points at the QVariant itself: build one from the managed value
                // and hand over its address for the duration of the activation.
                prelude.Add($"var {local} = {VariantMarshalling}.Value({name});");
                cleanup.Add($"{local}.Dispose();");
                pointers.Add($"(void*){local}.Handle");
            }
            else if (IsBoxed(t))
            {
                // By value: the wrapper's handle is the native object, and the signal function
                // reads it while the activate call is running.
                prelude.Add($"var {local} = {name}?.Handle ?? IntPtr.Zero;");
                pointers.Add($"(void*){local}");
            }
            else if (IsPtr(t))
            {
                // argv[i] has to point at a pointer variable, so the parameter itself becomes one.
                prelude.Add(IsClassPtr(t)
                    ? $"var {local} = (void*)({name}?.Handle ?? IntPtr.Zero);"
                    : $"var {local} = (void*){name};");
                pointers.Add($"&{local}");
            }
            else
            {
                prelude.Add($"{ManagedType(t)} {local} = {name};");
                pointers.Add($"&{local}");
            }
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
    // One container element with its module-aware spelling. A dependency class inside a container
    // (QList<Qt3DCore::QComponent*>) has to carry the dependency's namespace; ApiTypes.Managed
    // only knows the flat binding and would emit the simple name.
    static string ManagedElement(string e)
    {
        if (IsQVariantType(e)) return "object";
        if (IsPtr(e) && IsClassPtr(e))
            return CsTypeName(e.Substring(0, e.Length - 1));
        if (IsEnum(e)) return CsEnum(e);
        if (IsStruct(e) || IsBoxed(e)) return CsTypeName(e);
        return ApiTypes.Managed(e, ClassNames);
    }

    static string ManagedType(string t)
    {
        if (IsQVariantType(t)) return "object";
        if (IsUuid(t)) return "Guid";
        if (IsCharT(t)) return "char";
        if (IsCharPtrT(t)) return "string";
        if (t == "uchar*") return "byte[]";
        // A callback in parameter position is the delegate the managed caller hands over; in
        // return position it is the same delegate type (the managed wrapper reads the address and
        // wraps it). QFunctionPointer has no signature to wrap: it is the QtCore carrier struct
        // the caller opts into a signature on with As<TDelegate>.
        if (IsCallbackType(t)) return CallbackDelegateType(t);
        if (t == "QFunctionPointer") return CoreTypeName("QFunctionPointer");
        // An array of a struct declared by another module (QPoint[] seen from QtGui) carries
        // that module's namespace, like the single value does.
        if (IsArray(t)) return ManagedElement(t.Substring(0, t.Length - 1)) + "[]";
        if (t == "char*") return "string";
        if (IsListT(t)) return ManagedElement(ListElem(t)) + "[]";
        if (IsAssocT(t))
        {
            var (key, value) = AssocElems(t);
            return $"Dictionary<{ManagedElement(key)}, {ManagedElement(value)}>";
        }
        if (IsPtr(t)) return IsClassPtr(t) ? CsTypeName(t.Substring(0, t.Length - 1)) : "IntPtr";
        if (IsEnum(t)) return CsEnum(t);
        if (IsBoxed(t)) return CsTypeName(t);
        return ApiTypes.Managed(t, ClassNames);
    }

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


    static string NativeParamType(string t)
    {
        if (IsUuid(t)) return "Guid";
        // A UTF-16 string for a const QChar* buffer; a ushort for a single code unit (char would
        // be marshalled as one ANSI byte).
        if (IsCharPtrT(t)) return "[MarshalAs(UnmanagedType.LPWStr)] string";
        if (IsCharT(t)) return "ushort";
        if (IsArray(t)) return ManagedType(t);
        if (t == "char*") return "[MarshalAs(UnmanagedType.LPUTF8Str)] string";
        if (t == "TimeSpan") return "long";
        // The ABI carries the function's address; the managed thunk is what Qt calls on the way
        // in, and the wrapper wraps the address on the way out.
        if (IsCallbackType(t) || t == "QFunctionPointer") return "IntPtr";
        // A value crosses as a struct whose layout is the C++ one: two doubles, two ints. No handle,
        // no allocation, no entry in the runtime's tables - and nothing for the caller to release.
        // A struct declared by another module (QPointF seen from QtGui) carries that namespace.
        if (IsStruct(t)) return CsTypeName(t);
        if (IsStringListT(t) || IsListT(t) || IsAssocT(t) || t == "QByteArrayView") return "IntPtr";
        if (IsPtr(t) || IsBoxed(t)) return "IntPtr";
        if (IsEnum(t)) return "int";
        if (IsStringT(t)) return "[MarshalAs(UnmanagedType.LPWStr)] string";
        return CSharpScalar(t) ?? t;
    }

    // A by-reference parameter: the marshaller writes straight into the caller's variable, so
    // the P/Invoke declaration carries the C# keyword. A bool is one byte in C++ and four in the
    // default Win32 marshalling, hence the explicit I1.
    static string NativeParamTypeFor(JsonElement p)
    {
        if (IsOpaqueStructPtr(p))
            return "IntPtr";
        // The nullable struct stays in the *managed* signature; over the ABI the value and a "was it
        // given" flag travel instead (see NativeParamsFor), because a Nullable<T> cannot be marshalled.
        if (IsOptionalStructDefault(p))
            return CsTypeName(PType(p));
        if (!IsByRef(p))
            return NativeParamType(PType(p));
        string keyword = IsOut(p) ? "out " : "ref ";
        string managed = ParamManagedType(p);
        return managed == "bool" ? $"[MarshalAs(UnmanagedType.I1)] {keyword}bool" : keyword + managed;
    }

    // One model parameter is not always one native parameter: an in-out string contributes the
    // string it goes in as, plus the out slot the updated copy comes back through. A value stays one
    // parameter - the struct's layout is the C++ one, so it crosses as itself.
    static List<string> NativeParamsFor(JsonElement p)
    {
        // A value the caller may leave out travels with the flag saying whether it was given: the
        // managed side spells it as the nullable struct, and this is that state over the ABI.
        if (IsOptionalStructDefault(p))
            return new List<string>
            {
                $"{NativeParamTypeFor(p)} {SafeName(PName(p))}",
                $"[MarshalAs(UnmanagedType.I1)] bool {SafeName(PName(p))}Given",
            };
        if (!IsStringByRef(p))
            return new List<string> { $"{NativeParamTypeFor(p)} {SafeName(PName(p))}" };
        return new List<string>
        {
            $"[MarshalAs(UnmanagedType.LPWStr)] string {SafeName(PName(p))}",
            $"out IntPtr {StringOutLocal(p)}",
        };
    }

    static List<string> CppParamsFor(JsonElement p)
    {
        // The address of the caller's struct, cast back to Qt's type where the call is made.
        if (IsOpaqueStructPtr(p))
            return new List<string> { $"void* {NativeName(PName(p))}" };
        // A value type the caller may leave out travels as the value plus a flag saying whether it
        // was given: a value type has no "not given" of its own, and the glue is what puts the
        // declaration's own default in its place - the job a null handle used to do here.
        if (IsOptionalStructDefault(p))
            return new List<string>
            {
                $"{StructPod(PType(p))} {NativeName(PName(p))}",
                $"bool {GivenName(p)}",
            };
        // A struct the callee may rewrite (QTransform::quadToQuad's result, QDeadlineTimer::swap's
        // other) crosses as a pointer to the caller's POD, so the write-back reaches the managed
        // ref/out variable.
        if (IsByRef(p) && IsStruct(RefValueType(PType(p))))
            return new List<string> { $"{StructPod(RefValueType(PType(p)))}* {NativeName(PName(p))}" };
        if (IsStruct(PType(p)))
            return new List<string> { $"{StructPod(PType(p))} {NativeName(PName(p))}" };
        if (!IsStringByRef(p))
            return new List<string> { $"{CppParamTypeFor(p)} {NativeName(PName(p))}" };
        return new List<string>
        {
            $"const char16_t* {NativeName(PName(p))}",
            $"void** {NativeName(PName(p))}_out",
        };
    }

    // The call takes the local QString; the out pointer is filled after the call returns, and a
    // value hands over Qt's own type rebuilt from the POD.
    static List<string> CppArgsFor(JsonElement p)
    {
        if (IsOpaqueStructPtr(p))
            return new List<string> { $"static_cast<{RefValueType(PType(p))}*>({NativeName(PName(p))})" };
        // Given: Qt's own type rebuilt from the POD. Not given: the declaration's default, which the
        // model kept as C++ and which is exactly what Qt would have used.
        if (IsOptionalStructDefault(p))
            return new List<string>
            {
                $"({GivenName(p)} ? {StructFromPod(PType(p), NativeName(PName(p)))} : {StructDefaultExpr(PType(p), DefaultCppOf(p))})",
            };
        // The pointer points at the caller's POD, whose layout is Qt's; the reference Qt wants is
        // the value at that address.
        if (IsByRef(p) && IsStruct(RefValueType(PType(p))))
            return new List<string>
            {
                $"*reinterpret_cast<{RefValueType(PType(p))}*>({NativeName(PName(p))})",
            };
        if (IsStruct(PType(p)))
            return new List<string> { StructFromPod(PType(p), NativeName(PName(p))) };
        return new() { IsStringByRef(p) ? StringInLocal(NativeName(PName(p))) : CppArgFor(p) };
    }

    // ---------------------------------------------------------------------------------------------
    // Qt's value types (QPointF, QPoint). C++ passes them by value, every member is arithmetic, and
    // PyQt and PySide both model them as value types - so no wrapper exists per point, and
    // `series.replace(points)` costs one temporary list rather than one heap object plus one
    // finalizer each. The managed side is a struct (runtime/QtStructs.cs) laid out exactly like
    // the C++ one (two doubles, two ints), so it crosses the boundary as a plain POD: the glue
    // declares the matching type from QtSharpApiRuntime.h and rebuilds Qt's value from it.
    static bool IsStruct(string t) => ApiTypes.IsStruct(t);

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

    static string NativeRetType(string t)
    {
        // A Guid comes back by value through the return slot, like a runtime struct.
        if (IsUuid(t)) return "Guid";
        // A QChar comes back as its 16-bit code unit.
        if (IsCharT(t)) return "ushort";
        // A value comes back as the POD that matches its layout.
        if (IsStruct(t)) return CsTypeName(t);
        // A *pointer* to one comes back as the address it is. There is no array behind it: what Qt
        // returns a struct pointer from is a view into a container (QRegion's iterator, QRect*), and
        // the caller reads through it deliberately.
        if (IsStructPtr(t)) return "IntPtr";
        if (t == "void" || t == "bool" || t == "int" || t == "float" || t == "double")
            return t;
        var scalar = CSharpScalar(t);
        if (scalar != null)
            return scalar;
        // A duration crosses as nanoseconds.
        if (t == "TimeSpan") return "long";
        // A function type comes back as the function's address; the managed wrapper turns a
        // concrete one into its delegate (QtCallbackRegistry.Wrap) and a QFunctionPointer into the
        // carrier struct over that address.
        if (t == "QFunctionPointer" || IsCallbackType(t)) return "IntPtr";
        return IsEnum(t) ? "int" : "IntPtr";
    }

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

    static string ArgToNative(string t, string name)
    {
        // A Guid is the ABI value itself; the glue rebuilds Qt's QUuid from it.
        if (IsUuid(t)) return name;
        // A QChar is a 16-bit code unit; a QChar* is a UTF-16 string.
        if (IsCharPtrT(t)) return name;
        if (IsCharT(t)) return $"(ushort){name}";
        // A value goes over as its coordinates.
        // A value crosses as itself: the struct's layout is the C++ one, so nothing is built.
        if (IsStruct(t)) return name;
        if (IsArray(t) || IsStringT(t) || t == "char*") return name;
        if (t == "TimeSpan") return $"(long)({name}.Ticks * 100)";
        if (IsStringListT(t) || IsListT(t) || IsAssocT(t) || t == "QByteArrayView") return $"{name}Handle";
        // The carrier crosses the ABI as the raw address it holds.
        if (t == "QFunctionPointer") return $"{name}.Address";
        if (IsEnum(t)) return $"(int){name}";
        // Opaque pointers (QEvent*) are already raw handles and must not be dereferenced.
        if (IsClassPtr(t) || IsBoxed(t)) return $"{name}?.Handle ?? IntPtr.Zero";
        return name;
    }

    static string RetToManaged(string t, string call)
    {
        // A Guid came back through the return slot as itself.
        if (IsUuid(t)) return call;
        // A QChar came back as its 16-bit code unit.
        if (IsCharT(t)) return $"(char){call}";
        // QVariant comes back as the value it holds (null for an invalid one). The temporary the
        // glue allocated is released here - except for a Qt value type the runtime has no mapping
        // for, which comes back as the QVariant itself (the escape hatch).
        if (IsQVariantType(t)) return $"{VariantMarshalling}.ToManaged({call})";
        // A value came back through MarshalParams' out locals, not through the return slot.
        // A value comes back as the struct itself, straight out of the return slot.
        if (IsStruct(t)) return call;
        // A struct pointer *is* the address; the managed side reads it through its own accessors.
        if (IsStructPtr(t)) return call;
        // A concrete function type comes back as the delegate Qt declared. The thunk's Wrap looks
        // the address up in the registry first, so a callback installed through the binding returns
        // as the very same delegate instance; a foreign address is marshalled with the declared
        // signature and memoized. A null address is null.
        if (IsCallbackType(t)) return $"{CallbackThunkName(t)}.Wrap({call})";
        // QFunctionPointer promises no signature, so it comes back as the carrier struct over the
        // address; the caller supplies the signature with As<TDelegate>.
        if (t == "QFunctionPointer") return $"new {CoreTypeName("QFunctionPointer")}({call})";
        // The native side hands back a newly allocated QString for both string conventions.
        if (IsStringT(t) || t == "char*") return $"ApiRuntime.ToManaged({call})";
        if (t == "TimeSpan") return $"TimeSpan.FromTicks({call} / 100)";
        if (IsStringListT(t)) return $"ApiRuntime.ToManagedStringList({call})";
        if (IsListT(t)) return $"{ListHelper(ListElem(t))}.Read({call})";
        if (IsAssocT(t))
            return $"{AssocHelper(t)}.Read({call})";
        if (t == "QByteArrayView") return $"ApiRuntime.ToManagedBytes({call})";
        if (IsEnum(t)) return $"({CsEnum(t)}){call}";
        if (IsClassPtr(t)) return $"{CsTypeName(t.Substring(0, t.Length - 1))}.FromHandle({call})";
        if (IsBoxed(t)) return IsOwned(t) ? $"{CsTypeName(t)}.FromOwnedHandle({call})" : $"{CsTypeName(t)}.FromTransientHandle({call})";
        return call;
    }

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
            WriteNativeClass(sb, cls);

        foreach (var s in ApiTypes.RuntimeStructs)
            if (ModelRuntimeStructNames.Contains(s.Name))
                WriteStructNative(sb, s.Name);

        WriteFreeFunctionNative(sb);

        foreach (var elem in ListElements())
            WriteListNativeCpp(sb, elem);

        foreach (var assoc in AssocElements())
            WriteAssocNativeCpp(sb, assoc);

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

    static void WriteNativeClass(StringBuilder sb, JsonElement cls)
    {
        string name = Name(cls);
        var ctors = PublishedCtors(cls);
        var methods = Methods(cls).ToList();
        var virtuals = ChainOf(Name(cls));

        var declaredVirtuals = virtuals.Where(s => s.DeclaredHere).ToList();

        // The native subclass exists to install the vtable hooks of a managed subclass. A class
        // C++ would not let us derive from (private destructor: QClipboard, QStyleHints) simply
        // gets no subclass - its methods are still reachable through the free forwarders below.
        bool subclassable = Subclassable(cls);
        bool subclassCtor = SubclassCtor(cls);
        if (subclassable)
        {
        sb.AppendLine($"class QsApi_{name} : public {CppClass(name)}");
        sb.AppendLine("{");
        sb.AppendLine("public:");
        // One constructor per model constructor; the dispatch pair comes first so that the
        // generated forwarders below stay a mechanical translation. The parameters use the
        // C ABI types and are converted where the base constructor is called.
        for (int i = 0; i < ctors.Count; i++)
        {
            var ps = Params(ctors[i]);
            // The native subclass constructor keeps the real C++ parameter types, because
            // two constructors that differ only by enum (QColor::Spec vs Qt::GlobalColor)
            // would otherwise collapse into one signature through the flat C ABI.
            string sig = string.Join(", ", ps.Select(p => $"{CppCtorParamTypeFor(p)} {NativeName(PName(p))}"));
            string args = string.Join(", ", ps.Select(CppCtorArgFor));
            sb.AppendLine($"    QsApi_{name}(void* userData, QsApiDispatch dispatch{(ps.Count > 0 ? ", " + sig : "")})");
            sb.AppendLine($"        : {CppClass(name)}({args}), m_userData(userData), m_dispatch(dispatch) {{}}");
        }
        if (subclassCtor)
        {
            // The abstract class has no constructor of its own in the model; this is the one a
            // managed subclass uses, and it is concrete because every pure virtual this class
            // inherits is an override point we install.
            sb.AppendLine($"    QsApi_{name}(void* userData, QsApiDispatch dispatch)");
            sb.AppendLine($"        : {CppClass(name)}(), m_userData(userData), m_dispatch(dispatch) {{}}");
        }
        sb.AppendLine();
        // Qt declares most override points protected, and a protected base member is only
        // reachable through an object of the derived type - hence these members. Only the
        // slots this class declares need one: an inherited slot chains through the forwarder
        // that the class declaring it already generated.
        //
        // NOTE: the `self` parameter is typed as the generated subclass even though the object
        // may be an instance of a *further* generated subclass (a managed QLineEdit subclass
        // chains through the forwarder QObject generated). That is only sound because every
        // chain in this model puts the declaring base at offset 0 of the complete object, so
        // the cast is the identity; a fully conforming version would need the forwarder to be
        // a friend of a helper derived from the declaring class.
        var declaredOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < declaredVirtuals.Count; i++)
        {
            var v = declaredVirtuals[i].Virtual;
            string vname = Name(v);
            declaredOverloads.TryGetValue(vname, out int vseen);
            declaredOverloads[vname] = vseen + 1;
            string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
            if (Has(v, ApiModel.VirtualFlags.Pure | ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final))
                continue;
            var ps = Params(v);
            // The helper takes the flat C ABI parameters and converts them itself. A conversion
            // spells the C++ type it aims at, and Qt declares some of those *protected* nested
            // types (QAbstractItemView::CursorAction), which only a member of the class - this
            // helper - may name. Doing the cast here is what keeps the exported forwarder (a free
            // function, which has no such access) down to the ABI types.
            string sig = string.Join(", ", ps.SelectMany(CppParamsFor));
            string args = string.Join(", ", ps.SelectMany(CppArgsFor));
            string ret = ReturnType(v);
            sb.AppendLine($"    static {VirtualReturnCpp(v)} CallBase_{vname}{vSuffix}(QsApi_{name}* self{(ps.Count > 0 ? ", " + sig : "")})");
            sb.AppendLine("    {");
            foreach (string line in VirtualCallBody(
                         v, $"self->{CppClass(name)}::{vname}({args})", r => $"return {r};"))
                sb.AppendLine("        " + line);
            sb.AppendLine("    }");
        }
        // The protected members the model publishes (createIndex, beginInsertRows, ...): C++ lets
        // only a member call them, so the forwarder goes through one of these. The overload
        // suffix is counted exactly like the forwarder loop below counts it.
        var protectedOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in methods)
        {
            string pname = Name(m);
            protectedOverloads.TryGetValue(pname, out int pseen);
            protectedOverloads[pname] = pseen + 1;
            if (!IsProtected(m) || IsStatic(m))
                continue;
            string pSuffix = pseen == 0 ? "" : (pseen + 1).ToString();
            var pps = Params(m);
            // The helper takes the flat C ABI parameters and does the same conversion the free
            // forwarders do, so no C++ reference type has to be spelled here (QDataStream arrives
            // as void* and is dereferenced inside - spelling it as a parameter would mean knowing
            // whether the base declared it const, which the model does not record).
            string psig = string.Join(", ", pps.SelectMany(CppParamsFor));
            string pargs = string.Join(", ", pps.SelectMany(CppArgsFor));
            string pret = ReturnType(m);
            sb.AppendLine($"    static {pret} Call{Pascal(pname)}{pSuffix}(QsApi_{name}* self{(pps.Count > 0 ? ", " + psig : "")})");
            sb.AppendLine("    {");
            sb.AppendLine($"        {(pret == "void" ? "" : "return ")}self->{pname}({pargs});");
            sb.AppendLine("    }");
        }
        sb.AppendLine();
        sb.AppendLine("protected:");
        for (int i = 0; i < virtuals.Count; i++)
        {
            // A blocked slot is not installed: Qt's own (private) implementation is the only
            // correct one, and overriding it is what broke QStringListModel's indices. The slot id
            // stays the position in the chain, so the numbering still matches the dispatch switch.
            if (virtuals[i].Blocked)
                continue;
            var v = virtuals[i].Virtual;
            var ps = Params(v);
            string sig = string.Join(", ", ps.Select(p => $"{CppVirtualParamType(p)} {NativeName(PName(p))}"));
            string ret = ReturnType(v);
            // The override must repeat Qt's declaration exactly; the model type is the normalized
            // managed one (a const pointer or a typedef differs).
            string retCpp = VirtualReturnCpp(v);
            sb.AppendLine($"    {retCpp} {Name(v)}({sig}){ConstSuffix(v)} override");
            sb.AppendLine("    {");
            // A virtual may have a parameter named `result` (QWidget::nativeEvent's qintptr *result),
            // so the local the dispatch fills is spelled with a reserved prefix.
            if (ret != "void")
                sb.AppendLine($"        {retCpp} qs_api_result = {{}};");
            var argv = new List<string> { ret == "void" ? "nullptr" : "&qs_api_result" };
            argv.AddRange(ps.Select(p => DispatchArgStore(PType(p), NativeName(PName(p)))));
            sb.AppendLine($"        void* argv[] = {{ {string.Join(", ", argv)} }};");
            sb.AppendLine($"        m_dispatch(m_userData, {i + 1}, argv);");
            if (ret != "void")
                sb.AppendLine("        return qs_api_result;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        sb.AppendLine("private:");
        sb.AppendLine("    void* m_userData;");
        sb.AppendLine("    QsApiDispatch m_dispatch;");
        sb.AppendLine("};");
        sb.AppendLine();
        }

        // An abstract class with no constructor of its own still needs one entry point: a managed
        // subclass has to be able to build the native subclass that installs the vtable hooks.
        if (subclassCtor)
        {
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{name}_ctor(void* userData, QsApiDispatch dispatch)");
            sb.AppendLine("{");
            sb.AppendLine($"    return new QsApi_{name}(userData, dispatch);");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        for (int i = 0; i < ctors.Count && subclassable; i++)
        {
            var ps = Params(ctors[i]);
            string sig = string.Join(", ", new[] { "void* userData", "QsApiDispatch dispatch" }
                .Concat(ps.SelectMany(CppParamsFor)));
            string args = string.Join(", ", new[] { "userData", "dispatch" }
                .Concat(ps.SelectMany(CppArgsFor)));
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{name}_ctor{CtorSuffix(i)}({sig})");
            sb.AppendLine("{");
            sb.AppendLine($"    return new QsApi_{name}({args});");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (subclassable && HasClass(cls, ApiModel.ClassFlags.Owned) && (ctors.Count > 0 || subclassCtor))
        {
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{name}_dtor(void* self)");
            sb.AppendLine("{");
            if (DerivesFromQObject(Ref(cls)))
            {
                // A QObject's owner can move to Qt at runtime (setParent, or a layout adopting the
                // widget), and deleting something Qt owns is a double free - the crash the legacy
                // pipeline hit with QCommandLinkButton. Qt is asked first; an object it does not
                // own is still released, so nothing leaks.
                sb.AppendLine($"    {CppClass(name)}* object = static_cast<{CppClass(name)}*>(self);");
                sb.AppendLine("    if (object->parent())");
                sb.AppendLine("        return;");
                sb.AppendLine("    delete object;");
            }
            else
            {
                sb.AppendLine($"    delete static_cast<{CppClass(name)}*>(self);");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
        {
            string baseEntry = SimpleName(b);
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{name}__as_{baseEntry}(void* self)");
            sb.AppendLine("{");
            sb.AppendLine($"    return static_cast<{b}*>(static_cast<{CppClass(name)}*>(self));");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // A data member is inline storage, so its accessors are generated rather than exported.
        foreach (var field in FieldList(cls))
        {
            string fieldName = Name(field);
            string fieldType = field.GetProperty("type").GetString();
            string access = $"static_cast<{CppClass(name)}*>(self)->{fieldName}";
            // A member can be declared `const QWidget *widget`, so a pointer needs its const taken
            // off before it fits the void* slot.
            string getter = IsFlags(fieldType)
                ? $"static_cast<int>(({access}).toInt())"
                : IsPointerElem(fieldType) || IsArray(fieldType)
                    ? $"const_cast<void*>(static_cast<const void*>({access}))"
                    : ElemFromCpp(fieldType, access);
            string setter = IsFlags(fieldType)
                ? $"{fieldType}::fromInt(value)"
                : ElemToCpp(fieldType, "value");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {ElemAbiCppRet(fieldType)} qs_api_{name}_get_{fieldName}(void* self)");
            sb.AppendLine("{");
            sb.AppendLine($"    return {getter};");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{name}_set_{fieldName}(void* self, {ElemAbiCppParam(fieldType)} value)");
            sb.AppendLine("{");
            sb.AppendLine($"    static_cast<{CppClass(name)}*>(self)->{fieldName} = {setter};");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in methods)
        {
            string mname = Name(m);
            overloadCounts.TryGetValue(mname, out int seen);
            overloadCounts[mname] = seen + 1;
            string entrySuffix = seen == 0 ? "" : (seen + 1).ToString();

            var ps = Params(m);
            var nativeParams = new List<string>();
            if (!IsStatic(m))
                nativeParams.Add("void* self");
            nativeParams.AddRange(ps.SelectMany(CppParamsFor));
            string sig = string.Join(", ", nativeParams);
            string ret = ReturnType(m);
            string args = string.Join(", ", ps.SelectMany(CppArgsFor));
            // The protected helper takes the flat C ABI parameters and converts them itself, so it
            // is handed the raw values rather than the already-converted arguments.
            string rawArgs = string.Join(", ", ps.SelectMany(AbiArgs));
            string call;
            if (IsStatic(m))
                call = $"{CppClass(name)}::{mname}({args})";
            else if (IsProtected(m))
                call = $"QsApi_{name}::Call{Pascal(mname)}{entrySuffix}(static_cast<QsApi_{name}*>(self)"
                     + (rawArgs.Length > 0 ? ", " + rawArgs : "") + ")";
            else
                call = $"static_cast<{CppClass(name)}*>(self)->{mname}({args})";
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {NativeRetCpp(ret)} qs_api_{name}_{mname}{entrySuffix}({sig})");
            sb.AppendLine("{");
            foreach (string line in NativeBody(m, call))
                sb.AppendLine("    " + line);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Explicitly qualified call to the base implementation of each declared virtual, so a
        // managed override can chain to it (and so the default override does not disable Qt's
        // own handling). The qualification also keeps this from dispatching back into the
        // override, which would recurse forever.
        // The C ABI forwarder a pure virtual's C# stub calls when the object is *not* one of ours.
        // It needs no dispatch hooks - it is an ordinary virtual call on the real object - but Qt
        // declares some of those virtuals protected (QAbstractButton::paintEvent,
        // QGraphicsLayoutItem::sizeHint), and neither a free function nor a plain cast may call
        // one. A derived class with no overrides of its own can: the call below is virtual, so it
        // lands on whatever the object really implements, and the cast is the same identity cast
        // the chaining forwarders already rely on.
        bool anyForwardable = declaredVirtuals.Any(s => !Has(s.Virtual, ApiModel.VirtualFlags.NoBase));
        if (anyForwardable && subclassable)
        {
            sb.AppendLine($"class QsApiAccess_{name} : public {CppClass(name)}");
            sb.AppendLine("{");
            sb.AppendLine("public:");
            var accessOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var slot in declaredVirtuals)
            {
                var v = slot.Virtual;
                string vname = Name(v);
                accessOverloads.TryGetValue(vname, out int aseen);
                accessOverloads[vname] = aseen + 1;
                string aSuffix = aseen == 0 ? "" : (aseen + 1).ToString();
                if (Has(v, ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final))
                    continue;
                var ps = Params(v);
                // The helper takes and returns the same flat C ABI types the export does, so the
                // conversions (a boxed value arrives as a pointer, a QString as char16_t*) happen
                // in exactly one place.
                string sig = string.Join(", ", ps.SelectMany(CppParamsFor));
                string args = string.Join(", ", ps.SelectMany(CppArgsFor));
                string ret = ReturnType(v);
                string call = $"static_cast<QsApiAccess_{name}*>(self)->{vname}({args})";
                sb.AppendLine($"    static {NativeRetCpp(ret)} Call_{vname}{aSuffix}({CppClass(name)}* self{(sig.Length > 0 ? ", " + sig : "")})");
                sb.AppendLine("    {");
                foreach (string line in VirtualCallBody(v, call, r => NativeRetExpr(ret, r)))
                    sb.AppendLine("        " + line);
                sb.AppendLine("    }");
            }
            sb.AppendLine("};");
            sb.AppendLine();
        }

        // The suffix counter runs over every declared slot, exactly like the DllImport emitter, so
        // the two sides agree on the name even when a class declares several overloads.
        var pureOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in declaredVirtuals)
        {
            var v = slot.Virtual;
            string vname = Name(v);
            pureOverloads.TryGetValue(vname, out int pseen);
            pureOverloads[vname] = pseen + 1;
            string pSuffix = pseen == 0 ? "" : (pseen + 1).ToString();
            if (Has(v, ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final) || !subclassable)
                continue;

            var pureParams = Params(v);
            string pureSig = string.Join(", ", new[] { "void* self" }.Concat(pureParams.SelectMany(CppParamsFor)));
            string pureArgs = string.Join(", ", new[] { $"static_cast<{CppClass(name)}*>(self)" }
                .Concat(pureParams.SelectMany(AbiArgs)));
            string pureRet = ReturnType(v);
            // The access helper already returns the C ABI value, so this only forwards it.
            string forwarded = $"QsApiAccess_{name}::Call_{vname}{pSuffix}({pureArgs})";
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {NativeRetCpp(pureRet)} qs_api_{name}_{vname}{pSuffix}_vt({pureSig})");
            sb.AppendLine("{");
            sb.AppendLine("    " + (pureRet == "void" ? forwarded + ";" : "return " + forwarded + ";"));
            sb.AppendLine("}");
            sb.AppendLine();
        }

        var virtualOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < declaredVirtuals.Count && subclassable; i++)
        {
            var v = declaredVirtuals[i].Virtual;
            string vname = Name(v);
            virtualOverloads.TryGetValue(vname, out int vseen);
            virtualOverloads[vname] = vseen + 1;
            string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
            if (Has(v, ApiModel.VirtualFlags.Pure | ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final))
                continue;

            var ps = Params(v);
            string sig = string.Join(", ", new[] { "void* self" }
                .Concat(ps.SelectMany(CppParamsFor)));
            string ret = ReturnType(v);
            // The conversions happen in CallBase_ - a member of the generated subclass, which may
            // name the protected nested types some of those conversions target. This forwarder
            // only carries the ABI values across.
            string baseArgs = string.Join(", ", new[] { $"static_cast<QsApi_{name}*>(self)" }
                .Concat(ps.SelectMany(AbiArgs)));
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {NativeRetCpp(ret)} qs_api_{name}_{vname}__base{vSuffix}({sig})");
            sb.AppendLine("{");
            sb.AppendLine("    " + NativeRetExpr(ret, $"QsApi_{name}::CallBase_{vname}{vSuffix}({baseArgs})"));
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    // What the generated wrapper takes for one parameter. An out parameter Qt fills through a
    // non-const pointer stays that pointer; a non-const reference (`int &pos`) has to become a
    // pointer the C ABI can carry, and the call passes its pointee.
    static string CppParamTypeFor(JsonElement p)
    {
        if (IsOut(p))
            return PType(p);
        if (IsRef(p))
            return PType(p) + "*";
        return CppParamType(PType(p));
    }

    static string CppCtorParamTypeFor(JsonElement p)
        => IsByRef(p) ? CppParamTypeFor(p) : CppCtorParamType(PType(p));

    static string CppArgFor(JsonElement p)
    {
        // A function pointer arrives as an address. The cast has to name exactly the type Qt
        // declared (QtMessageHandler, QEasingCurve::EasingFunction, QFunctionPointer), which is
        // what the model's "cpp" field carries.
        if (IsFunctionType(PType(p)))
            return $"reinterpret_cast<{p.GetProperty("cpp").GetString()}>({NativeName(PName(p))})";
        // A duration arrives as nanoseconds and has to become the unit Qt declared
        // (std::chrono::milliseconds), which is why a duration parameter carries its clang
        // spelling.
        if (PType(p) == "TimeSpan")
            return $"std::chrono::duration_cast<{p.GetProperty("cpp").GetString()}>(std::chrono::nanoseconds({NativeName(PName(p))}))";
        // A raw byte buffer crosses as void*; the C++ declaration spelled it char* / const char*,
        // so the call casts back to that spelling.
        if (HasParam(p, ApiModel.ParamFlags.Buffer))
            return $"static_cast<{p.GetProperty("cpp").GetString()}>({NativeName(PName(p))})";
        if (IsRef(p))
            return "*" + NativeName(PName(p));
        return CppArg(PType(p), NativeName(PName(p)), DefaultCpp(p));
    }

    // The C++ default expression a boxed parameter carries when its default is not expressible as a
    // C# default (QColor(Qt::white), QHostAddress(QHostAddress::Any)); null when there is none.
    static string DefaultCpp(JsonElement p)
        => p.TryGetProperty("defaultCpp", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

    // The generated subclass's constructor takes the *real* C++ parameter types, so it is called
    // with the C ABI argument converted straight to that type - which for everything but a
    // by-reference parameter is the name itself.
    static string CppCtorArgFor(JsonElement p)
    {
        // The constructor's C++ parameter is the function type itself, not the ABI address.
        if (IsFunctionType(PType(p)))
            return $"reinterpret_cast<{p.GetProperty("cpp").GetString()}>({NativeName(PName(p))})";
        return IsRef(p) ? "*" + NativeName(PName(p)) : NativeName(PName(p));
    }

    static string CppParamType(string t)
    {
        // A Guid crosses as the 16-byte mirror of its layout; the glue converts it to QUuid.
        if (IsUuid(t)) return "QsApiGuid";
        // A QChar crosses as its 16-bit code unit; a QChar* buffer as the UTF-16 pointer.
        if (IsCharPtrT(t)) return "const char16_t*";
        if (IsCharT(t)) return "char16_t";
        // A value arrives as the POD with the same layout, and so does an array of them.
        if (IsStruct(t)) return StructPod(t);
        if (IsStructPtr(t)) return StructArrayAbiType(t);
        if (IsStringT(t)) return "const char16_t*";
        if (t == "char*") return "const char*";
        // A duration crosses the C ABI as nanoseconds and is converted to the unit Qt declared
        // where the call is made (see CppArgFor).
        if (t == "TimeSpan") return "long long";
        // A byte buffer goes as a writable pointer: Qt declares some entry points const and some
        // not (QImage's pixel-data constructor), and non-const converts to const but not back.
        if (t == "uchar*") return "unsigned char*";
        // Any other scalar pointer is an input array: the caller owns it and Qt only reads it.
        if (IsArray(t)) return "const " + t;
        // A function pointer crosses as an untyped address; the call casts it back to the type Qt
        // declared (see CppArgFor), exactly as the buffer convention does.
        if (IsFunctionType(t)) return "void*";
        if (IsStringListT(t) || IsListT(t) || IsAssocT(t) || t == "QByteArrayView") return "void*";
        if (IsPtr(t) || IsBoxed(t)) return "void*";
        if (IsEnum(t)) return "int";
        return CppAbiScalar(t);
    }

    // An array of the runtime structs crosses as the POD array: the layouts are Qt's own, so the
    // glue only has to restate the type (and the call casts it back - see CppArg). The pointer is
    // mutable because the model does not record const and Qt declares both directions: a buffer Qt
    // only reads (QPainter::drawPoints) and one it fills in (QRawFont::advancesForGlyphIndexes).
    // A mutable pointer converts to const implicitly, which is what makes one spelling do for both.
    static string StructArrayAbiType(string t) => StructPod(RefValueType(t)) + "*";

    // The real C++ types, used where the generated code has to speak to Qt directly.
    static string CppCtorParamType(string t)
    {
        if (IsStructPtr(t)) return StructArrayAbiType(t);
        if (IsStringT(t)) return "QString";
        if (t == "char*") return "const char*";
        if (t == "uchar*") return "unsigned char*";
        if (IsArray(t)) return "const " + t;
        if (IsStringListT(t)) return "const QStringList&";
        if (IsListT(t) || IsAssocT(t)) return $"const {CppTypeName(t)}&";
        // A byte[] argument is carried as a QByteArray, which converts to the view implicitly.
        if (t == "QByteArrayView") return "const QByteArray&";
        // The constructor takes the declared function type; the ABI carries an address.
        if (IsFunctionType(t)) return "void*";
        if (IsEnum(t) || IsPtr(t)) return CppTypeName(t);
        // QChar crosses as the real value; a QChar* buffer as the const pointer Qt declares.
        if (IsCharT(t)) return "QChar";
        if (IsCharPtrT(t)) return "const QChar*";
        if (IsBoxed(t)) return $"const {CppTypeName(t)}&";
        return CppAbiScalar(t);
    }

    static string CppArg(string t, string name, string defaultCpp = null)
    {
        // An array of runtime structs is the POD array whose layout Qt's own type has: the ABI
        // carries the POD, and the call restates it as the type Qt declared.
        if (IsStructPtr(t)) return $"reinterpret_cast<{RefValueType(t)}*>({name})";
        // A Guid arrives as the ABI mirror; Qt takes the real QUuid.
        if (IsUuid(t)) return $"QsApiGuidToQuuid({name})";
        // A QChar arrives as a 16-bit code unit; a QChar* buffer as a UTF-16 string. Qt's own
        // QString is the temporary that owns the code units for the duration of the call, and its
        // constData() is the QChar* the parameter wants.
        if (IsCharT(t)) return $"QChar({name})";
        if (IsCharPtrT(t)) return $"QString::fromUtf16({name}).constData()";
        // A null managed string must not reach QString::fromUtf16 as a null pointer.
        if (IsStringT(t)) return $"{name} ? QString::fromUtf16({name}) : QString()";
        if (IsStringListT(t)) return $"*static_cast<QStringList*>({name})";
        if (IsListT(t) || IsAssocT(t)) return $"*static_cast<{CppTypeName(t)}*>({name})";
        if (t == "QByteArrayView") return $"*static_cast<QByteArray*>({name})";
        if (t == "char*" || t == "uchar*") return name;      // already a matching pointer
        if (IsPtr(t)) return $"static_cast<{CppTypeName(t.Substring(0, t.Length - 1))}*>({name})";
        // QFlags has no cast from int; the named constructor is the documented way back.
        if (IsFlags(t)) return $"{CppTypeName(t)}::fromInt({name})";
        if (IsEnum(t)) return $"static_cast<{CppTypeName(t)}>({name})";
        // A managed null means "the parameter was not given". When the declaration's own default is
        // available as a C++ expression, that is what Qt would have used (QColor(Qt::white),
        // QHostAddress(QHostAddress::Any)); otherwise a default-constructed value is the closest.
        if (IsBoxed(t))
        {
            if (defaultCpp != null)
            {
                // `= {}` is value-initialisation, i.e. `T()`, not `T({})`.
                string expr = defaultCpp.Replace(" ", "") == "{}" ? $"{CppTypeName(t)}()" : $"{CppTypeName(t)}({defaultCpp})";
                return $"({name} ? *static_cast<{CppTypeName(t)}*>({name}) : {expr})";
            }
            return HasDefaultCtor(t)
                ? $"({name} ? *static_cast<{CppTypeName(t)}*>({name}) : QsApiDefaultValue<{CppTypeName(t)}>())"
                : $"*static_cast<{CppTypeName(t)}*>({name})";
        }
        // The ABI carries `long` as a fixed-width int (CppAbiScalar); cast back to the type Qt
        // declared so overload resolution picks the same function on every platform
        // (QLocale::toString(long), not toString(int)).
        if (t == "long" || t == "unsigned long")
            return $"static_cast<{t}>({name})";
        return name;
    }

    static string NativeRetCpp(string t)
    {
        // A Guid goes back as its 16-byte mirror.
        if (IsUuid(t)) return "QsApiGuid";
        // A QChar goes back as its 16-bit code unit.
        if (IsCharT(t)) return "char16_t";
        // A value goes back as the POD the managed struct is: a plain aggregate of the coordinates,
        // which both compilers pass the same way.
        if (IsStruct(t)) return StructPod(t);
        // A pointer to one goes back as the address Qt gave us.
        if (IsStructPtr(t)) return "void*";
        // The scalar types the C ABI can return directly; everything else comes back as a
        // pointer to something the managed side converts.
        if (t == "void" || t == "bool" || t == "int" || t == "float" || t == "double")
            return t;
        if (CSharpScalar(t) != null)
            return CppAbiScalar(t);
        if (t == "TimeSpan") return "long long";
        return IsEnum(t) ? "int" : "void*";
    }

    static string NativeRetExpr(string t, string call)
    {
        if (t == "void") return call + ";";
        // A QUuid is handed back as the 16 bytes System.Guid has the same layout for.
        if (IsUuid(t)) return $"return QsApiQuuidToGuid({call});";
        // A QChar is handed back as its 16-bit code unit.
        if (IsCharT(t)) return $"return {call}.unicode();";
        // A value is handed back as the POD the caller reads it from (the local is declared by
        // whoever built the call).
        if (IsStruct(t)) return $"return {StructToPod(t, call)};";
        // A struct pointer is a view Qt declares const; the C ABI hands out a plain address.
        if (IsStructPtr(t)) return $"return static_cast<void*>(const_cast<{RefValueType(t)}*>({call}));";
        // A QString copies, but the views next to it have to be converted explicitly: QString has
        // no constructor from QStringView.
        if (IsStringT(t)) return t == "QString" ? $"return new QString({call});" : $"return new QString({call}.toString());";
        if (IsStringListT(t)) return $"return new QStringList({call});";
        if (IsListT(t))
        {
            // Qt hands back a list of *const* pointers where the model can only say T*
            // (QInputDevice::devices() returns QList<const QInputDevice *>), and a QList is not
            // covariant, so that call does not even compile as a copy. The copy is built element
            // by element, with the const taken off - the pointers are the same objects.
            string elem = ListElem(t);
            if (IsClassPtr(elem))
                return "{\n"
                     + $"        auto qs_source = {call};\n"
                     + $"        auto* qs_list = new {CppTypeName(t)}();\n"
                     + "        for (auto qs_item : qs_source)\n"
                     + $"            qs_list->append(const_cast<{elem}>(qs_item));\n"
                     + "        return qs_list;\n"
                     + "    }";
            return $"return new {CppTypeName(t)}({call});";
        }
        if (IsAssocT(t)) return $"return new {CppTypeName(t)}({call});";
        if (t == "QByteArrayView") return $"return new QByteArray({call});";
        // A duration comes back as nanoseconds whatever unit Qt used.
        if (t == "TimeSpan")
            return $"return static_cast<long long>(std::chrono::duration_cast<std::chrono::nanoseconds>({call}).count());";
        if (t == "char*") return $"{{ const char* text = ({call}); return new QString(text ? QString::fromUtf8(text) : QString()); }}";
        if (IsFlags(t)) return $"return static_cast<int>({call}.toInt());";
        if (IsEnum(t)) return $"return static_cast<int>({call});";
        if (IsBoxed(t)) return $"return new {CppTypeName(t)}({call});";
        // Iterators and other const accessors return const pointers; the C ABI is void*.
        if (IsPtr(t)) return $"return static_cast<void*>(const_cast<{CppTypeName(t.Substring(0, t.Length - 1))}*>({call}));";
        // `long` comes back from Qt as the model's 32-bit spelling; narrow it explicitly so the
        // conversion is a stated part of the ABI rather than an implicit one.
        if (t == "long" || t == "unsigned long")
            return $"return static_cast<{CppAbiScalar(t)}>({call});";
        // A function type goes back as a plain address; a concrete one is wrapped into a delegate
        // on the managed side, QFunctionPointer into its carrier struct over the same address.
        if (t == "QFunctionPointer" || IsCallbackType(t))
            return $"return reinterpret_cast<void*>({call});";
        return $"return {call};";
    }

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

    static string CppVirtualParamTypeFor(string t)
    {
        if (IsStringT(t)) return "QString";
        if (t == "char*") return "const char*";
        if (IsPtr(t)) return CppTypeName(t.Substring(0, t.Length - 1)) + "*";
        if (IsEnum(t)) return CppTypeName(t);
        // Qt passes its value classes by const reference; a Guid crosses the ABI as its mirror.
        if (IsUuid(t)) return "const QUuid&";
        // QChar is a two-byte value Qt passes by value; a QChar* buffer is const in every use.
        if (IsCharT(t)) return "QChar";
        if (IsCharPtrT(t)) return "const QChar*";
        // An override has to repeat the base declaration exactly, and Qt passes its value types
        // by const reference (QAbstractItemDelegate::sizeHint(const QStyleOptionViewItem&, ...)).
        if (IsBoxed(t)) return $"const {CppTypeName(t)}&";
        return t;
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
