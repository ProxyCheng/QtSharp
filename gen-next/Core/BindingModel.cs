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
namespace QtSharp.Generator
{
    public sealed partial class BindingModel
    {
        // The identity the generated classes carry in [GeneratedCode]. The generator's own assembly
        // version is stable for a build, so the output stays deterministic.
        internal static readonly string GeneratedByVersion =
            typeof(ApiTypes).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        internal HashSet<string> handWrittenParts;

        internal List<JsonElement> Classes;
        internal string Namespace;
        internal ApiModel.ExtensionModule CurrentModule;
        internal string ModelPath = "";
        internal string ModelFileName = "model/qtsharp.api.json";
        /// <summary>Every class this run knows: the ones it emits plus its dependencies' classes.</summary>
        internal List<JsonElement> KnownClasses = new();
        internal TypeSystem Types;

        internal HashSet<string> ReadHandWrittenParts()
        {
            var parts = new HashSet<string>(StringComparer.Ordinal);
            string runtime = Path.Combine(RepoRoot, "runtime");
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

        internal static string RepoRoot
        {
            get
            {
                string dir = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (Directory.Exists(Path.Combine(dir, "model")) && Directory.Exists(Path.Combine(dir, "runtime")))
                        return dir;
                    dir = Path.GetDirectoryName(dir);
                }
                return Directory.GetCurrentDirectory();
            }
        }

        internal string Name(JsonElement e) => e.GetProperty("name").GetString();

        internal string ScopeOf(JsonElement e)
        {
            string qualified = e.GetProperty("qualified").GetString();
            int i = qualified.LastIndexOf("::", StringComparison.Ordinal);
            return i < 0 ? string.Empty : qualified.Substring(0, i);
        }

        // FNV-1a, 64-bit: deterministic across processes and platforms, and only used to keep two
        // exports apart, so 48 bits is plenty.
        internal string StableHash(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return (hash & 0xFFFFFFFFFFFFUL).ToString("x12");
        }

        internal bool PropagatesToSubclasses(string className) => className != "QIODevice";

        /// <summary>
        /// The classes this run's own model declares, before the runtime structs are filtered out of
        /// <see cref="Classes"/>. The structs' members are emitted from the model as well, but only when
        /// the model is the one that declares them - an extension module inherits them from the core
        /// binding's assembly instead of generating a second copy.
        /// </summary>
        internal HashSet<string> ModelClassNames = new(StringComparer.Ordinal);
        // The class rows whose members this run emits: ordinary classes plus the runtime structs
        // (QMetaEnum, QDate, ...). The latter are filtered out of Classes but their methods still
        // use containers, pairs and optionals, so SignatureTypes walks this list.
        internal List<JsonElement> EmittedClassRows = new();
        // The runtime structs this run's own model declares (un-namespaced core classes only).
        internal HashSet<string> ModelRuntimeStructNames = new(StringComparer.Ordinal);
        internal List<JsonElement> Enums;
        // Namespace-level functions (qCompress, qVersion, ...) - the QtCore/QtGui/QtWidgets module
        // functions, published as a static Qt class.
        internal List<JsonElement> Functions = new();
        internal Dictionary<string, JsonElement> ByName;
        // Keyed by the C++ reference name (qualified for a namespaced extension class, simple for a
        // core class). ByName is keyed by the simple name the emitters use; a simple name is
        // ambiguous when an extension class shadows a core one, so type resolution goes through
        // this table first.
        internal Dictionary<string, JsonElement> ByRef;
        // The owning extension module of every known class/enum (null for the core binding). A class
        // from another extension module is emitted in that module's assembly and namespace, so its
        // C# spelling is qualified with that namespace.
        internal Dictionary<string, ApiModel.ExtensionModule> ClassModules = new(StringComparer.Ordinal);
        internal Dictionary<string, ApiModel.ExtensionModule> EnumModules = new(StringComparer.Ordinal);
        internal HashSet<string> ClassNames;
        internal HashSet<string> EnumNames;
        internal HashSet<string> FlagsEnums;
        // The extension classes whose C++ name is namespace-qualified: simple name -> Qt3DCore::QEntity.
        // The generated C++ (base specifiers, casts, static calls) has to spell the qualified name.
        internal Dictionary<string, string> CppNames = new(StringComparer.Ordinal);

        // The arity classes the runtime publishes. Beyond this a signal is not worth growing them
        // for (no Qt signal in the bound modules comes close).
        internal const int MaxSignalArity = 8;
        // The same for enums whose published name dropped a module namespace: ChangeFlag ->
        // Qt3DCore::ChangeFlag, API -> Qt3DRender::API.
        internal Dictionary<string, string> CppEnumNames = new(StringComparer.Ordinal);

        internal string Ref(JsonElement cls)
            => cls.TryGetProperty("cppName", out var cpp) ? cpp.GetString() : Name(cls);

        internal string SimpleName(string modelType) => ApiTypes.SimpleName(modelType);

        // A C# string literal for a generated attribute. The model's names are identifiers and Qt
        // types (QList<...>), so only the escapes a compiler would need are handled.
        internal string CsString(string text)
            => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // A hand-written type that lives in the QtCore assembly. QFunctionPointer is the one such
        // type the model spells: the struct is not a bound class, so it has no entry in ClassModules
        // and needs the owning namespace spelled out for every dependent module.
        const string QtCoreNamespace = "QtSharp.QtCore";

        internal string CoreTypeName(string name)
            => Namespace == QtCoreNamespace ? name : QtCoreNamespace + "." + name;

        // Module descriptors are per-run objects, so ownership is compared by name rather than by
        // reference; the old generator's registry handed out singletons.
        internal bool SameModule(ApiModel.ExtensionModule owner)
            => owner == null ? CurrentModule == null : CurrentModule != null && owner.Name == CurrentModule.Name;

