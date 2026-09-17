using System.Collections.Generic;
using QtSharp.QtCore;
using QtSharp.QtWidgets;

// Creates every demo as its own top-level window so they can all be seen and exercised
// at the same time ("all" command). The returned list keeps the managed wrappers alive
// for as long as the application runs.
public static class AllDemos
{
    public static List<QWidget> CreateAll()
    {
        return new List<QWidget>
        {
            new AnalogClock(),
            new BasicLayoutsDialog(),
            new Calculator(),
            new CalendarWindow(),
            new DocumentViewerWindow(),
            new LineEditsWindow(),
            new MenusWindow(),
            new ScribbleWindow(),
            new ShapedClock(),
            new SlidersWindow(),
            new SpinBoxesWindow(),
            new SystemTrayWindow(),
            new WidgetGalleryWindow(),
            new WindowFlagsWindow(),
        };
    }

    public static void ShowTiled(List<QWidget> windows, int columns = 4,
                                 int dx = 380, int dy = 300, int x0 = 30, int y0 = 30)
    {
        int x = x0, y = y0, col = 0;
        foreach (var window in windows)
        {
            window.Move(new QPoint(x, y));
            window.Show();
            x += dx;
            if (++col == columns)
            {
                col = 0;
                x = x0;
                y += dy;
            }
        }
    }
}
