using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QtSharp.Generator
{
    public sealed class CallbackEmitter : BindingEmitter
    {
        public CallbackEmitter(GeneratorContext context) : base(context)
        {
        }

        // Every function-type spelling that appears in a published signature. One thunk class per
        // distinct spelling: the ABI shape is a function of the spelling alone, so two APIs taking the
        // same function type share the conversion code. Both positions count - a returned callback is
        // wrapped by the same class that installs one.
        public SortedSet<string> CallbackSpellings()
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            void Add(string t)
            {
                if (B.IsCallbackType(t))
                    found.Add(t);
            }

            foreach (var (fn, _) in B.FreeFunctionEntries())
            {
                Add(B.ReturnType(fn));
                foreach (var p in B.Params(fn))
                    Add(B.PType(p));
            }
            foreach (var cls in B.Classes)
            {
                foreach (string list in new[] { "methods", "virtuals", "constructors" })
                {
                    if (!cls.TryGetProperty(list, out var functions) || functions.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var fn in functions.EnumerateArray())
                    {
                        // Constructors have no return value, and only they lack the property.
                        if (list != "constructors")
                            Add(B.ReturnType(fn));
                        foreach (var p in B.Params(fn))
                            Add(B.PType(p));
                    }
                }
            }
            return found;
        }

        // The managed -> native bridge for one function type. Qt stores only the address, so the
        // generated delegate (a static thunk per spelling) does the value conversion and the runtime
        // registry keeps the thunk and the caller's delegate rooted. The delegate type itself is
        // Action/Func, which is what makes a callback an ordinary C# value.
        public void WriteCallbackThunks(StringBuilder sb, IEnumerable<string> spellings)
        {
            foreach (string spelling in spellings)
            {
                if (!ApiTypes.TryParseCallback(spelling, out string ret, out string[] arguments))
                    continue;

                string className = B.CallbackThunkName(spelling);
                string managed = B.CallbackDelegateType(spelling);
                var declarations = new List<string>();
                var parameters = new List<string>();
                var conversions = new List<string>();
                for (int i = 0; i < arguments.Length; i++)
                {
                    declarations.Add(B.CallbackAbiParam(arguments[i], i));
                    parameters.Add("arg" + i);
                    conversions.Add(B.CallbackParamToManaged(arguments[i], "arg" + i));
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
                sb.AppendLine($"        private delegate {B.CallbackAbiType(ret)} Native({string.Join(", ", declarations)});");
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
                    sb.AppendLine($"                    return {B.CallbackReturnToAbi(ret, call)};");
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
                        abiArguments.Add(B.CallbackManagedToAbi(arguments[i], "arg" + i));
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
                        : B.CallbackAbiToManagedReturn(ret, nativeCall);
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
                        sb.AppendLine($"                        return {B.CallbackAbiToManagedReturn(ret, nativeCall)};");
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
    
    }
}
