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
    static List<JsonElement> Classes;
    static List<JsonElement> Enums;
    static Dictionary<string, JsonElement> ByName;
    static HashSet<string> ClassNames;
    static HashSet<string> EnumNames;
    static HashSet<string> FlagsEnums;

    // Whether a value of this class returned by value is ours to free (a boxed value type) or
    // belongs to Qt (an event, which Qt builds on the stack).
    // "Owned" only means anything when the managed side can actually create the object: the
    // release path (FromOwnedHandle/Dispose/Destroy_*) is generated together with the
    // constructors, so a boxed class without published constructors has to be wrapped the
    // transient way (QItemSelection, whose constructors take an unbound QModelIndex).
    static bool IsOwned(string className)
        => ByName.TryGetValue(className, out var cls)
           && cls.GetProperty("owned").GetBoolean()
           && (cls.GetProperty("constructors").GetArrayLength() > 0 || SubclassCtor(cls));

    // An abstract class a managed subclass can derive from: it has no constructor of its own, but
    // the generated native subclass is concrete, so one can be built for it (QAbstractItemModel,
    // QAbstractTableModel). Only an abstract class is ever marked this way.
    static bool SubclassCtor(JsonElement cls)
        => cls.TryGetProperty("subclassCtor", out var s) && s.ValueKind == JsonValueKind.True;

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
        if (ByName.TryGetValue(className, out var cls))
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

    public static void Run(string modelPath, string outDir)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(modelPath));
        var root = document.RootElement;
        string ns = root.GetProperty("namespace").GetString();
        Classes = root.GetProperty("classes").EnumerateArray().ToList();
        Enums = root.GetProperty("enums").EnumerateArray().ToList();
        ByName = Classes.ToDictionary(c => c.GetProperty("name").GetString(), c => c, StringComparer.Ordinal);
        ClassNames = new HashSet<string>(Classes.Select(c => c.GetProperty("name").GetString()), StringComparer.Ordinal);
        EnumNames = new HashSet<string>(Enums.Select(e => e.GetProperty("qualified").GetString()), StringComparer.Ordinal);
        FlagsEnums = new HashSet<string>(
            Enums.Where(e => e.TryGetProperty("flags", out var f) && f.ValueKind == JsonValueKind.True)
                 .Select(e => e.GetProperty("qualified").GetString()), StringComparer.Ordinal);
        PrepareNames();
        derivesFromQObject.Clear();

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "QtSharp.Api.cs"), GenerateCSharp(ns));
        File.WriteAllText(Path.Combine(outDir, "QtSharp.Api.native.cpp"), GenerateNative());
        Console.WriteLine($"[codegen] wrote {outDir}/QtSharp.Api.cs + QtSharp.Api.native.cpp");
    }

    // ------------------------------------------------------------------ model queries
    static string Name(JsonElement e) => e.GetProperty("name").GetString();
    static string ReturnType(JsonElement m) => m.GetProperty("returnType").GetString();
    static List<JsonElement> Params(JsonElement m) => m.GetProperty("parameters").EnumerateArray().ToList();
    static string PType(JsonElement p) => p.GetProperty("type").GetString();
    static string PName(JsonElement p) => p.GetProperty("name").GetString();

    static bool IsStatic(JsonElement m)
        => m.TryGetProperty("static", out var s) && s.ValueKind == JsonValueKind.True;

    // C++ lets us derive from the class, so a native subclass can install the vtable hooks.
    static bool Subclassable(JsonElement cls)
        => !cls.TryGetProperty("subclassable", out var s) || s.ValueKind != JsonValueKind.False;

    // A pure virtual has no C++ body, so there is nothing to chain a managed override to.
    static bool IsPure(JsonElement v)
        => v.TryGetProperty("pure", out var p) && p.ValueKind == JsonValueKind.True;

    // A private virtual is the same from the generated subclass's point of view: Qt's
    // implementation exists but is not reachable (QAbstractListModel::columnCount).
    static bool IsNoBase(JsonElement v)
        => v.TryGetProperty("noBase", out var n) && n.ValueKind == JsonValueKind.True;

    // A virtual with nothing to chain to: the managed default implementation does nothing.
    static bool HasNoBase(JsonElement v) => IsPure(v) || IsNoBase(v);

    // A const virtual must be declared const to override anything (QWidget::sizeHint() const).
    static string ConstSuffix(JsonElement v)
        => v.TryGetProperty("const", out var c) && c.ValueKind == JsonValueKind.True ? " const" : "";

    // Whether a value type can be default-constructed in C++ (needed when a managed null is
    // passed for a boxed parameter).
    static bool HasDefaultCtor(string className)
        => ByName.TryGetValue(className, out var cls)
           && cls.GetProperty("constructors").EnumerateArray().Any(c => Params(c).Count == 0);

    static bool IsInheritedVirtual(JsonElement v)
        => v.TryGetProperty("inherited", out var i) && i.ValueKind == JsonValueKind.True;

    static string DeclaringClass(JsonElement v) => v.GetProperty("declaring").GetString();

    static bool IsOptional(JsonElement p)
        => p.TryGetProperty("optional", out var o) && o.ValueKind == JsonValueKind.True;

    // A parameter Qt writes back: a non-const scalar pointer (`out`, QColor::getRgb) or a
    // non-const scalar reference (`ref`, QValidator::validate's int &pos). The extractor records
    // which, and neither can carry a C# default value.
    static bool IsOut(JsonElement p)
        => p.TryGetProperty("out", out var o) && o.ValueKind == JsonValueKind.True;
    static bool IsRef(JsonElement p)
        => p.TryGetProperty("ref", out var r) && r.ValueKind == JsonValueKind.True;
    static bool IsByRef(JsonElement p) => IsOut(p) || IsRef(p);

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
        string sig = $"{ManagedType(type)} {SafeName(PName(p))}";
        if (!IsOptional(p))
            return sig;
        string literal = p.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;
        // An opaque handle is a value type: IntPtr.Zero is not a compile-time constant, so the
        // only usable default is `default`.
        if (literal == "null" && ManagedType(type) == "IntPtr")
            literal = "default";
        return sig + " = " + (literal ?? CSharpDefault(type));
    }

    static string CSharpDefault(string type)
    {
        if (ManagedType(type) == "IntPtr")
            return "default";       // a value type: IntPtr.Zero is not a constant
        if (IsArray(type) || IsPtr(type) || IsBoxed(type) || IsStringT(type) || type == "char*"
            || IsStringListT(type) || IsListT(type) || IsAssocT(type) || type == "QByteArrayView")
            return "null";
        return "default";
    }

    // A pointer to a scalar is an array on the managed side (QPolygon's const int* argument, a
    // byte buffer, and the out parameters Qt writes back through). char* (a string) and void*
    // (a raw handle) have their own meaning and are not arrays.
    static bool IsScalarBase(string t)
        => t != "void" && (CSharpScalar(t) != null || t is "int" or "float" or "double" or "bool");
    static bool IsArray(string t)
        => t.EndsWith("*") && t != "char*" && IsScalarBase(t.Substring(0, t.Length - 1));

    // Everything that surfaces as a managed string: QString, the views Qt declares next to it,
    // and the NUL-terminated C string convention.
    static bool IsStringT(string t) => t == "QString" || t == "QStringView" || t == "QAnyStringView";

    // The container the model carries; the runtime builds a QStringList around the managed
    // array for the duration of the call.
    static bool IsStringListT(string t) => t == "QStringList";

    // A generic container: QList<E> is marshalled element by element through a generated helper.
    static bool IsListT(string t) => t.StartsWith("QList<", StringComparison.Ordinal) && t.EndsWith(">");
    static string ListElem(string t) => t.Substring("QList<".Length, t.Length - "QList<".Length - 1);

    // The associative containers, marshalled pair by pair. QMap is ordered and QHash is not, but
    // the model publishes both as a managed Dictionary and both are walked with
    // constBegin()/key()/value().
    static bool IsMapT(string t) => t.StartsWith("QMap<", StringComparison.Ordinal) && t.EndsWith(">");
    static bool IsHashT(string t) => t.StartsWith("QHash<", StringComparison.Ordinal) && t.EndsWith(">");
    static bool IsAssocT(string t) => IsMapT(t) || IsHashT(t);
    static string AssocClass(string t) => IsHashT(t) ? "QHash" : "QMap";
    static string AssocPrefix(string t) => IsHashT(t) ? "Hash" : "Map";
    static string AssocTag(string t) => IsHashT(t) ? "hash" : "map";
    static (string Key, string Value) AssocElems(string t)
        => ApiTypes.SplitPair(t.Substring(AssocClass(t).Length + 1, t.Length - AssocClass(t).Length - 2));

    // The element kinds the generated converters distinguish: a scalar crosses the C ABI as
    // itself, a bound class as its handle, and a boxed value as a pointer to a copy.
    static bool IsScalarElem(string e) => CSharpScalar(e) != null || e == "int" || e == "float" || e == "double" || e == "bool";
    static bool IsPointerElem(string e) => e.EndsWith("*");

    // One container element crossing the C ABI, in the four directions the generated wrapper
    // needs. A QString goes as the UTF-16 pointer every other string entry point uses, and a
    // container handing an element *back* returns a heap copy, so its ABI type is void*.
    // An enum crosses the C ABI as the int it is.
    static bool IsEnumElem(string e) => IsEnum(e);
    static string ElemAbiCppParam(string e) => IsScalarElem(e) ? e : IsEnumElem(e) ? "int" : IsStringT(e) ? "const char16_t*" : "void*";
    static string ElemAbiCppRet(string e) => IsScalarElem(e) ? e : IsEnumElem(e) ? "int" : "void*";
    static string ElemAbiCsParam(string e) => IsScalarElem(e) ? ManagedType(e) : IsEnumElem(e) ? "int"
        : IsStringT(e) ? "[MarshalAs(UnmanagedType.LPWStr)] string" : "IntPtr";
    static string ElemAbiCsRet(string e) => IsScalarElem(e) ? ManagedType(e) : IsEnumElem(e) ? "int" : "IntPtr";
    // ABI -> real value (building the native container from managed data).
    static string ElemToCpp(string e, string expr) => IsScalarElem(e) ? expr
        : IsEnumElem(e) ? $"static_cast<{e}>({expr})"
        : IsStringT(e) ? $"({expr} ? QString::fromUtf16({expr}) : QString())"
        : IsPointerElem(e) ? $"static_cast<{e.Substring(0, e.Length - 1)}*>({expr})"
        : $"*static_cast<{e}*>({expr})";
    // Real value -> ABI (handing one element back to managed code; a QString has to be a copy).
    static string ElemFromCpp(string e, string expr) => IsScalarElem(e) ? expr
        : IsEnumElem(e) ? $"static_cast<int>({expr})"
        : IsStringT(e) ? $"new QString({expr})"
        : IsPointerElem(e) ? $"static_cast<void*>({expr})"
        : $"new {e}({expr})";
    // ABI -> managed value.
    static string ElemRead(string e, string expr) => IsScalarElem(e) ? expr
        : IsEnumElem(e) ? $"({CsEnum(e)}){expr}"
        : IsStringT(e) ? $"ApiRuntime.ToManaged({expr})"
        : IsPointerElem(e) ? $"{e.Substring(0, e.Length - 1)}.FromTransientHandle({expr})"
        : IsOwned(e) ? $"{e}.FromOwnedHandle({expr})" : $"{e}.FromTransientHandle({expr})";
    // Managed value -> ABI.
    static string ElemArg(string e, string expr) => IsScalarElem(e) || IsStringT(e) ? expr
        : IsEnumElem(e) ? $"(int){expr}"
        : $"{expr}?.Handle ?? IntPtr.Zero";

    // A C identifier for one element type: used for the native exports, the generated helper
    // class and the DllImport method names.
    static string ListStem(string elem)
        => elem.Replace("*", "Ptr").Replace(" ", "").Replace("::", "_").Replace("unsigned", "u");
    static string ListHelper(string elem) => "QsApiList_" + ListStem(elem);
    static string MapStem(string key, string value) => ListStem(key) + "_" + ListStem(value);
    static string AssocStem(string t)
    {
        var (key, value) = AssocElems(t);
        return MapStem(key, value);
    }
    static string AssocHelper(string t) => $"QsApi{AssocPrefix(t)}_{AssocStem(t)}";

    // The handle Qt's property-binding API hands out. Its value type is the only thing that
    // varies, so one value type gets one codec and the wrapper itself is generic.
    static bool IsBindableT(string t) => t.StartsWith("QBindable<", StringComparison.Ordinal) && t.EndsWith(">");
    static string BindableElem(string t) => t.Substring("QBindable<".Length, t.Length - "QBindable<".Length - 1);
    static string BindableHelper(string elem) => "QsApiBindable_" + ListStem(elem);
    static bool IsPtr(string t) => !IsArray(t) && t.EndsWith("*");
    static bool IsClassPtr(string t) => IsPtr(t) && ClassNames.Contains(t.Substring(0, t.Length - 1));
    static bool IsEnum(string t) => EnumNames.Contains(t);
    static bool IsFlags(string t) => FlagsEnums.Contains(t);
    static bool IsBoxed(string t) => ClassNames.Contains(t);
    static string CsEnum(string t) => t.Replace("::", ".");

    // ------------------------------------------------------------------ C#
    static string GenerateCSharp(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated> generated from model/qtsharp.api.json - do not edit");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using System.Security;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");

        WriteEnums(sb, "", 1);
        WriteEnums(sb, "Qt", 1);

        if (BindableElements().Count > 0)
            WriteBindableClass(sb);

        foreach (var cls in Classes)
            WriteClass(sb, cls);

        sb.AppendLine("    internal static partial class ApiNative");
        sb.AppendLine("    {");
        foreach (var cls in Classes)
            WriteNativeDeclarations(sb, cls);
        foreach (var elem in ListElements())
            WriteListNativeDeclarations(sb, elem);
        foreach (var assoc in AssocElements())
            WriteAssocNativeDeclarations(sb, assoc);
        foreach (var elem in BindableElements())
            WriteBindableNativeDeclarations(sb, elem);
        foreach (var boxed in VirtualBoxedReturns())
        {
            string stem = ListStem(boxed);
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_assign_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern void Assign_{stem}(IntPtr destination, IntPtr source);");
        }
        foreach (var boxed in VirtualBoxedParams())
        {
            string stem = ListStem(boxed);
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_clone_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr Clone_{stem}(IntPtr source);");
        }
        sb.AppendLine("    }");

        foreach (var elem in ListElements())
            WriteListHelper(sb, elem);
        foreach (var assoc in AssocElements())
            WriteAssocHelper(sb, assoc);
        foreach (var elem in BindableElements())
            WriteBindableHelper(sb, elem);

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Every element type that appears in a QList<...> anywhere in the model, in a stable order.
    static List<string> ListElements()
    {
        var elements = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var cls in Classes)
        {
            foreach (var fn in cls.GetProperty("methods").EnumerateArray()
                         .Concat(cls.GetProperty("virtuals").EnumerateArray())
                         .Concat(cls.GetProperty("constructors").EnumerateArray()))
            {
                var types = new List<string> { fn.TryGetProperty("returnType", out var r) ? r.GetString() : null };
                types.AddRange(fn.GetProperty("parameters").EnumerateArray().Select(PType));
                foreach (var t in types)
                    if (t != null && IsListT(t))
                        elements.Add(ListElem(t));
            }
        }
        return elements.ToList();
    }

    // The C# side of the container conversion: New/Free around a call, Read for a return.
    static void WriteListHelper(StringBuilder sb, string elem)
    {
        string helper = ListHelper(elem);
        string stem = ListStem(elem);
        string managed = ApiTypes.Managed(elem, ClassNames);
        // The element is the loop variable of the generated New(): a scalar crosses as itself,
        // a handle (class pointer or boxed value) as the raw pointer it wraps.
        string argConv = IsScalarElem(elem) ? "item" : "item?.Handle ?? IntPtr.Zero";

        sb.AppendLine();
        sb.AppendLine($"    internal static class {helper}");
        sb.AppendLine("    {");
        sb.AppendLine($"        public static IntPtr New({managed}[] items)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var list = ApiNative.List_{stem}_new();");
        sb.AppendLine("            if (items != null)");
        sb.AppendLine($"                foreach (var item in items)");
        sb.AppendLine($"                    ApiNative.List_{stem}_append(list, {argConv});");
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
        foreach (var cls in Classes)
        {
            foreach (var fn in cls.GetProperty("methods").EnumerateArray()
                         .Concat(cls.GetProperty("virtuals").EnumerateArray())
                         .Concat(cls.GetProperty("constructors").EnumerateArray()))
            {
                var all = new List<string> { fn.TryGetProperty("returnType", out var r) ? r.GetString() : null };
                all.AddRange(fn.GetProperty("parameters").EnumerateArray().Select(PType));
                foreach (var t in all)
                    if (t != null && IsAssocT(t))
                        types.Add(t);
            }
        }
        return types.ToList();
    }

    // Every value type a QBindable<...> is instantiated with, in a stable order.
    static List<string> BindableElements()
    {
        var elements = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var cls in Classes)
            foreach (var fn in cls.GetProperty("methods").EnumerateArray()
                         .Concat(cls.GetProperty("virtuals").EnumerateArray())
                         .Concat(cls.GetProperty("constructors").EnumerateArray()))
            {
                var all = new List<string> { fn.TryGetProperty("returnType", out var r) ? r.GetString() : null };
                all.AddRange(fn.GetProperty("parameters").EnumerateArray().Select(PType));
                foreach (var t in all)
                    if (t != null && IsBindableT(t))
                        elements.Add(BindableElem(t));
            }
        return elements.ToList();
    }

    // The generic wrapper, published once: a property handle that reads and writes through the
    // codec generated for its value type.
    static void WriteBindableClass(StringBuilder sb)
    {
        sb.AppendLine("    /// <summary>A handle to a Qt property (QBindable&lt;T&gt;), as the bindableFoo() accessors hand it out.</summary>");
        sb.AppendLine("    public sealed class QBindable<T> : IDisposable");
        sb.AppendLine("    {");
        sb.AppendLine("        IntPtr _handle;");
        sb.AppendLine("        readonly Func<IntPtr, T> _get;");
        sb.AppendLine("        readonly Action<IntPtr, T> _set;");
        sb.AppendLine("        readonly Action<IntPtr> _free;");
        sb.AppendLine();
        sb.AppendLine("        internal QBindable(IntPtr handle, Func<IntPtr, T> get, Action<IntPtr, T> set, Action<IntPtr> free)");
        sb.AppendLine("        {");
        sb.AppendLine("            _handle = handle;");
        sb.AppendLine("            _get = get;");
        sb.AppendLine("            _set = set;");
        sb.AppendLine("            _free = free;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public bool IsDisposed => _handle == IntPtr.Zero;");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>The property's current value; assigning writes it back through Qt.</summary>");
        sb.AppendLine("        public T Value");
        sb.AppendLine("        {");
        sb.AppendLine("            get => _get(_handle);");
        sb.AppendLine("            set => _set(_handle, value);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public void Dispose()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (_handle == IntPtr.Zero)");
        sb.AppendLine("                return;");
        sb.AppendLine("            _free(_handle);");
        sb.AppendLine("            _handle = IntPtr.Zero;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    // One value type's codec: the DllImports plus the factory the return conversion calls.
    static void WriteBindableHelper(StringBuilder sb, string elem)
    {
        string helper = BindableHelper(elem);
        string stem = ListStem(elem);
        string managed = ApiTypes.Managed(elem, ClassNames);

        sb.AppendLine();
        sb.AppendLine($"    internal static class {helper}");
        sb.AppendLine("    {");
        sb.AppendLine($"        public static QBindable<{managed}> FromOwnedHandle(IntPtr handle)");
        sb.AppendLine($"            => new QBindable<{managed}>(handle, Get, Set, ApiNative.Bindable_{stem}_free);");
        sb.AppendLine();
        sb.AppendLine($"        static {managed} Get(IntPtr handle) => {ElemRead(elem, $"ApiNative.Bindable_{stem}_value(handle)")};");
        sb.AppendLine();
        sb.AppendLine($"        static void Set(IntPtr handle, {managed} value) => ApiNative.Bindable_{stem}_setValue(handle, {ElemArg(elem, "value")});");
        sb.AppendLine("    }");
    }

    static void WriteBindableNativeDeclarations(StringBuilder sb, string elem)
    {
        string stem = ListStem(elem);
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_bindable_{stem}_value\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern {ElemAbiCsRet(elem)} Bindable_{stem}_value(IntPtr bindable);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_bindable_{stem}_setValue\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void Bindable_{stem}_setValue(IntPtr bindable, {ElemAbiCsParam(elem)} value);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_bindable_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void Bindable_{stem}_free(IntPtr bindable);");
    }

    static void WriteBindableNativeCpp(StringBuilder sb, string elem)
    {
        string stem = ListStem(elem);
        string bindable = $"QBindable<{elem}>";
        sb.AppendLine();
        sb.AppendLine($"// QBindable<{elem}>: read/write through the property Qt gave us.");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) {ElemAbiCppRet(elem)} qs_api_bindable_{stem}_value(void* bindable)");
        sb.AppendLine("{");
        sb.AppendLine($"    return {ElemFromCpp(elem, $"static_cast<{bindable}*>(bindable)->value()")};");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_bindable_{stem}_setValue(void* bindable, {ElemAbiCppParam(elem)} value)");
        sb.AppendLine("{");
        sb.AppendLine($"    static_cast<{bindable}*>(bindable)->setValue({ElemToCpp(elem, "value")});");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_bindable_{stem}_free(void* bindable)");
        sb.AppendLine("{");
        sb.AppendLine($"    delete static_cast<{bindable}*>(bindable);");
        sb.AppendLine("}");
    }

    // The C# side of the container conversion: New/Free around a call, Read for a return.
    static void WriteAssocHelper(StringBuilder sb, string t)
    {
        var (key, value) = AssocElems(t);
        string helper = AssocHelper(t);
        string prefix = AssocPrefix(t);
        string stem = AssocStem(t);
        string managedKey = ApiTypes.Managed(key, ClassNames);
        string managedValue = ApiTypes.Managed(value, ClassNames);

        sb.AppendLine();
        sb.AppendLine($"    internal static class {helper}");
        sb.AppendLine("    {");
        sb.AppendLine($"        public static IntPtr New(Dictionary<{managedKey}, {managedValue}> items)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var map = ApiNative.{prefix}_{stem}_new();");
        sb.AppendLine("            if (items != null)");
        sb.AppendLine("                foreach (var pair in items)");
        sb.AppendLine($"                    ApiNative.{prefix}_{stem}_insert(map, {ElemArg(key, "pair.Key")}, {ElemArg(value, "pair.Value")});");
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
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_new\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern IntPtr {prefix}_{stem}_new();");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_insert\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void {prefix}_{stem}_insert(IntPtr map, {abiKey} key, {abiValue} value);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_count\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern int {prefix}_{stem}_count(IntPtr map);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_key\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern {retKey} {prefix}_{stem}_key(IntPtr map, int index);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_value\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern {retValue} {prefix}_{stem}_value(IntPtr map, int index);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void {prefix}_{stem}_free(IntPtr map);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_assign_{AssocTag(t)}_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
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
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void* qs_api_{tag}_{stem}_new()");
        sb.AppendLine("{");
        sb.AppendLine($"    return new {container}();");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_{tag}_{stem}_insert(void* map, {abiKey} key, {abiValue} value)");
        sb.AppendLine("{");
        sb.AppendLine($"    static_cast<{container}*>(map)->insert({ElemToCpp(key, "key")}, {ElemToCpp(value, "value")});");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) int qs_api_{tag}_{stem}_count(const void* map)");
        sb.AppendLine("{");
        sb.AppendLine($"    return static_cast<int>(static_cast<const {container}*>(map)->size());");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) {retKey} qs_api_{tag}_{stem}_key(const void* map, int index)");
        sb.AppendLine("{");
        sb.AppendLine($"    auto it = static_cast<const {container}*>(map)->constBegin();");
        sb.AppendLine("    for (int i = 0; i < index; ++i)");
        sb.AppendLine("        ++it;");
        sb.AppendLine($"    return {ElemFromCpp(key, "it.key()")};");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) {retValue} qs_api_{tag}_{stem}_value(const void* map, int index)");
        sb.AppendLine("{");
        sb.AppendLine($"    auto it = static_cast<const {container}*>(map)->constBegin();");
        sb.AppendLine("    for (int i = 0; i < index; ++i)");
        sb.AppendLine("        ++it;");
        sb.AppendLine($"    return {ElemFromCpp(value, "it.value()")};");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_{tag}_{stem}_free(void* map)");
        sb.AppendLine("{");
        sb.AppendLine($"    delete static_cast<{container}*>(map);");
        sb.AppendLine("}");
        sb.AppendLine($"// A virtual override that returns a {container}: copy it into the caller's slot.");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_assign_{tag}_{stem}(void* destination, void* source)");
        sb.AppendLine("{");
        sb.AppendLine("    if (source)");
        sb.AppendLine($"        *static_cast<{container}*>(destination) = *static_cast<const {container}*>(source);");
        sb.AppendLine("}");
    }

    // The DllImports the generated helper class calls.
    static void WriteListNativeDeclarations(StringBuilder sb, string elem)
    {
        string stem = ListStem(elem);
        string abi = IsScalarElem(elem) ? ManagedType(elem) : "IntPtr";
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_list_{stem}_new\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern IntPtr List_{stem}_new();");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_list_{stem}_append\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void List_{stem}_append(IntPtr list, {abi} item);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_list_{stem}_count\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern int List_{stem}_count(IntPtr list);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_list_{stem}_at\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern {abi} List_{stem}_at(IntPtr list, int index);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_list_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void List_{stem}_free(IntPtr list);");
        sb.AppendLine();
        sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_assign_list_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
        sb.AppendLine($"        internal static extern void Assign_List_{stem}(IntPtr destination, IntPtr source);");
    }

    // The C++ side of the container conversion: one append/count/at per element type, with the
    // element crossing the C ABI as itself, as a handle, or as a pointer to a copy.
    static void WriteListNativeCpp(StringBuilder sb, string elem)
    {
        string stem = ListStem(elem);
        string list = $"QList<{elem}>";
        string abi = IsScalarElem(elem) ? elem : "void*";

        string appendConv = IsScalarElem(elem) ? "item"
            : IsPointerElem(elem) ? $"static_cast<{elem.Substring(0, elem.Length - 1)}*>(item)"
            : $"*static_cast<{elem}*>(item)";
        string atConv = IsScalarElem(elem)
            ? $"return static_cast<const {list}*>(list)->at(index);"
            : IsPointerElem(elem)
                ? $"return static_cast<void*>(static_cast<const {list}*>(list)->at(index));"
                : $"return new {elem}(static_cast<const {list}*>(list)->at(index));";

        sb.AppendLine();
        sb.AppendLine($"// QList<{elem}>: element-by-element conversion for the managed side.");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void* qs_api_list_{stem}_new()");
        sb.AppendLine("{");
        sb.AppendLine($"    return new {list}();");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_list_{stem}_append(void* list, {abi} item)");
        sb.AppendLine("{");
        sb.AppendLine($"    static_cast<{list}*>(list)->append({appendConv});");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) int qs_api_list_{stem}_count(const void* list)");
        sb.AppendLine("{");
        sb.AppendLine($"    return static_cast<int>(static_cast<const {list}*>(list)->size());");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) {abi} qs_api_list_{stem}_at(const void* list, int index)");
        sb.AppendLine("{");
        sb.AppendLine($"    {atConv}");
        sb.AppendLine("}");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_list_{stem}_free(void* list)");
        sb.AppendLine("{");
        sb.AppendLine($"    delete static_cast<{list}*>(list);");
        sb.AppendLine("}");
        sb.AppendLine($"// A virtual override that returns a {list}: copy it into the caller's slot.");
        sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_assign_list_{stem}(void* destination, void* source)");
        sb.AppendLine("{");
        sb.AppendLine("    if (source)");
        sb.AppendLine($"        *static_cast<{list}*>(destination) = *static_cast<const {list}*>(source);");
        sb.AppendLine("}");
    }

    static string ToCSharpElement(string elem)
        => IsScalarElem(elem) ? ManagedType(elem) : ApiTypes.Managed(elem, ClassNames);

    static string ReadElement(string elem)
    {
        string stem = ListStem(elem);
        string call = $"ApiNative.List_{stem}_at(list, i)";
        if (IsScalarElem(elem))
            return call;
        if (IsPointerElem(elem))
            return $"{elem.Substring(0, elem.Length - 1)}.FromTransientHandle({call})";
        // A boxed value comes back as a fresh copy that the wrapper owns.
        return IsOwned(elem) ? $"{elem}.FromOwnedHandle({call})" : $"{elem}.FromTransientHandle({call})";
    }

    static void WriteEnums(StringBuilder sb, string scope, int indent)
    {
        // Enums live either in the Qt namespace or nested inside the class that declares
        // them, mirroring how they are written in C++.
        var scoped = Enums.Where(e => ScopeOf(e) == scope).ToList();
        if (scoped.Count == 0)
            return;
        var pad = new string(' ', indent * 4);
        if (scope != "Qt")
        {
            // Namespace level, or nested inside the class that declares them.
            foreach (var e in scoped)
                WriteEnum(sb, e, indent);
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

    static void WriteEnum(StringBuilder sb, JsonElement e, int indent)
    {
        var pad = new string(' ', indent * 4);
        var items = e.GetProperty("items").EnumerateArray().ToList();
        var values = items.Select(i => i.GetProperty("value").GetInt64()).ToList();
        var underlying = values.Any(v => v < int.MinValue || v > int.MaxValue) ? "long" : "int";
        if (IsFlags(e.GetProperty("qualified").GetString()))
            sb.AppendLine($"{pad}[Flags]");
        sb.AppendLine($"{pad}public enum {Name(e)} : {underlying}");
        sb.AppendLine($"{pad}{{");
        foreach (var item in items)
            sb.AppendLine($"{pad}    {SafeName(Name(item))} = {item.GetProperty("value").GetInt64()},");
        sb.AppendLine($"{pad}}}");
        sb.AppendLine();
    }

    // A member cannot share a name with a nested type, so something has to give when Qt
    // declares both (QColor::Spec enum and QColor::spec()). Qt's owning type wins and the
    // accessor keeps its C++ name, which is also what the legacy pipeline does.
    //
    // The mapping is computed per class and looked up through the *declaring* class, because a
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
        foreach (var cls in Classes)
        {
            var hard = new HashSet<string>(StringComparer.Ordinal)
            {
                Name(cls), "Handle", "FromHandle", "FromOwnedHandle", "Dispose", "Dispatch",
            };
            foreach (var e in Enums.Where(e => ScopeOf(e) == Name(cls)))
                hard.Add(Name(e));
            foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
                hard.Add("As" + b);
            hardReservedByClass[Name(cls)] = hard;

            // A data member keeps its own name (it is published as the property), so its names
            // are only reserved against everything else.
            var fields = new HashSet<string>(
                FieldList(cls).Select(f => Pascal(Name(f))), StringComparer.Ordinal);

            // A property must not take the name of a virtual either: a virtual's C# name is the
            // same all the way down the chain (QWidget::sizeHint is re-declared by QCalendarWidget
            // and friends), so a property named SizeHint would leave them overriding a property.
            var virtualNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in ChainOf(Name(cls)))
                virtualNames.Add(Pascal(Name(slot.Virtual)));

            // A member cannot share a name with a nested type, so a property reserves its C#
            // name: the getter that would have taken it keeps its C++ spelling (QLineEdit::text
            // stays text(), the property is Text).
            var reserved = new HashSet<string>(hard, StringComparer.Ordinal);
            reserved.UnionWith(fields);
            foreach (var p in PropertyList(cls))
            {
                string pascal = Pascal(Name(p));
                if (!hard.Contains(pascal) && !fields.Contains(pascal) && !virtualNames.Contains(pascal))
                    reserved.Add(pascal);
            }
            reservedByClass[Name(cls)] = reserved;
            fieldNamesByClass[Name(cls)] = fields;
        }
    }

    static string MemberName(string className, string member)
        => reservedByClass.TryGetValue(className, out var reserved) && reserved.Contains(Pascal(member))
            ? member
            : Pascal(member);

    // One vtable slot a generated native subclass has to install: the method to forward, and
    // the class whose declaration of it we chain to.
    class VirtualSlot
    {
        public JsonElement Virtual;
        public string Declaring;
        public bool DeclaredHere;
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
        if (ByName.TryGetValue(className, out var cls))
        {
            var bases = cls.GetProperty("bases").EnumerateArray().Select(b => b.GetString()).ToList();
            if (bases.Count > 0)
                foreach (var inherited in ChainOf(bases[0]))
                    slots.Add(new VirtualSlot
                    {
                        Virtual = inherited.Virtual,
                        Declaring = inherited.Declaring,
                        DeclaredHere = false,
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
                    var slot = new VirtualSlot { Virtual = v, Declaring = className, DeclaredHere = true };
                    if (index >= 0)
                        slots[index] = slot;
                    else
                        slots.Add(slot);
                }
        }

        virtualChains[className] = slots;
        return slots;
    }

    static string VirtualKey(JsonElement v)
        => Name(v) + "(" + string.Join(",", Params(v).Select(PType)) + ")" + ConstSuffix(v);

    static void WriteClass(StringBuilder sb, JsonElement cls)
    {
        string name = Name(cls);
        bool boxed = cls.GetProperty("owned").GetBoolean();
        var bases = cls.GetProperty("bases").EnumerateArray().Select(b => b.GetString()).ToList();
        string baseName = bases.Count > 0 ? bases[0] : null;
        var extraBases = bases.Skip(1).ToList();
        var ctors = cls.GetProperty("constructors").EnumerateArray().ToList();
        var methods = cls.GetProperty("methods").EnumerateArray().ToList();
        var virtuals = ChainOf(Name(cls));
        var signals = cls.GetProperty("signals").EnumerateArray().ToList();

        // A class we allocate ourselves needs a release path; Qt-owned objects must not get
        // one, or the GC would free an object the parent still owns (ownership PoC rule 1).
        bool owned = boxed && (ctors.Count > 0 || SubclassCtor(cls));
        var declared = new List<string>();
        if (baseName != null)
            declared.Add(baseName);
        if (owned)
            declared.Add("IDisposable");
        // Only the root class implements it; every descendant inherits the interface and the
        // invalidation flag with it.
        if (baseName == null)
            declared.Add("IQtObjectView");
        string ifaces = declared.Count > 0 ? " : " + string.Join(", ", declared) : "";

        sb.AppendLine($"    public unsafe partial class {name}{ifaces}");
        sb.AppendLine("    {");
        if (baseName == null)
        {
            // The handle is behind a property so that a wrapper whose native object Qt destroyed
            // (or that was disposed) fails loudly instead of calling into freed memory - the same
            // contract PySide has, where a deleted C++ object raises RuntimeError.
            // internal, not private: a derived class's Dispose works on them (a private field is
            // inherited but not reachable).
            sb.AppendLine("        internal IntPtr __handle;");
            sb.AppendLine("        internal bool __invalidated;");
            sb.AppendLine();
            sb.AppendLine("        public IntPtr Handle");
            sb.AppendLine("        {");
            sb.AppendLine("            get");
            sb.AppendLine("            {");
            sb.AppendLine("                if (__invalidated)");
            sb.AppendLine("                    throw new ObjectDisposedException(GetType().Name);");
            sb.AppendLine("                return __handle;");
            sb.AppendLine("            }");
            sb.AppendLine("            protected set => __handle = value;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        void IQtObjectView.MarkDestroyed() => __invalidated = true;");
            // Whether this wrapper is the one that has to release the native object. Only a
            // constructor we published, or a boxed copy Qt handed over, sets it: a wrapper that
            // merely adopts an object (FromHandle/FromTransientHandle - the objects a view or a
            // scene owns) must never delete it at finalization, or the GC frees what Qt still
            // points at.
            sb.AppendLine("        internal bool __ownsNative;");
            sb.AppendLine();
        }

        // wrapping constructor (never allocates: it adopts an object someone else owns)
        sb.AppendLine($"        protected {name}(IntPtr handle){(baseName != null ? " : base(handle)" : "")}");
        sb.AppendLine("        {");
        if (baseName == null)
        {
            sb.AppendLine("            Handle = handle;");
            // Qt owns the object and will destroy it: the wrapper has to learn about that.
            if (DerivesFromQObject(name))
                sb.AppendLine("            ApiRuntime.Watch(handle);");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        int ctorIndex = 0;
        foreach (var ctor in ctors)
        {
            var ps = Params(ctor);
            string sig = string.Join(", ", ps.Select(ParamSig));
            sb.AppendLine($"        public {name}({sig}){(baseName != null ? " : base(IntPtr.Zero)" : "")}");
            sb.AppendLine("        {");
            // Deliberately not "context": Qt has parameters with that name (QShortcut).
            sb.AppendLine("            var __context = GCHandle.Alloc(this);");
            var marshal = MarshalParams(ps, null);
            foreach (string line in marshal.Prelude)
                sb.AppendLine("            " + line);
            string dispatch = virtuals.Count > 0
                ? "(IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch"
                : "IntPtr.Zero";
            string args = string.Join(", ", marshal.Args);
            string create = $"Handle = ApiNative.Ctor_{name}{CtorSuffix(ctorIndex++)}(GCHandle.ToIntPtr(__context), {dispatch}{(args.Length > 0 ? ", " + args : "")});";
            // This wrapper made the object, so it is the one that may release it. A QObject can
            // still be destroyed by Qt later (it gets reparented), so it is watched as well.
            string register = "            ApiRuntime.Register(Handle, this);\n            __ownsNative = true;";
            if (DerivesFromQObject(name))
                register += "\n            ApiRuntime.Watch(Handle);";
            EmitGuarded(sb, "            ", create + "\n" + register, marshal.Cleanup);
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // Q_PROPERTY declarations, as C# properties. These are pure sugar: the body calls the
        // accessor methods that are generated anyway, so no native glue is duplicated and the
        // accessors keep working under their own names.
        var virtualNames = new HashSet<string>(
            ChainOf(name).Select(s => Pascal(Name(s.Virtual))), StringComparer.Ordinal);
        foreach (var property in PropertyList(cls))
        {
            string propName = Pascal(Name(property));
            if (hardReservedByClass[name].Contains(propName) || fieldNamesByClass[name].Contains(propName)
                || virtualNames.Contains(propName))
                continue;
            string propType = property.GetProperty("type").GetString();
            string getter = property.GetProperty("getter").GetString();
            string setter = property.TryGetProperty("setter", out var s) ? s.GetString() : null;
            sb.AppendLine($"        /// <summary>The {Name(property)} property (Q_PROPERTY).</summary>");
            sb.AppendLine($"        public {ManagedType(propType)} {propName}");
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
                sb.AppendLine($"        public {managed} Get{prop}() => {read};");
                sb.AppendLine($"        public void Set{prop}({managed} value) => {write};");
                sb.AppendLine();
                continue;
            }
            sb.AppendLine($"        /// <summary>Data member {name}::{fieldName}.</summary>");
            sb.AppendLine($"        public {managed} {prop}");
            sb.AppendLine("        {");
            sb.AppendLine($"            get => {read};");
            sb.AppendLine($"            set => {write};");
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
            sb.AppendLine($"        /// <summary>Builds the native subclass a managed subclass of this abstract class dispatches through.</summary>");
            sb.AppendLine($"        protected {name}(){(baseName != null ? " : base(IntPtr.Zero)" : "")}");
            sb.AppendLine("        {");
            sb.AppendLine("            var __context = GCHandle.Alloc(this);");
            sb.AppendLine($"            Handle = ApiNative.Ctor_{name}(GCHandle.ToIntPtr(__context), {dispatch});");
            sb.AppendLine("            ApiRuntime.Register(Handle, this);");
            sb.AppendLine("            __ownsNative = true;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine($"        public static {name} FromHandle(IntPtr handle)");
        sb.AppendLine($"            => handle == IntPtr.Zero ? null : ApiRuntime.Wrap(handle, h => new {name}(h));");
        // Arguments handed to a virtual come from an object with a very short life (an event
        // lives on the caller's stack), so they must not enter the identity cache: the next
        // event would reuse the same address and get the previous wrapper back.
        sb.AppendLine();
        sb.AppendLine($"        internal static {name} FromTransientHandle(IntPtr handle)");
        sb.AppendLine($"            => handle == IntPtr.Zero ? null : new {name}(handle);");
        if (owned)
        {
            sb.AppendLine();
            // Qt handed over a copy it allocated for us, so this wrapper owns it.
            sb.AppendLine($"        internal static {name} FromOwnedHandle(IntPtr handle)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (handle == IntPtr.Zero)");
            sb.AppendLine("                return null;");
            sb.AppendLine($"            var wrapper = new {name}(handle);");
            sb.AppendLine("            wrapper.__ownsNative = true;");
            sb.AppendLine("            return wrapper;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        public void Dispose()");
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
            sb.AppendLine($"        /// <summary>View of this object as its {b} base (a separate subobject).</summary>");
            sb.AppendLine($"        public {b} As{b}() => {b}.FromHandle(ApiNative.{name}__as_{b}(Handle));");
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
            sb.AppendLine($"        public {staticMod}{ManagedType(ret)} {CSharpMemberName(mname)}({sig})");
            sb.AppendLine("        {");
            var marshal = MarshalParams(ps, IsStatic(m) ? null : "Handle");
            foreach (string line in marshal.Prelude)
                sb.AppendLine("            " + line);
            string call = $"ApiNative.{name}_{mname}{entrySuffix}({string.Join(", ", marshal.Args)})";
            // A call that hands an item over to Qt records the association, so the item's wrapper
            // dies with its owner.
            var transfer = OwnershipTransferOf(mname, ps, ret);
            // A postlude has to run between the call and the return, so the return value is kept
            // in a local when there is one.
            string body;
            if (marshal.Postlude.Count == 0 && transfer.IsItem)
                body = $"var __item = {RetToManaged(ret, call)};\n"
                     + "ApiRuntime.AssociateOwner(Handle, __item?.Handle ?? IntPtr.Zero);\n"
                     + "return __item;";
            else if (marshal.Postlude.Count == 0 && transfer.IsArgument)
                body = $"{call};\nApiRuntime.AssociateOwner(Handle, {transfer.Argument});";
            else if (marshal.Postlude.Count == 0)
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

            var ps = Params(v);
            string sig = string.Join(", ", ps.Select(ParamSig));
            string ret = ReturnType(v);
            // A base class already published this exact signature, so C# has to *override* it: a
            // plain `virtual` would only hide the base member, and a call through a base-typed
            // reference - which is how Qt and most callers see the object - would run the base
            // implementation instead (for QAbstractItemModel's pure virtuals, "no data at all").
            bool overridesBase = baseName != null && ChainOf(baseName).Any(s =>
                VirtualKey(s.Virtual) == VirtualKey(v) && ManagedType(ReturnType(s.Virtual)) == ManagedType(ret));
            sb.AppendLine($"        /// <summary>Virtual from {name}; override in a managed subclass.</summary>");
            sb.AppendLine($"        public {(overridesBase ? "override" : "virtual")} {ManagedType(ret)} {CSharpMemberName(vname)}({sig})");
            sb.AppendLine("        {");
            if (HasNoBase(v))
            {
                // Nothing to chain to: either Qt declared it pure (no implementation at all) or
                // private (an implementation we are not allowed to call). A managed subclass that
                // does not override this simply does nothing.
                sb.AppendLine("            // no base implementation to chain to");
                if (ret != "void")
                    sb.AppendLine("            return default;");
            }
            else
            {
                // The base call needs the same marshalling a normal call does: a container
                // parameter has to become a native container first.
                var baseMarshal = MarshalParams(ps, "Handle");
                foreach (string line in baseMarshal.Prelude)
                    sb.AppendLine("            " + line);
                string baseCall = $"ApiNative.{name}_{vname}__base{vSuffix}({string.Join(", ", baseMarshal.Args)})";
                string body = ret == "void" ? $"{baseCall};" : $"return {RetToManaged(ret, baseCall)};";
                EmitGuarded(sb, "            ", body, baseMarshal.Cleanup);
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // signals
        foreach (var s in signals)
        {
            string sname = Name(s);
            var ps = Params(s);
            var csTypes = ps.Select(p => ManagedType(PType(p))).ToList();
            string argType = csTypes.Count == 0 ? "Action" : "Action<" + string.Join(", ", csTypes) + ">";
            string mocSig = sname + "(" + string.Join(",", ps.Select(PType)) + ")";
            string adapter = csTypes.Count == 0
                ? "args => h()"
                : "args => h(" + string.Join(", ", csTypes.Select((t, i) => $"({t})args[{i}]")) + ")";
            sb.AppendLine($"        public QSignal<{argType}> {Pascal(sname)}Signal =>");
            sb.AppendLine($"            new QSignal<{argType}>(h => ApiRuntime.Connect(Handle, \"{mocSig}\", {adapter}));");
            sb.AppendLine();
        }

        // native subclass forwarding the virtuals into Dispatch
        if (virtuals.Count > 0)
        {
            sb.AppendLine("        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
            string modifier = baseName != null ? "new " : "";
            sb.AppendLine($"        internal {modifier}static void Dispatch(IntPtr userData, int methodId, IntPtr argv)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var target = ({name})GCHandle.FromIntPtr(userData).Target;");
            sb.AppendLine("            switch (methodId)");
            sb.AppendLine("            {");
            for (int i = 0; i < virtuals.Count; i++)
            {
                var v = virtuals[i].Virtual;
                var ps = Params(v);
                // The slot id is the position in the whole chain, and an inherited slot is
                // dispatched into the member the declaring class already published.
                string member = MemberName(virtuals[i].Declaring, Name(v));
                sb.AppendLine($"                case {i + 1}: // {Name(v)}");
                sb.AppendLine("                {");
                var reads = ps.Select((p, a) => DispatchRead(PType(p), a + 1)).ToList();
                string args = string.Join(", ", reads);
                if (ReturnType(v) == "void")
                    sb.AppendLine($"                    target.{member}({args});");
                else
                {
                    sb.AppendLine($"                    var result = target.{member}({args});");
                    sb.AppendLine("                    " + DispatchStore(ReturnType(v), "result"));
                }
                sb.AppendLine("                    break;");
                sb.AppendLine("                }");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        WriteEnums(sb, name, 2);

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static string CtorSuffix(int index) => index == 0 ? "" : (index + 1).ToString();

    // Stores the value a managed override produced into the slot the native caller provided.
    // Scalars go through the runtime's typed writers; a boxed value needs a per-type assignment
    // (the caller's slot holds the value itself, not a pointer to it).
    static string DispatchStore(string t, string value)
    {
        string dest = "Marshal.ReadIntPtr(argv, 0)";
        if (t == "void")
            return "";
        if (IsEnum(t))
            return $"ApiRuntime.WriteResult({dest}, (int){value});";
        if (t == "TimeSpan")
            return $"ApiRuntime.WriteResult({dest}, (long)({value}.Ticks * 100));";
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
    // Qt owns - a wrapper over it would delete it at finalization).
    static List<string> VirtualBoxedParams()
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var cls in Classes)
            foreach (var v in cls.GetProperty("virtuals").EnumerateArray())
                foreach (var p in v.GetProperty("parameters").EnumerateArray())
                {
                    var t = PType(p);
                    if (IsBoxed(t))
                        types.Add(t);
                }
        return types.ToList();
    }

    // The boxed types a virtual may return: each needs the assignment helper above.
    static List<string> VirtualBoxedReturns()
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var cls in Classes)
            foreach (var v in cls.GetProperty("virtuals").EnumerateArray())
            {
                var ret = ReturnType(v);
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
            if (IsStringByRef(p))
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
        var ctors = cls.GetProperty("constructors").EnumerateArray().ToList();
        var methods = cls.GetProperty("methods").EnumerateArray().ToList();
        var virtuals = ChainOf(Name(cls));

        var virtualOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in virtuals.Where(s => s.DeclaredHere))
        {
            var v = slot.Virtual;
            string vname = Name(v);
            virtualOverloads.TryGetValue(vname, out int vseen);
            virtualOverloads[vname] = vseen + 1;
            string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
            // Nothing to chain to, so no forwarder to declare (the suffix counter is still
            // advanced above, to stay aligned with the other three emitters).
            if (HasNoBase(v))
                continue;
            var ps = Params(v);
            string sig = string.Join(", ", new[] { "IntPtr self" }.Concat(ps.SelectMany(NativeParamsFor)));
            string ret = ReturnType(v);
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}_{vname}__base{vSuffix}\", CallingConvention = CallingConvention.Cdecl)]");
            if (ret == "bool")
                sb.AppendLine($"        [return: MarshalAs(UnmanagedType.I1)]");
            sb.AppendLine($"        internal static extern {NativeRetType(ret)} {name}_{vname}__base{vSuffix}({sig});");
        }

        for (int i = 0; i < ctors.Count; i++)
        {
            var ps = Params(ctors[i]);
            string sig = string.Join(", ", new[] { "IntPtr userData", "IntPtr dispatch" }
                .Concat(ps.SelectMany(NativeParamsFor)));
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}_ctor{CtorSuffix(i)}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr Ctor_{name}{CtorSuffix(i)}({sig});");
        }

        if (SubclassCtor(cls))
        {
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}_ctor\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr Ctor_{name}(IntPtr userData, IntPtr dispatch);");
        }

        foreach (var field in FieldList(cls))
        {
            string fieldName = Name(field);
            string fieldType = field.GetProperty("type").GetString();
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}_get_{fieldName}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern {ElemAbiCsRet(fieldType)} {name}_get_{fieldName}(IntPtr self);");
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}_set_{fieldName}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern void {name}_set_{fieldName}(IntPtr self, {ElemAbiCsParam(fieldType)} value);");
        }

        if (cls.GetProperty("owned").GetBoolean() && (ctors.Count > 0 || SubclassCtor(cls)))
        {
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}_dtor\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern void Destroy_{name}(IntPtr self);");
        }

        foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
        {
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}__as_{b}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr {name}__as_{b}(IntPtr self);");
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
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}_{mname}{entrySuffix}\", CallingConvention = CallingConvention.Cdecl)]");
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
        if (IsPtr(t)) return IsClassPtr(t) ? $"{t.Substring(0, t.Length - 1)}.FromTransientHandle({slot})" : slot;
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
        // A duration crosses as nanoseconds.
        if (t == "TimeSpan") return $"TimeSpan.FromTicks(Marshal.ReadInt64({slot}) / 100)";
        if (IsStringListT(t)) return $"ApiRuntime.ToManagedStringListTransient({slot})";
        // A container Qt hands to an override is Qt's: copy it out without freeing it.
        if (IsListT(t)) return $"{ListHelper(ListElem(t))}.ReadTransient({slot})";
        if (IsAssocT(t)) return $"{AssocHelper(t)}.ReadTransient({slot})";
        // A boxed value is a *copy*: the slot points at the argument (or at a temporary Qt
        // allocated), and the wrapper must own its own object.
        if (IsBoxed(t) && VirtualBoxedParams().Contains(t))
            return $"{t}.FromOwnedHandle(ApiNative.Clone_{ListStem(t)}({slot}))";
        if (IsBoxed(t)) return $"{t}.FromHandle({slot})";
        return slot;
    }

    // ------------------------------------------------------------------ type maps
    static string ManagedType(string t)
    {
        if (IsArray(t)) return ApiTypes.Managed(t, ClassNames);
        if (t == "char*") return "string";
        if (IsListT(t) || IsAssocT(t) || IsBindableT(t)) return ApiTypes.Managed(t, ClassNames);
        if (IsPtr(t)) return IsClassPtr(t) ? t.Substring(0, t.Length - 1) : "IntPtr";
        if (IsEnum(t)) return CsEnum(t);
        return ApiTypes.Managed(t, ClassNames);
    }

    static string NativeParamType(string t)
    {
        if (IsArray(t)) return ManagedType(t);
        if (t == "char*") return "[MarshalAs(UnmanagedType.LPUTF8Str)] string";
        if (t == "TimeSpan") return "long";
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
        if (!IsByRef(p))
            return NativeParamType(PType(p));
        string keyword = IsOut(p) ? "out " : "ref ";
        string managed = ParamManagedType(p);
        return managed == "bool" ? $"[MarshalAs(UnmanagedType.I1)] {keyword}bool" : keyword + managed;
    }

    // One model parameter is not always one native parameter: an in-out string contributes the
    // string it goes in as, plus the out slot the updated copy comes back through.
    static List<string> NativeParamsFor(JsonElement p)
    {
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
        if (!IsStringByRef(p))
            return new List<string> { $"{CppParamTypeFor(p)} {NativeName(PName(p))}" };
        return new List<string>
        {
            $"const char16_t* {NativeName(PName(p))}",
            $"void** {NativeName(PName(p))}_out",
        };
    }

    // The call takes the local QString; the out pointer is filled after the call returns.
    static List<string> CppArgsFor(JsonElement p)
        => new() { IsStringByRef(p) ? StringInLocal(NativeName(PName(p))) : CppArgFor(p) };



    // The body of an exported forwarder. Most are a single expression; an in-out string needs a
    // local to pass by reference and a statement that publishes the result.
    static List<string> NativeBody(JsonElement m, string call)
    {
        string ret = ReturnType(m);
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
        if (t == "void" || t == "bool" || t == "int" || t == "float" || t == "double")
            return t;
        var scalar = CSharpScalar(t);
        if (scalar != null)
            return scalar;
        // A duration crosses as nanoseconds.
        if (t == "TimeSpan") return "long";
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
        "unsigned char" => "byte",
        "char16_t" => "char",
        _ => null,
    };

    static string ArgToNative(string t, string name)
    {
        if (IsArray(t) || IsStringT(t) || t == "char*") return name;
        if (t == "TimeSpan") return $"(long)({name}.Ticks * 100)";
        if (IsStringListT(t) || IsListT(t) || IsAssocT(t) || t == "QByteArrayView") return $"{name}Handle";
        if (IsEnum(t)) return $"(int){name}";
        // Opaque pointers (QEvent*) are already raw handles and must not be dereferenced.
        if (IsClassPtr(t) || IsBoxed(t)) return $"{name}?.Handle ?? IntPtr.Zero";
        return name;
    }

    static string RetToManaged(string t, string call)
    {
        // The native side hands back a newly allocated QString for both string conventions.
        if (IsStringT(t) || t == "char*") return $"ApiRuntime.ToManaged({call})";
        if (t == "TimeSpan") return $"TimeSpan.FromTicks({call} / 100)";
        if (IsStringListT(t)) return $"ApiRuntime.ToManagedStringList({call})";
        if (IsListT(t)) return $"{ListHelper(ListElem(t))}.Read({call})";
        if (IsAssocT(t))
            return $"{AssocHelper(t)}.Read({call})";
        if (IsBindableT(t))
            return $"{BindableHelper(BindableElem(t))}.FromOwnedHandle({call})";
        if (t == "QByteArrayView") return $"ApiRuntime.ToManagedBytes({call})";
        if (IsEnum(t)) return $"({CsEnum(t)}){call}";
        if (IsClassPtr(t)) return $"{t.Substring(0, t.Length - 1)}.FromHandle({call})";
        if (IsBoxed(t)) return IsOwned(t) ? $"{t}.FromOwnedHandle({call})" : $"{t}.FromTransientHandle({call})";
        return call;
    }

    // ------------------------------------------------------------------ C++
    static string GenerateNative()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated> generated from model/qtsharp.api.json - do not edit");
        foreach (var header in Classes.Select(c => c.GetProperty("header").GetString()).Distinct().OrderBy(h => h, StringComparer.Ordinal))
            sb.AppendLine($"#include <{header}>");
        // The property-binding templates: the class headers only forward-declare QBindable<T>,
        // and the generated wrapper instantiates it.
        if (BindableElements().Count > 0)
            sb.AppendLine("#include <qproperty.h>");
        sb.AppendLine("#include \"QtSharpApiRuntime.h\"");
        sb.AppendLine();
        // A managed null for a boxed parameter means "nothing was passed". Most parameters can
        // take a default-constructed value, but a reference parameter needs an lvalue, so it
        // gets a process-wide instance instead.
        sb.AppendLine("template <class T> T& QsApiDefaultValue() { static T value{}; return value; }");
        sb.AppendLine();

        foreach (var cls in Classes)
            WriteNativeClass(sb, cls);

        foreach (var elem in ListElements())
            WriteListNativeCpp(sb, elem);

        foreach (var assoc in AssocElements())
            WriteAssocNativeCpp(sb, assoc);

        foreach (var elem in BindableElements())
            WriteBindableNativeCpp(sb, elem);

        // A managed override that returns a value type hands the value over by copying it into
        // the caller's slot.
        foreach (var boxed in VirtualBoxedReturns())
        {
            string stem = ListStem(boxed);
            sb.AppendLine();
            sb.AppendLine($"// {boxed}: the value a managed override returned, copied into the caller's slot.");
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_assign_{stem}(void* destination, void* source)");
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
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void* qs_api_clone_{stem}(const void* source)");
            sb.AppendLine("{");
            sb.AppendLine($"    return source ? new {boxed}(*static_cast<const {boxed}*>(source)) : nullptr;");
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    static void WriteNativeClass(StringBuilder sb, JsonElement cls)
    {
        string name = Name(cls);
        var ctors = cls.GetProperty("constructors").EnumerateArray().ToList();
        var methods = cls.GetProperty("methods").EnumerateArray().ToList();
        var virtuals = ChainOf(Name(cls));

        var declaredVirtuals = virtuals.Where(s => s.DeclaredHere).ToList();

        // The native subclass exists to install the vtable hooks of a managed subclass. A class
        // C++ would not let us derive from (private destructor: QClipboard, QStyleHints) simply
        // gets no subclass - its methods are still reachable through the free forwarders below.
        bool subclassable = Subclassable(cls);
        bool subclassCtor = SubclassCtor(cls);
        if (subclassable)
        {
        sb.AppendLine($"class QsApi_{name} : public {name}");
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
            sb.AppendLine($"        : {name}({args}), m_userData(userData), m_dispatch(dispatch) {{}}");
        }
        if (subclassCtor)
        {
            // The abstract class has no constructor of its own in the model; this is the one a
            // managed subclass uses, and it is concrete because every pure virtual this class
            // inherits is an override point we install.
            sb.AppendLine($"    QsApi_{name}(void* userData, QsApiDispatch dispatch)");
            sb.AppendLine($"        : {name}(), m_userData(userData), m_dispatch(dispatch) {{}}");
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
            if (HasNoBase(v))
                continue;
            var ps = Params(v);
            string sig = string.Join(", ", ps.Select(p => $"{CppVirtualParamType(p)} {NativeName(PName(p))}"));
            string args = string.Join(", ", ps.Select(p => NativeName(PName(p))));
            string ret = ReturnType(v);
            sb.AppendLine($"    static {ret} CallBase_{vname}{vSuffix}(QsApi_{name}* self{(ps.Count > 0 ? ", " + sig : "")})");
            sb.AppendLine("    {");
            sb.AppendLine($"        {(ret == "void" ? "" : "return ")}self->{name}::{vname}({args});");
            sb.AppendLine("    }");
        }
        sb.AppendLine();
        sb.AppendLine("protected:");
        for (int i = 0; i < virtuals.Count; i++)
        {
            var v = virtuals[i].Virtual;
            var ps = Params(v);
            string sig = string.Join(", ", ps.Select(p => $"{CppVirtualParamType(p)} {NativeName(PName(p))}"));
            string ret = ReturnType(v);
            sb.AppendLine($"    {ret} {Name(v)}({sig}){ConstSuffix(v)} override");
            sb.AppendLine("    {");
            if (ret != "void")
                sb.AppendLine($"        {ret} result = {{}};");
            var argv = new List<string> { ret == "void" ? "nullptr" : "&result" };
            argv.AddRange(ps.Select(p => DispatchArgStore(PType(p), NativeName(PName(p)))));
            sb.AppendLine($"        void* argv[] = {{ {string.Join(", ", argv)} }};");
            sb.AppendLine($"        m_dispatch(m_userData, {i + 1}, argv);");
            if (ret != "void")
                sb.AppendLine("        return result;");
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
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void* qs_api_{name}_ctor(void* userData, QsApiDispatch dispatch)");
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
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void* qs_api_{name}_ctor{CtorSuffix(i)}({sig})");
            sb.AppendLine("{");
            sb.AppendLine($"    return new QsApi_{name}({args});");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (subclassable && cls.GetProperty("owned").GetBoolean() && (ctors.Count > 0 || subclassCtor))
        {
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_{name}_dtor(void* self)");
            sb.AppendLine("{");
            if (DerivesFromQObject(name))
            {
                // A QObject's owner can move to Qt at runtime (setParent, or a layout adopting the
                // widget), and deleting something Qt owns is a double free - the crash the legacy
                // pipeline hit with QCommandLinkButton. Qt is asked first; an object it does not
                // own is still released, so nothing leaks.
                sb.AppendLine($"    {name}* object = static_cast<{name}*>(self);");
                sb.AppendLine("    if (object->parent())");
                sb.AppendLine("        return;");
                sb.AppendLine("    delete object;");
            }
            else
            {
                sb.AppendLine($"    delete static_cast<{name}*>(self);");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
        {
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void* qs_api_{name}__as_{b}(void* self)");
            sb.AppendLine("{");
            sb.AppendLine($"    return static_cast<{b}*>(static_cast<{name}*>(self));");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // A data member is inline storage, so its accessors are generated rather than exported.
        foreach (var field in FieldList(cls))
        {
            string fieldName = Name(field);
            string fieldType = field.GetProperty("type").GetString();
            string access = $"static_cast<{name}*>(self)->{fieldName}";
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
            sb.AppendLine($"extern \"C\" __declspec(dllexport) {ElemAbiCppRet(fieldType)} qs_api_{name}_get_{fieldName}(void* self)");
            sb.AppendLine("{");
            sb.AppendLine($"    return {getter};");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_{name}_set_{fieldName}(void* self, {ElemAbiCppParam(fieldType)} value)");
            sb.AppendLine("{");
            sb.AppendLine($"    static_cast<{name}*>(self)->{fieldName} = {setter};");
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
            string call = IsStatic(m)
                ? $"{name}::{mname}({args})"
                : $"static_cast<{name}*>(self)->{mname}({args})";
            sb.AppendLine($"extern \"C\" __declspec(dllexport) {NativeRetCpp(ret)} qs_api_{name}_{mname}{entrySuffix}({sig})");
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
        var virtualOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < declaredVirtuals.Count && subclassable; i++)
        {
            var v = declaredVirtuals[i].Virtual;
            string vname = Name(v);
            virtualOverloads.TryGetValue(vname, out int vseen);
            virtualOverloads[vname] = vseen + 1;
            string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
            if (HasNoBase(v))
                continue;

            var ps = Params(v);
            string sig = string.Join(", ", new[] { "void* self" }
                .Concat(ps.SelectMany(CppParamsFor)));
            string ret = ReturnType(v);
            string baseArgs = string.Join(", ", new[] { $"static_cast<QsApi_{name}*>(self)" }
                .Concat(ps.SelectMany(CppArgsFor)));
            sb.AppendLine($"extern \"C\" __declspec(dllexport) {NativeRetCpp(ret)} qs_api_{name}_{vname}__base{vSuffix}({sig})");
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
        // A duration arrives as nanoseconds and has to become the unit Qt declared
        // (std::chrono::milliseconds), which is why a duration parameter carries its clang
        // spelling.
        if (PType(p) == "TimeSpan")
            return $"std::chrono::duration_cast<{p.GetProperty("cpp").GetString()}>(std::chrono::nanoseconds({NativeName(PName(p))}))";
        if (IsRef(p))
            return "*" + NativeName(PName(p));
        return CppArg(PType(p), NativeName(PName(p)));
    }

    // The generated subclass's constructor takes the *real* C++ parameter types, so it is called
    // with the C ABI argument converted straight to that type - which for everything but a
    // by-reference parameter is the name itself.
    static string CppCtorArgFor(JsonElement p)
        => IsRef(p) ? "*" + NativeName(PName(p)) : NativeName(PName(p));

    static string CppParamType(string t)
    {
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
        if (IsStringListT(t) || IsListT(t) || IsAssocT(t) || t == "QByteArrayView") return "void*";
        // A bindable parameter would need a native QBindable the managed side cannot make; the
        // extractor rejects those, so this is only a guard.
        if (IsBindableT(t)) return "void*";
        if (IsPtr(t) || IsBoxed(t)) return "void*";
        if (IsEnum(t)) return "int";
        return t;
    }

    // The real C++ types, used where the generated code has to speak to Qt directly.
    static string CppCtorParamType(string t)
    {
        if (IsStringT(t)) return "QString";
        if (t == "char*") return "const char*";
        if (t == "uchar*") return "unsigned char*";
        if (IsArray(t)) return "const " + t;
        if (IsStringListT(t)) return "const QStringList&";
        if (IsListT(t) || IsAssocT(t)) return $"const {t}&";
        // A byte[] argument is carried as a QByteArray, which converts to the view implicitly.
        if (t == "QByteArrayView") return "const QByteArray&";
        if (IsEnum(t) || IsPtr(t)) return t;
        if (IsBoxed(t)) return $"const {t}&";
        return t;
    }

    static string CppArg(string t, string name)
    {
        // A null managed string must not reach QString::fromUtf16 as a null pointer.
        if (IsStringT(t)) return $"{name} ? QString::fromUtf16({name}) : QString()";
        if (IsStringListT(t)) return $"*static_cast<QStringList*>({name})";
        if (IsListT(t) || IsAssocT(t)) return $"*static_cast<{t}*>({name})";
        if (t == "QByteArrayView") return $"*static_cast<QByteArray*>({name})";
        if (t == "char*" || t == "uchar*") return name;      // already a matching pointer
        if (IsPtr(t)) return $"static_cast<{t.Substring(0, t.Length - 1)}*>({name})";
        // QFlags has no cast from int; the named constructor is the documented way back.
        if (IsFlags(t)) return $"{t}::fromInt({name})";
        if (IsEnum(t)) return $"static_cast<{t}>({name})";
        // A managed null would otherwise be dereferenced; a default-constructed value is the
        // closest thing to "the parameter was not given".
        if (IsBoxed(t))
            return HasDefaultCtor(t)
                ? $"({name} ? *static_cast<{t}*>({name}) : QsApiDefaultValue<{t}>())"
                : $"*static_cast<{t}*>({name})";
        return name;
    }

    static string NativeRetCpp(string t)
    {
        // The scalar types the C ABI can return directly; everything else comes back as a
        // pointer to something the managed side converts.
        if (t == "void" || t == "bool" || t == "int" || t == "float" || t == "double")
            return t;
        if (CSharpScalar(t) != null)
            return t;
        if (t == "TimeSpan") return "long long";
        return IsEnum(t) ? "int" : "void*";
    }

    static string NativeRetExpr(string t, string call)
    {
        if (t == "void") return call + ";";
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
                     + $"        auto* qs_list = new {t}();\n"
                     + "        for (auto qs_item : qs_source)\n"
                     + $"            qs_list->append(const_cast<{elem}>(qs_item));\n"
                     + "        return qs_list;\n"
                     + "    }";
            return $"return new {t}({call});";
        }
        if (IsAssocT(t)) return $"return new {t}({call});";
        // The bindable handle is a small value; managed code gets a heap copy it disposes.
        if (IsBindableT(t)) return $"return new {t}({call});";
        if (t == "QByteArrayView") return $"return new QByteArray({call});";
        // A duration comes back as nanoseconds whatever unit Qt used.
        if (t == "TimeSpan")
            return $"return static_cast<long long>(std::chrono::duration_cast<std::chrono::nanoseconds>({call}).count());";
        if (t == "char*") return $"{{ const char* text = ({call}); return new QString(text ? QString::fromUtf8(text) : QString()); }}";
        if (IsFlags(t)) return $"return static_cast<int>({call}.toInt());";
        if (IsEnum(t)) return $"return static_cast<int>({call});";
        if (IsBoxed(t)) return $"return new {t}({call});";
        // Iterators and other const accessors return const pointers; the C ABI is void*.
        if (IsPtr(t)) return $"return static_cast<void*>(const_cast<{t.Substring(0, t.Length - 1)}*>({call}));";
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
        if (IsPtr(t)) return t.Substring(0, t.Length - 1) + "*";
        if (IsEnum(t)) return t;
        // An override has to repeat the base declaration exactly, and Qt passes its value types
        // by const reference (QAbstractItemDelegate::sizeHint(const QStyleOptionViewItem&, ...)).
        if (IsBoxed(t)) return $"const {t}&";
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
