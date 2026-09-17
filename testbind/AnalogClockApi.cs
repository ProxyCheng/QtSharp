using System;
using QtSharp.Api;

// The official widgets/analogclock example, ported onto the *new* pipeline: every type below
// (QWidget, QPainter, QTimer, QColor, QTime, QPolygon, the enums) comes from
// model/qtsharp.api.json through the code generator - there is no hand-written glue.
//
// It exercises all three pillars at once: the QTimer signal, the paintEvent override that
// native Qt calls back into, and ownership (the painter and the boxed value types are ours
// to free, the timer is owned by its parent widget).
//
// Named ApiAnalogClock to keep it clearly separate from the legacy-pipeline AnalogClock that
// testapp's "analogclock" command still uses.
public sealed class ApiAnalogClock : QWidget
{
    public int PaintCount;
    public int CustomEventCount;
    public int UserEventCount;
    public int PressCount;
    public double LastPressX = double.NaN;
    public double LastPressY = double.NaN;
    public Qt.MouseButton LastButton = Qt.MouseButton.NoButton;

    public ApiAnalogClock() : base(null)
    {
        // Parented to the clock, so Qt owns it: no Dispose on this side (ownership rule 1).
        var timer = new QTimer(this);
        timer.TimeoutSignal.Connect(Update);
        timer.Start(1000);

        SetWindowTitle("Analog Clock (new pipeline)");
        Resize(200, 200);
    }

    // Declared in QObject, not in QWidget: this only works because the generated native
    // subclass overrides the *whole* virtual chain of the class it subclasses.
    public override void CustomEvent(QEvent e)
    {
        CustomEventCount++;
        // `type()` keeps its C++ name: Pascal-casing it would collide with the nested
        // QEvent.Type enum, which wins (same rule the legacy pipeline uses).
        if (e.type() == QEvent.Type.User)
            UserEventCount++;
        base.CustomEvent(e);
    }

    // The event is now a bound class rather than a raw pointer, so a handler can read it.
    public override void MousePressEvent(QMouseEvent e)
    {
        PressCount++;
        using var pos = e.Position();
        LastPressX = pos.X();
        LastPressY = pos.Y();
        LastButton = e.Button();
        base.MousePressEvent(e);
    }

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;

        int[] hourHand = { 7, 8, -7, 8, 0, -40, 7, 8 };
        int[] minuteHand = { 7, 8, -7, 8, 0, -70, 7, 8 };

        using var hourColor = new QColor(127, 0, 127, 255);
        using var minuteColor = new QColor(0, 127, 127, 255);
        using var time = QTime.CurrentTime();

        int side = Math.Min(width(), height());

        // QWidget is both a QObject and a QPaintDevice; the painting view of the same native
        // object is a separate subobject and is reached through the generated cast.
        using var painter = new QPainter(AsQPaintDevice());
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
        painter.Translate(width() / 2.0, height() / 2.0);
        painter.Scale(side / 200.0, side / 200.0);

        painter.SetPen(Qt.PenStyle.NoPen);
        painter.SetBrush(hourColor);

        using (var polygon = new QPolygon(4, hourHand))
        {
            painter.Save();
            painter.Rotate(30.0 * (time.Hour() + time.Minute() / 60.0));
            painter.DrawConvexPolygon(polygon);
            painter.Restore();
        }

        for (int i = 0; i < 12; ++i)
        {
            painter.DrawRect(73, -3, 16, 6);
            painter.Rotate(30.0);
        }

        painter.SetBrush(minuteColor);

        using (var polygon = new QPolygon(4, minuteHand))
        {
            painter.Save();
            painter.Rotate(6.0 * time.Minute());
            painter.DrawConvexPolygon(polygon);
            painter.Restore();
        }

        painter.SetPen(minuteColor);

        for (int j = 0; j < 60; ++j)
        {
            painter.DrawLine(92, 0, 96, 0);
            painter.Rotate(6.0);
        }

        painter.End();
    }
}

