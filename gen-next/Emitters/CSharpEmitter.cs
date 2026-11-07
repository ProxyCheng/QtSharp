using System;
using System.Collections.Generic;
using System.Linq;

namespace QtSharp.Generator
{
    /// <summary>
    /// The C# side of the binding. It owns the set of files this run produces; each declaration
    /// kind is a separate sub-emitter that appends to that set, so the driver stays a list of
    /// concerns rather than a chain of conditions.
    /// </summary>
    public sealed class CSharpEmitter : Emitter
    {
        public CSharpEmitter(GeneratorContext context) : base(context)
        {
        }

        public override void Emit()
        {
            string ns = Context.Model.Namespace ?? "QtSharp.Api";
            var binding = Context.Binding;
            var files = new CSharpFileSet(ns, binding.ModelFileName);

            new EnumEmitter(Context).Emit(files);
            new FreeFunctionEmitter(Context).Emit(files);

            foreach (var cls in binding.Classes)
            {
                var file = files.File(CSharpFileSet.HeaderKey(cls.GetProperty("header").GetString()));
                new CSharpClassEmitter(Context).WriteClass(file, cls);
                file.AppendLine("    internal static partial class ApiNative");
                file.AppendLine("    {");
                new NativeEmitter(Context).WriteNativeDeclarations(file, cls);
                file.AppendLine("    }");
                file.AppendLine();
            }

            var callbackSpellings = new CallbackEmitter(Context).CallbackSpellings();
            if (callbackSpellings.Count > 0)
                new CallbackEmitter(Context).WriteCallbackThunks(
                    files.File(CSharpFileSet.PlainKey(ns + ".Callbacks")), callbackSpellings);

            new QObjectRegistrationEmitter(Context).Emit(files);
            EmitVariantConversions(files, ns);
            EmitStructMembers(files);
            EmitShared(files, ns);
            EmitHelpers(files, ns);

            files.Save(Context.Output);
        }

        private void EmitVariantConversions(CSharpFileSet files, string ns)
        {
            var binding = Context.Binding;
            var variantClasses = binding.Classes
                .Where(c => binding.ConvertsToVariant(c) && binding.HasClass(c, ApiModel.ClassFlags.Owned))
                .ToList();
            var variantCtorTypes = binding.VariantCtorRegistrations();
            var variantStructs = ApiTypes.RuntimeStructs
                .Where(s => binding.ModelRuntimeStructNames.Contains(s.Name) && binding.StructHasVariant(s.Name))
                .Select(s => s.Name)
                .ToList();
            if (variantClasses.Count == 0 && variantCtorTypes.Count == 0 && variantStructs.Count == 0)
                return;

            var conversions = files.File(CSharpFileSet.PlainKey(ns + ".VariantConversions"));
            conversions.AppendLine("    internal static class VariantConversions");
            conversions.AppendLine("    {");
            conversions.AppendLine("        [ModuleInitializer]");
            conversions.AppendLine("        internal static void Register()");
            conversions.AppendLine("        {");
            foreach (var cls in variantClasses)
            {
                string name = binding.Name(cls);
                conversions.AppendLine(
                    $"            QtSharp.QtCore.QtVariantConversion.Register(typeof({name}), {binding.Quoted(binding.CppClass(name))}, static handle => {name}.FromOwnedHandle(handle));");
            }
            foreach (var (managed, cpp, unwrapBody) in variantCtorTypes)
            {
                string unwrap = unwrapBody == null ? "null" : $"static h => {unwrapBody}";
                conversions.AppendLine(
                    $"            QtSharp.QtCore.QtVariantConversion.RegisterFactory(typeof({managed}), {binding.Quoted(cpp)}, static v => new QtSharp.QtCore.QVariant(({managed})v).DetachHandle(), {unwrap});");
            }
            foreach (string name in variantStructs)
            {
                conversions.AppendLine(
                    $"            QtSharp.QtCore.QtVariantConversion.RegisterFactory(typeof({name}), {binding.Quoted(binding.CppClass(name))}, static v => ApiNative.{name}_toQVariant(({name})v), static h => ApiNative.{name}_fromQVariant(h));");
            }
            conversions.AppendLine("        }");
            conversions.AppendLine("    }");
            conversions.AppendLine();
        }

