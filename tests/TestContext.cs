using System;
using QtSharp.QtWidgets;

namespace QtSharp.Tests
{
    /// <summary>
    /// What a binding test gets: the one application object, the assertion counter and the exit
    /// code. Tests are automated only - there is no View product - so this surface is the same in
    /// every configuration.
    /// </summary>
    public sealed class TestContext
    {
        int _failures;

        public TestContext(QApplication app)
        {
            App = app;
        }

        /// <summary>The process's single QApplication, for tests that drive sockets or timers.</summary>
        public QApplication App { get; }

        /// <summary>The process exit code: zero when every check passed, one otherwise.</summary>
        public int ExitCode => _failures == 0 ? 0 : 1;

        /// <summary>Prints one <c>[PASS]</c>/<c>[FAIL]</c> line and counts a failure.</summary>
        public void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                _failures++;
        }
    }
}
