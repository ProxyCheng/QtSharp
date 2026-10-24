using System;
using System.Linq;
using QtSharp.Charts.Demos;

// The conformance runner for the ported Qt Charts examples:
//
//     ChartsTests                 the usage table
//     ChartsTests splinechart     run one
//
// The names come from DemoCatalog, so adding an example to that table is all it takes.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
        {
            Console.Write(DemoCatalog.Usage);
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] == "--list")
        {
            foreach (var name in DemoCatalog.Names)
                Console.WriteLine(name);
            return 0;
        }

        var app = new QApplication(args);
        if (DemoCatalog.TryRun(args[0], app, out int exitCode))
            return exitCode;

        Console.Error.WriteLine($"'{args[0]}' is not one of the ported examples.");
        Console.Write(DemoCatalog.Usage);
        return 1;
    }
}
