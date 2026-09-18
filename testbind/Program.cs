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

            if (args.Length > 0 && args[0] == "api-graphics")
            {
                return ApiGraphicsDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-core")
            {
                return ApiCoreDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-events")
            {
                return ApiEventsDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-calculator")
            {
                return ApiCalculatorDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-spinboxes")
            {
                return ApiSpinBoxesDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-shortcut")
            {
                return ApiShortcutDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-shapedclock")
            {
                return ShapedClockApi.Run();
            }

            if (args.Length > 0 && args[0] == "api-sliders")
            {
                return ApiSlidersDemo.Run();
            }

            if (args.Length > 0 && args[0] == "api-calendarwidget")
            {
                return CalendarWidgetApi.Run();
            }

            if (args.Length > 0 && args[0] == "api-menus")
            {
                return MenusApi.Run();
            }

            if (args.Length > 0 && args[0] == "api-basiclayouts")
            {
                return BasicLayoutsApi.Run();
            }


            if (args.Length > 0 && args[0] == "api-custommodel") { return ApiCustomModelDemo.Run(); }
            if (args.Length > 0 && args[0] == "api-metaobject") { return ApiMetaObjectDemo.Run(); }
            if (args.Length > 0 && args[0] == "api-ownership")
            {
                return ApiOwnershipDemo.Run();
            }

            // These two build their own QApplication through ApiRuntime as well: they used to sit
            // below, next to the PoCs, and that made the process hold *two* applications - the
            // legacy one here and the managed one - of which the legacy `using` disposed first.
            if (args.Length > 0 && args[0] == "api")
            {
                return ApiSmokeTest.Run();
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

            string which = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
            QWidget window = which switch
            {
                // The legacy demos the new pipeline has fully replaced are gone: lineedits,
                // calculator, spinboxes and shortcut are `api-lineedits` / `api-calculator` /
                // `api-spinboxes` / `api-shortcut`, and calendarwidget, menus and basiclayouts
                // are `api-calendarwidget` / `api-menus` / `api-basiclayouts`.
                "windowflags" => new WindowFlagsWindow(),
                "systray" => new SystemTrayWindow(),
                "scribble" => new ScribbleWindow(),
                "analogclock" => new AnalogClock(),
                "gallery" => new WidgetGalleryWindow(),
                "docviewer" => new DocumentViewerWindow(),
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


