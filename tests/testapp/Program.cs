using System;
using QtSharp.Tests;

// The test entry point: the binding tests own the command line, the single QApplication and the
// dispatch, so this file does nothing but hand the argument over.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            return TestHost.Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }
}
