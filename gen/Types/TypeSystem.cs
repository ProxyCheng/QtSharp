using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;

// A type handler owns everything the generator knows about one spelling family: how it maps to
// the managed surface, which ABI/C++ type it uses, how it marshals, and which container shape it
// has. Handlers are selected by the [TypeHandler] regular expressions, so adding a type is one
// class in one of the Types/*.cs files - not a new branch in a chain of IsXxx() tests.
//
// The base methods are the generic fallbacks; a handler overrides only what differs. The mainline
// calls HandlerFor(t) once and then dispatches to that handler.
namespace QtSharp.Generator
{
public sealed partial class TypeSystem
{
    public TypeSystem(BindingModel binding)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        typeHandlerEntries = BuildTypeHandlers();
        paramHandlers = BuildParamHandlers();
        fallbackHandler.S = this;
        inParamHandler.S = this;
    }

    internal BindingModel Binding { get; }
    internal HashSet<string> ClassNames => Binding.ClassNames;
    internal HashSet<string> EnumNames => Binding.EnumNames;
    internal HashSet<string> FlagsEnums => Binding.FlagsEnums;


    internal string CsTypeName(string t) => Binding.CsTypeName(t);
    internal string CppTypeName(string t) => Binding.CppTypeName(t);
    internal string CSharpScalar(string t) => Binding.CSharpScalar(t);
    internal bool IsOwned(string t) => Binding.IsOwned(t);
    internal bool IsNonCopyable(string t) => Binding.IsNonCopyable(t);
    internal bool HasDefaultCtor(string t) => Binding.HasDefaultCtor(t);
    internal string ClassPtrRead(string cls, string expr) => Binding.ClassPtrRead(cls, expr);
    internal string StructPod(string t) => Binding.StructPod(t);
    internal string StructFromPod(string t, string name) => Binding.StructFromPod(t, name);
    internal string StructToPod(string t, string value) => Binding.StructToPod(t, value);
    internal string StructDefaultExpr(string type, string defaultCpp) => Binding.StructDefaultExpr(type, defaultCpp);
    internal List<string> VirtualBoxedParams() => Binding.VirtualBoxedParams();
    internal List<string> VirtualBoxedReturns() => Binding.VirtualBoxedReturns();
    internal string ListStem(string e) => Binding.ListStem(e);
    internal string PointerElemRead(string e, string expr) => Binding.PointerElemRead(e, expr);
    internal const string VariantMarshalling = "QtSharp.QtCore.VariantMarshalling";

    internal string PType(JsonElement p) => Binding.PType(p);
    internal string PName(JsonElement p) => Binding.PName(p);
    internal string SafeName(string s) => Binding.SafeName(s);
    internal string NativeName(string s) => Binding.NativeName(s);
    internal string CppParamType(string t) => Binding.CppParamType(t);
    internal bool IsStringByRef(JsonElement p) => Binding.IsStringByRef(p);
    internal bool IsOut(JsonElement p) => Binding.IsOut(p);
    internal bool IsRef(JsonElement p) => Binding.IsRef(p);
    internal bool IsOptionalStructDefault(JsonElement p) => Binding.IsOptionalStructDefault(p);
    internal string ParamValueManagedType(JsonElement p) => Binding.ParamValueManagedType(p);
    internal bool IsOpaqueStructPtr(JsonElement p) => Binding.IsOpaqueStructPtr(p);
    internal string RefValueType(string t) => Binding.RefValueType(t);
    internal string DefaultCpp(JsonElement p) => Binding.DefaultCpp(p);
    internal string DefaultCppOf(JsonElement p) => Binding.DefaultCppOf(p);
    internal string GivenName(JsonElement p) => Binding.GivenName(p);
    internal string StringOutLocal(JsonElement p) => Binding.StringOutLocal(p);
    internal string StringInLocal(string name) => Binding.StringInLocal(name);
    internal string QualifyEnumDefault(string literal, string type) => Binding.QualifyEnumDefault(literal, type);
    internal bool HasParam(JsonElement p, ApiModel.ParamFlags flag) => Binding.HasParam(p, flag);
    internal bool IsOptional(JsonElement p) => Binding.IsOptional(p);
    internal string CoreTypeName(string name) => Binding.CoreTypeName(name);
    internal string CsEnum(string t) => Binding.CsEnum(t);
    internal string CppAbiScalar(string t) => Binding.CppAbiScalar(t);
    internal string StructArrayAbiType(string t) => Binding.StructArrayAbiType(t);
    internal string CallbackDelegateType(string t) => Binding.CallbackDelegateType(t);
    internal string CallbackThunkName(string t) => Binding.CallbackThunkName(t);
    internal string MapStem(string key, string value) => Binding.MapStem(key, value);
    internal IEnumerable<string> ContainerTypesIn(string t) => ContainerTypesInCore(t);

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    sealed class TypeHandlerAttribute : Attribute
    {
        public string Pattern { get; }
        public int Order { get; }

        public TypeHandlerAttribute(string pattern, int order)
        {
            Pattern = pattern;
            Order = order;
        }
    }

