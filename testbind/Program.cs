using System;
using QtSharp.QtWidgets;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            using var app = new QApplication(new[] { "qtsharp-examples" });

            string which = args.Length > 0 ? args[0].ToLowerInvariant() : "sliders";
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
                "analogclock" => new AnalogClock(),
                "calculator" => new Calculator(),
                _ => new SlidersWindow(),
            };
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
