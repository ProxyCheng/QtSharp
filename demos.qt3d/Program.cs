using System;
using System.Collections.Generic;
using QtSharp.Api;

namespace QtSharp.Qt3D.Demos
{
    // The dispatcher for the ported official Qt3D examples. `--list` is what
    // tools/run-window-tests.ps1 consumes; a demo name runs exactly that example.
    public static class Program
    {
        static readonly Dictionary<string, Func<QApplication, int>> Runners = new(StringComparer.Ordinal)
        {
            ["qt3d-simple"] = SimpleDemo.Run,
            ["qt3d-basicshapes"] = BasicShapesDemo.Run,
            ["qt3d-text"] = TextDemo.Run,
        };

        public static readonly string[] Names = new[]
        {
            "qt3d-simple", "qt3d-basicshapes", "qt3d-text",
        };

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--list")
            {
                foreach (string name in Names)
                    Console.WriteLine(name);
                return 0;
            }

            if (args.Length == 0 || !Runners.TryGetValue(args[0], out var run))
            {
                Console.WriteLine("[usage] " + string.Join(" | ", Names));
                return 0;
            }

            try
            {
                var app = new QApplication(args);
                return run(app);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] " + ex);
                return 1;
            }
        }
    }

    internal static class DemoWindow
    {
        // The CI gate builds and exercises the scene without a render loop - the object graph is
        // what the assertions cover, and a GPU is not guaranteed on a runner. QT3D_SHOW_DEMOS=1
        // displays each window for 1.5 s, which is how a failure is inspected locally.
        public static void ShowIfRequested(QWindow view, QApplication app)
        {
            if (Environment.GetEnvironmentVariable("QT3D_SHOW_DEMOS") != "1")
                return;
            view.Show();
            var timer = new QTimer();
            timer.SetSingleShot(true);
            timer.Timeout += () => app.Quit();
            timer.Start(1500);
            app.Exec();
        }
    }

    // The assertion shape the window runner counts: one [PASS] per successful check.
    internal static class Assert
    {
        public static int Failures;

        public static void Reset() => Failures = 0;

        public static void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                Failures++;
        }

        public static bool Near(float actual, float expected, float tolerance = 1e-4f)
            => Math.Abs(actual - expected) <= tolerance;

        public static bool Near(QVector3D actual, QVector3D expected, float tolerance = 1e-4f)
            => Near(actual.X(), expected.X(), tolerance)
               && Near(actual.Y(), expected.Y(), tolerance)
               && Near(actual.Z(), expected.Z(), tolerance);

        public static bool Near(QQuaternion actual, QQuaternion expected, float tolerance = 1e-4f)
            => Near(actual.Scalar(), expected.Scalar(), tolerance)
               && Near(actual.X(), expected.X(), tolerance)
               && Near(actual.Y(), expected.Y(), tolerance)
               && Near(actual.Z(), expected.Z(), tolerance);

        public static int Finish(string label)
        {
            Console.WriteLine(Failures == 0 ? $"[{label} OK]" : $"[{label} FAILED: {Failures}]");
            return Failures == 0 ? 0 : 1;
        }
    }
}
