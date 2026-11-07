namespace QtSharp.Generator
{
    /// <summary>One declaration kind inside the C# output; the driver decides the order.</summary>
    public abstract class CSharpSubEmitter
    {
        protected CSharpSubEmitter(GeneratorContext context)
        {
            Context = context;
        }

        protected GeneratorContext Context { get; }

        public abstract void Emit(CSharpFileSet files);
    }
}