        private void EmitStructMembers(CSharpFileSet files)
        {
            var binding = Context.Binding;
            foreach (var s in ApiTypes.RuntimeStructs)
            {
                if (!binding.ModelRuntimeStructNames.Contains(s.Name))
                    continue;
                var row = binding.CoreClassOf(s.Name);
                if (row == null)
                    continue;
                new StructEmitter(Context).WriteStructMembers(
                    files.File(CSharpFileSet.HeaderKey(row.Value.GetProperty("header").GetString())), s.Name);
            }
        }

        private void EmitShared(CSharpFileSet files, string ns)
        {
            var binding = Context.Binding;
            var shared = files.File(CSharpFileSet.PlainKey(ns + ".Shared"));
            shared.AppendLine("    internal static partial class ApiNative");
            shared.AppendLine("    {");
            shared.AppendLine("        // Where every P/Invoke below goes. The core binding calls the core glue; an extension");
            shared.AppendLine("        // module calls its own DLL, which is how it stays a separate package.");
            shared.AppendLine($"        internal const string Library = \"{binding.NativeLibraryName}\";");
            foreach (var boxed in binding.VirtualBoxedReturns())
            {
                string stem = binding.ListStem(boxed);
                shared.AppendLine();
                shared.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_assign_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
                shared.AppendLine($"        internal static extern void Assign_{stem}(IntPtr destination, IntPtr source);");
            }
            foreach (var boxed in binding.VirtualBoxedParams())
            {
                string stem = binding.ListStem(boxed);
                shared.AppendLine();
                shared.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_clone_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
                shared.AppendLine($"        internal static extern IntPtr Clone_{stem}(IntPtr source);");
            }
            shared.AppendLine("    }");
        }

        private void EmitHelpers(CSharpFileSet files, string ns)
        {
            var binding = Context.Binding;
            var containerTypes = binding.ContainerTypes();
            if (containerTypes.Count > 0)
            {
                var helpers = files.File(CSharpFileSet.PlainKey(ns + ".Helpers"));
                var glue = new ContainerGlueEmitter(Context);
                var apiNative = new CodeWriter(helpers, 1);
                apiNative.Open("internal static partial class ApiNative");
                var declarationWriter = new CodeWriter(helpers, 2);
                foreach (var type in containerTypes.Where(binding.IsSequenceT))
                    glue.WriteSequenceNativeDeclarations(declarationWriter, type);
                foreach (var type in containerTypes.Where(binding.IsAssocT))
                    glue.WriteAssocNativeDeclarations(declarationWriter, type);
                foreach (var type in containerTypes.Where(binding.IsPairT))
                    glue.WritePairNativeDeclarations(declarationWriter, type);
                apiNative.Close();
                var helperWriter = new CodeWriter(helpers, 1);
                foreach (var type in containerTypes.Where(binding.IsSequenceT))
                    glue.WriteSequenceHelper(helperWriter, type);
                foreach (var type in containerTypes.Where(binding.IsAssocT))
                    glue.WriteAssocHelper(helperWriter, type);
                foreach (var type in containerTypes.Where(binding.IsPairT))
                    glue.WritePairHelper(helperWriter, type);
            }

            var optionalTypes = binding.OptionalTypes();
            if (optionalTypes.Count > 0)
            {
                var helpers = files.File(CSharpFileSet.PlainKey(ns + ".Helpers"));
                var glue = new ContainerGlueEmitter(Context);
                var apiNative = new CodeWriter(helpers, 1);
                apiNative.Open("internal static partial class ApiNative");
                var declarationWriter = new CodeWriter(helpers, 2);
                foreach (var type in optionalTypes)
                    glue.WriteOptionalNativeDeclarations(declarationWriter, type);
                apiNative.Close();
                var helperWriter = new CodeWriter(helpers, 1);
                foreach (var type in optionalTypes)
                    glue.WriteOptionalHelper(helperWriter, type);
            }
        }
    }
}
