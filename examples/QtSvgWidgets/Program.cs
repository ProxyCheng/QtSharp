using System;

// The one entry point of the QtSvgWidgets examples: ExampleHost owns the command line, the single QApplication
// and dispatch, and the names come from this assembly's [Example] attributes.
internal static class Program
{
    [STAThread]
    static int Main(string[] args) => ExampleHost.Run(typeof(Program).Assembly, args);
}
