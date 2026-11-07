using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QtSharp.Generator
{
    public sealed class StructEmitter : BindingEmitter
    {
        public StructEmitter(GeneratorContext context) : base(context)
        {
        }

        /// <summary>
        /// The constructors the model declares for a value type, without the parameterless one: C#'s
        /// `default` already produces the same zero value Qt's own default constructor does.
        /// </summary>
        public List<JsonElement> StructCtors(string name)
        {
            var ctors = new List<JsonElement>();
            if (!B.ModelClassNames.Contains(name))
                return ctors;
            var cls = B.CoreClassOf(name);
            if (cls == null || !cls.Value.TryGetProperty("constructors", out var cs))
                return ctors;
            // C#'s `default` is Qt's default construction for almost every value type. QCalendar is
            // the exception: default(QCalendar) is the null backend, while QCalendar() is Gregorian,
            // so its parameterless constructor stays callable.
            bool keepZeroCtor = B.ZeroCtorStructs.Contains(name);
            foreach (var c in cs.EnumerateArray())
                if (B.Params(c).Count > 0 || keepZeroCtor)
                    ctors.Add(c);
            return ctors;
        }


        /// <summary>
        /// The comparison operators the model declares for a value type. C++ declares them as functions
        /// too, so they arrive here with names like `operator==`, and C# spells them as operators.
        /// </summary>
        public List<JsonElement> StructComparisons(string name)
        {
            var ops = new List<JsonElement>();
            if (!B.ModelClassNames.Contains(name))
                return ops;
            var cls = B.CoreClassOf(name);
            if (cls == null || !cls.Value.TryGetProperty("methods", out var ms))
                return ops;
            foreach (var m in ms.EnumerateArray())
                if (ComparisonSymbol(B.Name(m)) != null && B.Params(m).Count == 2)
                    ops.Add(m);
            return ops;
        }


        /// <summary>The C# spelling of a comparison the model spells the C++ way, or null.</summary>
        public string ComparisonSymbol(string modelName) => modelName switch
        {
            "operator<" => "<",
            "operator<=" => "<=",
            "operator>" => ">",
            "operator>=" => ">=",
            _ => null,
        };


        /// <summary>A C identifier for one of them, used in the export and P/Invoke names.</summary>
        public string ComparisonEntry(JsonElement op) => B.Name(op) switch
        {
            "operator==" => "eq",
            "operator!=" => "ne",
            "operator<" => "lt",
            "operator<=" => "le",
            "operator>" => "gt",
            "operator>=" => "ge",
            _ => "cmp",
        };

        public List<(JsonElement Method, string Entry, string Cs)> StructMembers(string name)
        {
            var members = new List<(JsonElement, string, string)>();
            if (!B.ModelClassNames.Contains(name))
                return members;
            var cls = B.CoreClassOf(name);
            if (cls == null || !cls.Value.TryGetProperty("methods", out var ms))
                return members;
            var written = B.HandWrittenMembers(name);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var signatures = new HashSet<string>(StringComparer.Ordinal);
            void Add(JsonElement m)
            {
                string n = B.Name(m);
                // An operator or the destructor is not a member C# can forward under that name.
                if (!B.IsIdentifier(n) || n == name)
                    return;
                // A struct has no inheritance, so the methods it inherits from its base classes have to
                // be folded in. QOperatingSystemVersion's major/minor/micro accessors live in
                // QOperatingSystemVersionBase: as a class they arrived through the base chain, as a
                // struct they would otherwise disappear. Own members win over an inherited overload.
                string signature = n + "(" + string.Join(",", B.Params(m).Select(B.PType)) + ")";
                if (!signatures.Add(signature))
                    return;
                string cs = B.MemberName(name, n);
                if (written.Contains(cs))
                    return;
                seen.TryGetValue(n, out int count);
                seen[n] = count + 1;
                members.Add((m, n + (count == 0 ? "" : (count + 1).ToString()), cs));
            }
            foreach (var m in ms.EnumerateArray())
                Add(m);
            foreach (var baseCls in B.BaseChainOf(cls.Value))
                if (baseCls.TryGetProperty("methods", out var baseMethods))
                    foreach (var m in baseMethods.EnumerateArray())
                        Add(m);
            return members;
        }


        public void WriteStructMembers(StringBuilder sb, string name)
        {
            var members = StructMembers(name);
            var ctors = StructCtors(name);
            var written = B.HandWrittenMembers(name);
            bool handWrittenCtor = written.Contains(name);
            bool hasVariant = B.StructHasVariant(name);
            // A type whose whole surface is hand-written still gets its constructors generated (the last
            // ones not to be), so the early return has to account for them.
            if (!hasVariant && members.Count == 0 && (handWrittenCtor || ctors.Count == 0))
                return;
            var row = ApiTypes.RuntimeStructOf(name).Value;
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>{name}: {string.Join(", ", row.Types)} fields, Qt's own members forwarded through them.</summary>");
            sb.AppendLine($"    [GeneratedCode(\"qtgen\", \"{BindingModel.GeneratedByVersion}\")]");
            string partial = B.handWrittenParts.Contains(name) ? "partial " : "";
            sb.AppendLine($"    public {partial}struct {name} : IEquatable<{name}>");
            sb.AppendLine("    {");
            // The storage. The field names are the model's, and their order is the POD's, which is Qt's
            // memory order - that is the whole of the C# side of a value type.
            // The field's C# spelling, not the model's: "long long" is `long` over here (QDate's
            // julian day), and a pointer field is IntPtr, while the POD keeps the C++ spelling it needs.
            for (int i = 0; i < row.Fields.Length; i++)
                sb.AppendLine($"        {B.ManagedStructFieldType(row.Types[i])} _{row.Fields[i]};");
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
                string symbol = ComparisonSymbol(B.Name(op));
                sb.AppendLine($"        public static bool operator {symbol}({name} a, {name} b) => ApiNative.{name}_{ComparisonEntry(op)}(a, b);");
            }
            if (StructComparisons(name).Count > 0)
                sb.AppendLine();
            // The nested types Qt declares on it (QLineF::IntersectionType): the class that would have
            // carried them is not generated, so the struct does.
            B.WriteEnums(sb, name, 2);
            // Constructors, forwarded like every other member: the glue builds Qt's own value from the
            // arguments and hands back its bytes, so no formula is translated by hand - QRect's
            // (x, y, width, height) becoming two corners is Qt's rule, not a copy of it.
            if (!handWrittenCtor)
            {
                int n = 0;
                foreach (var ctor in ctors)
                {
                    var cps = B.Params(ctor);
                    // The arguments go over the same way every other call's do: an enum as its int, a
                    // bound class as its handle, a nested value type as itself. Passing the managed
                    // spelling straight through compiled only while every argument happened to be a
                    // scalar (QDate's QCalendar overload and QVector's Qt::Initialization did not).
                    var marshal = B.MarshalParams(cps, null);
                    sb.AppendLine($"        public {name}({string.Join(", ", cps.Select(B.ParamSig))})");
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
                var ps = B.Params(m);
                string ret = B.ReturnType(m);
                string staticMod = B.IsStatic(m) ? "static " : "";
                // A member that mutates in place hands over its own storage by reference: the value
                // cannot be changed through a copy, and C# lets a struct method pass `ref this`.
                string self = B.IsStatic(m) ? null : MutatesInPlace(name, m, ret) ? "ref this" : "this";
                sb.AppendLine($"        public {staticMod}{B.ManagedRetType(ret)} {cs}({string.Join(", ", ps.Select(B.ParamSig))})");
                sb.AppendLine("        {");
                var marshal = B.MarshalParams(ps, self);
                foreach (string line in marshal.Prelude)
                    sb.AppendLine("            " + line);
                string call = $"ApiNative.{name}_{entry}({string.Join(", ", marshal.Args)})";
                string body = ret == "void" ? call + ";" : "return " + B.RetToManaged(ret, call) + ";";
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
                    sb.AppendLine($"        internal static extern {name} {name}_ctor{n}({string.Join(", ", B.Params(ctor).SelectMany(B.NativeParamsFor))});");
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
                var ps = B.Params(m);
                var nativeParams = new List<string>();
                // The value crosses as itself: the managed struct and the POD are the same bytes. A
                // mutating one crosses by reference, so the write-back reaches the caller's storage.
                if (!B.IsStatic(m))
                    nativeParams.Add(MutatesInPlace(name, m, B.ReturnType(m)) ? $"ref {name} self" : $"{name} self");
                nativeParams.AddRange(ps.SelectMany(B.NativeParamsFor));
                string ret = B.ReturnType(m);
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_{entry}\", CallingConvention = CallingConvention.Cdecl)]");
                if (ret == "bool")
                    sb.AppendLine("        [return: MarshalAs(UnmanagedType.I1)]");
                sb.AppendLine($"        internal static extern {B.NativeRetType(ret)} {name}_{entry}({string.Join(", ", nativeParams)});");
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
        public bool MutatesInPlace(string name, JsonElement m, string ret)
            // Qt's own mutators either return void (QSize::transpose) or return *this by reference for
            // chaining (QTransform::scale/translate/rotate/shear, QMatrix4x4's). A struct has no handle
            // behind which the mutation could happen, so both shapes have to write back through `ref
            // this` - the old class wrapper hid the second case.
            => !B.IsStatic(m) && !IsConstMethod(m) && (ret == "void" || ret == name);


        public bool IsConstMethod(JsonElement m)
            => B.HasMethod(m, ApiModel.MethodFlags.Const);


        public void WriteStructNative(StringBuilder sb, string name)
        {
            if (!B.HandWrittenMembers(name).Contains(name))
            {
                int n = 0;
                foreach (var ctor in StructCtors(name))
                {
                    var cps = B.Params(ctor);
                    sb.AppendLine($"extern \"C\" QS_API_EXPORT {B.StructPod(name)} qs_api_{name}_ctor{n}({string.Join(", ", cps.SelectMany(B.CppParamsFor))})");
                    sb.AppendLine("{");
                    // A parameterless constructor keeps its own line: `QCalendar qs_new();` would
                    // declare a function instead of building the value.
                    sb.AppendLine(cps.Count == 0
                        ? $"    {ApiTypes.CppStructName(name)} qs_new;"
                        : $"    {ApiTypes.CppStructName(name)} qs_new({string.Join(", ", cps.SelectMany(B.CppArgsFor))});");
                    sb.AppendLine($"    return {B.StructToPod(name, "qs_new")};");
                    sb.AppendLine("}");
                    sb.AppendLine();
                    n++;
                }
            }
            foreach (var op in StructComparisons(name))
            {
                string cmp = ComparisonEntry(op);
                sb.AppendLine($"extern \"C\" QS_API_EXPORT bool qs_api_{name}_cmp_{cmp}({B.StructPod(name)} a, {B.StructPod(name)} b)");
                sb.AppendLine("{");
                sb.AppendLine($"    return {B.StructFromPod(name, "a")} {ComparisonSymbol(B.Name(op))} {B.StructFromPod(name, "b")};");
                sb.AppendLine("}");
                sb.AppendLine();
            }
            {
                // Equality and hashing belong to Qt. The glue asks the traits in QtSharpApiRuntime.h:
                // operator== when the type declares one, otherwise the POD bytes; qHash when the type
                // declares one, otherwise a generated field-wise hash (or, for a type whose opaque
                // words are all there is, the bytes). The few types whose hash Qt cannot provide or
                // whose operator== ignores part of the value keep an expression in NativeHashStructs.
                string pod = B.StructPod(name);
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
            if (B.StructHasVariant(name))
            {
                // The conversion operator is the only way into a QVariant for a trivially copyable
                // QtGui type: qvariant.h has no constructor for it.
                string pod = B.StructPod(name);
                sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{name}_toQVariant({pod} qs_pod)");
                sb.AppendLine("{");
                sb.AppendLine($"    return new QVariant(static_cast<QVariant>({B.StructFromPod(name, "qs_pod")}));");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine($"extern \"C\" QS_API_EXPORT {pod} qs_api_{name}_fromQVariant(const void* variant)");
                sb.AppendLine("{");
                sb.AppendLine($"    {ApiTypes.CppStructName(name)} qs_value = static_cast<const QVariant*>(variant)->value<{ApiTypes.CppStructName(name)}>();");
                sb.AppendLine($"    return {B.StructToPod(name, "qs_value")};");
                sb.AppendLine("}");
                sb.AppendLine();
            }
            foreach (var (m, entry, cs) in StructMembers(name))
            {
                var ps = B.Params(m);
                string ret = B.ReturnType(m);
                bool mutates = MutatesInPlace(name, m, ret);
                string pod = B.StructPod(name);
                var nativeParams = new List<string>();
                if (!B.IsStatic(m))
                    nativeParams.Add(mutates ? $"{pod}* qs_pod" : $"{pod} qs_pod");
                nativeParams.AddRange(ps.SelectMany(B.CppParamsFor));
                string args = string.Join(", ", ps.SelectMany(B.CppArgsFor));
                string call = B.IsStatic(m) ? $"{ApiTypes.CppStructName(name)}::{B.Name(m)}({args})" : $"qs_self.{B.Name(m)}({args})";
                sb.AppendLine($"extern \"C\" QS_API_EXPORT {B.NativeRetCpp(ret)} qs_api_{name}_{entry}({string.Join(", ", nativeParams)})");
                sb.AppendLine("{");
                if (mutates)
                {
                    // The call changes qs_self in place, and the caller's storage has to see that; a
                    // value-returning mutator (QTransform::scale returns *this for chaining) also hands
                    // the result back, so the write-back must happen before the return statement.
                    sb.AppendLine($"    {ApiTypes.CppStructName(name)} qs_self = {B.StructFromPod(name, "(*qs_pod)")};");
                    if (ret == "void")
                    {
                        foreach (string line in B.NativeBody(m, call))
                            sb.AppendLine("    " + line);
                        sb.AppendLine($"    *qs_pod = {B.StructToPod(name, "qs_self")};");
                    }
                    else
                    {
                        sb.AppendLine($"    auto qs_result = {call};");
                        sb.AppendLine($"    *qs_pod = {B.StructToPod(name, "qs_self")};");
                        sb.AppendLine("    " + B.NativeRetExpr(ret, "qs_result"));
                    }
                }
                else
                {
                    if (!B.IsStatic(m))
                        sb.AppendLine($"    {ApiTypes.CppStructName(name)} qs_self = {B.StructFromPod(name, "qs_pod")};");
                    foreach (string line in B.NativeBody(m, call))
                        sb.AppendLine("    " + line);
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }
        }


    
    }
}