        // The C# spelling of a model type. A type owned by this module is unqualified; one owned by
        // another module (base or addon) is qualified with that module's namespace.
        internal string CsTypeName(string t)
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
                       && !SameModule(hoistedOwner)
                    ? hoistedOwner.Namespace + "." + hoisted
                    : hoisted;
            if (ClassModules.TryGetValue(t, out var owner) && owner != null)
                return SameModule(owner)
                    ? SimpleName(t)
                    : owner.Namespace + "." + SimpleName(t);
            if (EnumModules.TryGetValue(t, out var enumOwner))
            {
                return EnumCSharpName(t, enumOwner);
            }
            int i = t.LastIndexOf("::", StringComparison.Ordinal);
            return i >= 0 ? t.Replace("::", ".") : t;
        }

        // The full C++ name of an enum owned by module. A model keeps the owner-relative spelling
        // (QNode::Aspect, API), while a dependent module's signature spells the namespace too
        // (Qt3DCore::QNode::Aspect).
        internal string EnumFullName(ApiModel.ExtensionModule owner, string qualified)
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
        internal string EnumManagedName(string t, ApiModel.ExtensionModule owner)
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

        internal string EnumCSharpName(string t, ApiModel.ExtensionModule owner)
        {
            if (owner == null)
                return t.Replace("::", ".");
            string managed = EnumManagedName(t, owner);
            return SameModule(owner)
                ? managed
                : owner.Namespace + "." + managed;
        }

