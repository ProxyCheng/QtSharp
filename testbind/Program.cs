using System;
using QtSharp.Demos;

// The test entry point: it does nothing but hand the argument to the demo library.
//
// Every window this repository has ported lives in QtSharp.Demos (see demos/), and ApiDemos is the
// one place that maps a name to it - so this file is a dispatcher and nothing else. samples/hello
// uses the very same ApiDemos.TryRun, which is what keeps the two entry points from drifting.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            // --list prints the demo names one per line, which is what the repository's gate runs
            // through: the list lives in the library, so it cannot drift from the dispatcher.
            if (args.Length == 1 && args[0] == "--list")
            {
                foreach (string name in ApiDemos.Names)
                    Console.WriteLine(name);
                return 0;
            }

            if (ApiDemos.TryRun(args, out int exitCode))
                return exitCode;

            Console.WriteLine(ApiDemos.Usage);
            Console.WriteLine($"        ({ApiDemos.Names.Length} demos, all in QtSharp.Demos.dll)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }
}
