using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Parameter passing is selected by the same pattern as type handling: a handler declares the
// parameter shape it can take and the mainline calls it. The shapes are exclusive (optional
// struct, in-out string, out, ref, plain input), so the registry is ordered by specificity.
namespace QtSharp.Generator
{
public sealed partial class TypeSystem
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    sealed class ParamHandlerAttribute : Attribute
    {
        public int Order { get; }
        public ParamHandlerAttribute(int order) => Order = order;
    }

    internal abstract class ParamHandler
    {
        internal TypeSystem S;

        protected string PType(JsonElement p) => S.PType(p);
        protected string PName(JsonElement p) => S.PName(p);
        protected string SafeName(string s) => S.SafeName(s);
        protected string NativeName(string s) => S.NativeName(s);
        protected string CsTypeName(string t) => S.CsTypeName(t);
        protected string ManagedType(string t) => S.ManagedType(t);
        protected string NativeParamType(string t) => S.NativeParamType(t);
        protected string CppParamType(string t) => S.CppParamType(t);
        protected string CppArg(string t, string name, string defaultCpp = null) => S.CppArg(t, name, defaultCpp);
        protected string CppCtorParamType(string t) => S.CppCtorParamType(t);
        protected bool IsStruct(string t) => S.IsStruct(t);
        protected bool IsFunctionType(string t) => S.IsFunctionType(t);
        protected bool IsOptional(JsonElement p) => S.IsOptional(p);
        protected bool HasParam(JsonElement p, ApiModel.ParamFlags flag) => S.HasParam(p, flag);
        protected string ParamValueManagedType(JsonElement p) => S.ParamValueManagedType(p);
        protected bool IsOpaqueStructPtr(JsonElement p) => S.IsOpaqueStructPtr(p);
        protected string RefValueType(string t) => S.RefValueType(t);
        protected string StructPod(string t) => S.StructPod(t);
        protected string StructFromPod(string t, string name) => S.StructFromPod(t, name);
        protected string StructDefaultExpr(string type, string defaultCpp) => S.StructDefaultExpr(type, defaultCpp);
        protected string DefaultCpp(JsonElement p) => S.DefaultCpp(p);
        protected string DefaultCppOf(JsonElement p) => S.DefaultCppOf(p);
        protected string GivenName(JsonElement p) => S.GivenName(p);
        protected string StringOutLocal(JsonElement p) => S.StringOutLocal(p);
        protected string StringInLocal(string name) => S.StringInLocal(name);
        protected string QualifyEnumDefault(string literal, string type) => S.QualifyEnumDefault(literal, type);
        protected string CSharpDefault(string type) => S.CSharpDefault(type);
        protected (List<string> Prelude, string Arg, List<string> Cleanup) MarshalArgOf(string t, string name)
            => S.MarshalArg(t, name);
        public virtual bool CanHandle(JsonElement p) => false;
        public virtual bool IsByRef(JsonElement p) => false;
        public virtual bool IsOut(JsonElement p) => false;
        public virtual bool IsBuffer(JsonElement p) => false;

        public virtual string ManagedType(JsonElement p) => ParamValueManagedType(p);
        public virtual string Signature(JsonElement p)
            => $"{ManagedType(p)} {SafeName(PName(p))}";
        public virtual string NativeType(JsonElement p) => NativeParamType(PType(p));
        public virtual IEnumerable<string> NativeParams(JsonElement p)
            => new[] { $"{NativeType(p)} {SafeName(PName(p))}" };
        public virtual IEnumerable<string> CppParams(JsonElement p)
            => new[] { $"{CppParamType(p)} {NativeName(PName(p))}" };
        public virtual IEnumerable<string> CppArgs(JsonElement p)
            => new[] { CppArg(p) };
        public virtual IEnumerable<string> AbiArgs(JsonElement p)
            => new[] { NativeName(PName(p)) };

        public virtual (List<string> Prelude, List<string> Args, List<string> Postlude, List<string> Cleanup)
            Marshal(JsonElement p, string name)
        {
            var (prelude, arg, cleanup) = MarshalArgOf(PType(p), name);
            return (prelude, new List<string> { arg }, new List<string>(), cleanup);
        }

        public virtual string CppParamType(JsonElement p)
            => S.CppParamType(PType(p));
        public virtual string CppArg(JsonElement p)
            => S.CppArg(PType(p), NativeName(PName(p)), DefaultCpp(p));
        public virtual string CppCtorParamType(JsonElement p)
            => S.CppCtorParamType(PType(p));
        public virtual string CppCtorArg(JsonElement p)
            => NativeName(PName(p));
        public virtual string TypeKey(JsonElement p) => ManagedType(p);
    }

