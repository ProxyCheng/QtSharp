using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace QtSharp.Generator
{
    /// <summary>
    /// Builds the run's class/enum vocabulary from the model and its dependencies. This is the
    /// one place that decides which rows this assembly emits and which belong to another module.
    /// </summary>
    public sealed partial class BindingModel
    {
        public BindingModel(GeneratorContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            ModelPath = Path.GetFullPath(context.Model.Path);
            ModelFileName = Path.GetFileName(context.Model.Path);
            Namespace = context.Model.Namespace ?? ApiModel.CoreNamespace;
            CurrentModule = ModuleCatalog.Describe(context.ModuleName);
            if (context.Options.Module != null && CurrentModule == null)
                throw new InvalidOperationException($"Unknown module '{context.Options.Module}'.");
            if (CurrentModule != null)
                CurrentModule.Namespace = Namespace;
            NativeLibraryName = CurrentModule?.NativeLibrary ?? "QtSharp.Api";

            var modelClasses = context.Model.Classes.ToList();
            var modelEnums = context.Model.Enums.ToList();
            var classNames = new HashSet<string>(StringComparer.Ordinal);
            var emittedRows = new List<JsonElement>();
            var runtimeStructs = new HashSet<string>(StringComparer.Ordinal);
            var emitted = new List<JsonElement>();
            foreach (var cls in modelClasses)
            {
                string name = Name(cls);
                classNames.Add(name);
                if (name is not ("QUuid" or "QChar"))
                    emittedRows.Add(cls);
                if (IsRuntimeStructClass(cls))
                    runtimeStructs.Add(name);
                if (!IsRuntimeStructClass(cls) && name is not ("QUuid" or "QChar"))
                    emitted.Add(cls);
            }

            ModelClassNames = classNames;
            EmittedClassRows = emittedRows;
            ModelRuntimeStructNames = runtimeStructs;
            Classes = emitted;

            // An addon model carries its dependencies' enums too, tagged with the owning module;
            // only the owner emits them, or every addon assembly would declare QtCore's enums.
            Enums = context.ModuleName == null
                ? modelEnums
                : modelEnums.Where(e => !e.TryGetProperty("module", out var owner)
                                        || owner.ValueKind != JsonValueKind.String
                                        || owner.GetString() == context.ModuleName).ToList();
            Functions = context.Model.Functions.ToList();

            ClassModules = new Dictionary<string, ApiModel.ExtensionModule>(StringComparer.Ordinal);
            EnumModules = new Dictionary<string, ApiModel.ExtensionModule>(StringComparer.Ordinal);

            void RegisterEnum(JsonElement e, ApiModel.ExtensionModule fallback)
            {
                string qualified = e.GetProperty("qualified").GetString();
                var owner = fallback;
                if (e.TryGetProperty("module", out var ownerName) && ownerName.ValueKind == JsonValueKind.String)
                    owner = ModuleCatalog.Describe(ownerName.GetString()) ?? fallback;
                EnumModules[qualified] = owner;
                EnumModules[EnumFullName(owner, qualified)] = owner;
            }

            var dependencyClasses = new List<JsonElement>();
            var dependencyEnums = new List<JsonElement>();
            foreach (var dependency in context.Dependencies)
            {
                var descriptor = ModuleCatalog.Describe(dependency.Module);
                if (descriptor != null && dependency.Namespace != null)
                    descriptor.Namespace = dependency.Namespace;
                foreach (var cls in dependency.Classes)
                {
                    dependencyClasses.Add(cls);
                    ClassModules[Ref(cls)] = descriptor;
                }
                foreach (var e in dependency.Enums)
                {
                    dependencyEnums.Add(e);
                    RegisterEnum(e, descriptor);
                }
            }

            foreach (var cls in Classes)
                ClassModules[Ref(cls)] = CurrentModule;
            foreach (var e in modelEnums)
                RegisterEnum(e, CurrentModule);

            var knownClasses = dependencyClasses.Concat(modelClasses).ToList();
            var knownEnums = dependencyEnums.Concat(modelEnums).ToList();
            KnownClasses = knownClasses;

            ByName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            ByRef = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            CppNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var cls in knownClasses)
            {
                ByName[Name(cls)] = cls;
                ByRef[Ref(cls)] = cls;
                if (cls.TryGetProperty("cppName", out var cpp))
                    CppNames[Name(cls)] = cpp.GetString();
            }
            ClassNames = new HashSet<string>(knownClasses.Select(Ref), StringComparer.Ordinal);
            foreach (var cls in knownClasses)
                ClassNames.Add(Name(cls));

            EnumNames = new HashSet<string>(StringComparer.Ordinal);
            CppEnumNames = new Dictionary<string, string>(StringComparer.Ordinal);
            FlagsEnums = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in knownEnums)
            {
                string qualified = e.GetProperty("qualified").GetString();
                string full = EnumFullName(
                    EnumModules.TryGetValue(qualified, out var owner) ? owner : null, qualified);
                string cpp = e.TryGetProperty("cppQualified", out var cppQualified)
                    ? cppQualified.GetString()
                    : full;
                EnumNames.Add(qualified);
                EnumNames.Add(full);
                CppEnumNames[qualified] = cpp;
                CppEnumNames[full] = cpp;
                if (e.TryGetProperty("flags", out var f) && f.ValueKind == JsonValueKind.True)
                {
                    FlagsEnums.Add(qualified);
                    FlagsEnums.Add(full);
                }
            }

            PrepareNames();
            handWrittenParts = ReadHandWrittenParts();
            Types = new TypeSystem(this);
        }
    }
}
