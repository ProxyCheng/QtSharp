using System;

// The official examples/widgets/widgets/analogclock example, ported onto the *new* pipeline:
// every type below (QWidget, QPainter, QTimer, QColor, QTime, QPolygon, the enums) comes from
// model/qtsharp.api.json through the code generator - there is no hand-written glue.
//
// The painting is a line-by-line port of the C++ window:
//   * the same two colours, QColor(127, 0, 127) and QColor(0, 127, 127, 191);
//   * the same three-point hand polygons, QPoint(7,8) / QPoint(-7,8) / QPoint(0,-40) and
//     QPoint(7,8) / QPoint(-7,8) / QPoint(0,-70);
//   * the same side = min(width, height), translate(width/2, height/2), scale(side/200.0) maths;
//   * 12 hour ticks from drawLine(88, 0, 96, 0) with rotate(30.0), then 60 minute ticks from
//     drawLine(92, 0, 96, 0), skipping every fifth, with rotate(6.0).
// It also keeps the mechanism checks the earlier revision of this demo had (the QTimer signal,
// the paintEvent override, an inherited-virtual override, the mouse payload) so it still works as
// the qtwidgets-analogclock self-test.
//
// TEST SEAM: PaintEvent never reads the wall clock directly. It reads the (_hour, _minute,
// _second) fields, which the constructor fills from QTime::currentTime() and the test pins with
// SetTimeForTest(QTime). That is what makes every pixel assertion in AnalogClock
// deterministic instead of a function of when the test happens to run.
public sealed class ApiAnalogClock : QWidget
{
    // ---- test-visible state (the demo asserts on these) ---------------------------------
    public int PaintCount;
    public int CustomEventCount;
    public int UserEventCount;
    public int PressCount;
    public double LastPressX = double.NaN;
    public double LastPressY = double.NaN;
    public Qt.MouseButton LastButton = Qt.MouseButton.NoButton;
    public int TimerTicks;

    /// <summary>The QTimer the official window starts; exposed so the test can inspect the wiring.</summary>
    public QTimer ClockTimer { get; }

    // The seam (see the file header): the wall clock at construction, pinnable by the test.
    int _hour, _minute, _second;

    public ApiAnalogClock()
        : base(null)
    {
        // Parented to the clock, so Qt owns it: no Dispose on this side (ownership rule 1).
        // The official window ticks once a second.
        ClockTimer = new QTimer(this);
        ClockTimer.TimeoutSignal.Connect(Update);
        ClockTimer.TimeoutSignal.Connect(() => TimerTicks++);
        ClockTimer.Start(1000);

        // QTime is a value now: there is nothing to dispose.
        var now = QTime.CurrentTime();
        SetTimeForTest(now);

        SetWindowTitle("Analog Clock (new pipeline)");
        Resize(200, 200);
    }

    /// <summary>
    /// Pins the time the next PaintEvent paints. The implementation copies h/m/s out, so the
    /// caller may dispose <paramref name="t"/> immediately. Nothing in the painted output
    /// depends on the wall clock after this call.
    /// </summary>
    public void SetTimeForTest(QTime t)
    {
        _hour = t.Hour();
        _minute = t.Minute();
        _second = t.Second();
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

    // The event is a bound class rather than a raw pointer, so a handler can read it.
    public override void MousePressEvent(QMouseEvent e)
    {
        PressCount++;
        var pos = e.Position();
        {
            LastPressX = pos.X();
            LastPressY = pos.Y();
        }
        LastButton = e.Button();
        base.MousePressEvent(e);
    }

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;

        // The official hand polygons: three points each. `QPolygon(3, int[])` is the managed
        // spelling of the C++ `drawConvexPolygon(hand, 3)`.
        int[] hourHand = { 7, 8, -7, 8, 0, -40 };
        int[] minuteHand = { 7, 8, -7, 8, 0, -70 };

        // The official colours (the hour hand is fully opaque, the minute hand is not).
        using var hourColor = new QColor(127, 0, 127);
        using var minuteColor = new QColor(0, 127, 127, 191);

        // The official angles, including the minute carry in the hour hand and the second
        // carry in the minute hand.
        double hourAngle = 30.0 * (_hour + _minute / 60.0);
        double minuteAngle = 6.0 * (_minute + _second / 60.0);

        int side = Math.Min(width(), height());

        // QWidget is both a QObject and a QPaintDevice; the painting view of the same native
        // object is a separate subobject and is reached through the generated cast.
        using var painter = new QPainter(AsQPaintDevice());
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
        painter.Translate(width() / 2, height() / 2);
        painter.Scale(side / 200.0, side / 200.0);

        painter.SetPen(Qt.PenStyle.NoPen);
        painter.SetBrush(hourColor);

        using (var polygon = new QPolygon(3, hourHand))
        {
            painter.Save();
            painter.Rotate(hourAngle);
            painter.DrawConvexPolygon(polygon);
            painter.Restore();
        }

        painter.SetPen(hourColor);

        for (int i = 0; i < 12; ++i)
        {
            painter.DrawLine(88, 0, 96, 0);
            painter.Rotate(30.0);
        }

        painter.SetPen(Qt.PenStyle.NoPen);
        painter.SetBrush(minuteColor);

        using (var polygon = new QPolygon(3, minuteHand))
        {
            painter.Save();
            painter.Rotate(minuteAngle);
            painter.DrawConvexPolygon(polygon);
            painter.Restore();
        }

        painter.SetPen(minuteColor);

        for (int j = 0; j < 60; ++j)
        {
            if ((j % 5) != 0)
                painter.DrawLine(92, 0, 96, 0);
            painter.Rotate(6.0);
        }

        painter.End();
    }
}

