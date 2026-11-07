using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;

// A type handler owns everything the generator knows about one spelling family: how it maps to
// the managed surface, which ABI/C++ type it uses, how it marshals, and which container shape it
// has. Handlers are selected by the [TypeHandler] regular expressions, so adding a type is one
// class in one of the Types/*.cs files - not a new branch in a chain of IsXxx() tests.
//
// The base methods are the generic fallbacks; a handler overrides only what differs. The mainline
// calls HandlerFor(t) once and then dispatches to that handler.
public static partial class ApiCodeGen
{
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
    sealed record ContainerInfo(
        string Family, string Class, string Prefix, string Tag, string Stem,
        string Helper, string Element, string Key, string Value, bool IsSet);

    abstract class TypeHandler
    {
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

    static readonly List<TypeHandlerEntry> typeHandlerEntries = BuildTypeHandlers();
    static readonly Dictionary<string, TypeHandler> typeHandlerCache = new(StringComparer.Ordinal);
    static readonly TypeHandler fallbackHandler = new FallbackHandler();

    static List<TypeHandlerEntry> BuildTypeHandlers()
    {
        var entries = new List<(int Order, int Length, TypeHandlerEntry Entry)>();
        foreach (var type in typeof(ApiCodeGen).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(TypeHandler).IsAssignableFrom(type))
                continue;
            foreach (var attribute in type.GetCustomAttributes<TypeHandlerAttribute>())
            {
                var handler = (TypeHandler)Activator.CreateInstance(type, nonPublic: true);
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

    static TypeHandler HandlerFor(string t)
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

    static TypeHandler H(string t) => HandlerFor(t);

    // Mainline dispatch: one handler call per operation.
    static string ManagedType(string t) => H(t).ManagedType(t);
    static string ManagedElement(string e) => H(e).ManagedElement(e);
    static string NativeParamType(string t) => H(t).NativeParamType(t);
    static string NativeRetType(string t) => H(t).NativeRetType(t);
    static string CppAbiType(string t) => H(t).CppAbiType(t);
    static string ArgToNative(string t, string name) => H(t).ArgToNative(t, name);
    static string RetToManaged(string t, string call) => H(t).RetToManaged(t, call);
    static string CSharpDefault(string type) => H(type).DefaultValue(type);
    static string NativeRetExpr(string t, string call) => H(t).NativeRetExpr(t, call);
    static string CppArg(string t, string name, string defaultCpp = null) => H(t).CppArg(t, name, defaultCpp);
    static string NativeRetCpp(string t) => H(t).CppAbiRetType(t);
    static string CppVirtualParamTypeFor(string t) => H(t).CppVirtualParamType(t);
    static string DispatchRead(string t, int index) => H(t).DispatchRead(t, index);
    static string SignalRead(string t, int index) => H(t).SignalRead(t, index);
    static string DispatchStore(string t, string value, int slot = 0) => H(t).DispatchStore(t, value, slot);
    static string DispatchArgStore(string t, string name) => H(t).DispatchArgStore(t, name);
    static string ElemAbiCppParam(string e) => H(e).ElementAbiCppParam(e);
    static string ElemAbiCppRet(string e) => H(e).ElementAbiCppRet(e);
    static string ElemAbiCsParam(string e) => H(e).ElementAbiCsParam(e);
    static string ElemAbiCsRet(string e) => H(e).ElementAbiCsRet(e);
    static string ElemToCpp(string e, string expr) => H(e).ElementToCpp(e, expr);
    static string ElemFromCpp(string e, string expr) => H(e).ElementFromCpp(e, expr);
    static string ElemRead(string e, string expr) => H(e).ElementRead(e, expr);
    static string ElemArg(string e, string expr) => H(e).ElementArg(e, expr);
    static (List<string> Prelude, string Arg, List<string> Cleanup) ElemToAbi(
        string e, string expr, string prefix) => H(e).ElementToAbi(e, expr, prefix);
    static (List<string> Prelude, List<string> Pointers, List<string> Cleanup) EmitParam(
        string t, string name, string local) => H(t).EmitParam(t, name, local);
    static (List<string> Prelude, string Arg, List<string> Cleanup) MarshalArg(
        string t, string name) => H(t).MarshalArg(t, name);
    static ContainerInfo ContainerOf(string t) => H(t).Container(t);

    static string ArgvSlot(int index) => $"Marshal.ReadIntPtr(argv, {index} * IntPtr.Size)";
    static string ArgvDest(int slot) => $"Marshal.ReadIntPtr(argv, {slot} * IntPtr.Size)";

    // A managed null for a boxed value means "the parameter was not given": use the declaration's
    // own default when the model kept one, otherwise the type's default constructor.
    static string BoxedCppArg(string t, string name, string defaultCpp)
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
    static bool IsVoid(string t) => H(t).IsVoid(t);
    static bool IsScalar(string t) => H(t).IsScalar(t);
    static bool IsScalarBase(string t) => H(t).IsScalar(t);
    static bool IsEnum(string t) => H(t).IsEnum(t);
    static bool IsFlags(string t) => H(t).IsFlags(t);
    static bool IsStringT(string t) => H(t).IsString(t);
    static bool IsStringLikeT(string t) => H(t).IsStringLike(t);
    static bool IsCStringT(string t) => H(t).IsCString(t);
    static bool IsCharT(string t) => H(t).IsChar(t);
    static bool IsCharPtrT(string t) => H(t).IsCharPtr(t);
    static bool IsUuid(string t) => H(t).IsUuid(t);
    static bool IsStruct(string t) => H(t).IsStruct(t);
    static bool IsStructPtr(string t) => H(t).IsStructPointer(t);
    static bool IsArray(string t) => H(t).IsArray(t);
    static bool IsByteBufferT(string t) => H(t).IsByteBuffer(t);
    static bool IsPtr(string t) => H(t).IsPointer(t);
    static bool IsClassPtr(string t) => H(t).IsClassPointer(t);
    static bool IsBoxed(string t) => H(t).IsBoxed(t);
    static bool IsQVariantType(string t) => H(t).IsVariant(t);
    static bool IsCallbackType(string t) => H(t).IsCallback(t);
    static bool IsQFunctionPointerT(string t) => H(t).IsFunctionPointer(t);
    static bool IsFunctionType(string t) => H(t).IsFunction(t);
    static bool IsStringListT(string t) => H(t).IsStringList(t);
    static bool IsSequenceT(string t) => H(t).IsSequence(t);
    static bool IsAssocT(string t) => H(t).IsAssoc(t);
    static bool IsPairT(string t) => H(t).IsPair(t);
    static bool IsOptionalT(string t) => H(t).IsOptional(t);
    static bool IsByteArrayViewT(string t) => H(t).IsByteArrayView(t);
    static bool IsTimeSpanT(string t) => H(t).IsTimeSpan(t);
    static bool IsNativeContainerT(string t) => H(t).IsNativeContainer(t);
    static bool IsGeneratedHelperT(string t) => H(t).IsGeneratedHelper(t);
    static bool IsHandleMarshalledT(string t) => H(t).IsHandleMarshalled(t);
    static bool IsValueTypeManaged(string t) => H(t).IsValueType(t);

    static bool IsScalarElem(string e) => H(e).IsScalar(e);
    static bool IsEnumElem(string e) => H(e).IsEnum(e);

    // A C++ scalar spelling, used by the scalar-array candidate and the scalar handler.
    static bool IsScalarName(string t)
        => t != "void" && (CSharpScalar(t) != null
                           || t is "int" or "float" or "double" or "bool");

    // The text between the outer angle brackets of a template spelling. The caller has already
    // matched the shape with a [TypeHandler] regex.
    static string TemplateInner(string t)
    {
        int open = t.IndexOf('<');
        return t.Substring(open + 1, t.Length - open - 2);
    }
}
