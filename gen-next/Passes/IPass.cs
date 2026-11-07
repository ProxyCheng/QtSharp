namespace QtSharp.Generator
{
    /// <summary>
    /// A model transformation. Passes run in a deterministic order between loading and emission;
    /// they are the place for naming, inheritance, ownership and type-shape decisions, so the
    /// emitters only read a finished model.
    /// </summary>
    public interface IPass
    {
        string Name { get; }
        void Run(GeneratorContext context);
    }
}
