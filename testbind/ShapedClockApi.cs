using System;
using QtSharp.Api;

// The official widgets/shapedclock example, ported onto the *new* pipeline. Every type below
// comes from model/qtsharp.api.json through the code generator - there is no hand-written glue.
//
// It is the counterpart of the legacy-pipeline ShapedClock.cs (which testapp's "shapedclock"
// command still uses): a frameless, translucent, round-windowed analog clock that can be dragged
// with the left mouse button.
//
// Two things this port does that the legacy one could not:
//   * the drag uses the *real* check `QMouseEvent::buttons()`, which is bound now (the legacy
//     port had to remember the pressed state in a `dragging` flag because the accessor was
//     missing);
//   * `globalPosition()` and `frameGeometry()` are both bound, so the drag offset is computed
//     exactly the way the C++ example computes it.
public sealed class ApiShapedClock : QWidget
{
    // Test-visible state (the demo drives the handlers and then asserts on these).
    public int PaintCount;
    public int PressCount;
    public Qt.MouseButton LastButton = Qt.MouseButton.NoButton;
    public Qt.MouseButtons LastButtons = Qt.MouseButtons.NoButton;
    public int LastGlobalX;
    public int LastGlobalY;
    public int PressFrameX;
    public int PressFrameY;
    public int DragOffsetX;
    public int DragOffsetY;
    public int DragMoveCount;

    bool dragging;
    QPoint dragPosition;

    public ApiShapedClock()
        : base(null, Qt.WindowFlags.FramelessWindowHint | Qt.WindowFlags.WindowStaysOnTopHint)
    {
        SetAttribute(Qt.WidgetAttribute.WA_TranslucentBackground);

        // Parented to the clock, so Qt owns it (ownership rule 1). Without an event loop it does
        // not tick, but it is part of the example and keeping it matches the official window.
        var timer = new QTimer(this);
        timer.TimeoutSignal.Connect(Update);
        timer.Start(1000);

        var quitAction = new QAction("E&xit", this);
        quitAction.TriggeredSignal.Connect(_ => ApiRuntime.Quit());
        using (var sequence = new QKeySequence(QKeySequence.StandardKey.Quit))
            quitAction.SetShortcut(sequence);
        AddAction(quitAction);

        ContextMenuPolicy = Qt.ContextMenuPolicy.ActionsContextMenu;
        SetToolTip("Drag the clock with the left mouse button.\nUse the right mouse button to open a context menu.");
        SetWindowTitle("Shaped Analog Clock");

        Resize(200, 200);
        UpdateMask();
    }

    public override QSize SizeHint() => new QSize(200, 200);

    // The round window: the mask is recomputed whenever the clock is resized (the official
    // example's resizeEvent).
    public override void ResizeEvent(QResizeEvent e)
    {
        UpdateMask();
        base.ResizeEvent(e);
    }

    void UpdateMask()
    {
        int side = Math.Min(width(), height());
        using var rect = new QRect((width() - side) / 2, (height() - side) / 2, side, side);
        using var mask = new QRegion(rect, QRegion.RegionType.Ellipse);
        SetMask(mask);
    }

    public override void MousePressEvent(QMouseEvent e)
    {
        // The accessor the legacy port was missing: `buttons()` (the bitmask) rather than the
        // single `button()` that a press carries. (The bitmask is Qt.MouseButtons, the single
        // value Qt.MouseButton - two distinct enums, so the test is a bitwise one.)
        if ((e.Buttons() & Qt.MouseButtons.LeftButton) != 0)
        {
            PressCount++;
            LastButton = e.Button();
            LastButtons = e.Buttons();

            using (var global = e.GlobalPosition())
            {
                LastGlobalX = (int)Math.Round(global.X());
                LastGlobalY = (int)Math.Round(global.Y());
            }
            using (var frame = FrameGeometry)
            using (var topLeft = frame.TopLeft())
            {
                PressFrameX = topLeft.X();
                PressFrameY = topLeft.Y();
                DragOffsetX = LastGlobalX - PressFrameX;
                DragOffsetY = LastGlobalY - PressFrameY;
            }

            dragPosition = new QPoint(DragOffsetX, DragOffsetY);
            dragging = true;
            e.Accept();
            return;
        }

        base.MousePressEvent(e);
    }