        // An enum literal in a default argument is recorded as C# calls would spell it in the
        // declaring assembly (Qt.ImageConversionFlags.AutoColor). When the enum belongs to another
        // module, the prefix has to become that module's qualified name.
        internal string QualifyEnumDefault(string literal, string modelType)
        {
            if (string.IsNullOrEmpty(literal))
                return literal;
            string t = RefValueType(modelType);
            if (!EnumModules.TryGetValue(t, out var owner) || owner == null)
                return literal;
            string local = EnumManagedName(t, owner);
            string full = SameModule(owner)
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
        internal bool TryLookup(string className, out JsonElement cls)
            => ByRef.TryGetValue(className, out cls) || ByName.TryGetValue(className, out cls);

        // A class-table name used by the emitters: a simple name is the class this run emits (an
        // extension class shadows the core one), a qualified name is a base/type reference.
        internal bool TryLookupInternal(string className, out JsonElement cls)
            => className.Contains("::", StringComparison.Ordinal)
                ? TryLookup(className, out cls)
                : ByName.TryGetValue(className, out cls);

        // The spelling of a container element in the generated C++. A namespaced enum keeps its
        // owning namespace (QSortPolicy::SortType -> Qt3DRender::QSortPolicy::SortType); everything
        // else is already spelled the way Qt's headers expect it (QPair stays the Qt alias).
        internal string CppElementType(string e)
            => CppEnumNames.TryGetValue(e, out var qualified) ? qualified : e;

        internal string CppClass(string simpleName)
            => CppNames.TryGetValue(simpleName, out var qualified) ? qualified : simpleName;

        // A C# string literal for generated source (the model's names never contain control
        // characters, but a backslash-quote round trip keeps this safe if one ever does).
        internal string Quoted(string s)
            => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // A model type spelled in C++. The model drops the C++ namespace of a Qt3D class from the
        // class table itself but keeps it in signatures (Qt3DCore::QEntity*); an enum nested in one
        // of those classes is still written QBuffer::AccessType, so the owning class has to be
        // qualified here. A core namespace enum (Qt::Alignment) is left alone.
        internal string CppTypeName(string t)
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
        internal bool IsOwned(string className)
            => TryLookup(className, out var cls)
               && HasClass(cls, ApiModel.ClassFlags.Owned)
               && (cls.GetProperty("constructors").GetArrayLength() > 0 || SubclassCtor(cls));

        // An abstract class a managed subclass can derive from: it has no constructor of its own, but
        // the generated native subclass is concrete, so one can be built for it (QAbstractItemModel,
        // QAbstractTableModel). Only an abstract class is ever marked this way.
        internal bool SubclassCtor(JsonElement cls)
            => HasClass(cls, ApiModel.ClassFlags.SubclassCtor);

        // The public data members the model published for a class.
        internal List<JsonElement> FieldList(JsonElement cls)
            => cls.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array
                ? f.EnumerateArray().ToList()
                : new List<JsonElement>();

        // The Q_PROPERTY declarations the model published for a class.
        internal List<JsonElement> PropertyList(JsonElement cls)
            => cls.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Array
                ? p.EnumerateArray().ToList()
                : new List<JsonElement>();

        // Whether a bound class is a QObject (directly or through a bound base). A QObject's owner can
        // move to Qt at runtime - the parent in setParent, or a layout adopting a widget - so the
        // generated destructor has to ask Qt before deleting anything (see WriteNativeClass).
        readonly Dictionary<string, bool> derivesFromQObject = new(StringComparer.Ordinal);

        internal bool DerivesFromQObject(string className)
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
        readonly Dictionary<string, bool> derivesFromEvent = new(StringComparer.Ordinal);

        internal bool DerivesFromEvent(string className)
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
        internal string ClassPtrRead(string cls, string expression)
            => DerivesFromEvent(cls)
                ? $"{CsTypeName(cls)}.FromTransientHandle({expression})"
                : $"{CsTypeName(cls)}.FromHandle({expression})";

        /// <summary>
        /// The native library the generated P/Invokes call. The core binding points at QtSharp.Api; an
        /// extension module (QtSharp.Charts and whatever follows it) points at its own DLL. That is what
        /// lets a module be packaged on its own, against the core binding, without recompiling the core
        /// - the module's generated file carries the name it needs instead of borrowing the runtime's.
        /// </summary>
        internal string NativeLibraryName = "QtSharp.Api";

        /// <summary>The model file this run generated from, for the banner comment.</summary>
        readonly HashSet<string> WindowsOnlyMembers = new(StringComparer.Ordinal)
        {
            "QProcess.nativeArguments",
            "QProcess.setNativeArguments",
        };

        internal IEnumerable<JsonElement> Methods(JsonElement cls)
            => cls.GetProperty("methods").EnumerateArray()
                .Where(m => !WindowsOnlyMembers.Contains(Name(cls) + "." + Name(m)));
        internal string ReturnType(JsonElement m) => m.GetProperty("returnType").GetString();
        internal List<JsonElement> Params(JsonElement m) => m.GetProperty("parameters").EnumerateArray().ToList();

        // The constructors the binding publishes. One that takes a const QChar* is dropped: Qt keeps
        // the pointer (QTextBoundaryFinder::QTextBoundaryFinder does not copy the string), while the
        // UTF-16 buffer behind a managed string only lives for the call - the object would hold a
        // dangling pointer. Its other entry points are still published. Every emitter walks this list,
        // so the constructor ordinals in C# and in the glue stay in step.
        internal List<JsonElement> PublishedCtors(JsonElement cls)
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
        internal string PType(JsonElement p) => p.GetProperty("type").GetString();
        internal string PName(JsonElement p) => p.GetProperty("name").GetString();

        internal bool IsStatic(JsonElement m)
            => HasMethod(m, ApiModel.MethodFlags.Static);

        // C++ lets us derive from the class, so a native subclass can install the vtable hooks.
        internal bool Subclassable(JsonElement cls)
            => HasClass(cls, ApiModel.ClassFlags.Subclassable);

        // The value types Qt itself converts into QVariant (`operator QVariant()`): QColor, QBrush,
        // QIcon and friends. They get the IQtVariantValue marker and a conversion registration.
        internal bool ConvertsToVariant(JsonElement cls)
            => HasClass(cls, ApiModel.ClassFlags.ToVariant);

        // QVariant is the one Qt type the managed surface does not name. Like PySide6 (where the class
        // is not exposed at all), a function that takes or returns one takes or returns `object`: the
        // generated wrapper converts at the boundary through VariantMarshalling, so callers hand over
        // and receive ordinary C# values, null is the invalid variant, and no temporary QVariant
        // wrapper - and no Dispose - leaks into a call site. The native ABI is unchanged (a pointer to
        // the QVariant, as any boxed value), so all of IsBoxed's machinery (assign/clone helpers, the
        // virtual trampoline slots) still applies.
        internal const string VariantMarshalling = "QtSharp.QtCore.VariantMarshalling";

        // The value types qvariant.h itself declares a constructor for (QPoint, QUrl, QDate,
        // QModelIndex, ...): one constructor parameter each, and QVariant has the matching ToXxx()
        // accessor. Both directions are already generated, so registering the pair here is what lets
        // QVariant's object entry points follow the header instead of a hand-kept list in
        // BclInterop.Variant.cs. Only the module that declares a type emits its registration.
        internal List<(string Managed, string Cpp, string Unwrap)> VariantCtorRegistrations()
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
        internal string VariantAccessor(JsonElement qvariant, string type)
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
        internal bool EnumIsEmitted(JsonElement e)
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
        internal int FlagBits(JsonElement e, Func<string, int> bit)
        {
            int bits = 0;
            if (!e.TryGetProperty("flags", out var list) || list.ValueKind != JsonValueKind.Array)
                return bits;
            foreach (var entry in list.EnumerateArray())
                bits |= bit(entry.GetString());
            return bits;
        }

        internal ApiModel.ClassFlags ClassFlagsOf(JsonElement cls)
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

        internal ApiModel.MethodFlags MethodFlagsOf(JsonElement m)
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

        internal ApiModel.ParamFlags ParamFlagsOf(JsonElement p)
            => (ApiModel.ParamFlags)FlagBits(p, name => name switch
            {
                "buffer" => (int)ApiModel.ParamFlags.Buffer,
                "optional" => (int)ApiModel.ParamFlags.Optional,
                _ => 0,
            });

        internal bool HasClass(JsonElement cls, ApiModel.ClassFlags flag)
            => (ClassFlagsOf(cls) & flag) != 0;
        internal bool HasMethod(JsonElement m, ApiModel.MethodFlags flag)
            => (MethodFlagsOf(m) & flag) != 0;
        internal bool HasParam(JsonElement p, ApiModel.ParamFlags flag)
            => (ParamFlagsOf(p) & flag) != 0;

        // The traits a virtual declaration carries, read from the model's one flag list.
        internal ApiModel.VirtualFlags VirtualFlagsOf(JsonElement v)
            => (ApiModel.VirtualFlags)FlagBits(v, name => name switch
            {
                "pure" => (int)ApiModel.VirtualFlags.Pure,
                "const" => (int)ApiModel.VirtualFlags.Const,
                "noBase" => (int)ApiModel.VirtualFlags.NoBase,
                "final" => (int)ApiModel.VirtualFlags.Final,
                _ => 0,
            });

        internal bool Has(JsonElement v, ApiModel.VirtualFlags flag)
            => (VirtualFlagsOf(v) & flag) != 0;

        // A virtual with nothing to chain to: the managed default implementation does nothing.
        internal bool HasNoBase(JsonElement v)
            => Has(v, ApiModel.VirtualFlags.Pure | ApiModel.VirtualFlags.NoBase);

        // A const virtual must be declared const to override anything (QWidget::sizeHint() const).
        internal string ConstSuffix(JsonElement v)
            => Has(v, ApiModel.VirtualFlags.Const) ? " const" : "";

        // Whether a value type can be default-constructed in C++ (needed when a managed null is
        // passed for a boxed parameter).
        internal bool HasDefaultCtor(string className)
            => TryLookup(className, out var cls)
               && cls.GetProperty("constructors").EnumerateArray().Any(c => Params(c).Count == 0);

        internal bool IsInheritedVirtual(JsonElement v)
            => v.TryGetProperty("inherited", out var i) && i.ValueKind == JsonValueKind.True;

        internal string DeclaringClass(JsonElement v) => v.GetProperty("declaring").GetString();

        internal bool IsOptional(JsonElement p)
            => HasParam(p, ApiModel.ParamFlags.Optional);

        // A protected member of an extension class (QAbstractItemModel::createIndex, beginInsertRows,
        // ...). C++ only lets a *member* call it, so the generated subclass exposes a static helper
        // the native forwarders go through - the same shape as the CallBase_* forwarders.
        internal bool IsProtected(JsonElement m)
            => HasMethod(m, ApiModel.MethodFlags.Protected);

        // How a parameter crosses: "out" (Qt writes one value through a non-const pointer), "ref"
        // (in and out through a non-const reference), or null for a plain input.
        internal string ParamModeName(JsonElement p)
            => p.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;

        // The model spells the mode as a string; the codegen works with the exclusive enum, so the
        // illegal "both out and ref" state cannot be built by accident.
        internal ApiModel.ParamMode ParamModeOf(JsonElement p)
            => ParamModeName(p) switch
            {
                "out" => ApiModel.ParamMode.Out,
                "ref" => ApiModel.ParamMode.Ref,
                _ => ApiModel.ParamMode.In,
            };

        internal bool IsOut(JsonElement p) => ParamModeOf(p) == ApiModel.ParamMode.Out;
        internal bool IsRef(JsonElement p) => ParamModeOf(p) == ApiModel.ParamMode.Ref;
        internal bool IsByRef(JsonElement p) => ParamModeOf(p) != ApiModel.ParamMode.In;

        // The value a by-reference parameter carries: an out pointer's model type is the scalar
        // pointer the C++ declaration has (int*), a ref's is the scalar itself (int).
        internal string RefValueType(string t) => t.EndsWith("*") ? t.Substring(0, t.Length - 1) : t;

        internal string ParamManagedType(JsonElement p) => ParamFor(p).ManagedType(p);

        // A container parameter accepts the read-only interface for its kind, so a List<T>, an array,
        // a HashSet<T> or a Dictionary all work without an overload. Returns stay concrete.
        internal string ParamValueManagedType(JsonElement p)
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
        internal bool IsStringByRef(JsonElement p) => IsByRef(p) && PType(p) == "QString";
        internal string StringOutLocal(JsonElement p) => "__out_" + PName(p);
        internal string StringInLocal(string nativeName) => "qs_in_" + nativeName;

        // Qt's headers are full of default arguments; carrying them into C# keeps navigation
        // like `new QObject()` or `SetRenderHint(hint)` working.
        internal string ParamSig(JsonElement p) => ParamFor(p).Signature(p);

        // CSharpDefault lives in TypeHandlers.cs with the other per-kind mappings.

        internal bool IsOpaqueStructPtr(JsonElement p) => IsOptional(p) && IsStructPtr(PType(p));

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
        internal bool IsPlainConstructorDefault(string expression)
            // A braced initialiser or a reference cannot be pasted into the ternary; a scope-qualified
            // enumerator (Qt::white for QPixmap::fill's QColor) can, and the glue wraps it in the
            // value type below.
            => expression.IndexOf('{') < 0 && expression.IndexOf('&') < 0;

        internal bool IsOptionalStructDefault(JsonElement p)
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
        readonly HashSet<string> SelfDefaultStructs = new(StringComparer.Ordinal)
        {
            "QModelIndex",
        };

        // The structs whose zero value is not Qt's default construction, so the parameterless
        // constructor is published instead of being folded into C#'s `default`. QCalendar's default
        // constructor is the Gregorian calendar (a zeroed QCalendar is the null backend), and a
        // QKeyFrame's is BezierInterpolation with zeroed vectors (zero is ConstantInterpolation).
        internal readonly HashSet<string> ZeroCtorStructs = new(StringComparer.Ordinal)
        {
            "QCalendar",
            "QKeyFrame",
        };

        internal bool IsSelfDefaultConstruction(string type, string expression)
            => expression.Replace(" ", "").TrimEnd() == type + "()";

        /// <summary>The C name of the "was it given" flag an optional value type travels with.</summary>
        internal string GivenName(JsonElement p) => "qs_given_" + NativeName(PName(p));

        /// <summary>
        /// One parameter handed on to another ABI-shaped function (a virtual's `_vt` twin, a protected
        /// helper) as the arguments that call takes: its name, plus the flag when the type is an
        /// optional value type.
        /// </summary>
        internal IEnumerable<string> AbiArgs(JsonElement p) => ParamFor(p).AbiArgs(p);

        internal string DefaultCppOf(JsonElement p)
            => p.GetProperty("defaultCpp").GetString();

        // The C++ expression for an optional struct parameter's declared default. `{}` is value
        // initialisation (T()); a constructor call is already a T; anything else (Qt::white for
        // QPixmap::fill's QColor) is converted through T's own constructor so the ternary has one type.
        internal string StructDefaultExpr(string type, string defaultCpp)
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
        internal readonly Dictionary<string, string> HoistedEnumNames = new(StringComparer.Ordinal)
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
        internal string CppAbiScalar(string t) => t switch
        {
            "long" => "int",
            "unsigned long" => "unsigned int",
            _ => t,
        };
        internal bool IsPointerElem(string e) => e.EndsWith("*");

        // Reading a pointer element. A QObject goes through FromHandle, so a QList<QObject*> element
        // (QObject::children()) is the object's actual bound class and keeps its identity, exactly
        // like a QObject* return value. Any other pointer keeps the transient wrapper: a
        // QGraphicsItem has no meta-object and no lifetime the identity cache could track.
        internal string PointerElemRead(string e, string expr)
        {
            string pointee = e.Substring(0, e.Length - 1);
            return $"{CsTypeName(pointee)}.{(DerivesFromQObject(pointee) ? "FromHandle" : "FromTransientHandle")}({expr})";
        }
        // A C identifier for one element type: used for the native exports, the generated helper
        // class and the DllImport method names.
        internal string ListStem(string elem)
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
        internal string MapStem(string key, string value) => ListStem(key) + "_" + ListStem(value);
        // A boxed class with no accessible copy constructor (Q_DISABLE_COPY: QDataStream). A virtual
        // takes such a value as a transient handle over Qt's object instead of a copy.
        internal bool IsNonCopyable(string t)
            => ByName.TryGetValue(t, out var c)
               && HasClass(c, ApiModel.ClassFlags.NonCopyable);
        internal string CsEnum(string t) => CsTypeName(t);

        // ------------------------------------------------------------------ C#
        // The C# binding is split the way Qt's C++ headers are: every class, value-type struct and
        // free function goes into the file named after the header the model records for it
        // (QtWidgets/qwidget.h -> generated/QtWidgets/qwidget.cs). Namespace-level enums have no
        // header in the model and live in <namespace>.Enums.cs; the generated list/assoc
        // helpers and the shared P/Invokes have their own files too.
        internal string HeaderFileKey(string header)
        {
            string path = (header ?? string.Empty).Replace('\\', '/');
            if (path.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 2);
            if (path.Length == 0)
                path = "misc";
            return "generated/" + path + ".cs";
        }

        internal string PlainFileKey(string name) => "generated/" + name + ".cs";

        internal IEnumerable<string> SignatureTypes()
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
        internal List<string> ContainerTypes()
        {
            var types = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var t in SignatureTypes())
                foreach (var container in ContainerTypesIn(t))
                    types.Add(container);
            return types.ToList();
        }

