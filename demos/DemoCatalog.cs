using System;
using System.Collections.Generic;
using System.Linq;

namespace QtSharp.Demos
{
    /// <summary>
    /// The dispatcher for the ported official Qt examples in the base binding. The demos are grouped
    /// by the module they exercise and named after it: <c>qtgui-*</c> for the painting examples,
    /// <c>qtwidgets-*</c> for the widget examples. The addons have their own catalogs
    /// (<c>ChartsDemos</c>, Qt3D) because they build against separate assemblies.
    ///
    /// The list lives in exactly one place: <c>testapp --list</c> reads it, so the gate and the
    /// table cannot drift. Binding-specific tests (ownership, exceptions, C#-declared signals, ...)
    /// live in the separate QtSharp.BindingTests assembly, not here.
    /// </summary>
    public static class DemoCatalog
    {
        // Discovered from [Demo("name")] on the classes in this assembly; there is no second list
        // to maintain. DemoDiscovery sorts by name, so Usage and --list are deterministic.
        static readonly IReadOnlyList<(string Name, Func<QApplication, int> Run)> Demos =
            DemoDiscovery.Find(typeof(DemoCatalog).Assembly);

        static readonly Dictionary<string, Func<QApplication, int>> Runners =
            Demos.ToDictionary(d => d.Name, d => d.Run, StringComparer.Ordinal);

        /// <summary>The demo names, in the order Usage lists them.</summary>
        public static readonly string[] Names = Demos.Select(d => d.Name).ToArray();

        /// <summary>The text the test app prints when it is given nothing to run.</summary>
        public static readonly string Usage = BuildUsage();

        static string BuildUsage()
        {
            var text = new System.Text.StringBuilder("[usage] ");
            for (int i = 0; i < Names.Length; i++)
            {
                if (i > 0)
                    text.Append(i % 4 == 0 ? " |\n        " : " | ");
                text.Append(Names[i]);
            }
            return text.ToString();
        }

        /// <summary>
        /// Whether an off-screen render covers the widget's logical area. QWidget::grab() hands back
        /// a pixmap in *device* pixels, so a widget that is 200 logical pixels across comes back
        /// 200 * devicePixelRatio() pixels wide on a scaled display; comparing against the logical
        /// number only holds on a 100% display. A fractional ratio also rounds the product (297
        /// logical at 1.5 is 445.5 device), hence the one-pixel allowance.
        /// </summary>
        public static bool GrabCovers(QPixmap pixmap, int logicalWidth, int logicalHeight)
            => pixmap != null && !pixmap.IsNull()
               && Math.Abs(pixmap.Width() - logicalWidth * pixmap.DevicePixelRatio()) < 1.0
               && Math.Abs(pixmap.Height() - logicalHeight * pixmap.DevicePixelRatio()) < 1.0;

        /// <summary>
        /// Runs the demo called <paramref name="args"/>[0] (case-insensitively) and reports its exit
        /// code. Returns false when there is nothing to run, in which case the caller tries the
        /// binding tests and finally prints the usage.
        ///
        /// The application object is built here, once, from the command line the program was started
        /// with - the <c>app = QApplication(sys.argv)</c> line of a PyQt script - so a demo only has
        /// to build its widgets and, if it wants a run loop, end with <c>return app.Exec();</c>.
        /// </summary>
        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 1;
            if (args == null || args.Length == 0)
                return false;
            if (!Runners.TryGetValue(args[0].ToLowerInvariant(), out var run))
                return false;

            var app = new QApplication(args);
            exitCode = run(app);
            return true;
        }

        /// <summary>The demo called <paramref name="name"/>, with an application object already made.</summary>
        public static bool TryRun(string name, QApplication app, out int exitCode)
        {
            exitCode = 1;
            if (string.IsNullOrEmpty(name) || !Runners.TryGetValue(name.ToLowerInvariant(), out var run))
                return false;

            exitCode = run(app);
            return true;
        }
    }
}
