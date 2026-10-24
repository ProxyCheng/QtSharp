using System;
using QtSharp.Demos;
using QtSharp.Tests;

// The test entry point: it does nothing but hand the argument to one of the two catalogs.
//
// The official Qt examples live in QtSharp.Demos (demos/, organized and named by module); the
// binding-specific tests live in QtSharp.BindingTests (tests/). Both catalogs expose --list, so
// the gate and the name tables cannot drift.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            // --list prints every runnable name one per line, which is what the repository's gate
            // runs through.
            if (args.Length == 1 && args[0] == "--list")
            {
                foreach (string name in DemoCatalog.Names)
                    Console.WriteLine(name);
                foreach (string name in BindingTests.Names)
                    Console.WriteLine(name);
                return 0;
            }

            if (DemoCatalog.TryRun(args, out int exitCode))
                return exitCode;
            if (BindingTests.TryRun(args, out exitCode))
                return exitCode;

            Console.WriteLine(DemoCatalog.Usage);
            Console.WriteLine(BindingTests.Usage);
            Console.WriteLine(
                $"        ({DemoCatalog.Names.Length} official demos in QtSharp.Demos.dll, " +
                $"{BindingTests.Names.Length} binding tests in QtSharp.BindingTests.dll)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }
}
