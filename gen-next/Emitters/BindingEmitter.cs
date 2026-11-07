using System;
using System.Collections.Generic;
using System.Text.Json;

namespace QtSharp.Generator
{
    /// <summary>
    /// Base for the declaration emitters. It forwards the model vocabulary and the type-system
    /// operations under their natural names, so an emitter reads like the generated shape it
    /// describes while the decisions stay in <see cref="BindingModel"/> and the type handlers.
    /// </summary>
    public abstract partial class BindingEmitter
    {
        protected BindingEmitter(GeneratorContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        protected GeneratorContext Context { get; }
        protected BindingModel B => Context.Binding;
    }
}