    public override void MouseMoveEvent(QMouseEvent e)
    {
        // The original: `if (event->buttons() & Qt::LeftButton) { move(event->globalPos() - dragPosition); ... }`.
        if ((e.Buttons() & Qt.MouseButtons.LeftButton) != 0)
        {
            using (var global = e.GlobalPosition())
                ApplyDrag(new QPoint((int)Math.Round(global.X()), (int)Math.Round(global.Y())));
            e.Accept();
            return;
        }

        base.MouseMoveEvent(e);
    }

    public override void MouseReleaseEvent(QMouseEvent e)
    {
        if (e.Button() == Qt.MouseButton.LeftButton)
            dragging = false;
        base.MouseReleaseEvent(e);
    }

    /// <summary>
    /// The body of the drag, shared with the move handler. The runtime has a synthetic *press*
    /// probe but no synthetic *move* probe, so a headless test cannot reach MouseMoveEvent with a
    /// real event; it drives the exact same code through this seam instead.
    /// </summary>
    public void DragTo(QPoint globalPosition) => ApplyDrag(globalPosition);

    void ApplyDrag(QPoint globalPosition)
    {
        if (!dragging || dragPosition == null)
            return;

        Move(globalPosition.X() - dragPosition.X(), globalPosition.Y() - dragPosition.Y());
        DragMoveCount++;
    }

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;

        int[] hourHand = { 5, 14, -5, 14, -4, -71, 4, -71 };
        int[] minuteHand = { 4, 14, -4, 14, -3, -89, 3, -89 };
        int[] secondsHand = { 1, 14, -1, 14, -1, -89, 1, -89 };

        using var palette = Palette;
        using var hourColor = palette.Color(QPalette.ColorRole.Text);
        using var minuteColor = palette.Color(QPalette.ColorRole.Text);
        using var secondsColor = palette.Color(QPalette.ColorRole.Accent);

        int side = Math.Min(width(), height());

        using var painter = new QPainter(AsQPaintDevice());
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
        painter.Translate(width() / 2.0, height() / 2.0);
        painter.Scale(side / 200.0, side / 200.0);

        using (var windowColor = palette.Color(QPalette.ColorRole.Window))
        {
            painter.SetPen(Qt.PenStyle.NoPen);
            painter.SetBrush(windowColor);
        }
        painter.SetOpacity(0.9);
        painter.DrawEllipse(-98, -98, 196, 196);
        painter.SetOpacity(1.0);

