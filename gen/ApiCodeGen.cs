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
           && cls.GetProperty("constructors").GetArrayLength() > 0;

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

    // Qt's headers are full of default arguments; carrying them into C# keeps navigation
    // like `new QObject()` or `SetRenderHint(hint)` working.
    static string ParamSig(JsonElement p)
    {
        string type = PType(p);
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
            || IsStringListT(type) || IsListT(type) || type == "QByteArrayView")
            return "null";
        return "default";
    }

    static bool IsArray(string t) => t == "int*" || t == "uchar*";

    // Everything that surfaces as a managed string: QString, the views Qt declares next to it,
    // and the NUL-terminated C string convention.
    static bool IsStringT(string t) => t == "QString" || t == "QStringView" || t == "QAnyStringView";

    // The container the model carries; the runtime builds a QStringList around the managed
    // array for the duration of the call.
    static bool IsStringListT(string t) => t == "QStringList";

    // A generic container: QList<E> is marshalled element by element through a generated helper.
    static bool IsListT(string t) => t.StartsWith("QList<", StringComparison.Ordinal) && t.EndsWith(">");
    static string ListElem(string t) => t.Substring("QList<".Length, t.Length - "QList<".Length - 1);

    // The element kinds the generated converters distinguish: a scalar crosses the C ABI as
    // itself, a bound class as its handle, and a boxed value as a pointer to a copy.
    static bool IsScalarElem(string e) => CSharpScalar(e) != null || e == "int" || e == "float" || e == "double" || e == "bool";
    static bool IsPointerElem(string e) => e.EndsWith("*");

    // A C identifier for one element type: used for the native exports, the generated helper
    // class and the DllImport method names.
    static string ListStem(string elem)
        => elem.Replace("*", "Ptr").Replace(" ", "").Replace("::", "_").Replace("unsigned", "u");
    static string ListHelper(string elem) => "QsApiList_" + ListStem(elem);
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
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using System.Security;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");

        WriteEnums(sb, "", 1);
        WriteEnums(sb, "Qt", 1);

        foreach (var cls in Classes)
            WriteClass(sb, cls);

        sb.AppendLine("    internal static partial class ApiNative");
        sb.AppendLine("    {");
        foreach (var cls in Classes)
            WriteNativeDeclarations(sb, cls);
        foreach (var elem in ListElements())
            WriteListNativeDeclarations(sb, elem);
        foreach (var boxed in VirtualBoxedReturns())
        {
            string stem = ListStem(boxed);
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_assign_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern void Assign_{stem}(IntPtr destination, IntPtr source);");
        }
        sb.AppendLine("    }");

        foreach (var elem in ListElements())
            WriteListHelper(sb, elem);

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
        sb.AppendLine($"        public static {managed}[] Read(IntPtr list)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (list == IntPtr.Zero)");
        sb.AppendLine("                return null;");
        sb.AppendLine($"            int count = ApiNative.List_{stem}_count(list);");
        sb.AppendLine($"            var result = new {managed}[count];");
        sb.AppendLine("            for (int i = 0; i < count; i++)");
        sb.AppendLine($"                result[i] = {ReadElement(elem)};");
        sb.AppendLine($"            ApiNative.List_{stem}_free(list);");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
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

    static void PrepareNames()
    {
        reservedByClass = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var cls in Classes)
        {
            var reserved = new HashSet<string>(StringComparer.Ordinal)
            {
                Name(cls), "Handle", "FromHandle", "FromOwnedHandle", "Dispose", "Dispatch",
            };
            foreach (var e in Enums.Where(e => ScopeOf(e) == Name(cls)))
                reserved.Add(Name(e));
            foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
                reserved.Add("As" + b);
            reservedByClass[Name(cls)] = reserved;
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
        bool owned = boxed && ctors.Count > 0;
        var declared = new List<string>();
        if (baseName != null)
            declared.Add(baseName);
        if (owned)
            declared.Add("IDisposable");
        string ifaces = declared.Count > 0 ? " : " + string.Join(", ", declared) : "";

        sb.AppendLine($"    public unsafe partial class {name}{ifaces}");
        sb.AppendLine("    {");
        if (baseName == null)
        {
            sb.AppendLine("        public IntPtr Handle { get; protected set; }");
            sb.AppendLine();
        }

        // wrapping constructor (never allocates: it adopts an object someone else owns)
        sb.AppendLine($"        protected {name}(IntPtr handle){(baseName != null ? " : base(handle)" : "")}");
        sb.AppendLine("        {");
        if (baseName == null)
            sb.AppendLine("            Handle = handle;");
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
            EmitGuarded(sb, "            ", create + "\n            ApiRuntime.Register(Handle, this);", marshal.Cleanup);
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
            sb.AppendLine($"        internal static {name} FromOwnedHandle(IntPtr handle)");
            sb.AppendLine($"            => handle == IntPtr.Zero ? null : new {name}(handle);");
            sb.AppendLine();
            sb.AppendLine($"        public void Dispose()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (Handle == IntPtr.Zero)");
            sb.AppendLine("                return;");
            sb.AppendLine($"            ApiNative.Destroy_{name}(Handle);");
            sb.AppendLine("            Handle = IntPtr.Zero;");
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
            string body = ret == "void" ? $"{call};" : $"return {RetToManaged(ret, call)};";
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
            sb.AppendLine($"        /// <summary>Virtual from {name}; override in a managed subclass.</summary>");
            sb.AppendLine($"        public virtual {ManagedType(ret)} {CSharpMemberName(vname)}({sig})");
            sb.AppendLine("        {");
            if (IsPure(v))
            {
                // Nothing to chain to: a managed subclass that does not override this simply
                // does nothing (Qt never calls a pure virtual it has no implementation for).
                sb.AppendLine("            // pure virtual: no base implementation to call");
                if (ret != "void")
                    sb.AppendLine("            return default;");
            }
            else
            {
                string args = string.Join(", ", new[] { "Handle" }.Concat(ps.Select(p => ArgToNative(PType(p), SafeName(PName(p))))));
                string baseCall = $"ApiNative.{name}_{vname}__base{vSuffix}({args})";
                if (ret == "void")
                    sb.AppendLine($"            {baseCall};");
                else
                    sb.AppendLine($"            return {RetToManaged(ret, baseCall)};");
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
        if (IsBoxed(t))
            return $"ApiNative.Assign_{ListStem(t)}({dest}, {value}?.Handle ?? IntPtr.Zero);";
        if (IsPtr(t))
            return $"Marshal.WriteIntPtr({dest}, {value}?.Handle ?? IntPtr.Zero);";
        if (IsStringT(t) || t == "char*")
            return $"ApiRuntime.WriteResult({dest}, {value});";
        return $"ApiRuntime.WriteResult({dest}, {value});";
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

    // A parameter that is not a plain scalar/pointer/handle needs a temporary native object for
    // the duration of the call: the managed array becomes a QStringList here, and the cleanup
    // runs in a finally so an exception cannot leak it.
    static (List<string> Prelude, List<string> Args, List<string> Cleanup) MarshalParams(
        IEnumerable<JsonElement> ps, string self)
    {
        var prelude = new List<string>();
        var args = new List<string>();
        var cleanup = new List<string>();
        if (self != null)
            args.Add(self);
        foreach (var p in ps)
        {
            string t = PType(p), n = SafeName(PName(p));
            if (IsStringListT(t))
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
        return (prelude, args, cleanup);
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
            var ps = Params(v);
            string sig = string.Join(", ", new[] { "IntPtr self" }.Concat(ps.Select(p => $"{NativeParamType(PType(p))} {SafeName(PName(p))}")));
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
                .Concat(ps.Select(p => $"{NativeParamType(PType(p))} {SafeName(PName(p))}")));
            sb.AppendLine();
            sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(ApiRuntime.Library, EntryPoint = \"qs_api_{name}_ctor{CtorSuffix(i)}\", CallingConvention = CallingConvention.Cdecl)]");
            sb.AppendLine($"        internal static extern IntPtr Ctor_{name}{CtorSuffix(i)}({sig});");
        }

        if (cls.GetProperty("owned").GetBoolean() && ctors.Count > 0)
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
            nativeParams.AddRange(ps.Select(p => $"{NativeParamType(PType(p))} {SafeName(PName(p))}"));
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
        // The override receives Qt's value types by const reference, so the address has to be
        // taken off the const.
        => IsBoxed(t) ? $"const_cast<{t}*>(&{name})" : IsPtr(t) ? name : "&" + name;

    static string DispatchRead(string t, int index)
    {
        string slot = $"Marshal.ReadIntPtr(argv, {index} * IntPtr.Size)";
        if (IsPtr(t)) return IsClassPtr(t) ? $"{t.Substring(0, t.Length - 1)}.FromTransientHandle({slot})" : slot;
        if (t == "bool") return $"Marshal.ReadByte({slot}) != 0";
        if (t == "int") return $"Marshal.ReadInt32({slot})";
        if (t == "double") return $"BitConverter.Int64BitsToDouble(Marshal.ReadInt64({slot}))";
        if (t == "QString") return $"ApiRuntime.ToManaged({slot})";
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
        if (IsBoxed(t)) return $"{t}.FromHandle({slot})";
        return slot;
    }

    // ------------------------------------------------------------------ type maps
    static string ManagedType(string t)
    {
        if (IsArray(t)) return ApiTypes.Managed(t, ClassNames);
        if (t == "char*") return "string";
        if (IsListT(t)) return ApiTypes.Managed(t, ClassNames);
        if (IsPtr(t)) return IsClassPtr(t) ? t.Substring(0, t.Length - 1) : "IntPtr";
        if (IsEnum(t)) return CsEnum(t);
        return ApiTypes.Managed(t, ClassNames);
    }

    static string NativeParamType(string t)
    {
        if (IsArray(t)) return ManagedType(t);
        if (t == "char*") return "[MarshalAs(UnmanagedType.LPUTF8Str)] string";
        if (IsStringListT(t) || IsListT(t) || t == "QByteArrayView") return "IntPtr";
        if (IsPtr(t) || IsBoxed(t)) return "IntPtr";
        if (IsEnum(t)) return "int";
        if (IsStringT(t)) return "[MarshalAs(UnmanagedType.LPWStr)] string";
        return CSharpScalar(t) ?? t;
    }

    static string NativeRetType(string t)
    {
        if (t == "void" || t == "bool" || t == "int" || t == "float" || t == "double")
            return t;
        var scalar = CSharpScalar(t);
        if (scalar != null)
            return scalar;
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
        if (IsStringListT(t) || IsListT(t) || t == "QByteArrayView") return $"{name}Handle";
        if (IsEnum(t)) return $"(int){name}";
        // Opaque pointers (QEvent*) are already raw handles and must not be dereferenced.
        if (IsClassPtr(t) || IsBoxed(t)) return $"{name}?.Handle ?? IntPtr.Zero";
        return name;
    }

    static string RetToManaged(string t, string call)
    {
        // The native side hands back a newly allocated QString for both string conventions.
        if (IsStringT(t) || t == "char*") return $"ApiRuntime.ToManaged({call})";
        if (IsStringListT(t)) return $"ApiRuntime.ToManagedStringList({call})";
        if (IsListT(t)) return $"{ListHelper(ListElem(t))}.Read({call})";
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

        // A managed override that returns a value type hands the value over by copying it into
        // the caller's slot.
        foreach (var boxed in VirtualBoxedReturns())
        {
            string stem = ListStem(boxed);
            sb.AppendLine();
            sb.AppendLine($"// {boxed}: the value a managed override returned, copied into the caller's slot.");
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_assign_{stem}(void* destination, void* source)");
            sb.AppendLine("{");
            sb.AppendLine($"    *static_cast<{boxed}*>(destination) = *static_cast<const {boxed}*>(source);");
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
            string sig = string.Join(", ", ps.Select(p => $"{CppCtorParamType(PType(p))} {NativeName(PName(p))}"));
            string args = string.Join(", ", ps.Select(p => NativeName(PName(p))));
            sb.AppendLine($"    QsApi_{name}(void* userData, QsApiDispatch dispatch{(ps.Count > 0 ? ", " + sig : "")})");
            sb.AppendLine($"        : {name}({args}), m_userData(userData), m_dispatch(dispatch) {{}}");
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
            if (IsPure(v))
                continue;
            var ps = Params(v);
            string sig = string.Join(", ", ps.Select(p => $"{CppVirtualParamType(PType(p))} {NativeName(PName(p))}"));
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
            string sig = string.Join(", ", ps.Select(p => $"{CppVirtualParamType(PType(p))} {NativeName(PName(p))}"));
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

        for (int i = 0; i < ctors.Count && subclassable; i++)
        {
            var ps = Params(ctors[i]);
            string sig = string.Join(", ", new[] { "void* userData", "QsApiDispatch dispatch" }
                .Concat(ps.Select(p => $"{CppParamType(PType(p))} {NativeName(PName(p))}")));
            string args = string.Join(", ", new[] { "userData", "dispatch" }
                .Concat(ps.Select(p => CppArg(PType(p), NativeName(PName(p))))));
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void* qs_api_{name}_ctor{CtorSuffix(i)}({sig})");
            sb.AppendLine("{");
            sb.AppendLine($"    return new QsApi_{name}({args});");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (subclassable && cls.GetProperty("owned").GetBoolean() && ctors.Count > 0)
        {
            sb.AppendLine($"extern \"C\" __declspec(dllexport) void qs_api_{name}_dtor(void* self)");
            sb.AppendLine("{");
            sb.AppendLine($"    delete static_cast<{name}*>(self);");
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
            nativeParams.AddRange(ps.Select(p => $"{CppParamType(PType(p))} {NativeName(PName(p))}"));
            string sig = string.Join(", ", nativeParams);
            string ret = ReturnType(m);
            string args = string.Join(", ", ps.Select(p => CppArg(PType(p), NativeName(PName(p)))));
            string call = IsStatic(m)
                ? $"{name}::{mname}({args})"
                : $"static_cast<{name}*>(self)->{mname}({args})";
            sb.AppendLine($"extern \"C\" __declspec(dllexport) {NativeRetCpp(ret)} qs_api_{name}_{mname}{entrySuffix}({sig})");
            sb.AppendLine("{");
            sb.AppendLine("    " + NativeRetExpr(ret, call));
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
            if (IsPure(v))
                continue;
            string vname = Name(v);
            virtualOverloads.TryGetValue(vname, out int vseen);
            virtualOverloads[vname] = vseen + 1;
            string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();

            var ps = Params(v);
            string sig = string.Join(", ", new[] { "void* self" }
                .Concat(ps.Select(p => $"{CppParamType(PType(p))} {NativeName(PName(p))}")));
            string ret = ReturnType(v);
            string baseArgs = string.Join(", ", new[] { $"static_cast<QsApi_{name}*>(self)" }
                .Concat(ps.Select(p => CppArg(PType(p), NativeName(PName(p))))));
            sb.AppendLine($"extern \"C\" __declspec(dllexport) {NativeRetCpp(ret)} qs_api_{name}_{vname}__base{vSuffix}({sig})");
            sb.AppendLine("{");
            sb.AppendLine("    " + NativeRetExpr(ret, $"QsApi_{name}::CallBase_{vname}{vSuffix}({baseArgs})"));
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    static string CppParamType(string t)
    {
        if (IsStringT(t)) return "const char16_t*";
        if (t == "char*") return "const char*";
        if (t == "int*") return "const int*";
        if (t == "uchar*") return "const unsigned char*";
        if (IsStringListT(t) || IsListT(t) || t == "QByteArrayView") return "void*";
        if (IsPtr(t) || IsBoxed(t)) return "void*";
        if (IsEnum(t)) return "int";
        return t;
    }

    // The real C++ types, used where the generated code has to speak to Qt directly.
    static string CppCtorParamType(string t)
    {
        if (IsStringT(t)) return "QString";
        if (t == "char*") return "const char*";
        if (t == "int*") return "const int*";
        if (t == "uchar*") return "const unsigned char*";
        if (IsStringListT(t)) return "const QStringList&";
        if (IsListT(t)) return $"const {t}&";
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
        if (IsListT(t)) return $"*static_cast<{t}*>({name})";
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
        return IsEnum(t) ? "int" : "void*";
    }

    static string NativeRetExpr(string t, string call)
    {
        if (t == "void") return call + ";";
        if (IsStringT(t)) return $"return new QString({call});";
        if (IsStringListT(t)) return $"return new QStringList({call});";
        if (IsListT(t)) return $"return new {t}({call});";
        if (t == "QByteArrayView") return $"return new QByteArray({call});";
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
    static string CppVirtualParamType(string t)
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
