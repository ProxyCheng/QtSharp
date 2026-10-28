using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using QtSharp.QtCore;
using QtSharp.QtWidgets;

namespace QtSharp.Examples.Hosting
{
    /// <summary>
    /// The one entry point every module executable shares: it owns the command line, the single
    /// <see cref="QApplication"/>, dispatch and the exit code.
    ///
    /// <c>--list</c> prints the discovered names one per line in both builds (the gate consumes
    /// it), <c>&lt;name&gt;</c> runs one example. The View build additionally takes
    /// <c>--seconds N</c>, which closes the window after N seconds and replaces the old
    /// QTSHARP_DEMO_MS/QT3D_SHOW_DEMOS switches.
    /// </summary>
    public static class ExampleHost
    {
        public static int Run(Assembly assembly, string[] args)
        {
            var examples = ExampleDiscovery.Find(assembly);

            if (args == null || args.Length == 0)
            {
                Console.WriteLine(BuildUsage(examples));
                return 0;
            }

            if (args.Length == 1 && args[0] == "--list")
            {
                foreach (var example in examples)
                    Console.WriteLine(example.Name);
                return 0;
            }

            string name = null;
            int seconds = 0;
            int nameIndex = -1;
            var qtArgs = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--seconds")
                {
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out seconds) || seconds <= 0)
                    {
                        Console.Error.WriteLine("--seconds needs a positive number of seconds.");
                        return 1;
                    }
                    i++;
                }
                else if (name == null)
                {
                    name = args[i];
                    nameIndex = i;
                }
                else
                {
                    Console.Error.WriteLine($"Unexpected argument '{args[i]}'.");
                    Console.WriteLine(BuildUsage(examples));
                    return 1;
                }
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (i == nameIndex)
                    continue;
                if (args[i] == "--seconds")
                {
                    i++;
                    continue;
                }
                qtArgs.Add(args[i]);
            }

            var runner = examples.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            if (name == null || runner.Run == null)
            {
                Console.Error.WriteLine($"'{name}' is not one of the examples in this executable.");
                Console.WriteLine(BuildUsage(examples));
                return 1;
            }

            // One application per process, made here and only here. The example name and --seconds
            // are ours, so Qt is given the remaining switches only.
#if QTSHARP_EXAMPLE_SMOKE
            UseOffscreenPlatform();
#endif
            var app = new QApplication(qtArgs.ToArray());
            var context = new ExampleContext();
            int exitCode = runner.Run(context);

#if QTSHARP_EXAMPLE_VIEW
            // A headless example (the console ports) returns after printing; there is no window to
            // wait for and an event loop would never end.
            if (!context.HasWindows)
                return exitCode;

            QTimer stop = null;
            if (seconds > 0)
            {
                stop = new QTimer();
                stop.SetSingleShot(true);
                stop.TimeoutSignal.Connect(() => app.Quit());
                stop.Start(seconds * 1000);
            }

            exitCode = app.Exec();

            if (stop != null)
            {
                stop.Stop();
                stop.Dispose();
            }

            // The context owns the only strong references to the windows; keep it reachable until
            // the loop above has returned.
            GC.KeepAlive(context);
#endif
            return exitCode;
        }

        static string BuildUsage(IReadOnlyList<(string Name, Func<ExampleContext, int> Run)> examples)
        {
            var text = new System.Text.StringBuilder("[usage] ");
            for (int i = 0; i < examples.Count; i++)
            {
                if (i > 0)
                    text.Append(i % 4 == 0 ? " |\n        " : " | ");
                text.Append(examples[i].Name);
            }
            return text.ToString();
        }

#if QTSHARP_EXAMPLE_SMOKE
        /// <summary>
        /// Chooses the QPA platform before the QApplication is constructed - the plugin is picked
        /// there. The Smoke product defaults to <c>offscreen</c>, so an example can Show() a widget for
        /// layout and painting without mapping a window; <c>QTSHARP_EXAMPLE_PLATFORM</c> is the
        /// explicit override (a real display while debugging), and a QT_QPA_PLATFORM the caller set
        /// is honoured as well. The one line it prints is not an assertion.
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
#endif
    }
}
