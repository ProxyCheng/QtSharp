using System;
using QtSharp.QtWidgets;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            // The new pipeline creates its own QApplication through its runtime, so its
            // commands have to run before the legacy one below is constructed.
            if (args.Length > 0 && args[0] == "api-analogclock")
            {
                return ApiAnalogClockDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-lineedits")
            {
                return ApiLineEditsDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-widgets")
            {
                return ApiWidgetsDemo.Run();
            }

            using var app = new QApplication(new[] { "qtsharp-examples" });

            if (args.Length > 0 && args[0] == "metasig")
            {
                return MetaSignalTest.Run();
            }

            if (args.Length > 0 && args[0] == "vdispatch")
            {
                return VirtualDispatchTest.Run();
            }

            if (args.Length > 0 && args[0] == "own")
            {
                return OwnershipTest.Run();
            }

            if (args.Length > 0 && args[0] == "api")
            {
                return ApiSmokeTest.Run();
            }

            string which = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
            QWidget window = which switch
            {
                "sliders" => new SlidersWindow(),
                "spinboxes" => new SpinBoxesWindow(),
                "windowflags" => new WindowFlagsWindow(),
                "lineedits" => new LineEditsWindow(),
                "shapedclock" => new ShapedClock(),
                "calendar" => new CalendarWindow(),
                "systray" => new SystemTrayWindow(),
                "scribble" => new ScribbleWindow(),
                "shortcut" => new ShortcutWindow(),
                "analogclock" => new AnalogClock(),
                "calculator" => new Calculator(),
                "gallery" => new WidgetGalleryWindow(),
                "docviewer" => new DocumentViewerWindow(),
                "menus" => new MenusWindow(),
                "basiclayouts" => new BasicLayoutsDialog(),
                _ => null,
            };

            if (window == null)
            {
                var demos = AllDemos.CreateAll();
                AllDemos.ShowTiled(demos);
                Console.WriteLine($"[OK] {demos.Count} demos shown side by side; close them to exit...");
                return QApplication.Exec();
            }

            window.Show();
            Console.WriteLine($"[OK] {which} shown; close it to exit...");
            return QApplication.Exec();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }
}
