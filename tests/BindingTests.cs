using System;
using System.Collections.Generic;
using System.Linq;

namespace QtSharp.Tests
{
    /// <summary>
    /// The binding-specific tests: behaviour that Qt's own examples do not cover because it belongs
    /// to the *binding* (ownership rules, the native exception boundary, C#-declared signals and
    /// properties, value types) or to modules whose official examples are not ported yet (the
    /// QtNetwork transport, the document modules). They are kept out of the official demo catalog
    /// and run by the same test app, under <c>test-*</c> names.
    /// </summary>
    public static class BindingTests
    {
        static readonly IReadOnlyList<(string Name, Func<QApplication, int> Run)> Tests =
            DemoDiscovery.Find(typeof(BindingTests).Assembly);

        static readonly Dictionary<string, Func<QApplication, int>> Runners =
            Tests.ToDictionary(t => t.Name, t => t.Run, StringComparer.Ordinal);

        /// <summary>The test names, in the order Usage lists them.</summary>
        public static readonly string[] Names = Tests.Select(t => t.Name).ToArray();

        public static readonly string Usage = BuildUsage();

        static string BuildUsage()
        {
            var text = new System.Text.StringBuilder("[tests] ");
            for (int i = 0; i < Names.Length; i++)
            {
                if (i > 0)
                    text.Append(i % 5 == 0 ? " |\n        " : " | ");
                text.Append(Names[i]);
            }
            return text.ToString();
        }

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
    }
}
