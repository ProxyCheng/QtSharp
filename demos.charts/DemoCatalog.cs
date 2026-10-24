using System;
using System.Collections.Generic;
using System.Linq;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// The ported Qt examples. Each class carries [Demo("qtcharts-...")]; this catalog discovers
    /// them, so the list comes from the library and a new example is covered as soon as it is
    /// registered.
    ///
    /// The names follow the Qt examples they came from. In Qt 6.11 most of the stand-alone widgets
    /// examples were folded into examples/charts/chartsgallery, one widget per chart type; those are
    /// the names without a "chart" suffix. zoomlinechart is still its own example upstream.
    /// </summary>
    public static class DemoCatalog
    {
        // Discovered from [Demo("qtcharts-...")] on the classes in this assembly.
        static readonly IReadOnlyList<(string Name, Func<QApplication, int> Run)> Runners =
            DemoDiscovery.Find(typeof(DemoCatalog).Assembly);

        public static IReadOnlyList<string> Names => Runners.Select(r => r.Name).ToList();

        public static string Usage
        {
            get
            {
                var text = new System.Text.StringBuilder();
                text.AppendLine("Qt Charts examples ported from Qt's own examples/charts:");
                foreach (var runner in Runners)
                    text.AppendLine("    " + runner.Name);
                return text.ToString();
            }
        }

        /// <summary>Runs one of them, or reports that the name is unknown.</summary>
        public static bool TryRun(string name, QApplication app, out int exitCode)
        {
            foreach (var runner in Runners)
            {
                if (runner.Name != name)
                    continue;
                exitCode = runner.Run(app);
                return true;
            }
            exitCode = 0;
            return false;
        }
    }
}