        using (var time = QTime.CurrentTime())
        {
            painter.SetBrush(hourColor);

            painter.Save();
            painter.Rotate(30.0 * (time.Hour() + time.Minute() / 60.0));
            using (var polygon = new QPolygon(4, hourHand))
                painter.DrawConvexPolygon(polygon);
            painter.Restore();

            for (int i = 0; i < 12; ++i)
            {
                painter.DrawRect(73, -3, 16, 6);
                painter.Rotate(30.0);
            }

            painter.SetBrush(minuteColor);

            painter.Save();
            painter.Rotate(6.0 * time.Minute());
            using (var polygon = new QPolygon(4, minuteHand))
                painter.DrawConvexPolygon(polygon);
            painter.Restore();

            painter.SetBrush(secondsColor);

            painter.Save();
            painter.Rotate(6.0 * time.Second());
            using (var polygon = new QPolygon(4, secondsHand))
                painter.DrawConvexPolygon(polygon);
            painter.DrawEllipse(-3, -3, 6, 6);
            painter.DrawEllipse(-5, -68, 10, 10);
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

// Runs the demo as a test: start the application, build the clock, force an off-screen render and
// drive the mouse handlers, then assert. No event loop is needed - `grab()` paints synchronously.
public static class ShapedClockApi
{
    public static int Run()
    {
        ApiRuntime.StartApplication();

        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var clock = new ApiShapedClock();
        clock.Show();

        // A forced synchronous repaint, so the paintEvent override is reached even though no
        // event loop runs; grab() then renders the same widget off-screen.
        clock.Repaint();
        using (var rendered = clock.Grab())
            Check(rendered != null && !rendered.IsNull() && rendered.Width() == 200 && rendered.Height() == 200,
                $"grab() rendered the shaped clock off-screen (got {rendered?.Width()}x{rendered?.Height()})");

        Check(clock.PaintCount > 0,
            $"paintEvent reached C# {clock.PaintCount} time(s) through the generated dispatch");

        // QFlags surfaces as a [Flags] enum over the native QFlags type, and windowFlags() round-trips.
        var flags = clock.WindowFlags();
        Check(flags.HasFlag(Qt.WindowFlags.FramelessWindowHint) && flags.HasFlag(Qt.WindowFlags.WindowStaysOnTopHint),
            $"the frameless/stays-on-top flags took effect (got 0x{(int)flags:X})");

        // The round window: an ellipse region, not a rectangle (many scanline rects), spanning the
        // whole 200x200 widget.
        using (var mask = clock.Mask())
        {
            bool maskSet = mask != null && !mask.IsNull() && !mask.IsEmpty();
            int maskWidth = 0;
            int maskHeight = 0;
            int rectCount = maskSet ? mask.RectCount() : 0;
            if (maskSet)
            {
                using var bounds = mask.BoundingRect();
                maskWidth = bounds.Width();
                maskHeight = bounds.Height();
            }
            // An ellipse region is a set of scanline rects, and Qt's scanline approximation makes
            // the bounding box one row short at the bottom, so 199 is the correct height here.
            Check(maskSet && rectCount > 1 && maskWidth == 200 && maskHeight >= 199,
                $"the round mask is set ({rectCount} scanline rects, bounding {maskWidth}x{maskHeight})");
        }

        // The virtual override: the boxed QSize comes back through the generated accessor.
        using (var hint = clock.SizeHint())
            Check(hint.Width() == 200 && hint.Height() == 200,
                $"sizeHint() reports 200x200 (got {hint.Width()}x{hint.Height()})");

        // The context menu (the official example's right-click menu) is the widget's actions.
        Check(clock.ContextMenuPolicy == Qt.ContextMenuPolicy.ActionsContextMenu,
            $"contextMenuPolicy is ActionsContextMenu (got {clock.ContextMenuPolicy})");

        QAction quitAction = null;
        foreach (var action in clock.Actions())
            if (action.text() == "E&xit")
                quitAction = action;

        Check(quitAction != null, "the quit QAction is installed on the clock");
        if (quitAction != null)
        {
            // StandardKey.Quit has no binding on Windows (Windows apps quit with Alt+F4), so the
            // platform's own sequence may legitimately be empty; the contract is that the action
            // carries exactly that sequence.
            using (var actual = quitAction.Shortcut)
            using (var expected = new QKeySequence(QKeySequence.StandardKey.Quit))
                Check(actual != null && actual.Count() == expected.Count() && actual.ToString() == expected.ToString(),
                    $"the quit action carries the StandardKey.Quit shortcut (got '{actual?.ToString()}', "
                    + $"platform's Quit = '{expected?.ToString()}')");
        }

        // A real mouse press delivered by Qt, with its payload read through the bound event class:
        // button() *and* buttons() (the accessor the legacy port lacked).
        bool pressed = ApiRuntime.SendMousePress(clock.Handle, 42, 24);
        Check(pressed && clock.PressCount == 1 && clock.LastButton == Qt.MouseButton.LeftButton,
            $"mousePressEvent payload reached C# (delivered={pressed}, count={clock.PressCount}, button={clock.LastButton})");
        Check((clock.LastButtons & Qt.MouseButtons.LeftButton) != 0,
            $"QMouseEvent::buttons() is bound and reports the held button (got {clock.LastButtons})");
        Check(clock.LastGlobalX == 42 && clock.LastGlobalY == 24,
            $"globalPosition() reached C# (got {clock.LastGlobalX},{clock.LastGlobalY})");
        Check(clock.DragOffsetX == clock.LastGlobalX - clock.PressFrameX
              && clock.DragOffsetY == clock.LastGlobalY - clock.PressFrameY,
            $"the press stored globalPosition - frameGeometry().topLeft "
            + $"(offset {clock.DragOffsetX},{clock.DragOffsetY} from frame {clock.PressFrameX},{clock.PressFrameY})");

        // The drag itself: the press stored `globalPosition - frameGeometry().topLeft`, and a move
        // to `press + delta` has to move the window by exactly that delta. Driven through the
        // shared drag body because there is no synthetic move probe.
        using (var posBefore = clock.pos())
        {
            int dx = 37;
            int dy = -21;
            clock.DragTo(new QPoint(clock.LastGlobalX + dx, clock.LastGlobalY + dy));
            using (var posAfter = clock.pos())
                Check(clock.DragMoveCount == 1
                      && posAfter.X() - posBefore.X() == dx && posAfter.Y() - posBefore.Y() == dy,
                    $"the drag moved the window by ({dx},{dy}) (from {posBefore.X()},{posBefore.Y()} "
                    + $"to {posAfter.X()},{posAfter.Y()})");
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