    // The parsed shape of a container spelling. The handler that owns the spelling builds it, so
    // the mainline never has to spell "QList<...>" apart or know List from Set.
    internal sealed record ContainerInfo(
        string Family, string Class, string Prefix, string Tag, string Stem,
        string Helper, string Element, string Key, string Value, bool IsSet);

    abstract class TypeHandler
    {
        internal TypeSystem S;

        protected string CsTypeName(string t) => S.CsTypeName(t);
        protected string CppTypeName(string t) => S.CppTypeName(t);
        protected string CSharpScalar(string t) => S.CSharpScalar(t);
        protected bool IsScalarName(string t) => S.IsScalarName(t);
        protected bool IsOwned(string t) => S.IsOwned(t);
        protected bool IsNonCopyable(string t) => S.IsNonCopyable(t);
        protected bool HasDefaultCtor(string t) => S.HasDefaultCtor(t);
        protected string ClassPtrRead(string cls, string expr) => S.ClassPtrRead(cls, expr);
        protected string StructPod(string t) => S.StructPod(t);
        protected string StructFromPod(string t, string name) => S.StructFromPod(t, name);
        protected string StructToPod(string t, string value) => S.StructToPod(t, value);
        protected string StructDefaultExpr(string type, string defaultCpp) => S.StructDefaultExpr(type, defaultCpp);
        protected List<string> VirtualBoxedParams() => S.VirtualBoxedParams();
        protected List<string> VirtualBoxedReturns() => S.VirtualBoxedReturns();
        protected string BoxedCppArg(string t, string name, string defaultCpp) => S.BoxedCppArg(t, name, defaultCpp);
        protected string ListStem(string e) => S.ListStem(e);
        protected string PointerElemRead(string e, string expr) => S.PointerElemRead(e, expr);
        protected string ArgvSlot(int index) => S.ArgvSlot(index);
        protected string ArgvDest(int slot) => S.ArgvDest(slot);
        protected string ManagedElementOf(string e) => S.ManagedElement(e);
        protected string TemplateInner(string t) => S.TemplateInner(t);
        protected (string Key, string Value) AssocElems(string t) => S.AssocElems(t);
        protected (string First, string Second) PairElems(string t) => S.PairElems(t);
        protected string OptionalElem(string t) => S.ContainerOf(t).Element;
        protected string MapStem(string k, string v) => S.MapStem(k, v);
        protected bool IsClassPtr(string t) => S.IsClassPtr(t);
        protected bool IsValueTypeManaged(string t) => S.IsValueTypeManaged(t);
        protected string RefValueType(string t) => S.RefValueType(t);
        protected string CsEnum(string t) => S.CsEnum(t);
        protected string CppAbiScalar(string t) => S.CppAbiScalar(t);
        protected string StructArrayAbiType(string t) => S.StructArrayAbiType(t);
        protected HashSet<string> ClassNames => S.ClassNames;
        protected HashSet<string> EnumNames => S.EnumNames;
        protected HashSet<string> FlagsEnums => S.FlagsEnums;
        protected string CoreTypeName(string n) => S.CoreTypeName(n);
        protected string CallbackDelegateType(string t) => S.CallbackDelegateType(t);
        protected string CallbackThunkName(string t) => S.CallbackThunkName(t);
        protected const string VariantMarshalling = "QtSharp.QtCore.VariantMarshalling";
        // Mapping operations.
        public virtual string ManagedType(string t) => ApiTypes.Managed(t, ClassNames);
        public virtual string ManagedElement(string t) => ManagedType(t);
        public virtual string NativeParamType(string t) => CSharpScalar(t) ?? t;
        public virtual string NativeRetType(string t) => IsEnum(t) ? "int" : "IntPtr";
        public virtual string CppAbiType(string t) => CppAbiScalar(t);
        public virtual string CppCtorParamType(string t) => CppAbiScalar(t);
        public virtual string ArgToNative(string t, string name) => name;
        public virtual string RetToManaged(string t, string call) => call;
        public virtual string DefaultValue(string t) => "default";
        public virtual string NativeRetExpr(string t, string call) => $"return {call};";
        public virtual string CppArg(string t, string name, string defaultCpp) => name;
        public virtual string CppAbiRetType(string t) => "void*";
        public virtual string CppVirtualParamType(string t) => t;

