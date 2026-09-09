using System;
using QtSharp.QtWidgets;

static class Program
{
    [STAThread]
    static int Main()
    {
        try
        {
            using var app = new QApplication(new[] { "analogclock" });

            using var clock = new AnalogClock();
            clock.Show();
            Console.WriteLine("[OK] AnalogClock shown; close it to exit...");

            return QApplication.Exec();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }
}
