using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace QtSharp.Generator
{
    /// <summary>
    /// The files one run will write. Emitters only create writers; the driver decides when the
    /// set is complete and writes it, so a failed run never leaves half a binding on disk.
    /// </summary>
    public sealed class OutputFileSet
    {
        private readonly Dictionary<string, StringBuilder> files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StringBuilder> rootFiles = new(StringComparer.Ordinal);

        public CodeWriter File(string relativePath)
        {
            if (!files.TryGetValue(relativePath, out var text))
            {
                text = new StringBuilder();
                files.Add(relativePath, text);
            }
            return new CodeWriter(text);
        }

        /// <summary>Adds a complete file whose text the caller has already assembled.</summary>
        public void Add(string relativePath, string text)
        {
            var builder = new StringBuilder(text);
            files[relativePath] = builder;
        }

        /// <summary>Adds a file that lives beside <c>generated/</c>, such as the native glue.</summary>
        public void AddRoot(string relativePath, string text)
            => rootFiles[relativePath] = new StringBuilder(text);

        public bool IsEmpty => files.Count == 0 && rootFiles.Count == 0;

        public void Save(string root, string generatedDirectory = "generated")
        {
            string target = Path.Combine(root, generatedDirectory);
            if (files.Count > 0)
                Directory.CreateDirectory(target);

            // A type that moved to another header must not leave a stale file behind, and an
            // unchanged file keeps its timestamp so a regen does not force a rebuild.
            var expected = new HashSet<string>(
                files.Keys.Select(key => Path.GetFullPath(Path.Combine(target, key))),
                StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(target))
                foreach (string existing in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
                    if (!expected.Contains(Path.GetFullPath(existing)))
                        System.IO.File.Delete(existing);

            foreach (var file in files)
                WriteIfChanged(Path.Combine(target, file.Key), file.Value.ToString());
            foreach (var file in rootFiles)
                WriteIfChanged(Path.Combine(root, file.Key), file.Value.ToString());
        }

        private static void WriteIfChanged(string path, string text)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            if (System.IO.File.Exists(path) && System.IO.File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, bytes);
        }
    }
}