        // Virtual-dispatch reads and writes. The defaults match the raw-handle cases; the
        // handlers that need a conversion override the method that owns it.
        public virtual string DispatchRead(string t, int index) => ArgvSlot(index);
        public virtual string SignalRead(string t, int index) => ArgvSlot(index);

        public virtual string DispatchStore(string t, string value, int slot)
        {
            string dest = ArgvDest(slot);
            if (IsVoid(t))
                return "";
            if (IsEnum(t))
                return $"ApiRuntime.WriteResult({dest}, (int){value});";
            if (IsTimeSpan(t))
                return $"ApiRuntime.WriteResult({dest}, (long)({value}.Ticks * 100));";
            if (IsStruct(t))
                return $"Marshal.StructureToPtr({value}, {dest}, false);";
            if (IsVariant(t))
                return $"using (var __variant = {VariantMarshalling}.Value({value})) "
                     + $"ApiNative.Assign_{ListStem(t)}({dest}, __variant.Handle);";
            if (IsBoxed(t))
                return $"ApiNative.Assign_{ListStem(t)}({dest}, {value}?.Handle ?? IntPtr.Zero);";
            if (IsClassPointer(t))
                return $"Marshal.WriteIntPtr({dest}, {value}?.Handle ?? IntPtr.Zero);";
            if (IsPointer(t))
                return $"Marshal.WriteIntPtr({dest}, {value});";
            if (IsString(t))
                return $"ApiRuntime.Write({dest}, {value});";
            if (IsStringList(t))
                return $"ApiRuntime.WriteStringList({dest}, {value});";
            if (IsNativeContainer(t))
                return $"{Container(t).Helper}.Write({dest}, {value});";
            return $"ApiRuntime.WriteResult({dest}, {value});";
        }

        public virtual string DispatchArgStore(string t, string name)
        {
            if (IsUuid(t))
                return $"const_cast<void*>(static_cast<const void*>(&{name}))";
            if (IsBoxed(t))
                return $"const_cast<{t}*>(&{name})";
            if (IsPointer(t) || IsArray(t))
                return $"const_cast<void*>(static_cast<const void*>({name}))";
            if (IsString(t))
                return $"const_cast<void*>(static_cast<const void*>(&{name}))";
            if (IsHandleMarshalled(t))
                return $"const_cast<void*>(static_cast<const void*>(&{name}))";
            return "&" + name;
        }

        // One container element crossing the C ABI, in the directions the generated helper code
        // needs. The default is the handle case; the scalar/string/value handlers refine it.
        public virtual string ElementAbiCppParam(string e) => "void*";
        public virtual string ElementAbiCppRet(string e) => "void*";
        public virtual string ElementAbiCsParam(string e) => "IntPtr";
        public virtual string ElementAbiCsRet(string e) => "IntPtr";
        public virtual string ElementToCpp(string e, string expr)
            => e.EndsWith("*")
                ? $"static_cast<{e.Substring(0, e.Length - 1)}*>({expr})"
                : $"*static_cast<{e}*>({expr})";
        public virtual string ElementFromCpp(string e, string expr)
            => e.EndsWith("*") ? $"static_cast<void*>({expr})" : $"new {e}({expr})";
        public virtual string ElementRead(string e, string expr)
            => e.EndsWith("*")
                ? PointerElemRead(e, expr)
                : IsOwned(e)
                    ? $"{CsTypeName(e)}.FromOwnedHandle({expr})"
                    : $"{CsTypeName(e)}.FromTransientHandle({expr})";
        public virtual string ElementArg(string e, string expr) => $"{expr}?.Handle ?? IntPtr.Zero";

