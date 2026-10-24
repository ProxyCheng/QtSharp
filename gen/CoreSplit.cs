using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// Splits the combined base model (qtsharp.api.json) into one model per Qt module. The combined
// extraction remains the checked-in gate; these derived models are what the ten base assemblies
// are generated from, and what addon models use as dependency models.
public static partial class ApiModel
{
    public static int RunSplitCore(string modelPath, string outDir)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(modelPath));
        var root = document.RootElement;
        var modules = ExtensionModules.Where(m => m.IsCore).ToList();
        var byQtModule = modules.ToDictionary(m => m.QtModules[0], m => m, StringComparer.Ordinal);

        var classes = root.GetProperty("classes").EnumerateArray().ToList();
        var enums = root.GetProperty("enums").EnumerateArray().ToList();
        var functions = root.TryGetProperty("functions", out var fns) && fns.ValueKind == JsonValueKind.Array
            ? fns.EnumerateArray().ToList()
            : new List<JsonElement>();

        // A class belongs to the module named by the first segment of its header
        // (QtWidgets/qwidget.h -> qtwidgets).
        var classModule = new Dictionary<string, ExtensionModule>(StringComparer.Ordinal);
        var classRow = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var cls in classes)
        {
            string header = cls.GetProperty("header").GetString();
            string qtModule = header.Split('/')[0];
            if (!byQtModule.TryGetValue(qtModule, out var module))
                throw new InvalidOperationException($"Class '{cls.GetProperty("name").GetString()}' has header '{header}', which names no base module.");
            string name = cls.GetProperty("name").GetString();
            classModule[name] = module;
            classRow[name] = cls;
        }

        // A class cannot live in a module that does not build on its base class: the split would
        // either invent an assembly cycle or fail to compile. Qt's forwarding headers make the
        // header directory the wrong owner now and then (QtGui/qevent.h re-exports QtCore's
        // QEvent, so QEvent would land in QtGui while QObject and QChildEvent - both QtCore -
        // derive from it). The dependency graph is authoritative: when the owning module does not
        // depend on the base's module, the base belongs on the foundational side of that
        // contradiction, so move it to the owning module. Repeating until stable also handles a
        // misplaced base with a misplaced base of its own.
        bool moved = true;
        while (moved)
        {
            moved = false;
            foreach (var cls in classes)
            {
                string name = cls.GetProperty("name").GetString();
                var module = classModule[name];
                if (!cls.TryGetProperty("bases", out var bases) || bases.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var baseElement in bases.EnumerateArray())
                {
                    string baseName = baseElement.GetString().TrimEnd('*', '&');
                    if (!classRow.TryGetValue(baseName, out var baseClass))
                        continue;
                    var baseModule = classModule[baseName];
                    if (ReferenceEquals(baseModule, module) || DependsOn(module, baseModule))
                        continue;
                    classModule[baseName] = module;
                    moved = true;
                    Console.WriteLine($"[split-core] {baseName}: {baseModule.QtModules[0]} -> {module.QtModules[0]} " +
                                      $"(base of {name}; a module cannot depend on the module it is split out of)");
                }
            }
        }

        // The same contradiction in a signature would be a split bug even though it is not a
        // cycle: fail loudly instead of emitting an assembly that cannot resolve the type.
        foreach (var cls in classes)
        {
            string name = cls.GetProperty("name").GetString();
            var module = classModule[name];
            foreach (string referenced in ReferencedClassNames(cls))
            {
                if (!classModule.TryGetValue(referenced, out var referencedModule))
                    continue;
                if (!ReferenceEquals(referencedModule, module) && !DependsOn(module, referencedModule))
                    throw new InvalidOperationException(
                        $"'{name}' ({module.QtModules[0]}) references '{referenced}' " +
                        $"({referencedModule.QtModules[0]}), which its module does not depend on.");
            }
        }

        var classesByModule = modules.ToDictionary(m => m.Name, _ => new List<JsonElement>(), StringComparer.Ordinal);
        foreach (var cls in classes)
            classesByModule[classModule[cls.GetProperty("name").GetString()].Name].Add(cls);

        // An enum's declaration says which module owns it (Qt::BrushStyle is declared in
        // QtGui/qbrush.h even though its C++ scope is Qt, and Qt::Alignment in QtCore). Older
        // models have no module field, so fall back to the owning class's module; a namespace-level
        // enum without one is the Qt namespace's and belongs to QtCore.
        var enumsByModule = modules.ToDictionary(m => m.Name, _ => new List<JsonElement>(), StringComparer.Ordinal);
        foreach (var e in enums)
        {
            string qualified = e.GetProperty("qualified").GetString();
            ExtensionModule module;
            if (e.TryGetProperty("module", out var declaredModule) && declaredModule.ValueKind == JsonValueKind.String
                && byQtModule.TryGetValue(BaseQtModule(declaredModule.GetString()), out module))
            {
                // recorded by the extractor
            }
            else
            {
                int at = qualified.LastIndexOf("::", StringComparison.Ordinal);
                string scope = at < 0 ? string.Empty : qualified.Substring(0, at);
                if (scope.Length == 0 || scope == "Qt")
                    module = byQtModule["QtCore"];
                else if (!classModule.TryGetValue(scope, out module))
                {
                    // A nested scope (Outer::Inner) is not expected in the base model; fall back to
                    // the last segment so a new nested enum does not silently disappear.
                    string simple = scope.Substring(scope.LastIndexOf("::", StringComparison.Ordinal) + 2);
                    if (!classModule.TryGetValue(simple, out module))
                        throw new InvalidOperationException($"Enum '{qualified}' has an unknown owning class.");
                }
            }
            enumsByModule[module.Name].Add(e);
        }

        var functionsByModule = modules.ToDictionary(m => m.Name, _ => new List<JsonElement>(), StringComparer.Ordinal);
        foreach (var fn in functions)
        {
            string header = fn.GetProperty("header").GetString();
            string qtModule = header.Split('/')[0];
            if (!byQtModule.TryGetValue(qtModule, out var module))
                module = byQtModule["QtCore"];
            functionsByModule[module.Name].Add(fn);
        }

        Directory.CreateDirectory(outDir);
        int formatVersion = root.GetProperty("formatVersion").GetInt32();
        string qtVersion = root.GetProperty("qtVersion").GetString();
        foreach (var module in modules)
        {
            string path = Path.Combine(outDir, $"qtsharp.{module.Name}.json");
            using var stream = File.Create(path);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", formatVersion);
            writer.WriteString("qtVersion", qtVersion);
            writer.WriteString("namespace", module.Namespace);
            WriteArray(writer, "enums", enumsByModule[module.Name]);
            WriteArray(writer, "functions", functionsByModule[module.Name]);
            WriteArray(writer, "classes", classesByModule[module.Name]);
            writer.WriteEndObject();
        }

        foreach (var module in modules)
        {
            Console.WriteLine($"[split-core] {module.Name}: {classesByModule[module.Name].Count} class(es), " +
                              $"{enumsByModule[module.Name].Count} enum(s), " +
                              $"{functionsByModule[module.Name].Count} function(s) -> qtsharp.{module.Name}.json");
        }
        return 0;
    }

    // A model's module field is a registry name (qtcore); the splitter's table is keyed by the
    // Qt module directory (QtCore).
    static string BaseQtModule(string moduleName)
        => ExtensionModules.FirstOrDefault(m => m.Name == moduleName)?.QtModules.FirstOrDefault();

    // Whether module's dependency closure contains dependency (Qt's module graph).
    static bool DependsOn(ExtensionModule module, ExtensionModule dependency)
        => ModuleClosure(module).Any(m => ReferenceEquals(m, dependency));

    // Every class name a declaration names: its bases, and the types of its methods and signals.
    // The walk is over the JSON, so a new field that carries a type is covered as soon as it uses
    // one of the type-bearing property names.
    static IEnumerable<string> ReferencedClassNames(JsonElement declaration)
    {
        var names = new List<string>();
        void Walk(JsonElement element, string property)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var member in element.EnumerateObject())
                        Walk(member.Value, member.Name);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Walk(item, property);
                    break;
                case JsonValueKind.String when property is "type" or "returnType" or "bases":
                    names.Add(element.GetString().TrimEnd('*', '&'));
                    break;
            }
        }
        Walk(declaration, "");
        return names;
    }

    static void WriteArray(Utf8JsonWriter writer, string name, List<JsonElement> items)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var item in items)
            item.WriteTo(writer);
        writer.WriteEndArray();
    }
}
