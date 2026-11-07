using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QtSharp.Generator
{
    /// <summary>
    /// Namespace-level Qt functions. They are published as static members of the same class that
    /// carries the Qt-namespace enums, grouped by the header that declares them.
    /// </summary>
    public sealed class FreeFunctionEmitter : BindingEmitter
    {
        public FreeFunctionEmitter(GeneratorContext context) : base(context)
        {
        }

        public void Emit(CSharpFileSet files)
        {
            foreach (var group in B.FreeFunctionEntries()
                         .GroupBy(f => f.Function.GetProperty("header").GetString(), System.StringComparer.Ordinal))
            {
                var functions = group.ToList();
                var file = files.File(CSharpFileSet.HeaderKey(group.Key));
                WriteFreeFunctions(file, functions);
                file.AppendLine("    internal static partial class ApiNative");
                file.AppendLine("    {");
                WriteFreeFunctionNativeDeclarations(file, functions);
                file.AppendLine("    }");
                file.AppendLine();
            }
        }

        private void WriteFreeFunctions(
            StringBuilder sb, IReadOnlyList<(JsonElement Function, string Entry)> functions)
        {
            if (functions.Count == 0)
                return;
            sb.AppendLine("    /// <summary>Qt's namespace-level functions.</summary>");
            sb.AppendLine($"    public static partial class {B.NamespaceClassName}");
            sb.AppendLine("    {");
            foreach (var (fn, entry) in functions)
            {
                string fname = B.Name(fn);
                var ps = B.Params(fn);
                string sig = string.Join(", ", ps.Select(B.ParamSig));
                string ret = B.ReturnType(fn);
                sb.AppendLine($"        /// <summary>Calls <c>{fname}</c>.</summary>");
                sb.AppendLine($"        public static {B.ManagedRetType(ret)} {B.Pascal(fname)}({sig})");
                sb.AppendLine("        {");
                var marshal = B.MarshalParams(ps, null);
                foreach (string line in marshal.Prelude)
                    sb.AppendLine("            " + line);
                string call = $"ApiNative.{entry}({string.Join(", ", marshal.Args)})";
                string body;
                if (marshal.Postlude.Count == 0)
                    body = ret == "void" ? $"{call};" : $"return {B.RetToManaged(ret, call)};";
                else
                {
                    string writeBack = string.Join("\n", marshal.Postlude);
                    body = ret == "void"
                        ? $"{call};\n{writeBack}"
                        : $"var __result = {B.RetToManaged(ret, call)};\n{writeBack}\nreturn __result;";
                }
                B.EmitGuarded(sb, "            ", body, marshal.Cleanup);
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        internal void EmitNative(StringBuilder sb)
        {
            foreach (var (fn, entry) in B.FreeFunctionEntries())
            {
                string fname = B.Name(fn);
                var ps = B.Params(fn);
                string sig = string.Join(", ", ps.SelectMany(B.CppParamsFor));
                string ret = B.ReturnType(fn);
                string args = string.Join(", ", ps.SelectMany(B.CppArgsFor));
                NativeTemplates.AppendExport(sb, B.NativeRetCpp(ret), $"qs_api_{entry}", sig,
                    B.NativeBody(fn, $"{fname}({args})"));
                sb.AppendLine();
            }
        }

        private void WriteFreeFunctionNativeDeclarations(
            StringBuilder sb, IReadOnlyList<(JsonElement Function, string Entry)> functions)
        {
            foreach (var (fn, entry) in functions)
            {
                var ps = B.Params(fn);
                string sig = string.Join(", ", ps.SelectMany(B.NativeParamsFor));
                string ret = B.ReturnType(fn);
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{entry}\", CallingConvention = CallingConvention.Cdecl)]");
                if (ret == "bool")
                    sb.AppendLine("        [return: MarshalAs(UnmanagedType.I1)]");
                sb.AppendLine($"        internal static extern {B.NativeRetType(ret)} {entry}({sig});");
            }
        }
    }
}