        public virtual (List<string> Prelude, string Arg, List<string> Cleanup)
            ElementToAbi(string e, string expr, string prefix)
            => (new List<string>(), ElementArg(e, expr), new List<string>());

        // One signal argument's native storage, shaped like the moc-generated signal function.
        public virtual (List<string> Prelude, List<string> Pointers, List<string> Cleanup)
            EmitParam(string t, string name, string local)
        {
            if (IsStringLike(t))
                return (new List<string> { $"var {local} = ApiRuntime.NewQString({name});" },
                    new List<string> { $"(void*){local}" },
                    new List<string> { $"ApiRuntime.FreeQString({local});" });
            if (IsStringList(t))
                return (new List<string> { $"var {local} = ApiRuntime.NewStringList({name});" },
                    new List<string> { $"(void*){local}" },
                    new List<string> { $"ApiRuntime.FreeStringList({local});" });
            if (IsNativeContainer(t))
            {
                string helper = Container(t).Helper;
                return (new List<string> { $"var {local} = {helper}.New({name});" },
                    new List<string> { $"(void*){local}" },
                    new List<string> { $"{helper}.Free({local});" });
            }
            if (IsByteArrayView(t))
                return (new List<string> { $"var {local} = ApiRuntime.NewByteArray({name});" },
                    new List<string> { $"(void*){local}" },
                    new List<string> { $"ApiRuntime.FreeByteArray({local});" });
            if (IsTimeSpan(t))
                return (new List<string> { $"long {local} = (long)({name}.Ticks * 100);" },
                    new List<string> { $"&{local}" },
                    new List<string>());
            if (IsEnum(t))
                return (new List<string> { $"int {local} = (int){name};" },
                    new List<string> { $"&{local}" },
                    new List<string>());
            if (IsStruct(t))
                return (new List<string> { $"var {local} = {name};" },
                    new List<string> { $"&{local}" },
                    new List<string>());
            if (IsVariant(t))
                return (new List<string> { $"var {local} = {VariantMarshalling}.Value({name});" },
                    new List<string> { $"(void*){local}.Handle" },
                    new List<string> { $"{local}.Dispose();" });
            if (IsBoxed(t))
                return (new List<string> { $"var {local} = {name}?.Handle ?? IntPtr.Zero;" },
                    new List<string> { $"(void*){local}" },
                    new List<string>());
            if (IsPointer(t))
            {
                string init = IsClassPointer(t)
                    ? $"var {local} = (void*)({name}?.Handle ?? IntPtr.Zero);"
                    : $"var {local} = (void*){name};";
                return (new List<string> { init },
                    new List<string> { $"&{local}" },
                    new List<string>());
            }
            return (new List<string> { $"{ManagedType(t)} {local} = {name};" },
                new List<string> { $"&{local}" },
                new List<string>());
        }

        // One managed parameter on its way to a native call.
        public virtual (List<string> Prelude, string Arg, List<string> Cleanup)
            MarshalArg(string t, string name)
        {
            if (IsCallback(t))
                return (new List<string>(), $"{CallbackThunkName(t)}.Get({name})", new List<string>());
            if (IsStringList(t))
                return (new List<string> { $"var {name}Handle = ApiRuntime.NewStringList({name});" },
                    $"{name}Handle",
                    new List<string> { $"ApiRuntime.FreeStringList({name}Handle);" });
            if (IsGeneratedHelper(t))
            {
                string helper = Container(t).Helper;
                return (new List<string> { $"var {name}Handle = {helper}.New({name});" },
                    $"{name}Handle",
                    new List<string> { $"{helper}.Free({name}Handle);" });
            }
            if (IsByteArrayView(t))
                return (new List<string> { $"var {name}Handle = ApiRuntime.NewByteArray({name});" },
                    $"{name}Handle",
                    new List<string> { $"ApiRuntime.FreeByteArray({name}Handle);" });
            if (IsVariant(t))
                return (new List<string> { $"var {name}Lease = {VariantMarshalling}.Parameter({name});" },
                    $"{name}Lease.Handle",
                    new List<string> { $"{name}Lease.Dispose();" });
            return (new List<string>(), ArgToNative(t, name), new List<string>());
        }