// Runs the demo as a test: show the clock, let a real Qt event loop paint it for a moment,
// then quit and assert that the generated virtual dispatch actually reached managed code.
public static class ApiAnalogClockDemo
{
    public static int Run()
    {
        ApiRuntime.StartApplication();

        var clock = new ApiAnalogClock();
        clock.Show();

        var stop = new QTimer();
        stop.SetSingleShot(true);
        stop.TimeoutSignal.Connect(() => ApiRuntime.Quit());
        // Long enough to be looked at: QTSHARP_DEMO_MS=8000 leaves the clock on screen.
        int runFor = int.TryParse(Environment.GetEnvironmentVariable("QTSHARP_DEMO_MS"), out var ms) ? ms : 1500;
        stop.Start(runFor);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // A forced synchronous repaint, so the test also passes when the session never
        // exposes the window.
        clock.Repaint();
        ApiRuntime.Exec();
        sw.Stop();

        // QObject::event routes user events to the virtual customEvent(), which this class
        // overrides even though QWidget never declares it.
        bool delivered = ApiRuntime.SendUserEvent(clock.Handle);

        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        Check(clock.PaintCount > 0,
            $"AnalogClock painted {clock.PaintCount} time(s) through the generated paintEvent dispatch");
        Check(delivered && clock.CustomEventCount == 1,
            $"override of an inherited virtual (QObject::customEvent) reached C# (count={clock.CustomEventCount})");
        Check(clock.UserEventCount == 1,
            $"the event was bound as a class, not a raw pointer (QEvent::type() == User)");

        // A real mouse press, delivered by Qt, with its payload read through the bound
        // event classes (QMouseEvent::position()/button() come from the bound base chain).
        bool pressed = ApiRuntime.SendMousePress(clock.Handle, 42, 24);
        Check(pressed && clock.PressCount == 1 && clock.LastPressX == 42 && clock.LastPressY == 24
              && clock.LastButton == Qt.MouseButton.LeftButton,
            $"mousePressEvent payload reached C# (button={clock.LastButton}, at {clock.LastPressX},{clock.LastPressY})");

        // QFlags surfaces as a [Flags] enum over the native QFlags type, so a mask has to
        // survive both directions.
        var probe = new QWidget(null);
        probe.SetWindowFlags(Qt.WindowFlags.FramelessWindowHint | Qt.WindowFlags.WindowStaysOnTopHint);
        var flags = probe.WindowFlags();
        Check(flags.HasFlag(Qt.WindowFlags.FramelessWindowHint) && flags.HasFlag(Qt.WindowFlags.WindowStaysOnTopHint),
            $"QFlags round-trip through the native QFlags type (got 0x{(int)flags:X})");

        // Geometry value types are boxed handles too, so a round-trip exercises construction,
        // by-const-reference parameters, by-value returns and ownership at once.
        probe.Resize(123, 45);
        using (var rect = probe.rect())
            Check(rect.Width() == 123 && rect.Height() == 45,
                $"QRect return + accessors (got {rect.Width()}x{rect.Height()})");
        probe.Move(new QPoint(7, 9));
        using (var pos = probe.pos())
            Check(pos.X() == 7 && pos.Y() == 9,
                $"QPoint round-trip through move() (got {pos.X()},{pos.Y()})");

        // The remaining value types are exercised through an offscreen QImage: a pen, a
        // brush and a painter path drawn into native pixel storage, then read back. That is
        // the strongest check available without a screen.
        using (var image = new QImage(new QSize(32, 32), QImage.Format.Format_ARGB32))
        {
            image.Fill(new QColor(0, 0, 0, 255));

            using (var painter = new QPainter(image))
            {
                painter.SetPen(new QPen(new QColor(255, 0, 0, 255)));
                painter.SetBrush(new QBrush(new QColor(0, 255, 0, 255)));
                painter.DrawEllipse(new QRect(2, 2, 20, 20));

                using (var path = new QPainterPath())
                {
                    path.MoveTo(new QPointF(0, 0));
                    path.LineTo(new QPointF(31, 31));
                    painter.DrawPath(path);
                }
                painter.End();
            }

            // (12,6) is inside the ellipse but off the red diagonal drawn below it.
            using (var inside = image.PixelColor(12, 6))
                Check(inside.Green() == 255 && inside.Blue() == 0,
                    $"QImage + QPen/QBrush/QPainterPath painted a filled ellipse (got rgb {inside.Red()},{inside.Green()},{inside.Blue()})");
            using (var outside = image.PixelColor(30, 1))
                Check(outside.Red() == 0 && outside.Green() == 0,
                    $"the rest of the image is still the fill colour (got rgb {outside.Red()},{outside.Green()},{outside.Blue()})");
        }

        var action = new QAction("Open");
        Check(action.text() == "Open", $"QAction constructed and read back (got '{action.text()}')");

        Console.WriteLine($"[info] {sw.Elapsed.TotalMilliseconds:F0} ms of a real Qt event loop, driven by a generated QTimer signal");
        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
