using System;

namespace QtSharp.Generator
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 3 || args[0] != "codegen")
            {
                Console.Error.WriteLine("usage: qtgen-next codegen <model.json> <outDir> [--module <name>] [--native-only]");
                return 2;
            }

            string module = null;
            bool nativeOnly = false;
            for (int i = 3; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--module" when i + 1 < args.Length:
                        module = args[++i];
                        break;
                    case "--native-only":
                        nativeOnly = true;
                        break;
                    default:
                        Console.Error.WriteLine($"unknown argument: {args[i]}");
                        return 2;
                }
            }

            try
            {
                GeneratorDriver.Run(new GeneratorOptions(args[1], args[2], module, nativeOnly));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EXCEPTION] {ex}");
                return 1;
            }
        }
    }
}
