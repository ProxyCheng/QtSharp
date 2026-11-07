using System;
using System.IO;

namespace QtSharp.Generator
{
    /// <summary>
    /// Opens the module closure and builds the class/enum vocabulary and the type system that
    /// reads it. Everything downstream resolves names through this object.
    /// </summary>
    public sealed class BindingModelPass : IPass
    {
        public string Name => "binding-model";

        public void Run(GeneratorContext context)
        {
            LoadDependencies(context);
            context.Binding = new BindingModel(context);
        }

        private static void LoadDependencies(GeneratorContext context)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(context.Options.ModelPath));
            foreach (string dependency in ModuleCatalog.Closure(context.ModuleName))
            {
                if (dependency == context.ModuleName)
                    continue;
                string fileName = $"qtsharp.{dependency}.json";
                string path = Path.Combine(directory, fileName);
                if (!File.Exists(path))
                {
                    string configured = Environment.GetEnvironmentVariable("QTSHARP_MODEL_DIR");
                    if (!string.IsNullOrEmpty(configured))
                        path = Path.Combine(configured, fileName);
                }
                if (!File.Exists(path))
                    path = Path.Combine(BindingModel.RepoRoot, "model", fileName);
                if (!File.Exists(path))
                    throw new FileNotFoundException(
                        $"'{dependency}' is a dependency of this module, but {fileName} was not found " +
                        $"beside '{context.Options.ModelPath}'.");
                context.AddDependency(ModelDocument.Load(path, dependency));
            }
        }
    }
}
