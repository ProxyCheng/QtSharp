using System;

namespace QtSharp.Generator
{
    /// <summary>
    /// One output concern. C# bindings, C++ glue, struct helpers and documentation each get an
    /// emitter instance; they read the completed context and never mutate the model.
    /// </summary>
    public abstract class Emitter
    {
        protected Emitter(GeneratorContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        protected GeneratorContext Context { get; }

        public abstract void Emit();
    }
}
