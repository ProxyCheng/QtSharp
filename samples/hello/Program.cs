using System;
using QtSharp.Api;

// A minimal Qt application written against the generated binding: a window, a layout, a signal
// wired to a handler, and a widget that paints itself from C#.
//
// Build and run:
//     dotnet build samples\hello\Hello.csproj -c Release
//     samples\hello\bin\Release\net10.0\hello.exe
//
// Set HELLO_AUTOCLOSE_MS to have it quit by itself (that is how the repository's gate runs it).
public sealed class CircleWidget : QWidget
{
    int _diameter = 40;

    public override QSize SizeHint() => new QSize(220, 160);

    public void SetDiameter(int value)
    {
        _diameter = value;
        Update();               // ask for a repaint
    }

    public override void PaintEvent(QPaintEvent e)
    {
        using var painter = new QPainter(AsQPaintDevice());
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
        painter.FillRect(Rect, new QColor(40, 44, 52));
        painter.SetPen(Qt.PenStyle.NoPen);
        painter.SetBrush(new QColor(97, 175, 239));
        int x = (Width - _diameter) / 2;
        int y = (Height - _diameter) / 2;
        painter.DrawEllipse(new QRect(x, y, _diameter, _diameter));
    }
}

public sealed class HelloWindow : QWidget
{
    readonly QLabel _label = new QLabel("type a name and press Greet");
    readonly QLineEdit _edit = new QLineEdit();
    readonly CircleWidget _circle = new CircleWidget();

    public HelloWindow()
    {
        _edit.SetPlaceholderText("your name");

        var greet = new QPushButton("Greet");
        // The generated signal property takes the handler Qt's moc will call back into.
        greet.ClickedSignal.Connect(_ => Greet());

        var row = new QHBoxLayout();
        row.AddWidget(_edit);
        row.AddWidget(greet);

        var column = new QVBoxLayout();
        column.AddWidget(_label);
        column.AddLayout(row);
        column.AddWidget(_circle);
        SetLayout(column);

        SetWindowTitle("Hello, QtSharp");
        Resize(320, 280);
    }

    void Greet()
    {
        string name = _edit.Text;
        _label.SetText(name.Length == 0 ? "type a name and press Greet" : $"Hello, {name}!");
        _circle.SetDiameter(Math.Min(120, 40 + name.Length * 8));
    }

    /// <summary>The value a test can read back (the window's own state).</summary>
    public string Greeting => _label.Text;
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The application object is created the same way it is in PyQt/PySide. It belongs to the
        // process (it has no Dispose), and creating it again would hand back this instance.
        var app = new QApplication(args);

        var window = new HelloWindow();
        window.Show();

        // A real event loop, with an exit for scripted runs.
        if (int.TryParse(Environment.GetEnvironmentVariable("HELLO_AUTOCLOSE_MS"), out int ms))
        {
            var stop = new QTimer();
            stop.SetSingleShot(true);
            stop.TimeoutSignal.Connect(() => ApiRuntime.Quit());
            stop.Start(ms);
        }

        Console.WriteLine("[OK] hello shown; close the window to exit...");
        ApiRuntime.Exec();
        return 0;
    }
}
