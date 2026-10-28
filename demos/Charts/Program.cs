using System;

// The one entry point of the Charts examples: DemoHost owns the command line, the single
// QApplication and dispatch, and the names come from this assembly's [Demo] attributes.
internal static class Program
{
    [STAThread]
    static int Main(string[] args) => DemoHost.Run(typeof(Program).Assembly, args);
}