// Runs the demo as a test. It never enters the application's run loop (app.Exec()): the time is
// pinned through the SetTimeForTest seam and the repaints are driven by Repaint()/Grab(), so the
// pixel checks below hold on a machine with no usable display. A short ProcessEvents() burst only
// proves the QTimer -> managed-slot wiring, and nothing about the painting depends on it.
[Demo("qtwidgets-analogclock")]
public static class AnalogClock
{
    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var clock = new ApiAnalogClock();
        clock.Show();
        clock.Resize(200, 200);

        // ---- geometry helpers, all expressed in the 200x200 logical space the painter uses --
        // A point (x, y) of the painter's local coordinate system, rotated by `angleDegrees`
        // exactly the way QPainter::rotate() does (a positive angle turns clockwise on screen),
        // mapped to a pixel of the grabbed image. The mapping folds in the device-pixel ratio.
        (int X, int Y) PixelAt(QImage img, double x, double y, double angleDegrees)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            double rx = x * cos - y * sin;
            double ry = x * sin + y * cos;
            double sx = img.Width() / 200.0;
            double sy = img.Height() / 200.0;
            return ((int)Math.Round(img.Width() / 2.0 + rx * sx),
                    (int)Math.Round(img.Height() / 2.0 + ry * sy));
        }

        // How strongly a 3x3 neighbourhood around (px, py) leans to magenta (red == blue, low
        // green) or to teal (green == blue, low red). Comparing channel *relations* rather than
        // absolute values keeps the check independent of the widget's background colour, and the
        // neighbourhood covers an antialiased one-pixel line.
        int BestScore(QImage img, int px, int py, bool magenta)
        {
            int w = img.Width(), h = img.Height();
            int best = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int x = px + dx, y = py + dy;
                if (x < 0 || y < 0 || x >= w || y >= h)
                    continue;
                using var c = img.PixelColor(x, y);
                int score = magenta
                    ? Math.Min(c.Red() - c.Green(), c.Blue() - c.Green())
                    : Math.Min(c.Green() - c.Red(), c.Blue() - c.Red());
                if (score > best)
                    best = score;
            }
            return best;
        }

        // Counts, along a ray from the centre, the pixels that carry a hand colour. The ray is
        // the direction the rotated hand points; r runs over the painted part of the hand.
        int RayCount(QImage img, double angleDegrees, int from, int to, bool magenta)
        {
            int count = 0;
            for (int r = from; r <= to; r++)
            {
                var p = PixelAt(img, 0, -r, angleDegrees);
                if (BestScore(img, p.X, p.Y, magenta) >= 12)
                    count++;
            }
            return count;
        }

        // Samples the tick ring: `radius` at every `stepDegrees`, optionally skipping the angles
        // that are multiples of `skipMultiplesOf`.
        int RingCount(QImage img, double radius, bool magenta, int stepDegrees, int skipMultiplesOf = 0)
        {
            int count = 0;
            for (int angle = 0; angle < 360; angle += stepDegrees)
            {
                if (skipMultiplesOf > 0 && (angle % skipMultiplesOf) == 0)
                    continue;
                var p = PixelAt(img, radius, 0, angle);
                if (BestScore(img, p.X, p.Y, magenta) >= 12)
                    count++;
            }
            return count;
        }

        // True when anything is painted around (px, py) compared with an empty part of the
        // widget (the upper-left interior, which no hand and no tick reaches).
        bool IsInk(QImage img, int px, int py)
        {
            var bgPt = PixelAt(img, -70, -70, 0.0);
            using var bg = img.PixelColor(bgPt.X, bgPt.Y);
            int bgR = bg.Red(), bgG = bg.Green(), bgB = bg.Blue();
            int w = img.Width(), h = img.Height();
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int x = px + dx, y = py + dy;
                if (x < 0 || y < 0 || x >= w || y >= h)
                    continue;
                using var c = img.PixelColor(x, y);
                if (Math.Abs(c.Red() - bgR) + Math.Abs(c.Green() - bgG) + Math.Abs(c.Blue() - bgB) > 40)
                    return true;
            }
            return false;
        }

        // ---- 3:00:00: hour hand at 3 o'clock (+x), minute hand at 12 (-y) -------------------
        var fixedTime = new QTime(3, 0, 0);
        clock.SetTimeForTest(fixedTime);

        // Drive the repaint ourselves, then grab the same widget off-screen. No event loop.
        clock.Repaint();
        using var rendered = clock.Grab();

        Check(DemoCatalog.GrabCovers(rendered, 200, 200),
            $"grab() rendered the clock at the requested 200x200 (got {rendered?.Width()}x{rendered?.Height()})");
        Check(clock.PaintCount > 0,
            $"paintEvent reached C# {clock.PaintCount} time(s) through the generated dispatch");

        using var image = rendered.ToImage();

        int hourRay = RayCount(image, 90.0, 12, 38, true);
        Check(hourRay >= 22,
            $"the hour hand is painted along the 3 o'clock ray (magenta pixels {hourRay}/27)");

        int minuteRay = RayCount(image, 0.0, 12, 62, false);
        Check(minuteRay >= 45,
            $"the minute hand is painted along the 12 o'clock ray (teal pixels {minuteRay}/51)");

        var hourSample = PixelAt(image, 0, -30, 90.0);
        Check(BestScore(image, hourSample.X, hourSample.Y, true) >= 40,
            "the hour-hand sample carries the official hour colour (magenta 127,0,127)");

        var minuteSample = PixelAt(image, 0, -50, 0.0);
        Check(BestScore(image, minuteSample.X, minuteSample.Y, false) >= 40,
            "the minute-hand sample carries the official minute colour (teal 0,127,127,191)");

        var empty = PixelAt(image, -60, 0, 0.0);
        Check(!IsInk(image, empty.X, empty.Y),
            "a direction with no hand and no tick is still background");

        int hourTicks = RingCount(image, 90, true, 30);
        Check(hourTicks == 12, $"all 12 hour ticks are painted at r=90 (found {hourTicks}/12)");

        int minuteTicks = RingCount(image, 94, false, 6, 30);
        Check(minuteTicks >= 46, $"the 48 minute ticks are painted at r=94 (found {minuteTicks}/48)");

        int tealAtHourAngles = RingCount(image, 94, false, 30);
        Check(tealAtHourAngles == 0,
            $"every fifth minute tick is skipped (teal at the 12 hour angles: {tealAtHourAngles}/12)");

        int hourAtRing = RingCount(image, 94, true, 30);
        Check(hourAtRing == 12,
            $"the hour ticks reach the r=94 ring under the skipped minute ticks (found {hourAtRing}/12)");

        var ringInner = PixelAt(image, 94, 0, 0.0);
        var ringOuter = PixelAt(image, 99, 0, 0.0);
        Check(IsInk(image, ringInner.X, ringInner.Y) && !IsInk(image, ringOuter.X, ringOuter.Y),
            "the tick ring ends before r=99 (painted at r=94, empty at r=99)");

        // ---- 3:30:00: the hour hand carries the minutes and sits halfway between 3 and 4 -----
        var halfPast = new QTime(3, 30, 0);
        clock.SetTimeForTest(halfPast);
        using var renderedHalf = clock.Grab();
        using var imageHalf = renderedHalf.ToImage();

        int hourHalf = RayCount(imageHalf, 105.0, 12, 38, true);
        Check(hourHalf >= 22,
            $"the hour hand tracks the minutes: at 3:30 it points at 105 degrees (magenta {hourHalf}/27)");

        int minuteHalf = RayCount(imageHalf, 180.0, 12, 62, false);
        Check(minuteHalf >= 45,
            $"at 3:30 the minute hand points at 6 o'clock (180 degrees) (teal {minuteHalf}/51)");

        var hourAt90 = PixelAt(imageHalf, 0, -30, 90.0);
        Check(BestScore(imageHalf, hourAt90.X, hourAt90.Y, true) < 40,
            "at 3:30 the hour hand has left the 3 o'clock ray, so the minute carry term is real");

        // ---- the QTimer wiring --------------------------------------------------------------
        QTimer timer = clock.ClockTimer;
        Check(timer != null && timer.IsActive() && timer.interval() == 1000 && timer.Interval == 1000,
            $"the official QTimer is created, parented and started at 1000 ms "
            + $"(active={timer?.IsActive()}, interval={timer?.interval()})");

        // Prove the timeout actually reaches managed code without paying for exec(): shorten the
        // interval, wait past it, and pump the pending events ourselves.
        int paintsBeforeTimer = clock.PaintCount;
        timer.SetInterval(10);
        System.Threading.Thread.Sleep(150);
        for (int i = 0; i < 5; i++)
            QCoreApplication.ProcessEvents();

        Check(clock.TimerTicks > 0,
            $"the timer timeout reached the managed handler ({clock.TimerTicks} tick(s))");
        Check(clock.PaintCount > paintsBeforeTimer,
            $"a tick calls update(), so a repaint is dispatched (paints {paintsBeforeTimer} -> {clock.PaintCount})");

        // ---- inherited-virtual override and the bound mouse payload -------------------------
        bool delivered = ApiRuntime.SendUserEvent(clock.Handle);
        Check(delivered && clock.CustomEventCount == 1,
            $"override of an inherited virtual (QObject::customEvent) reached C# (count={clock.CustomEventCount})");
        Check(clock.UserEventCount == 1,
            "the event was bound as a class, not a raw pointer (QEvent::type() == User)");

        bool pressed = ApiRuntime.SendMousePress(clock.Handle, 42, 24);
        Check(pressed && clock.PressCount == 1 && clock.LastPressX == 42 && clock.LastPressY == 24
              && clock.LastButton == Qt.MouseButton.LeftButton,
            $"mousePressEvent payload reached C# (delivered={pressed}, count={clock.PressCount}, "
            + $"button={clock.LastButton}, at {clock.LastPressX},{clock.LastPressY})");

        // ---- the value types the earlier revision exercised ---------------------------------
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
        var rect = probe.rect();
            Check(rect.Width() == 123 && rect.Height() == 45,
                $"QRect return + accessors (got {rect.Width()}x{rect.Height()})");
        probe.Move(new QPoint(7, 9));
        var pos = probe.pos();
            Check(pos.X() == 7 && pos.Y() == 9,
                $"QPoint round-trip through move() (got {pos.X()},{pos.Y()})");

        // The remaining value types are exercised through an offscreen QImage: a pen, a brush and
        // a painter path drawn into native pixel storage, then read back.
        using (var image2 = new QImage(new QSize(32, 32), QImage.Format.Format_ARGB32))
        {
            image2.Fill(new QColor(0, 0, 0, 255));

            using (var painter = new QPainter(image2))
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
            using (var inside = image2.PixelColor(12, 6))
                Check(inside.Green() == 255 && inside.Blue() == 0,
                    $"QImage + QPen/QBrush/QPainterPath painted a filled ellipse (got rgb {inside.Red()},{inside.Green()},{inside.Blue()})");
            using (var outside = image2.PixelColor(30, 1))
                Check(outside.Red() == 0 && outside.Green() == 0,
                    $"the rest of the image is still the fill colour (got rgb {outside.Red()},{outside.Green()},{outside.Blue()})");
        }

        var action = new QAction("Open");
        Check(action.text() == "Open", $"QAction constructed and read back (got '{action.text()}')");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