    [ParamHandler(0)]
    sealed class OptionalStructParamHandler : ParamHandler
    {
        public override bool CanHandle(JsonElement p) => S.IsOptionalStructDefault(p);
        public override string Signature(JsonElement p)
            => $"{CsTypeName(PType(p))}? {SafeName(PName(p))} = null";
        public override string NativeType(JsonElement p) => CsTypeName(PType(p));
        public override IEnumerable<string> NativeParams(JsonElement p)
            => new[]
            {
                $"{NativeType(p)} {SafeName(PName(p))}",
                $"[MarshalAs(UnmanagedType.I1)] bool {SafeName(PName(p))}Given",
            };
        public override IEnumerable<string> CppParams(JsonElement p)
            => new[]
            {
                $"{StructPod(PType(p))} {NativeName(PName(p))}",
                $"bool {GivenName(p)}",
            };
        public override IEnumerable<string> CppArgs(JsonElement p)
            => new[]
            {
                $"({GivenName(p)} ? {StructFromPod(PType(p), NativeName(PName(p)))} : {StructDefaultExpr(PType(p), DefaultCppOf(p))})",
            };
        public override IEnumerable<string> AbiArgs(JsonElement p)
            => new[] { NativeName(PName(p)), GivenName(p) };
        public override (List<string> Prelude, List<string> Args, List<string> Postlude, List<string> Cleanup)
            Marshal(JsonElement p, string name)
            => (new List<string>(), new List<string> { $"{name} ?? default", $"{name}.HasValue" },
                new List<string>(), new List<string>());
    }

    [ParamHandler(10)]
    sealed class StringInOutParamHandler : ParamHandler
    {
        public override bool CanHandle(JsonElement p) => S.IsStringByRef(p);
        public override bool IsByRef(JsonElement p) => true;
        public override string Signature(JsonElement p)
            => $"ref {ManagedType(p)} {SafeName(PName(p))}";
        public override string NativeType(JsonElement p) => $"ref {ManagedType(p)}";
        public override IEnumerable<string> NativeParams(JsonElement p)
            => new[]
            {
                $"[MarshalAs(UnmanagedType.LPWStr)] string {SafeName(PName(p))}",
                $"out IntPtr {StringOutLocal(p)}",
            };
        public override IEnumerable<string> CppParams(JsonElement p)
            => new[]
            {
                $"const char16_t* {NativeName(PName(p))}",
                $"void** {NativeName(PName(p))}_out",
            };
        public override IEnumerable<string> CppArgs(JsonElement p)
            => new[] { StringInLocal(NativeName(PName(p))) };
        public override IEnumerable<string> AbiArgs(JsonElement p)
            => new[] { NativeName(PName(p)), $"{NativeName(PName(p))}_out" };
        public override (List<string> Prelude, List<string> Args, List<string> Postlude, List<string> Cleanup)
            Marshal(JsonElement p, string name)
            => (new List<string>(),
                new List<string> { name, $"out IntPtr {StringOutLocal(p)}" },
                new List<string> { $"{name} = ApiRuntime.ToManaged({StringOutLocal(p)});" },
                new List<string>());
        public override string CppParamType(JsonElement p) => PType(p) + "*";
        public override string CppArg(JsonElement p) => "*" + NativeName(PName(p));
        public override string CppCtorArg(JsonElement p) => "*" + NativeName(PName(p));
        public override string TypeKey(JsonElement p) => "ref " + ManagedType(p);
    }

    [ParamHandler(20)]
    sealed class OutParamHandler : ParamHandler
    {
        public override bool CanHandle(JsonElement p) => S.IsOut(p);
        public override bool IsByRef(JsonElement p) => true;
        public override bool IsOut(JsonElement p) => true;
        public override string ManagedType(JsonElement p)
            => S.ManagedType(RefValueType(PType(p)));
        public override string Signature(JsonElement p)
            => $"out {ManagedType(p)} {SafeName(PName(p))}";
        public override string NativeType(JsonElement p)
            => ManagedType(p) == "bool"
                ? "[MarshalAs(UnmanagedType.I1)] out bool"
                : "out " + ManagedType(p);
        public override string CppParamType(JsonElement p) => PType(p);
        public override string CppArg(JsonElement p)
            => S.CppArg(PType(p), NativeName(PName(p)), DefaultCpp(p));
        public override string CppCtorParamType(JsonElement p) => PType(p);
        public override string TypeKey(JsonElement p) => "out " + ManagedType(p);
        public override (List<string> Prelude, List<string> Args, List<string> Postlude, List<string> Cleanup)
            Marshal(JsonElement p, string name)
            => (new List<string>(), new List<string> { "out " + name },
                new List<string>(), new List<string>());
    }

