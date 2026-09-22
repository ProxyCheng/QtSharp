using System;
using System.Collections.Generic;

namespace QtSharp.Api
{
    /// <summary>
    /// The dispatcher for the ported official examples: every window this repository has rewritten
    /// on the binding, under the name it is run by.
    ///
    /// Both entry points use it, so the list lives in exactly one place:
    ///     testbind  - testapp api-analogclock        (the test app; no argument prints Usage)
    ///     samples/hello - hello api-analogclock      (no argument shows the hello window instead)
    ///
    /// WindowFlagsApi.cs exists but is deliberately not wired in: it dies about three runs in ten
    /// while Qt tears the process down (see its header). Its 115 assertions all pass.
    /// </summary>
    public static class ApiDemos
    {
        static readonly Dictionary<string, Func<QApplication, int>> Runners = new(StringComparer.Ordinal)
        {
            ["api"] = ApiSmokeTest.Run,
            ["api-analogclock"] = ApiAnalogClockDemo.Run,
            ["api-lineedits"] = ApiLineEditsDemo.Run,
            ["api-widgets"] = ApiWidgetsDemo.Run,
            ["api-graphics"] = ApiGraphicsDemo.Run,
            ["api-core"] = ApiCoreDemo.Run,
            ["api-events"] = ApiEventsDemo.Run,
            ["api-custommodel"] = ApiCustomModelDemo.Run,
            ["api-ownership"] = ApiOwnershipDemo.Run,
            ["api-metaobject"] = ApiMetaObjectDemo.Run,
            ["api-calculator"] = ApiCalculatorDemo.Run,
            ["api-spinboxes"] = ApiSpinBoxesDemo.Run,
            ["api-shortcut"] = ApiShortcutDemo.Run,
            ["api-shapedclock"] = ShapedClockApi.Run,
            ["api-sliders"] = ApiSlidersDemo.Run,
            ["api-calendarwidget"] = CalendarWidgetApi.Run,
            ["api-menus"] = MenusApi.Run,
            ["api-basiclayouts"] = BasicLayoutsApi.Run,
            ["api-scribble"] = ScribbleApi.Run,
            ["api-gallery"] = WidgetsGalleryApi.Run,
            ["api-systray"] = SystemTrayApi.Run,
            ["api-docviewer"] = DocumentViewerApi.Run,
            ["api-basicdrawing"] = ApiBasicDrawingDemo.Run,
            ["api-gradients"] = ApiGradientsDemo.Run,
            ["api-painterpaths"] = ApiPainterPathsDemo.Run,
            ["api-completer"] = ApiCompleterDemo.Run,
            ["api-customsortfilter"] = ApiCustomSortFilterDemo.Run,
            ["api-stardelegate"] = ApiStarDelegateDemo.Run,
            ["api-composition"] = ApiCompositionDemo.Run,
            ["api-transform"] = ApiTransformationsDemo.Run,
            ["api-pathstroke"] = ApiPathStrokeDemo.Run,
        };

        /// <summary>The demo names, in the order Usage lists them.</summary>
        public static readonly string[] Names = new[]
        {
            "api", "api-analogclock", "api-lineedits", "api-widgets", "api-graphics",
            "api-core", "api-events", "api-ownership", "api-custommodel", "api-calculator",
            "api-spinboxes", "api-shortcut", "api-metaobject", "api-shapedclock", "api-sliders",
            "api-calendarwidget", "api-menus", "api-basiclayouts", "api-scribble", "api-gallery",
            "api-systray", "api-docviewer", "api-basicdrawing", "api-gradients",
            "api-painterpaths", "api-completer", "api-customsortfilter",
            "api-stardelegate", "api-composition", "api-transform", "api-pathstroke",
        };

        /// <summary>The text both entry points print when they are given nothing to run.</summary>
        public static readonly string Usage = BuildUsage();

        static string BuildUsage()
        {
            var text = new System.Text.StringBuilder("[usage] ");
            for (int i = 0; i < Names.Length; i++)
            {
                if (i > 0)
                    text.Append(i % 5 == 0 ? " |\n        " : " | ");
                text.Append(Names[i]);
            }
            return text.ToString();
        }

    /// <summary>
    /// Runs the demo called <paramref name="args"/>[0] (case-insensitively) and reports its exit
    /// code. Returns false when there is nothing to run, in which case the caller decides what to
    /// do instead (the test app prints the usage, the sample shows the hello window).
    ///
    /// The application object is built here, once, from the command line the program was started
    /// with - the `app = QApplication(sys.argv)` line of a PyQt script - so a demo only has to
    /// build its widgets and, if it wants a run loop, end with `return app.Exec();`.
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