        // Selection: the regex is only a candidate; a handler can narrow it with the name tables
        // (a plain identifier may be a struct, an enum, or a bound class).
        public virtual bool CanHandle(string t) => true;
        public virtual ContainerInfo Container(string t) => null;

        // Capabilities the generator asks about. These are answered by the handler that owns the
        // spelling, not by a central classification switch.
        public virtual bool IsVoid(string t) => false;
        public virtual bool IsScalar(string t) => false;
        public virtual bool IsEnum(string t) => false;
        public virtual bool IsFlags(string t) => false;
        public virtual bool IsString(string t) => false;
        public virtual bool IsCString(string t) => false;
        public virtual bool IsChar(string t) => false;
        public virtual bool IsCharPtr(string t) => false;
        public virtual bool IsUuid(string t) => false;
        public virtual bool IsStruct(string t) => false;
        public virtual bool IsStructPointer(string t) => false;
        public virtual bool IsScalarArray(string t) => false;
        public virtual bool IsArray(string t) => IsScalarArray(t) || IsStructPointer(t);
        public virtual bool IsByteBuffer(string t) => false;
        public virtual bool IsClassPointer(string t) => false;
        public virtual bool IsOpaquePointer(string t) => false;
        public virtual bool IsPointer(string t)
            => IsCString(t) || IsCharPtr(t) || IsClassPointer(t) || IsOpaquePointer(t);
        public virtual bool IsBoxed(string t) => false;
        public virtual bool IsVariant(string t) => false;
        public virtual bool IsCallback(string t) => false;
        public virtual bool IsFunctionPointer(string t) => false;
        public virtual bool IsFunction(string t) => IsCallback(t) || IsFunctionPointer(t);
        public virtual bool IsStringList(string t) => false;
        public virtual bool IsByteArrayView(string t) => false;
        public virtual bool IsTimeSpan(string t) => false;
        public virtual bool IsSequence(string t) => false;
        public virtual bool IsAssoc(string t) => false;
        public virtual bool IsPair(string t) => false;
        public virtual bool IsOptional(string t) => false;
        public virtual bool IsNativeContainer(string t) => IsSequence(t) || IsAssoc(t) || IsPair(t);
        public virtual bool IsGeneratedHelper(string t) => IsNativeContainer(t) || IsOptional(t);
        public virtual bool IsHandleMarshalled(string t)
            => IsStringList(t) || IsGeneratedHelper(t) || IsByteArrayView(t);
        public virtual bool IsStringLike(string t) => IsString(t) || IsCString(t);
        public virtual bool IsValueType(string t) => false;
    }

    sealed class FallbackHandler : TypeHandler
    {
    }

    sealed record TypeHandlerEntry(Regex Pattern, TypeHandler Handler);

    private readonly List<TypeHandlerEntry> typeHandlerEntries;
    readonly Dictionary<string, TypeHandler> typeHandlerCache = new(StringComparer.Ordinal);
    readonly TypeHandler fallbackHandler = new FallbackHandler();

