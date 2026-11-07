using System;
using System.Collections.Generic;

namespace QtSharp.Generator
{
    /// <summary>
    /// The per-run state. Passes transform the model through the context and emitters read it;
    /// there are no per-run static fields below the driver.
    /// </summary>
    public sealed class GeneratorContext : IDisposable
    {
        public GeneratorContext(GeneratorOptions options, ModelDocument model)
        {
            Options = options;
            Model = model;
            Output = new OutputFileSet();
            dependencies = new List<ModelDocument>();
        }

        private readonly List<ModelDocument> dependencies;

        public GeneratorOptions Options { get; }
        public ModelDocument Model { get; }
        public IReadOnlyList<ModelDocument> Dependencies => dependencies;
        public OutputFileSet Output { get; }

        /// <summary>Filled by <see cref="BindingModelPass"/>: the class/enum vocabulary and type system.</summary>
        public BindingModel Binding { get; internal set; }

        public string ModuleName => Options.Module ?? Model.Module;

        /// <summary>Opens a model whose types this module may derive from or refer to.</summary>
        internal void AddDependency(ModelDocument dependency) => dependencies.Add(dependency);

        public void Dispose()
        {
            foreach (var dependency in dependencies)
                dependency.Dispose();
            Model.Dispose();
        }
    }
}
