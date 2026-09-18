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

            if (args.Length > 0 && args[0] == "api-scribble")
            {
                return ScribbleApi.Run();
            }

            if (args.Length > 0 && args[0] == "api-gallery")
            {
                return WidgetsGalleryApi.Run();
            }

            if (args.Length > 0 && args[0] == "api-systray")
            {
                return SystemTrayApi.Run();
            }

            if (args.Length > 0 && args[0] == "api-docviewer")
            {
                return DocumentViewerApi.Run();
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

            string which = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            QWidget window = which switch
            {
                // One legacy demo is left: its new-pipeline port exists (WindowFlagsApi.cs) but is
                // parked, because it dies about 3 runs in 10 while Qt tears the process down (see
                // its header). Every other Qt example is now the new pipeline's `api-*`, and the
                // tiled "all" mode went with them.
                "windowflags" => new WindowFlagsWindow(),
                _ => null,
            };

            if (window == null)
            {
                Console.WriteLine("[usage] new pipeline: api | api-analogclock | api-lineedits | api-widgets |");
                Console.WriteLine("                    api-graphics | api-core | api-events | api-ownership |");
                Console.WriteLine("                    api-custommodel | api-calculator | api-spinboxes | api-shortcut |");
                Console.WriteLine("                    api-metaobject | api-shapedclock | api-sliders | api-calendarwidget |");
                Console.WriteLine("                    api-menus | api-basiclayouts | api-scribble | api-gallery |");
                Console.WriteLine("                    api-systray | api-docviewer");
                Console.WriteLine("        legacy PoCs : metasig | vdispatch | own");
                Console.WriteLine("        legacy demo : windowflags");
                return 0;
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


