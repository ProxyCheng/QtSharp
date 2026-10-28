using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using QtSharp.QtWidgets;

namespace QtSharp.Tests
{
    /// <summary>
    /// The test app's dispatcher: it owns the command line, the single QApplication and dispatch.
    /// The test executable only ever runs the automated build - a test has no View product and
    /// its <c>--list</c> contains only <c>test-*</c> names.
    /// </summary>
    public static class TestHost
    {
        public static int Run(string[] args)
        {
            var tests = TestDiscovery.Find(typeof(TestHost).Assembly);

            if (args == null || args.Length == 0)
            {
                Console.WriteLine(BuildUsage(tests));
                return 0;
            }

            if (args.Length == 1 && args[0] == "--list")
            {
                foreach (var test in tests)
                    Console.WriteLine(test.Name);
                return 0;
            }

            var runner = tests.FirstOrDefault(
                t => string.Equals(t.Name, args[0], StringComparison.OrdinalIgnoreCase));
            if (runner.Run == null)
            {
                Console.Error.WriteLine($"'{args[0]}' is not one of the binding tests.");
                Console.WriteLine(BuildUsage(tests));
                return 1;
            }

            // The QPA plugin is chosen when the QApplication is constructed. Tests run offscreen:
            // nothing they do needs a desktop window, and a test must not map one.
            UseOffscreenPlatform();
            var app = new QApplication(args);
            return runner.Run(new TestContext(app));
        }

        /// <summary>
        /// Chooses the QPA platform before the QApplication is constructed. QTSHARP_EXAMPLE_PLATFORM is
        /// the explicit override, a QT_QPA_PLATFORM the caller set is honoured, and otherwise the
        /// tests run offscreen. The one line it prints is not an assertion.
        /// </summary>
        static void UseOffscreenPlatform()
        {
            string platform = Environment.GetEnvironmentVariable("QTSHARP_EXAMPLE_PLATFORM");
            string source = "QTSHARP_EXAMPLE_PLATFORM";
            if (string.IsNullOrEmpty(platform))
            {
                platform = Environment.GetEnvironmentVariable("QT_QPA_PLATFORM");
                source = "QT_QPA_PLATFORM";
            }
            if (string.IsNullOrEmpty(platform))
            {
                platform = "offscreen";
                source = "default";
            }
            SetEnvironment("QT_QPA_PLATFORM", platform);
            // The offscreen plugin has no font database of its own on Windows; without the system
            // font directory QFontMetrics reports synthetic rectangles and layout collapses.
            if (platform == "offscreen" && OperatingSystem.IsWindows()
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("QT_QPA_FONTDIR")))
                SetEnvironment("QT_QPA_FONTDIR",
                    Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
            Console.WriteLine(source == "default"
                ? "[platform] offscreen (set QTSHARP_EXAMPLE_PLATFORM=windows to override)"
                : $"[platform] {platform} (from {source})");
        }

        // Qt reads its platform environment through the C runtime, whose copy is not updated by
        // Environment.SetEnvironmentVariable. _wputenv_s updates both, so the plugin Qt picks and
        // the font directory it finds are the ones chosen here. On Unix setenv is the same table.
        [DllImport("ucrtbase", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int _wputenv_s(string name, string value);

        static void SetEnvironment(string name, string value)
        {
            if (OperatingSystem.IsWindows())
                _wputenv_s(name, value);
            else
                Environment.SetEnvironmentVariable(name, value);
        }

        static string BuildUsage(IReadOnlyList<(string Name, Func<TestContext, int> Run)> tests)
        {
            var text = new System.Text.StringBuilder("[tests] ");
            for (int i = 0; i < tests.Count; i++)
            {
                if (i > 0)
                    text.Append(i % 5 == 0 ? " |\n        " : " | ");
                text.Append(tests[i].Name);
            }
            return text.ToString();
        }
    }
}
