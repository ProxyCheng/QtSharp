using System;
using QtSharp.QtWidgets;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var app = new QApplication(args);
        var window = new QWidget();
        window.WindowTitle = "Hello from C#";
        window.Resize(320, 120);
        window.Show();
        return app.Exec();
    }
}
