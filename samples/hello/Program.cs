using System;
using QtSharp.Api;

// A minimal Qt application written against the generated binding: a window, a layout, a signal
// wired to a handler, a widget that paints itself from C#, and a class that declares its *own*
// signal and properties in C# (see HelloCounter.cs - the pyqtSignal / pyqtProperty equivalent).
//
// Build and run:
//     dotnet build samples\hello\Hello.csproj -c Release
//     samples\hello\bin\Release\net10.0\hello.exe
//
// It is also the entry point for trying the ported official examples out: pass a demo name and this
// program runs that one instead of the hello window. The names are listed by `hello help` (or by
// `testapp` with no argument); both entry points call the same ApiDemos.TryRun.
//
//     hello api-calculator      run a ported official example
//     hello help                list them
//     hello selftest            check the C#-declared signal/property, no window (the gate runs it)
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
    readonly HelloCounter _counter = new HelloCounter();
    readonly QLabel _count = new QLabel();

    public HelloWindow()
    {
        _edit.SetPlaceholderText("your name");

        var greet = new QPushButton("Greet");
        // The generated signal property takes the handler Qt's moc will call back into.
        greet.Clicked += _ => Greet();

        // The signal declared in C# (HelloCounter.cs) is subscribed to exactly the same way: the
        // handler runs when the counter emits, whether the emit came from C# or from Qt
        // (setProperty). The event is the C#-idiomatic shape; the raw declaration
        // (HelloCounter.ResetSignal.Connect) is the other spelling, and Emit only exists there.
        _counter.ValueChanged += v => RefreshCount();
        HelloCounter.ResetSignal.Connect(_counter, () => RefreshCount());

        var bump = new QPushButton("Count");
        bump.Clicked += _ => ClickCount();

        var reset = new QPushButton("Reset via Qt");
        // Qt's own path into the C# property: setProperty runs the QMetaProperty setter, which
        // notifies - so this fires the event just like the Count button does.
        reset.Clicked += _ => SetCountThroughQt(0);

        RefreshCount();

        var row = new QHBoxLayout();
        row.AddWidget(_edit);
        row.AddWidget(greet);

        var counterRow = new QHBoxLayout();
        counterRow.AddWidget(bump);
        counterRow.AddWidget(reset);
        counterRow.AddWidget(_count, 1);

        var column = new QVBoxLayout();
        column.AddWidget(_label);
        column.AddLayout(row);
        column.AddWidget(_circle);
        column.AddLayout(counterRow);
        SetLayout(column);

        SetWindowTitle("Hello, QtSharp");
        Resize(320, 300);
    }

    void Greet()
    {
        string name = _edit.Text;
        _label.SetText(name.Length == 0 ? "type a name and press Greet" : $"Hello, {name}!");
        _circle.SetDiameter(Math.Min(120, 40 + name.Length * 8));
    }

    /// <summary>What the Count button does: a plain C# call that emits the declared signal.</summary>
    public void ClickCount() => _counter.Bump();

    /// <summary>What "Reset via Qt" does: write the property through QObject::setProperty.</summary>
    public void SetCountThroughQt(int value)
    {
        using var qvalue = new QVariant(value);
        _counter.SetProperty("value", qvalue);
    }

    void RefreshCount()
    {
        // Read the same property back through Qt as well, to show both sides agree.
        using var viaQt = _counter.Property("value");
        using var labelViaQt = _counter.Property("label");
        _count.SetText($"{labelViaQt} = {_counter.Current}, and Qt says {viaQt.ToInt()}");
    }

    /// <summary>The value a test can read back (the window's own state).</summary>
    public string Greeting => _label.Text;

    /// <summary>The HelloCounter's C#-side value.</summary>
    public int CounterValue => _counter.Current;

    /// <summary>The HelloCounter as the window displays it ("clicks = 2, and Qt says 2").</summary>
    public string CounterText => _count.Text;

    /// <summary>The declared members as Qt sees them (the meta-object the runtime installed).</summary>
    public QMetaObject CounterMetaObject() => _counter.MetaObject();

    /// <summary>The counter itself, for a test that wants to subscribe to it.</summary>
    public HelloCounter Counter => _counter;

    /// <summary>
    /// Drives the sample's C#-declared signal and properties the way the UI does and checks the
    /// result - `hello selftest`, which is what the repository's gate runs, so this example cannot
    /// rot. No window is shown and the run loop is never entered.
    /// </summary>
    public static int SelfTest()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // The declarations really are in the meta-object the runtime built for this class.
        var window = new HelloWindow();
        var meta = window.CounterMetaObject();
        Check(meta.ClassName() == "HelloCounter",
            $"the declared members give the object a meta-object of the C# class (got '{meta.ClassName()}')");
        Check(meta.IndexOfSignal("valueChanged(int)") >= 0 && meta.IndexOfSignal("reset()") >= 0,
            "both C#-declared signals are in the meta-object");
        Check(meta.IndexOfProperty("value") >= 0 && meta.IndexOfProperty("label") >= 0,
            "both C#-declared properties are in the meta-object");
        Check(meta.IndexOfSignal("objectNameChanged(QString)") >= 0,
            "the inherited Qt members are still there");

        // The Count button: a C# emit reaches the handler that updates the label.
        Check(window.CounterValue == 0, $"the HelloCounter starts at 0 (got {window.CounterValue})");
        window.ClickCount();
        window.ClickCount();
        Check(window.CounterValue == 2 && window.CounterText.Contains("= 2"),
            $"two counts reach the handler (value {window.CounterValue}, label \"{window.CounterText}\")");

        // The "Reset via Qt" button: setProperty runs the C# setter, which notifies.
        window.SetCountThroughQt(41);
        Check(window.CounterValue == 41 && window.CounterText.Contains("= 41"),
            $"setProperty from Qt runs the C# setter and notifies (value {window.CounterValue}, "
            + $"label \"{window.CounterText}\")");

        // The read-only property, read back through Qt's QVariant.
        Check(window.CounterText.StartsWith("clicks", StringComparison.Ordinal),
            $"the read-only string property reads back through Qt (label \"{window.CounterText}\")");

        // The C#-declared signal is also an ordinary C# event: += subscribes, -= unsubscribes.
        int seen = -1;
        Action<int> onValue = v => seen = v;
        window.Counter.ValueChanged += onValue;
        window.ClickCount();
        int afterSubscribe = seen;
        window.Counter.ValueChanged -= onValue;
        window.ClickCount();
        Check(afterSubscribe == window.CounterValue - 1 && seen == afterSubscribe,
            $"+= subscribes and -= unsubscribes a C#-declared signal "
            + $"(handler last saw {seen}, value is {window.CounterValue})");

        // And a *Qt* signal has both spellings: the generated event (Clicked) and the QSignal
        // property (ClickedSignal), which is where Emit lives.
        var probe = new QPushButton();
        int probeClicks = 0;
        Action<bool> onClick = _ => probeClicks++;
        probe.Clicked += onClick;
        probe.ClickedSignal.Emit(false);
        probe.Clicked -= onClick;
        probe.ClickedSignal.Emit(false);
        Check(probeClicks == 1,
            $"a Qt signal as a C# event connects and disconnects (fired {probeClicks}x)");

        // The sample's own window still works, so the demo edits did not break it.
        Check(window.Greeting.StartsWith("type a name", StringComparison.Ordinal),
            $"the hello window is built (got \"{window.Greeting}\")");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool selfTest = args.Length > 0 && args[0].Equals("selftest", StringComparison.OrdinalIgnoreCase);

        // "hello api-analogclock" runs one of the ported official examples instead of the hello
        // window (ApiDemos builds the application object itself, so this comes first). "hello
        // help" - or a demo name that does not exist - prints the list.
        if (!selfTest && args.Length > 0)
        {
            if (ApiDemos.TryRun(args, out int demoExit))
                return demoExit;

            bool askedForHelp = args[0] == "help" || args[0] == "?" || args[0] == "-h" || args[0] == "--help";
            if (askedForHelp || args[0].StartsWith("api", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(ApiDemos.Usage);
                return askedForHelp ? 0 : 1;
            }
        }

        // The application object is created the same way it is in PyQt/PySide. It belongs to the
        // process (it has no Dispose), and creating it again would hand back this instance.
        var app = new QApplication(args);

        if (selfTest)
            return HelloWindow.SelfTest();

        // The demos in QtSharp.Demos are ordinary public widgets, so you can also build one here
        // and drive it yourself instead of handing the whole run over to ApiDemos.TryRun:
        //
        //     var demo = new ApiCalendarWidgetWindow();   // or ApiScribbleArea, ApiTransformWindow...
        //     var layout = new QVBoxLayout();
        //     layout.AddWidget(demo);                     // or just demo.Show()
        //     SetLayout(layout);
        //
        // or run one against the application object you just built:
        //
        //     if (ApiDemos.TryRun("api-calculator", app, out int exit))
        //         return exit;
        //
        // what you cannot do is *derive* from them - the demo classes are deliberately sealed, so
        // that a demo stays a readable, self-contained example. ApiArthurFrame (PathStrokeApi.cs)
        // is the one exception: it is a small base widget with a virtual Paint(QPainter) hook.
        var window = new HelloWindow();
        window.Show();

        // A real event loop, with an exit for scripted runs. app.Exec() is Qt's exec(), exposed on
        // the instance the way PyQt exposes it.
        if (int.TryParse(Environment.GetEnvironmentVariable("HELLO_AUTOCLOSE_MS"), out int ms))
        {
            var stop = new QTimer();
            stop.SetSingleShot(true);
            stop.TimeoutSignal.Connect(() => app.Quit());
            stop.Start(ms);
        }

        Console.WriteLine("[OK] hello shown; close the window to exit...");
        return app.Exec();
    }
}