    List<TypeHandlerEntry> BuildTypeHandlers()
    {
        var entries = new List<(int Order, int Length, TypeHandlerEntry Entry)>();
        foreach (var type in typeof(TypeSystem).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(TypeHandler).IsAssignableFrom(type))
                continue;
            foreach (var attribute in type.GetCustomAttributes<TypeHandlerAttribute>())
            {
                var handler = (TypeHandler)Activator.CreateInstance(type, nonPublic: true);
                handler.S = this;
                var regex = new Regex(attribute.Pattern,
                    RegexOptions.CultureInvariant | RegexOptions.Compiled);
                entries.Add((attribute.Order, attribute.Pattern.Length,
                    new TypeHandlerEntry(regex, handler)));
            }
        }
        // A more specific pattern wins at the same order (long long before long); the order
        // separates the exact spellings from the broad pointer/identifier fallbacks.
        return entries
            .OrderBy(e => e.Order)
            .ThenByDescending(e => e.Length)
            .Select(e => e.Entry)
            .ToList();
    }

    TypeHandler HandlerFor(string t)
    {
        if (t == null)
            return fallbackHandler;
        if (typeHandlerCache.TryGetValue(t, out var cached))
            return cached;
        TypeHandler match = null;
        foreach (var entry in typeHandlerEntries)
        {
            if (entry.Pattern.IsMatch(t) && entry.Handler.CanHandle(t))
            {
                match = entry.Handler;
                break;
            }
        }
        match ??= fallbackHandler;
        typeHandlerCache[t] = match;
        return match;
    }

    TypeHandler H(string t) => HandlerFor(t);

    // Mainline dispatch: one handler call per operation.
    internal string ManagedType(string t) => H(t).ManagedType(t);
    internal string ManagedElement(string e) => H(e).ManagedElement(e);
    internal string NativeParamType(string t) => H(t).NativeParamType(t);
    internal string NativeRetType(string t) => H(t).NativeRetType(t);
    internal string CppAbiType(string t) => H(t).CppAbiType(t);
    internal string ArgToNative(string t, string name) => H(t).ArgToNative(t, name);
    internal string RetToManaged(string t, string call) => H(t).RetToManaged(t, call);
    internal string CSharpDefault(string type) => H(type).DefaultValue(type);
    internal string NativeRetExpr(string t, string call) => H(t).NativeRetExpr(t, call);
    internal string CppArg(string t, string name, string defaultCpp = null) => H(t).CppArg(t, name, defaultCpp);
    internal string NativeRetCpp(string t) => H(t).CppAbiRetType(t);
    internal string CppVirtualParamTypeFor(string t) => H(t).CppVirtualParamType(t);
    internal string CppCtorParamType(string t) => H(t).CppCtorParamType(t);
    internal string DispatchRead(string t, int index) => H(t).DispatchRead(t, index);
    internal string SignalRead(string t, int index) => H(t).SignalRead(t, index);
    internal string DispatchStore(string t, string value, int slot = 0) => H(t).DispatchStore(t, value, slot);
    internal string DispatchArgStore(string t, string name) => H(t).DispatchArgStore(t, name);
    internal string ElemAbiCppParam(string e) => H(e).ElementAbiCppParam(e);
    internal string ElemAbiCppRet(string e) => H(e).ElementAbiCppRet(e);
    internal string ElemAbiCsParam(string e) => H(e).ElementAbiCsParam(e);
    internal string ElemAbiCsRet(string e) => H(e).ElementAbiCsRet(e);
    internal string ElemToCpp(string e, string expr) => H(e).ElementToCpp(e, expr);
    internal string ElemFromCpp(string e, string expr) => H(e).ElementFromCpp(e, expr);
    internal string ElemRead(string e, string expr) => H(e).ElementRead(e, expr);
    internal string ElemArg(string e, string expr) => H(e).ElementArg(e, expr);
    internal (List<string> Prelude, string Arg, List<string> Cleanup) ElemToAbi(
        string e, string expr, string prefix) => H(e).ElementToAbi(e, expr, prefix);
    internal (List<string> Prelude, List<string> Pointers, List<string> Cleanup) EmitParam(
        string t, string name, string local) => H(t).EmitParam(t, name, local);
    internal (List<string> Prelude, string Arg, List<string> Cleanup) MarshalArg(
        string t, string name) => H(t).MarshalArg(t, name);
    internal ContainerInfo ContainerOf(string t) => H(t).Container(t);

    internal string ArgvSlot(int index) => $"Marshal.ReadIntPtr(argv, {index} * IntPtr.Size)";
    internal string ArgvDest(int slot) => $"Marshal.ReadIntPtr(argv, {slot} * IntPtr.Size)";

    // A managed null for a boxed value means "the parameter was not given": use the declaration's
    // own default when the model kept one, otherwise the type's default constructor.
    internal string BoxedCppArg(string t, string name, string defaultCpp)
    {
        if (defaultCpp != null)
        {
            string expr = defaultCpp.Replace(" ", "") == "{}"
                ? $"{CppTypeName(t)}()"
                : $"{CppTypeName(t)}({defaultCpp})";
            return $"({name} ? *static_cast<{CppTypeName(t)}*>({name}) : {expr})";
        }
        return HasDefaultCtor(t)
            ? $"({name} ? *static_cast<{CppTypeName(t)}*>({name}) : QsApiDefaultValue<{CppTypeName(t)}>())"
            : $"*static_cast<{CppTypeName(t)}*>({name})";
    }

    // Capability queries used by the surrounding emitter. The answer comes from the handler, so
    // these are dispatch points too, not a second classification table.
    internal bool IsVoid(string t) => H(t).IsVoid(t);
    internal bool IsScalar(string t) => H(t).IsScalar(t);
    internal bool IsScalarBase(string t) => H(t).IsScalar(t);
    internal bool IsEnum(string t) => H(t).IsEnum(t);
    internal bool IsFlags(string t) => H(t).IsFlags(t);
    internal bool IsStringT(string t) => H(t).IsString(t);
    internal bool IsStringLikeT(string t) => H(t).IsStringLike(t);
    internal bool IsCStringT(string t) => H(t).IsCString(t);
    internal bool IsCharT(string t) => H(t).IsChar(t);
    internal bool IsCharPtrT(string t) => H(t).IsCharPtr(t);
    internal bool IsUuid(string t) => H(t).IsUuid(t);
    internal bool IsStruct(string t) => H(t).IsStruct(t);
    internal bool IsStructPtr(string t) => H(t).IsStructPointer(t);
    internal bool IsArray(string t) => H(t).IsArray(t);
    internal bool IsByteBufferT(string t) => H(t).IsByteBuffer(t);
    internal bool IsPtr(string t) => H(t).IsPointer(t);
    internal bool IsClassPtr(string t) => H(t).IsClassPointer(t);
    internal bool IsBoxed(string t) => H(t).IsBoxed(t);
    internal bool IsQVariantType(string t) => H(t).IsVariant(t);
    internal bool IsCallbackType(string t) => H(t).IsCallback(t);
    internal bool IsQFunctionPointerT(string t) => H(t).IsFunctionPointer(t);
    internal bool IsFunctionType(string t) => H(t).IsFunction(t);
    internal bool IsStringListT(string t) => H(t).IsStringList(t);
    internal bool IsSequenceT(string t) => H(t).IsSequence(t);
    internal bool IsAssocT(string t) => H(t).IsAssoc(t);
    internal bool IsPairT(string t) => H(t).IsPair(t);
    internal bool IsOptionalT(string t) => H(t).IsOptional(t);
    internal bool IsByteArrayViewT(string t) => H(t).IsByteArrayView(t);
    internal bool IsTimeSpanT(string t) => H(t).IsTimeSpan(t);
    internal bool IsNativeContainerT(string t) => H(t).IsNativeContainer(t);
    internal bool IsGeneratedHelperT(string t) => H(t).IsGeneratedHelper(t);
    internal bool IsHandleMarshalledT(string t) => H(t).IsHandleMarshalled(t);
    internal bool IsValueTypeManaged(string t) => H(t).IsValueType(t);

    internal bool IsScalarElem(string e) => H(e).IsScalar(e);
    internal bool IsEnumElem(string e) => H(e).IsEnum(e);

    // A C++ scalar spelling, used by the scalar-array candidate and the scalar handler.
    internal bool IsScalarName(string t)
        => t != "void" && (CSharpScalar(t) != null
                           || t is "int" or "float" or "double" or "bool");

    // The text between the outer angle brackets of a template spelling. The caller has already
    // matched the shape with a [TypeHandler] regex.
    internal string TemplateInner(string t)
    {
        int open = t.IndexOf('<');
        return t.Substring(open + 1, t.Length - open - 2);
    }
}
}