        // Every std::optional<T> spelling that appears in a signature. Optionals are top-level
        // values only (never a container element or a pair half).
        internal List<string> OptionalTypes()
        {
            var types = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var t in SignatureTypes())
                if (t != null && IsOptionalT(t))
                    types.Add(t);
            return types.ToList();
        }


        internal Dictionary<string, HashSet<string>> reservedByClass;

        // Names a generated property must not take: the class itself, the members the code generator
        // owns, a nested type, a data member. A property that would collide is skipped and its
        // accessors stay the only way in (QLineEdit::echoMode, whose type is the nested EchoMode).
        internal readonly Dictionary<string, HashSet<string>> hardReservedByClass = new(StringComparer.Ordinal);

        // The C# names a class's data members take: a Q_PROPERTY cannot claim one of them either.
        internal readonly Dictionary<string, HashSet<string>> fieldNamesByClass = new(StringComparer.Ordinal);

        internal void PrepareNames()
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

        internal string MemberName(string className, string member)
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
        internal class Members
        {
            public readonly HashSet<string> AllNames = new(StringComparer.Ordinal);
            public readonly HashSet<string> NonMethodNames = new(StringComparer.Ordinal);
            public readonly HashSet<string> MethodKeys = new(StringComparer.Ordinal);
        }

        readonly Dictionary<string, Members> declaredMembers = new(StringComparer.Ordinal);
        readonly Dictionary<string, Members> inheritedMembers = new(StringComparer.Ordinal);

        // The parameter spelling that makes up a method's signature (without the name or the default).
        internal string ParamTypeKey(JsonElement p) => ParamFor(p).TypeKey(p);

        internal string MethodKey(string name, IEnumerable<JsonElement> ps)
            => name + "(" + string.Join(",", ps.Select(ParamTypeKey)) + ")";

        internal void MergeInto(Members target, Members source)
        {
            target.AllNames.UnionWith(source.AllNames);
            target.NonMethodNames.UnionWith(source.NonMethodNames);
            target.MethodKeys.UnionWith(source.MethodKeys);
        }

        // Whether this run emits the class, i.e. whether it lands in the same assembly as the classes
        // being written. An extension module emits only its own classes; everything else it knows comes
        // from the core assembly.
        internal bool Emits(string className)
            => Classes.Any(c => Name(c) == className);

        /// <summary>
        /// The members a generated class declares that are <c>internal</c>, so they only exist within
        /// their own assembly. <c>Dispatch</c> is the one: every class declares its own vtable
        /// dispatcher as <c>internal static</c>, which means a class in *another* assembly cannot hide
        /// it - writing <c>new</c> across that boundary is exactly what CS0109 complains about.
        /// </summary>
        readonly string[] AssemblyLocalNames = { "Dispatch" };

        Members WithoutAssemblyLocal(Members source)
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
        internal Members InheritedMembers(string className)
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

        Members DeclaredMembers(string className)
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
        internal string NewMethod(Members inherited, string name, string paramKeys)
            => inherited.NonMethodNames.Contains(name)
               || inherited.MethodKeys.Contains(name + "(" + paramKeys + ")")
                ? "new "
                : "";
        internal string NewMethod(Members inherited, string name, IEnumerable<JsonElement> ps)
            => NewMethod(inherited, name, string.Join(",", ps.Select(ParamTypeKey)));
        internal string NewName(Members inherited, string name)
            => inherited.AllNames.Contains(name) ? "new " : "";

        // Whether any class in this hierarchy (the root or one of its descendants, following the
        // primary base) can be constructed - the only place the release flags are ever assigned.
        readonly Dictionary<string, bool> subtreeCtorCache = new(StringComparer.Ordinal);
        internal bool SubtreeHasCtor(string className)
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
        internal IEnumerable<JsonElement> Children(string className)
            => Classes.Where(c => c.GetProperty("bases").EnumerateArray()
                .Select(b => b.GetString()).Select(SimpleName).FirstOrDefault() == className);

        // Whether any class here declares a virtual whose stub reads `__managedSubclass`.
        readonly Dictionary<string, bool> subtreeVirtualCache = new(StringComparer.Ordinal);
        internal bool SubtreeDeclaresVirtual(string className)
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
        internal class VirtualSlot
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

        readonly Dictionary<string, List<VirtualSlot>> virtualChains = new(StringComparer.Ordinal);

        // Every virtual in the class's inheritance chain, base-first, one slot per signature. A
        // class that redeclares an inherited virtual keeps the slot where the base put it, which
        // is what makes the generated native override table and the managed dispatch switch agree.
        internal List<VirtualSlot> ChainOf(string className)
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
            slots.RemoveAll(s => s.Declaring != className && !PropagatesToSubclasses(s.Declaring));

            virtualChains[className] = slots;
            return slots;
        }

        internal string VirtualKey(JsonElement v)
            => Name(v) + "(" + string.Join(",", Params(v).Select(PType)) + ")" + ConstSuffix(v);

        // The C++ type a virtual's native override has to return, verbatim from clang. An override
        // repeats the declaration exactly - `const QMetaObject *`, `qreal`, `QValidator::State` - and
        // the model's returnType is the normalized managed type instead.
        internal string VirtualReturnCpp(JsonElement v)
            => v.TryGetProperty("returnCpp", out var cpp) && cpp.ValueKind == JsonValueKind.String
                ? cpp.GetString()
                : ReturnType(v);

        // The C# name of a virtual: the one its base-most declaring class published. A class that
        // redeclares an inherited virtual cannot choose its own spelling - C# overrides keep the
        // base member's name - and the base-most declarer is where that name was decided (QStyle's
        // PixelMetric enum forces pixelMetric() on QCommonStyle and QProxyStyle).
        internal string VirtualCsName(string className, JsonElement v)
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
        internal bool IsRuntimeStructClass(JsonElement c)
            => ApiTypes.IsStruct(Name(c))
               && (!c.TryGetProperty("cppName", out var cpp)
                   || ApiTypes.NamespacedStructs.TryGetValue(Name(c), out var expect)
                      && expect == cpp.GetString());

        internal JsonElement? CoreClassOf(string name)
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
        internal IEnumerable<JsonElement> BaseChainOf(JsonElement cls)
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

        internal bool IsIdentifier(string n)
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
        internal HashSet<string> HandWrittenMembers(string name)
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
        internal bool StructHasVariant(string name)
            => CoreClassOf(name) is JsonElement cls && ConvertsToVariant(cls);


        internal string CtorSuffix(int index) => index == 0 ? "" : (index + 1).ToString();

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
        internal List<string> VirtualBoxedParams()
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
        internal List<string> VirtualBoxedReturns()
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
        readonly HashSet<string> OwnershipTransfers = new(StringComparer.Ordinal)
        {
            "addItem", "addToGroup", "addRect", "addEllipse", "addLine", "addPath", "addPixmap",
            "addPolygon", "addSimpleText", "addText",
        };

        // Whether the object this call hands over is already tracked by the runtime, and how the
        // generated body names it: as the return value, or as the argument that was adopted.
        internal (bool IsItem, bool IsArgument, string Argument) OwnershipTransferOf(
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
        internal bool TryPinnedBuffer(IReadOnlyList<JsonElement> ps, out int dataIndex, out int cleanupIndex,
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

        internal bool IsCleanupCallback(string t)
            => ApiTypes.TryParseCallback(t, out string ret, out string[] arguments)
               && ret == "void" && arguments.Length == 1 && arguments[0] == "void*";

        internal (List<string> Prelude, List<string> Args, List<string> Postlude, List<string> Cleanup) MarshalParams(
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

        internal void EmitGuarded(StringBuilder sb, string indent, string body, List<string> cleanup,
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
        internal string SignalMocType(JsonElement p) => CppTypeName(PType(p));

        // Reading one signal argument out of the argv array moc builds. Unlike a virtual's argv (which
        // carries the argument itself), a signal's argv[i] is the *address* of the argument storage:
        // moc hands the signal function's own parameters over by address. So a pointer parameter has
        // to be dereferenced, while a boxed value is already the address of the value.
        // The native argument storage a signal's emit needs, in the shape the moc-generated signal
        // function would have built on its own stack: scalars/enums/durations by value, a pointer as
        // a pointer variable (whose address goes into argv), a boxed value as the object itself, and
        // a string/container as a native object built for the call.
        internal (List<string> Prelude, List<string> Pointers, List<string> Cleanup) EmitParams(List<JsonElement> ps)
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
        internal List<string> EmitLambda(List<JsonElement> ps, string mocSig)
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
        internal string ManagedRetType(string t)
            => IsStructPtr(t) ? "IntPtr" : ManagedType(t);

        // The delegate the managed caller hands over for a function type. Action/Func are the BCL
        // shapes PySide's PyObject* and PyQt's Callable both stand for, and they keep the callback a
        // first-class C# value (nothing custom to learn).
        internal string CallbackDelegateType(string t)
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
        internal string CallbackManagedType(string t)
        {
            if (t == "QString") return "string";
            if (t.EndsWith("*", StringComparison.Ordinal)) return "IntPtr";
            if (IsEnum(t)) return CsEnum(t);
            return CSharpScalar(t) ?? t;
        }

        // The same values over the C ABI: an enum is an int, a pointer/QString reference is an
        // address, and the scalar spellings are the C# ones (long -> int on the MSVC target).
        internal string CallbackAbiType(string t)
        {
            if (t == "void") return "void";
            if (IsEnum(t)) return "int";
            // A QString argument is Qt's const QString&: the ABI carries the address of the object.
            if (t == "QString" || t.EndsWith("*", StringComparison.Ordinal)) return "IntPtr";
            return CSharpScalar(t) ?? t;
        }

        internal string CallbackAbiParam(string t, int index)
        {
            // A C++ bool is one byte; the default delegate marshalling would make it a 4-byte BOOL.
            string type = t == "bool" ? "[MarshalAs(UnmanagedType.I1)] bool" : CallbackAbiType(t);
            return type + " arg" + index;
        }

        internal string CallbackParamToManaged(string t, string argument)
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

        internal string CallbackReturnToAbi(string t, string expression)
            => IsEnum(t) ? $"(int){expression}" : expression;

        // The same conversion in the other direction, used when the binding calls a callback that
        // native code installed. A QString is the caller's job (it has to build and free the native
        // object), so this covers the values that cross as themselves or as an int.
        internal string CallbackManagedToAbi(string t, string argument)
            => IsEnum(t) ? $"(int){argument}" : argument;

        internal string CallbackAbiToManagedReturn(string t, string expression)
            => IsEnum(t) ? $"({CsEnum(t)}){expression}" : expression;

        internal string CallbackThunkName(string t) => "CallbackThunk_" + StableHash(t);


        // NativeParamType/NativeRetType/ArgToNative/RetToManaged are dispatched in TypeHandlers.cs.

        // A by-reference parameter: the marshaller writes straight into the caller's variable, so
        // the P/Invoke declaration carries the C# keyword. A bool is one byte in C++ and four in the
        // default Win32 marshalling, hence the explicit I1.

        // One model parameter is not always one native parameter: an in-out string contributes the
        // string it goes in as, plus the out slot the updated copy comes back through. A value stays one
        // parameter - the struct's layout is the C++ one, so it crosses as itself.
        internal List<string> NativeParamsFor(JsonElement p) => ParamFor(p).NativeParams(p).ToList();

        internal List<string> CppParamsFor(JsonElement p) => ParamFor(p).CppParams(p).ToList();

        // The call takes the local QString; the out pointer is filled after the call returns, and a
        // value hands over Qt's own type rebuilt from the POD.
        internal List<string> CppArgsFor(JsonElement p) => ParamFor(p).CppArgs(p).ToList();

        // ---------------------------------------------------------------------------------------------
        // Qt's value types (QPointF, QPoint). C++ passes them by value, every member is arithmetic, and
        // PyQt and PySide both model them as value types - so no wrapper exists per point, and
        // `series.replace(points)` costs one temporary list rather than one heap object plus one
        // finalizer each. The managed side is a struct (runtime/QtStructs.cs) laid out exactly like
        // the C++ one (two doubles, two ints), so it crosses the boundary as a plain POD: the glue
        // declares the matching type from QtSharpApiRuntime.h and rebuilds Qt's value from it.

        /// <summary>The POD the glue declares for a value type (QPointF -> QsApiPointF).</summary>
        internal string StructPod(string t)
        {
            // A Qt3D value type reaches here qualified (Qt3DAnimation::QKeyFrame); the POD's name is
            // the simple one, as is the C# struct's.
            string name = ApiTypes.StructName(t) ?? t;
            return "QsApi" + (name.StartsWith("Q", StringComparison.Ordinal) ? name.Substring(1) : name);
        }

        /// <summary>Qt's value rebuilt from the POD, for a call: QPointF(p.x, p.y).</summary>
        internal string StructFromPod(string t, string name)
        {
            var entry = ApiTypes.RuntimeStructOf(t).Value;
            // A type whose memory layout is not its accessor order carries its own conversion (QRect).
            if (entry.FromPod != null)
                return entry.FromPod.Replace("@pod", name);
            return $"{ApiTypes.CppStructName(t)}({string.Join(", ", entry.Fields.Select(f => $"{name}.{f}"))})";
        }

        /// <summary>The POD the glue returns, built from Qt's value.</summary>
        internal string StructToPod(string t, string value)
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
        internal List<string> VirtualCallBody(JsonElement v, string call, Func<string, string> finish)
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
        internal List<string> NativeBody(JsonElement m, string call)
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
        internal string CSharpScalar(string t) => t switch
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
        internal string ManagedStructFieldType(string t)
            => CSharpScalar(t) ?? (t.EndsWith("*", StringComparison.Ordinal) ? "IntPtr" : t);

        // ArgToNative and RetToManaged are dispatched in TypeHandlers.cs.

        // The parameter that makes an owned return conditional (`ownedWhenNull` in the model): the
        // caller owns the object only when this parameter was null. Absent means the return is always
        // borrowed, exactly like a plain method.
        internal string OwnedWhenNull(JsonElement m)
            => m.TryGetProperty("ownedWhenNull", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;

        // A method that hands a new object to the caller. Conditional ownership counts: the model's
        // `ownedWhenNull` is the explicit predicate, and the generated body chooses the factory.
        internal bool ReturnsOwned(JsonElement m)
            => HasMethod(m, ApiModel.MethodFlags.OwnedReturn) || OwnedWhenNull(m) != null;

        // The managed expression for an owned return. Only a pointer to a class the binding can
        // release is expressible: the release path (FromOwnedHandle/Dispose/Destroy_<X>) is generated
        // together with the class's constructors, and a value or an opaque pointer has nothing to
        // own. A model entry that names anything else is a generator error, not a silent borrow -
        // otherwise the caller would be handed an object it can never release.
        internal string OwnedRetToManaged(string t, string call)
        {
            if (IsClassPtr(t) && IsOwned(t.Substring(0, t.Length - 1)))
                return $"{CsTypeName(t.Substring(0, t.Length - 1))}.FromOwnedHandle({call})";
            throw new InvalidOperationException(
                $"A method return is marked owned, but '{t}' has no release path " +
                "(a bound class pointer with published constructors is required).");
        }

        // ------------------------------------------------------------------ C++
        internal string CppCtorParamTypeFor(JsonElement p) => ParamFor(p).CppCtorParamType(p);


        // The C++ default expression a boxed parameter carries when its default is not expressible as a
        // C# default (QColor(Qt::white), QHostAddress(QHostAddress::Any)); null when there is none.
        internal string DefaultCpp(JsonElement p)
            => p.TryGetProperty("defaultCpp", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

        // The generated subclass's constructor takes the *real* C++ parameter types, so it is called
        // with the C ABI argument converted straight to that type - which for everything but a
        // by-reference parameter is the name itself.
        internal string CppCtorArgFor(JsonElement p) => ParamFor(p).CppCtorArg(p);

        internal string CppParamType(string t) => CppAbiType(t);

        // An array of the runtime structs crosses as the POD array: the layouts are Qt's own, so the
        // glue only has to restate the type (and the call casts it back - see CppArg). The pointer is
        // mutable because the model does not record const and Qt declares both directions: a buffer Qt
        // only reads (QPainter::drawPoints) and one it fills in (QRawFont::advancesForGlyphIndexes).
        // A mutable pointer converts to const implicitly, which is what makes one spelling do for both.
        internal string StructArrayAbiType(string t) => StructPod(RefValueType(t)) + "*";

        // The real C++ types, used where the generated code has to speak to Qt directly.
        internal string CppCtorParamType(string t) => Types.CppCtorParamType(t);

        // Overrides must repeat the base declaration exactly, so these are the real C++ types
        // rather than the void* used by the exported C entry points.
        //
        // The model carries the spelling clang printed for a virtual's parameters, and that is what
        // an override has to repeat: the normalized model type has already lost the pointee const
        // (const QMimeData * becomes QMimeData*) and the reference (const QList<int> & becomes a
        // by-value QList<int>), and a mismatch is a silent C3668 that leaves the base implementation
        // in place.
        internal string CppVirtualParamType(JsonElement p)
        {
            if (p.TryGetProperty("cpp", out var cpp) && cpp.ValueKind == JsonValueKind.String)
                return cpp.GetString();
            return CppVirtualParamTypeFor(PType(p));
        }

        // ------------------------------------------------------------------ identifiers
        internal string Pascal(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        internal string NativeName(string s) => s == "this" ? "self_" : s;

        readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
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

        internal string SafeName(string s) => CSharpKeywords.Contains(s) ? "@" + s : s;

        // ------------------------------------------------------------------ type system forwarding
        // The type system is a separate service; these keep the model's analysis code reading like
        // the rest of the generator while the actual mapping decisions stay behind the handlers.
        internal string ManagedType(string t) => Types.ManagedType(t);
        internal string ManagedElement(string e) => Types.ManagedElement(e);
        internal string NativeParamType(string t) => Types.NativeParamType(t);
        internal string NativeRetType(string t) => Types.NativeRetType(t);
        internal string CppAbiType(string t) => Types.CppAbiType(t);
        internal string ArgToNative(string t, string name) => Types.ArgToNative(t, name);
        internal string RetToManaged(string t, string call) => Types.RetToManaged(t, call);
        internal string CSharpDefault(string type) => Types.CSharpDefault(type);
        internal string NativeRetExpr(string t, string call) => Types.NativeRetExpr(t, call);
        internal string CppAbiRetType(string t) => Types.NativeRetCpp(t);
        internal string CppVirtualParamTypeFor(string t) => Types.CppVirtualParamTypeFor(t);
        internal string DispatchRead(string t, int index) => Types.DispatchRead(t, index);
        internal string SignalRead(string t, int index) => Types.SignalRead(t, index);
        internal string DispatchStore(string t, string value, int slot = 0) => Types.DispatchStore(t, value, slot);
        internal string DispatchArgStore(string t, string name) => Types.DispatchArgStore(t, name);
        internal string ElemAbiCppParam(string e) => Types.ElemAbiCppParam(e);
        internal string ElemAbiCppRet(string e) => Types.ElemAbiCppRet(e);
        internal string ElemAbiCsParam(string e) => Types.ElemAbiCsParam(e);
        internal string ElemAbiCsRet(string e) => Types.ElemAbiCsRet(e);
        internal string ElemToCpp(string e, string expr) => Types.ElemToCpp(e, expr);
        internal string ElemFromCpp(string e, string expr) => Types.ElemFromCpp(e, expr);
        internal string ElemRead(string e, string expr) => Types.ElemRead(e, expr);
        internal string ElemArg(string e, string expr) => Types.ElemArg(e, expr);
        internal (List<string> Prelude, string Arg, List<string> Cleanup) ElemToAbi(
            string e, string expr, string prefix) => Types.ElemToAbi(e, expr, prefix);
        internal (List<string> Prelude, List<string> Pointers, List<string> Cleanup) EmitParam(
            string t, string name, string local) => Types.EmitParam(t, name, local);
        internal (List<string> Prelude, string Arg, List<string> Cleanup) MarshalArg(
            string t, string name) => Types.MarshalArg(t, name);
        internal TypeSystem.ContainerInfo ContainerOf(string t) => Types.ContainerOf(t);
        internal TypeSystem.ContainerInfo Container(string t) => Types.ContainerOf(t);
        internal string OptionalElem(string t) => Types.ContainerOf(t).Element;
        internal IEnumerable<string> ContainerTypesIn(string t) => Types.ContainerTypesInCore(t);
        internal TypeSystem.ParamHandler ParamFor(JsonElement p) => Types.ParamFor(p);

        internal bool IsVoid(string t) => Types.IsVoid(t);
        internal bool IsScalar(string t) => Types.IsScalar(t);
        internal bool IsEnum(string t) => Types.IsEnum(t);
        internal bool IsFlags(string t) => Types.IsFlags(t);
        internal bool IsStringT(string t) => Types.IsStringT(t);
        internal bool IsStringLikeT(string t) => Types.IsStringLikeT(t);
        internal bool IsCStringT(string t) => Types.IsCStringT(t);
        internal bool IsCharT(string t) => Types.IsCharT(t);
        internal bool IsCharPtrT(string t) => Types.IsCharPtrT(t);
        internal bool IsUuid(string t) => Types.IsUuid(t);
        internal bool IsStruct(string t) => Types.IsStruct(t);
        internal bool IsStructPtr(string t) => Types.IsStructPtr(t);
        internal bool IsArray(string t) => Types.IsArray(t);
        internal bool IsByteBufferT(string t) => Types.IsByteBufferT(t);
        internal bool IsPtr(string t) => Types.IsPtr(t);
        internal bool IsClassPtr(string t) => Types.IsClassPtr(t);
        internal bool IsBoxed(string t) => Types.IsBoxed(t);
        internal bool IsQVariantType(string t) => Types.IsQVariantType(t);
        internal bool IsCallbackType(string t) => Types.IsCallbackType(t);
        internal bool IsQFunctionPointerT(string t) => Types.IsQFunctionPointerT(t);
        internal bool IsFunctionType(string t) => Types.IsFunctionType(t);
        internal bool IsStringListT(string t) => Types.IsStringListT(t);
        internal bool IsSequenceT(string t) => Types.IsSequenceT(t);
        internal bool IsAssocT(string t) => Types.IsAssocT(t);
        internal bool IsPairT(string t) => Types.IsPairT(t);
        internal bool IsOptionalT(string t) => Types.IsOptionalT(t);
        internal bool IsByteArrayViewT(string t) => Types.IsByteArrayViewT(t);
        internal bool IsTimeSpanT(string t) => Types.IsTimeSpanT(t);
        internal bool IsNativeContainerT(string t) => Types.IsNativeContainerT(t);
        internal bool IsGeneratedHelperT(string t) => Types.IsGeneratedHelperT(t);
        internal bool IsHandleMarshalledT(string t) => Types.IsHandleMarshalledT(t);
        internal bool IsValueTypeManaged(string t) => Types.IsValueTypeManaged(t);
        internal bool IsScalarElem(string e) => Types.IsScalarElem(e);
        internal bool IsEnumElem(string e) => Types.IsEnumElem(e);

    }
}
