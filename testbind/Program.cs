using System;
using QtSharp.QtCore;
using QtSharp.QtWidgets;

static class Program
{
    static int Main()
    {
        try
        {
            var q = new QString("a,b,c");
            string[] parts = q.Split(new QString(","), 0, 0);
            Console.WriteLine($"[OK] QStringList split => [{string.Join("|", parts)}]");

            using var app = new QApplication(new[] { "qtsharp" });

            using var window = new QWidget(null, 0);
            window.WindowTitle = "Layout demo";
            window.Resize(400, 200);

            var layout = new QVBoxLayout();
            var edit = new QLineEdit(null);
            var button = new QPushButton(null);
            button.Text = "Click me";

            layout.AddWidget(edit);
            layout.AddWidget(button);
            window.Layout = layout;

            window.Show();
            Console.WriteLine("[OK] Window shown; close it to exit...");

            int rc = QApplication.Exec();
            Console.WriteLine($"[OK] exec exited with code {rc}");
            return rc;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }
}
