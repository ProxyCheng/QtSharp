using System;
using System.Collections.Generic;

namespace QtSharp.Generator
{
    /// <summary>
    /// Runs passes in registration order and records the order in one place. A pass may add
    /// context state a later pass consumes; emitters must not run until the pipeline finishes.
    /// </summary>
    public sealed class PassPipeline
    {
        private readonly List<IPass> passes = new();

        public PassPipeline Add(IPass pass)
        {
            passes.Add(pass ?? throw new ArgumentNullException(nameof(pass)));
            return this;
        }

        public void Run(GeneratorContext context)
        {
            foreach (var pass in passes)
                pass.Run(context);
        }
    }
}