    [ParamHandler(30)]
    sealed class RefParamHandler : ParamHandler
    {
        public override bool CanHandle(JsonElement p) => S.IsRef(p);
        public override bool IsByRef(JsonElement p) => true;
        public override string Signature(JsonElement p)
            => $"ref {ManagedType(p)} {SafeName(PName(p))}";
        public override string NativeType(JsonElement p)
            => ManagedType(p) == "bool"
                ? "[MarshalAs(UnmanagedType.I1)] ref bool"
                : "ref " + ManagedType(p);
        public override IEnumerable<string> CppParams(JsonElement p)
            => IsStruct(RefValueType(PType(p)))
                ? new[] { $"{StructPod(RefValueType(PType(p)))}* {NativeName(PName(p))}" }
                : new[] { $"{CppParamType(p)} {NativeName(PName(p))}" };
        public override IEnumerable<string> CppArgs(JsonElement p)
            => IsStruct(RefValueType(PType(p)))
                ? new[] { $"*reinterpret_cast<{RefValueType(PType(p))}*>({NativeName(PName(p))})" }
                : new[] { "*" + NativeName(PName(p)) };
        public override string CppParamType(JsonElement p) => PType(p) + "*";
        public override string CppArg(JsonElement p) => "*" + NativeName(PName(p));
        public override string CppCtorArg(JsonElement p) => "*" + NativeName(PName(p));
        public override string CppCtorParamType(JsonElement p) => PType(p) + "*";
        public override string TypeKey(JsonElement p) => "ref " + ManagedType(p);
        public override (List<string> Prelude, List<string> Args, List<string> Postlude, List<string> Cleanup)
            Marshal(JsonElement p, string name)
            => (new List<string>(), new List<string> { "ref " + name },
                new List<string>(), new List<string>());
    }

    sealed class InParamHandler : ParamHandler
    {
        public override bool CanHandle(JsonElement p) => true;
        public override bool IsBuffer(JsonElement p)
            => HasParam(p, ApiModel.ParamFlags.Buffer);
        public override string ManagedType(JsonElement p)
            => IsOpaqueStructPtr(p) ? "IntPtr" : ParamValueManagedType(p);
        public override string Signature(JsonElement p)
        {
            string sig = $"{ManagedType(p)} {SafeName(PName(p))}";
            if (!IsOptional(p))
                return sig;
            string type = PType(p);
            string literal = p.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString()
                : null;
            literal = QualifyEnumDefault(literal, type);
            if (literal == "null" && (S.ManagedType(type) == "IntPtr" || IsStruct(RefValueType(type))))
                literal = "default";
            return sig + " = " + (literal ?? CSharpDefault(type));
        }
        public override string NativeType(JsonElement p)
            => IsOpaqueStructPtr(p) ? "IntPtr" : NativeParamType(PType(p));
        public override IEnumerable<string> CppParams(JsonElement p)
        {
            if (IsOpaqueStructPtr(p))
                return new[] { $"void* {NativeName(PName(p))}" };
            if (IsStruct(PType(p)))
                return new[] { $"{StructPod(PType(p))} {NativeName(PName(p))}" };
            return new[] { $"{CppParamType(p)} {NativeName(PName(p))}" };
        }
        public override IEnumerable<string> CppArgs(JsonElement p)
        {
            if (IsOpaqueStructPtr(p))
                return new[] { $"static_cast<{RefValueType(PType(p))}*>({NativeName(PName(p))})" };
            if (IsStruct(PType(p)))
                return new[] { StructFromPod(PType(p), NativeName(PName(p))) };
            return new[] { CppArg(p) };
        }
        public override string CppParamType(JsonElement p)
            => S.CppParamType(PType(p));
        public override string CppArg(JsonElement p)
        {
            string t = PType(p);
            if (IsFunctionType(t))
                return $"reinterpret_cast<{p.GetProperty("cpp").GetString()}>({NativeName(PName(p))})";
            if (t == "TimeSpan")
                return $"std::chrono::duration_cast<{p.GetProperty("cpp").GetString()}>(std::chrono::nanoseconds({NativeName(PName(p))}))";
            if (HasParam(p, ApiModel.ParamFlags.Buffer))
                return $"static_cast<{p.GetProperty("cpp").GetString()}>({NativeName(PName(p))})";
            return S.CppArg(t, NativeName(PName(p)), DefaultCpp(p));
        }
        public override string CppCtorParamType(JsonElement p)
            => IsFunctionType(PType(p))
                ? p.GetProperty("cpp").GetString()
                : S.CppCtorParamType(PType(p));
        public override string CppCtorArg(JsonElement p)
            => IsFunctionType(PType(p))
                ? $"reinterpret_cast<{p.GetProperty("cpp").GetString()}>({NativeName(PName(p))})"
                : NativeName(PName(p));
        public override string TypeKey(JsonElement p) => ParamValueManagedType(p);
    }

    private readonly List<ParamHandler> paramHandlers;
    readonly ParamHandler inParamHandler = new InParamHandler();

    List<ParamHandler> BuildParamHandlers()
    {
        var handlers = new List<(int Order, ParamHandler Handler)>();
        foreach (var type in typeof(TypeSystem).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(ParamHandler).IsAssignableFrom(type))
                continue;
            var attribute = type.GetCustomAttributes(typeof(ParamHandlerAttribute), false)
                .Cast<ParamHandlerAttribute>().FirstOrDefault();
            if (attribute == null)
                continue;
            handlers.Add((attribute.Order,
                (ParamHandler)Activator.CreateInstance(type, nonPublic: true)));
            handlers[^1].Handler.S = this;
        }
        return handlers.OrderBy(h => h.Order).Select(h => h.Handler).ToList();
    }

    internal ParamHandler ParamFor(JsonElement p)
    {
        foreach (var handler in paramHandlers)
            if (handler.CanHandle(p))
                return handler;
        return inParamHandler;
    }
}
}
