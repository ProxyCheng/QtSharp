using System;

namespace QtSharp.Generator
{
    /// <summary>
    /// The one place that knows the order of a run: load the model, run the passes, run the
    /// emitters, then commit the output. The old generator mixed these three stages; keeping the
    /// driver small is what makes the rest of the pipeline testable in isolation.
    /// </summary>
    public static class GeneratorDriver
    {
        public static void Run(GeneratorOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            using var context = new GeneratorContext(
                options, ModelDocument.Load(options.ModelPath, options.Module));

            new PassPipeline()
                .Add(new BindingModelPass())
                .Run(context);

            if (options.NativeOnly)
            {
                // The combined model only produces the shared native glue; a C# tree left from an
                // earlier run would be a second, uncompiled copy of the binding.
                string generated = System.IO.Path.Combine(options.OutputDirectory, "generated");
                if (System.IO.Directory.Exists(generated))
                    System.IO.Directory.Delete(generated, true);
            }
            else
            {
                new TypeTableEmitter(context).Emit();
                new CSharpEmitter(context).Emit();
            }
            new NativeEmitter(context).Emit();

            if (!context.Output.IsEmpty)
                context.Output.Save(options.OutputDirectory);
        }
    }
}
