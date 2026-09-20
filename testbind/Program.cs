using System;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string which = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            switch (which)
            {
                case "api": return ApiSmokeTest.Run();
                case "api-analogclock": return ApiAnalogClockDemo.Run();
                case "api-lineedits": return ApiLineEditsDemo.Run();
                case "api-widgets": return ApiWidgetsDemo.Run();
                case "api-graphics": return ApiGraphicsDemo.Run();
                case "api-core": return ApiCoreDemo.Run();
                case "api-events": return ApiEventsDemo.Run();
                case "api-custommodel": return ApiCustomModelDemo.Run();
                case "api-ownership": return ApiOwnershipDemo.Run();
                case "api-metaobject": return ApiMetaObjectDemo.Run();
                case "api-calculator": return ApiCalculatorDemo.Run();
                case "api-spinboxes": return ApiSpinBoxesDemo.Run();
                case "api-shortcut": return ApiShortcutDemo.Run();
                case "api-shapedclock": return ShapedClockApi.Run();
                case "api-sliders": return ApiSlidersDemo.Run();
                case "api-calendarwidget": return CalendarWidgetApi.Run();
                case "api-menus": return MenusApi.Run();
                case "api-basiclayouts": return BasicLayoutsApi.Run();
                case "api-scribble": return ScribbleApi.Run();
                case "api-gallery": return WidgetsGalleryApi.Run();
                case "api-systray": return SystemTrayApi.Run();
                case "api-docviewer": return DocumentViewerApi.Run();
                case "api-basicdrawing": return ApiBasicDrawingDemo.Run();
                case "api-gradients": return ApiGradientsDemo.Run();
                case "api-painterpaths": return ApiPainterPathsDemo.Run();
                case "api-completer": return ApiCompleterDemo.Run();
                case "api-customsortfilter": return ApiCustomSortFilterDemo.Run();
                case "api-stardelegate": return ApiStarDelegateDemo.Run();
                case "api-composition": return ApiCompositionDemo.Run();

                // WindowFlagsApi.cs exists but is deliberately not wired in: it dies about
                // three runs in ten while Qt tears the process down (see its header).
            }

            Console.WriteLine("[usage] api | api-analogclock | api-lineedits | api-widgets | api-graphics |");
            Console.WriteLine("        api-core | api-events | api-ownership | api-custommodel | api-calculator |");
            Console.WriteLine("        api-spinboxes | api-shortcut | api-metaobject | api-shapedclock | api-sliders |");
            Console.WriteLine("        api-calendarwidget | api-menus | api-basiclayouts | api-scribble | api-gallery |");
            Console.WriteLine("        api-systray | api-docviewer | api-basicdrawing | api-gradients |");
            Console.WriteLine("        api-painterpaths | api-completer | api-customsortfilter |");
            Console.WriteLine("        api-stardelegate | api-composition");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }
}
