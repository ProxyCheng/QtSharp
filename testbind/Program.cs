using System;
using QtSharp.Api;

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
