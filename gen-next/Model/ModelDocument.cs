using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace QtSharp.Generator
{
    /// <summary>
    /// The checked-in JSON model, opened once per run. Passes and emitters read it through this
    /// object; nothing below this layer keeps a static copy of the model.
    /// </summary>
    public sealed class ModelDocument : IDisposable
    {
        private readonly JsonDocument document;

        private ModelDocument(string path, string module, JsonDocument document)
        {
            Path = path;
            Module = module;
            this.document = document;

            var root = document.RootElement;
            Classes = ReadArray(root, "classes");
            Enums = ReadArray(root, "enums");
            Functions = ReadArray(root, "functions");
            Namespace = root.TryGetProperty("namespace", out var ns) ? ns.GetString() : null;
            QtVersion = root.TryGetProperty("qtVersion", out var qt) ? qt.GetString() : null;
        }

        public string Path { get; }
        public string Module { get; }
        public string Namespace { get; }
        public string QtVersion { get; }
        public IReadOnlyList<JsonElement> Classes { get; }
        public IReadOnlyList<JsonElement> Enums { get; }
        public IReadOnlyList<JsonElement> Functions { get; }

        public static ModelDocument Load(string path, string module)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("The model file was not found.", path);

            var json = JsonDocument.Parse(File.ReadAllText(path));
            return new ModelDocument(path, module, json);
        }

        private static IReadOnlyList<JsonElement> ReadArray(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
                return Array.Empty<JsonElement>();
            return array.EnumerateArray().ToList();
        }

        public void Dispose() => document.Dispose();
    }
}
