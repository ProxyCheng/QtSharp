namespace QtSharp.Generator
{
    /// <summary>Everything the driver needs to know before it loads a model.</summary>
    public sealed class GeneratorOptions
    {
        public GeneratorOptions(string modelPath, string outputDirectory, string module, bool nativeOnly)
        {
            ModelPath = modelPath;
            OutputDirectory = outputDirectory;
            Module = module;
            NativeOnly = nativeOnly;
        }

        public string ModelPath { get; }
        public string OutputDirectory { get; }
        public string Module { get; }
        public bool NativeOnly { get; }
    }
}
