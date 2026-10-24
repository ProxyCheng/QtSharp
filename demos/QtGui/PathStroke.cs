using System;
using System.Collections.Generic;

// Port of examples/widgets/painting/pathstroke on the new pipeline.
//
// The example animates seven control points around a circle, joins them into a QPainterPath (with
// cubics in CurveMode, with straight lines in LineMode), strokes it - either with an ordinary QPen
// or, for the "Custom" pen style, by building the stroke outline with a QPainterPathStroker and
// filling it - and draws the control points and the connecting polyline on top. Everything is
// painted inside the rounded, tiled ArthurFrame background.
//
// The same code drives one QBasicTimer, so the timerEvent() override really reads the event (that
// is what the QTimerEvent binding added for this port).
//
// Deviations from the official source, all mechanical:
//   * the official `ArthurFrame::paint(QPainter *)` virtual is a plain C# virtual on ApiArthurFrame
//     and its paintEvent() is the managed PaintEvent override - same shape, one less C++ file;
//   * the official keeps its off-screen image in a function-local `static QImage *`; here it is an
//     instance field (the static is shared by every ArthurFrame, which the example never notices);
//   * `layoutForSmallScreens()` is not ported: it is the phone layout, and this port runs the
//     desktop one (`layoutForDesktop`);
//   * the official `clicked()` signal only fires from the small-screen code path, so `Clicked`
//     stays unused here too (it is kept so the port reads like the original);
//   * the touch half of `event()` cannot be reached from the self-test (there is no touch hardware,
//     and QTouchEvent has no public constructor to synthesize one), so the *logic* lives in
//     HandleTouchPoints() - which the self-test drives with its own TouchPoint values - and Event()
//     maps the real QTouchEvent's points into them.
public class ApiArthurFrame : QWidget
{
    protected readonly QPixmap Tile;

    QImage offscreen;
    bool preferImage;

    public ApiArthurFrame(QWidget parent = null) : base(parent)
    {
        Tile = new QPixmap(128, 128);
        // QPixmap::fill(QColor) is not bound (its default argument is a QColor, which C# cannot
        // express as a default), so the tile is filled through a painter instead.
        using (var painter = new QPainter(Tile))
        {
            using var white = new QColor(Qt.GlobalColor.white);
            using var gray = new QColor(230, 230, 230);
            var whole = new QRect(0, 0, 128, 128);
            var topLeft = new QRect(0, 0, 64, 64);
            var bottomRight = new QRect(64, 64, 64, 64);
            painter.FillRect(whole, white);
            painter.FillRect(topLeft, gray);
            painter.FillRect(bottomRight, gray);
            painter.End();
        }
    }

    public bool PreferImage() => preferImage;
    public void SetPreferImage(bool value) => preferImage = value;

    /// <summary>The official `virtual void paint(QPainter *)` hook, called by PaintEvent.</summary>
    public virtual void Paint(QPainter painter) { }

    public override void PaintEvent(QPaintEvent e)
    {
        using var painter = new QPainter();

        if (preferImage)
        {
            if (offscreen == null || offscreen.Width() != width() || offscreen.Height() != height())
            {
                offscreen?.Dispose();
                offscreen = new QImage(width(), height(), QImage.Format.Format_RGB32);
            }
            painter.Begin(offscreen);

            const int o = 10;
            using var currentPalette = palette();
            using var background = currentPalette.Brush(QPalette.ColorRole.Window);
            painter.FillRect(0, 0, o, o, background);
            painter.FillRect(width() - o, 0, o, o, background);
            painter.FillRect(0, height() - o, o, o, background);
            painter.FillRect(width() - o, height() - o, o, o, background);
        }
        else
        {
            painter.Begin(AsQPaintDevice());
        }

        var dirty = e.Rect();
            painter.SetClipRect(dirty);
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);

        // The rounded-corner clip path of the official ArthurFrame.
        using var clipPath = new QPainterPath();
        var full = rect();
        {
            double left = full.X() + 1;
            double top = full.Y() + 1;
            double right = full.Right();
            double bottom = full.Bottom();
            const double radius2 = 8 * 2;

            clipPath.MoveTo(right - radius2, top);
            clipPath.ArcTo(right - radius2, top, radius2, radius2, 90, -90);
            clipPath.ArcTo(right - radius2, bottom - radius2, radius2, radius2, 0, -90);
            clipPath.ArcTo(left, bottom - radius2, radius2, radius2, 270, -90);
            clipPath.ArcTo(left, top, radius2, radius2, 180, -90);
            clipPath.CloseSubpath();
        }

        painter.Save();
        painter.SetClipPath(clipPath, Qt.ClipOperation.IntersectClip);
        var fullRect = rect();
            painter.DrawTiledPixmap(fullRect, Tile);

        Paint(painter);

        painter.Restore();

        const int level = 180;
        using (var outline = new QPen(new QBrush(new QColor(level, level, level)), 2.0,
                                      Qt.PenStyle.SolidLine, Qt.PenCapStyle.SquareCap,
                                      Qt.PenJoinStyle.BevelJoin))
            painter.SetPen(outline);
        painter.SetBrush(Qt.BrushStyle.NoBrush);
        painter.DrawPath(clipPath);

        if (preferImage)
        {
            painter.End();
            painter.Begin(AsQPaintDevice());
            var dirtyRect = e.Rect();
                painter.DrawImage(dirtyRect, offscreen, dirtyRect);
        }
    }
}

public sealed class ApiPathStrokeRenderer : ApiArthurFrame
{
    public enum PathMode { CurveMode, LineMode }

    public const int PointSize = 10;

    public PathMode Mode = PathMode.CurveMode;
    public double PenWidth = 1.0;
    public Qt.PenJoinStyle JoinStyle = Qt.PenJoinStyle.BevelJoin;
    public Qt.PenCapStyle CapStyle = Qt.PenCapStyle.FlatCap;
    public Qt.PenStyle PenStyle = Qt.PenStyle.SolidLine;
    public int ActivePoint = -1;
    public int PaintCount;
    public int UpdatePointsCalls;
    public int ClickedCount;
    public bool WasAnimated = true;
    public bool SmallScreen;
    public bool MouseDrag;

    public Action Clicked;

    readonly List<QPointF> points = new();
    readonly List<QPointF> vectors = new();
    readonly Dictionary<int, int> fingerPointMapping = new();
    readonly QBasicTimer timer = new();
    int mousePressX;
    int mousePressY;

    public ApiPathStrokeRenderer(QWidget parent = null) : base(parent)
    {
        SetSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding);
        SetAttribute(Qt.WidgetAttribute.WA_AcceptTouchEvents);
        Resize(400, 400);
    }

    public IReadOnlyList<QPointF> Points => points;
    public IReadOnlyList<QPointF> Vectors => vectors;
    public bool TimerActive() => timer.IsActive();
    public int TimerId() => timer.TimerId();
    public int MappedFingerCount() => fingerPointMapping.Count;

    public override QSize SizeHint() => new QSize(500, 500);

    public void SetAnimation(bool animation)
    {
        timer.Stop();
        if (animation)
            timer.Start(25, this);
    }

    public void SetRealPenWidth(double width) { PenWidth = width; Update(); }
    public void SetPenWidth(int width) { PenWidth = width / 10.0; Update(); }

    public void SetFlatCap() { CapStyle = Qt.PenCapStyle.FlatCap; Update(); }
    public void SetSquareCap() { CapStyle = Qt.PenCapStyle.SquareCap; Update(); }
    public void SetRoundCap() { CapStyle = Qt.PenCapStyle.RoundCap; Update(); }

    public void SetBevelJoin() { JoinStyle = Qt.PenJoinStyle.BevelJoin; Update(); }
    public void SetMiterJoin() { JoinStyle = Qt.PenJoinStyle.MiterJoin; Update(); }
    public void SetSvgMiterJoin() { JoinStyle = Qt.PenJoinStyle.SvgMiterJoin; Update(); }
    public void SetRoundJoin() { JoinStyle = Qt.PenJoinStyle.RoundJoin; Update(); }

    public void SetCurveMode() { Mode = PathMode.CurveMode; Update(); }
    public void SetLineMode() { Mode = PathMode.LineMode; Update(); }

    public void SetSolidLine() { PenStyle = Qt.PenStyle.SolidLine; Update(); }
    public void SetDashLine() { PenStyle = Qt.PenStyle.DashLine; Update(); }
    public void SetDotLine() { PenStyle = Qt.PenStyle.DotLine; Update(); }
    public void SetDashDotLine() { PenStyle = Qt.PenStyle.DashDotLine; Update(); }
    public void SetDashDotDotLine() { PenStyle = Qt.PenStyle.DashDotDotLine; Update(); }
    /// <summary>The "Custom" style: no QPen at all, the stroke outline is stroked by hand.</summary>
    public void SetCustomDashLine() { PenStyle = Qt.PenStyle.NoPen; Update(); }

    public override void Paint(QPainter painter)
    {
        PaintCount++;

        if (points.Count == 0)
            InitializePoints();

        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
        painter.SetPen(Qt.PenStyle.NoPen);

        // The path through the control points: cubics in CurveMode, straight lines in LineMode.
        using var path = new QPainterPath();
        path.MoveTo(points[0]);
        if (Mode == PathMode.LineMode)
        {
            for (int i = 1; i < points.Count; i++)
                path.LineTo(points[i]);
        }
        else
        {
            int i = 1;
            while (i + 2 < points.Count)
            {
                path.CubicTo(points[i], points[i + 1], points[i + 2]);
                i += 3;
            }
            while (i < points.Count)
            {
                path.LineTo(points[i]);
                i++;
            }
        }

        // Draw the path. The "custom" pen goes through a QPainterPathStroker and a fill.
        if (PenStyle == Qt.PenStyle.NoPen)
        {
            using var stroker = new QPainterPathStroker();
            stroker.SetWidth(PenWidth);
            stroker.SetJoinStyle(JoinStyle);
            stroker.SetCapStyle(CapStyle);

            const double space = 4;
            stroker.SetDashPattern(new[]
            {
                1.0, space, 3.0, space, 9.0, space, 27.0, space, 9.0, space, 3.0, space,
            });
            using var stroke = stroker.CreateStroke(path);
            using var red = new QBrush(new QColor(Qt.GlobalColor.red));
            painter.FillPath(stroke, red);
        }
        else
        {
            using var red = new QBrush(new QColor(Qt.GlobalColor.red));
            using var pen = new QPen(red, PenWidth, PenStyle, CapStyle, JoinStyle);
            painter.StrokePath(path, pen);
        }

        // Draw the control points, then the polyline through them.
        using (var handlePen = new QPen(new QColor(50, 100, 120, 200)))
            painter.SetPen(handlePen);
        using (var handleBrush = new QBrush(new QColor(200, 200, 210, 120)))
            painter.SetBrush(handleBrush);
        foreach (var point in points)
        {
            var ellipse = new QRectF(point.X() - PointSize, point.Y() - PointSize,
                                           PointSize * 2.0, PointSize * 2.0);
            painter.DrawEllipse(ellipse);
        }

        using (var polylinePen = new QPen(new QBrush(new QColor(Qt.GlobalColor.lightGray)), 0.0,
                                          Qt.PenStyle.SolidLine, Qt.PenCapStyle.SquareCap,
                                          Qt.PenJoinStyle.BevelJoin))
            painter.SetPen(polylinePen);
        painter.SetBrush(Qt.BrushStyle.NoBrush);
        using (var polygon = new QPolygonF(points.ToArray()))
            painter.DrawPolyline(polygon);
    }

    void InitializePoints()
    {
        const int count = 7;
        points.Clear();
        vectors.Clear();

        double centerX = width() / 2.0;
        double centerY = height() / 2.0;
        double rotation = 360.0 / count;

        using var m = new QTransform();
        using var vm = new QTransform();
        using (vm.Shear(2.0, -1.0)) { }
        using (vm.Scale(3.0, 3.0)) { }

        for (int i = 0; i < count; i++)
        {
            var baseVector = new QPointF(0.1, 0.25);
            var byM = m.Map(baseVector);
            var vector = vm.Map(byM);
                vectors.Add(new QPointF(vector.X(), vector.Y()));

            var basePoint = new QPointF(0.0, 100.0);
            var mapped = m.Map(basePoint);
                points.Add(new QPointF(mapped.X() + centerX, mapped.Y() + centerY));

            using (m.Rotate(rotation)) { }
        }
    }

    void UpdatePoints()
    {
        UpdatePointsCalls++;

        const double pad = 10;
        double left = pad;
        double right = width() - pad;
        double top = pad;
        double bottom = height() - pad;

        for (int i = 0; i < points.Count; i++)
        {
            var position = points[i];
            var vector = vectors[i];
            double x = position.X() + vector.X();
            double y = position.Y() + vector.Y();
            double vx = vector.X();
            double vy = vector.Y();

            if (x < left || x > right)
            {
                vx = -vx;
                x = x < left ? left : right;
            }
            if (y < top || y > bottom)
            {
                vy = -vy;
                y = y < top ? top : bottom;
            }

            points[i] = new QPointF(x, y);
            vectors[i] = new QPointF(vx, vy);
        }

        Update();
    }

    public override void TimerEvent(QTimerEvent e)
    {
        if (e.TimerId() == timer.TimerId())
            UpdatePoints();
    }

    public override void MousePressEvent(QMouseEvent e)
    {
        if (fingerPointMapping.Count > 0)
            return;

        ActivePoint = -1;
        double distance = -1;
        var position = e.Position();
        {
            for (int i = 0; i < points.Count; i++)
            {
                var line = new QLineF(position, points[i]);
                double d = line.Length();
                if ((distance < 0 && d < 8 * PointSize) || d < distance)
                {
                    distance = d;
                    ActivePoint = i;
                }
            }

            if (ActivePoint != -1)
            {
                WasAnimated = timer.IsActive();
                SetAnimation(false);
                // The official calls mouseMoveEvent() here, *before* recording the press
                // position, so on the first press it compares against a default-constructed
                // QPoint (0,0) - which arms the drag and pulls the point onto the cursor.
                MouseMoveEvent(e);
            }

            var press = position.ToPoint();
            {
                mousePressX = press.X();
                mousePressY = press.Y();
            }
        }

        // If we are not running in small-screen mode, always assume we are dragging.
        MouseDrag = !SmallScreen;
    }

    public override void MouseMoveEvent(QMouseEvent e)
    {
        if (fingerPointMapping.Count > 0)
            return;

        var position = e.Position();
        var current = position.ToPoint();

        // More than 25 pixels away means the user is dragging (QPoint's Manhattan length).
        if (!MouseDrag
            && Math.Abs(mousePressX - current.X()) + Math.Abs(mousePressY - current.Y()) > 25)
            MouseDrag = true;

        if (MouseDrag && ActivePoint >= 0 && ActivePoint < points.Count)
        {
            points[ActivePoint] = new QPointF(position.X(), position.Y());
            Update();
        }
    }

    public override void MouseReleaseEvent(QMouseEvent e)
    {
        if (fingerPointMapping.Count > 0)
            return;

        ActivePoint = -1;
        SetAnimation(WasAnimated);

        if (!MouseDrag && SmallScreen)
        {
            ClickedCount++;
            Clicked?.Invoke();
        }
    }

    public override bool Event(QEvent e)
    {
        var type = e.type();
        if (type == QEvent.Type.TouchBegin || type == QEvent.Type.TouchUpdate
            || type == QEvent.Type.TouchEnd)
        {
            // The event Qt hands a handler is really a QTouchEvent; the wrapper for it is
            // transient (QTouchEvent has no destructor path of its own), so it is not disposed.
            var touch = QTouchEvent.FromHandle(e.Handle);
            var touchPoints = touch.TouchPoints();
            var mapped = new TouchPoint[touchPoints.Length];
            try
            {
                for (int i = 0; i < touchPoints.Length; i++)
                {
                    var position = touchPoints[i].position();
                    mapped[i] = new TouchPoint(touchPoints[i].id(), touchPoints[i].state(),
                                               position.X(), position.Y());
                }
            }
            finally
            {
                foreach (var point in touchPoints)
                    point.Dispose();
            }
            return HandleTouchPoints(type, mapped);
        }

        return base.Event(e);
    }

    /// <summary>
    /// What the touch handler needs out of a QEventPoint. A plain value so the logic below can be
    /// driven from managed code: a QTouchEvent cannot be constructed without touch hardware, so
    /// Event() maps the real event's points into these and the self-test builds its own.
    /// </summary>
    public readonly struct TouchPoint
    {
        public TouchPoint(int id, QEventPoint.State state, double x, double y)
        {
            Id = id;
            State = state;
            X = x;
            Y = y;
        }

        public readonly int Id;
        public readonly QEventPoint.State State;
        public readonly double X;
        public readonly double Y;
    }

    /// <summary>
    /// The touch half of the official event() override: each finger grabs the nearest free control
    /// point and drags it. Returns whether the event was handled (the official returns false and
    /// calls ignore() when nothing is mapped).
    /// </summary>
    public bool HandleTouchPoints(QEvent.Type type, IReadOnlyList<TouchPoint> touchPoints)
    {
        if (type == QEvent.Type.TouchEnd)
        {
            if (fingerPointMapping.Count == 0)
                return false;
            fingerPointMapping.Clear();
            SetAnimation(WasAnimated);
            return true;
        }

        bool touchBegin = type == QEvent.Type.TouchBegin;

        foreach (var point in touchPoints)
        {
            int id = point.Id;

            switch (point.State)
            {
                case QEventPoint.State.Pressed:
                {
                    var taken = new HashSet<int>(fingerPointMapping.Values);
                    int activePoint = -1;
                    double distance = -1;
                    for (int i = 0; i < points.Count; i++)
                    {
                        if (taken.Contains(i))
                            continue;
                        var line = new QLineF(point.X, point.Y, points[i].X(), points[i].Y());
                        double d = line.Length();
                        if ((distance < 0 && d < 12 * PointSize) || d < distance)
                        {
                            distance = d;
                            activePoint = i;
                        }
                    }
                    if (activePoint != -1)
                    {
                        fingerPointMapping[id] = activePoint;
                        points[activePoint] = new QPointF(point.X, point.Y);
                    }
                    break;
                }

                case QEventPoint.State.Released:
                    if (fingerPointMapping.TryGetValue(id, out int released))
                    {
                        points[released] = new QPointF(point.X, point.Y);
                        fingerPointMapping.Remove(id);
                    }
                    break;

                case QEventPoint.State.Updated:
                    if (fingerPointMapping.TryGetValue(id, out int updated))
                    {
                        points[updated] = new QPointF(point.X, point.Y);
                    }
                    break;
            }
        }

        if (fingerPointMapping.Count == 0)
            return false;

        if (touchBegin)
        {
            WasAnimated = timer.IsActive();
            SetAnimation(false);
        }
        Update();
        return true;
    }
}

/// <summary>
/// The official PathStrokeControls: the four radio-button groups, the pen width slider and the
/// animate button. Only the desktop layout is ported (see the file header).
/// </summary>
public sealed class ApiPathStrokeControls : QWidget
{
    public readonly ApiPathStrokeRenderer Renderer;
    public readonly QGroupBox CapGroup;
    public readonly QGroupBox JoinGroup;
    public readonly QGroupBox StyleGroup;
    public readonly QGroupBox PathModeGroup;
    public readonly QSlider PenWidthSlider;
    public readonly QPushButton AnimatedButton;
    public readonly QRadioButton[] CapButtons = new QRadioButton[3];
    public readonly QRadioButton[] JoinButtons = new QRadioButton[4];
    public readonly QRadioButton[] StyleButtons = new QRadioButton[6];
    public readonly QRadioButton[] PathModeButtons = new QRadioButton[2];

    public int OkPressedCount;
    public int QuitPressedCount;

    public ApiPathStrokeControls(QWidget parent, ApiPathStrokeRenderer renderer) : base(parent)
    {
        Renderer = renderer;

        var mainGroup = new QGroupBox(this);
        mainGroup.SetFixedWidth(180);
        mainGroup.SetTitle("Path Stroking");

        CapGroup = BuildRadioGroup(mainGroup, "Cap Style",
            new[] { "Flat", "Square", "Round" }, CapButtons, new Action[]
            {
                renderer.SetFlatCap, renderer.SetSquareCap, renderer.SetRoundCap,
            });

        JoinGroup = BuildRadioGroup(mainGroup, "Join Style",
            new[] { "Bevel", "Miter", "SvgMiter", "Round" }, JoinButtons, new Action[]
            {
                renderer.SetBevelJoin, renderer.SetMiterJoin, renderer.SetSvgMiterJoin, renderer.SetRoundJoin,
            });

        // The official draws an icon preview of each pen style instead of shipping image files.
        var lineIconSize = new QSize(64, 16);
        StyleGroup = new QGroupBox(mainGroup);
        StyleGroup.SetTitle("Pen Style");
        StyleGroup.SetSizePolicy(QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Fixed);
        var styleNames = new[] { "Solid", "Dash", "Dot", "Dash Dot", "Dash Dot Dot", "Custom" };
        var styleSetters = new Action[]
        {
            renderer.SetSolidLine, renderer.SetDashLine, renderer.SetDotLine,
            renderer.SetDashDotLine, renderer.SetDashDotDotLine, renderer.SetCustomDashLine,
        };
        var styleGroupLayout = new QVBoxLayout(StyleGroup);
        for (int i = 0; i < StyleButtons.Length; i++)
        {
            var button = new QRadioButton(StyleGroup);
            if (i < 5)
            {
                button.SetIconSize(lineIconSize);
                using var icon = new QIcon(LineStyleIcon((Qt.PenStyle)i, lineIconSize));
                button.SetIcon(icon);
                button.SetToolTip(styleNames[i]);
            }
            else
            {
                button.SetText(styleNames[i]);
            }
            int index = i;
            button.ClickedSignal.Connect(_ => styleSetters[index]());
            styleGroupLayout.AddWidget(button);
            StyleButtons[i] = button;
        }
        for (int i = 0; i < 5; i++)
            StyleButtons[i].SetFixedHeight(StyleButtons[0].SizeHint().Height());

        PathModeGroup = BuildRadioGroup(mainGroup, "Line Style",
            new[] { "Curves", "Lines" }, PathModeButtons, new Action[]
            {
                renderer.SetCurveMode, renderer.SetLineMode,
            });

        var penWidthGroup = new QGroupBox(mainGroup);
        penWidthGroup.SetTitle("Pen Width");
        PenWidthSlider = new QSlider(Qt.Orientation.Horizontal, penWidthGroup);
        PenWidthSlider.SetSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed);
        PenWidthSlider.SetRange(0, 500);
        PenWidthSlider.ValueChangedSignal.Connect(width => renderer.SetPenWidth(width));
        var penWidthLayout = new QVBoxLayout(penWidthGroup);
        penWidthLayout.AddWidget(PenWidthSlider);

        AnimatedButton = new QPushButton(mainGroup);
        AnimatedButton.SetText("Animate");
        AnimatedButton.SetCheckable(true);
        AnimatedButton.ToggledSignal.Connect(animation => renderer.SetAnimation(animation));

        var mainLayout = new QVBoxLayout(this);
        mainLayout.SetContentsMargins(0, 0, 0, 0);
        mainLayout.AddWidget(mainGroup);

        var mainGroupLayout = new QVBoxLayout(mainGroup);
        mainGroupLayout.SetContentsMargins(3, 3, 3, 3);
        mainGroupLayout.AddWidget(CapGroup);
        mainGroupLayout.AddWidget(JoinGroup);
        mainGroupLayout.AddWidget(StyleGroup);
        mainGroupLayout.AddWidget(penWidthGroup);
        mainGroupLayout.AddWidget(PathModeGroup);
        mainGroupLayout.AddWidget(AnimatedButton);
        mainGroupLayout.AddStretch(1);

        // The official defaults.
        AnimatedButton.SetChecked(true);
        PenWidthSlider.SetValue(50);
        CapButtons[0].SetChecked(true);
        JoinButtons[0].SetChecked(true);
        PathModeButtons[0].SetChecked(true);
        StyleButtons[0].SetChecked(true);
    }

    static QGroupBox BuildRadioGroup(QWidget parent, string title, string[] labels,
                                     QRadioButton[] buttons, Action[] setters)
    {
        var group = new QGroupBox(parent);
        group.SetTitle(title);
        group.SetSizePolicy(QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Fixed);

        var layout = new QVBoxLayout(group);
        for (int i = 0; i < labels.Length; i++)
        {
            var button = new QRadioButton(group);
            button.SetText(labels[i]);
            int index = i;
            button.ClickedSignal.Connect(_ => setters[index]());
            layout.AddWidget(button);
            buttons[i] = button;
        }
        return group;
    }

    /// <summary>The official lineStyleIcon(): a 2px line drawn through a transparent pixmap.</summary>
    QPixmap LineStyleIcon(Qt.PenStyle style, QSize size)
    {
        var pixmap = new QPixmap(size);
        using (var painter = new QPainter(pixmap))
        {
            painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
            using (var transparent = new QColor(0, 0, 0, 0))
            {
                var area = new QRect(0, 0, size.Width(), size.Height());
                painter.FillRect(area, transparent);
            }
            using var currentPalette = palette();
            using var color = currentPalette.Color(QPalette.ColorRole.WindowText);
            using var brush = new QBrush(color);
            using var pen = new QPen(brush, 2.0, style, Qt.PenCapStyle.FlatCap, Qt.PenJoinStyle.BevelJoin);
            painter.SetPen(pen);
            int y = size.Height() / 2;
            painter.DrawLine(2, y, size.Width() - 2, y);
        }
        return pixmap;
    }

    public void EmitQuitSignal() => QuitPressedCount++;
    public void EmitOkSignal() => OkPressedCount++;
}

/// <summary>The official PathStrokeWidget: the renderer next to the controls.</summary>
public sealed class ApiPathStrokeWidget : QWidget
{
    public readonly ApiPathStrokeRenderer Renderer;
    public readonly ApiPathStrokeControls Controls;

    public ApiPathStrokeWidget(QWidget parent = null) : base(parent)
    {
        SetWindowTitle("Path Stroking");

        Renderer = new ApiPathStrokeRenderer(this);
        Controls = new ApiPathStrokeControls(null, Renderer);

        var layout = new QHBoxLayout(this);
        layout.AddWidget(Renderer);
        layout.AddWidget(Controls);
    }
}

[Demo("qtgui-pathstroke")]
public static class PathStroke
{
    static int CountInk(QImage image, Func<int, int, int, bool> match)
    {
        int count = 0;
        for (int y = 0; y < image.Height(); y++)
        {
            for (int x = 0; x < image.Width(); x++)
            {
                uint p = image.Pixel(x, y);
                if (match((int)((p >> 16) & 0xFF), (int)((p >> 8) & 0xFF), (int)(p & 0xFF)))
                    count++;
            }
        }
        return count;
    }

    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // The stroke is red; the frame's tile is white/grey; the control handles are a pale blue.
        static bool Red(int r, int g, int b) => r > 140 && g < 110 && b < 110;

        using var renderer = new ApiPathStrokeRenderer();
        renderer.Show();

        // --- the control points -------------------------------------------------------------
        using (var image = renderer.Grab().ToImage())
        {
            Check(renderer.PaintCount >= 1, $"the renderer painted (PaintCount {renderer.PaintCount})");
            Check(renderer.Points.Count == 7 && renderer.Vectors.Count == 7,
                $"initializePoints builds seven points and seven vectors "
                + $"({renderer.Points.Count}/{renderer.Vectors.Count})");

            double cx = renderer.width() / 2.0, cy = renderer.height() / 2.0;
            double minRadius = double.MaxValue, maxRadius = 0;
            foreach (var point in renderer.Points)
            {
                double radius = Math.Sqrt(Math.Pow(point.X() - cx, 2) + Math.Pow(point.Y() - cy, 2));
                minRadius = Math.Min(minRadius, radius);
                maxRadius = Math.Max(maxRadius, radius);
            }
            Check(Math.Abs(minRadius - 100) < 0.01 && Math.Abs(maxRadius - 100) < 0.01,
                $"the seven points sit on a circle of radius 100 around the centre "
                + $"(radius {minRadius:F2}..{maxRadius:F2})");

            // The pale control-point handles and the light grey polyline are painted too.
            Check(CountInk(image, (r, g, b) => r > 180 && b > 190 && g > 180) > 300,
                "the control-point handles are painted");
        }

        // initializePoints only runs while the point list is empty.
        renderer.Repaint();
        renderer.Repaint();
        Check(renderer.Points.Count == 7, "initializePoints is not run twice");

        // --- the animation ------------------------------------------------------------------
        var first = new QPointF(renderer.Points[0].X(), renderer.Points[0].Y());
        Check(!renderer.TimerActive(), "the timer starts stopped");
        renderer.SetAnimation(true);
        Check(renderer.TimerActive() && renderer.TimerId() != 0,
            $"setAnimation(true) starts a single timer (TimerId {renderer.TimerId()})");

        int updatesBefore = renderer.UpdatePointsCalls;
        var loop = new QEventLoop();
        var loopTimer = new QTimer();
        loopTimer.SetSingleShot(true);
        loopTimer.TimeoutSignal.Connect(() => loop.Quit());
        loopTimer.Start(250);
        loop.Exec();
        loop.Dispose();
        loopTimer.Dispose();

        Check(renderer.UpdatePointsCalls > updatesBefore,
            $"the timer drives updatePoints through timerEvent "
            + $"({updatesBefore} -> {renderer.UpdatePointsCalls} calls)");
        Check(Math.Abs(renderer.Points[0].X() - first.X()) + Math.Abs(renderer.Points[0].Y() - first.Y()) > 0.001,
            "the animated point moved");

        // The bouncing keeps every point inside the padded widget.
        double pad = 10;
        bool inside = true;
        foreach (var point in renderer.Points)
            if (point.X() < pad - 0.01 || point.X() > renderer.width() - pad + 0.01
                || point.Y() < pad - 0.01 || point.Y() > renderer.height() - pad + 0.01)
                inside = false;
        Check(inside,
            $"updatePoints keeps every point inside the 10px padding "
            + $"({renderer.width()}x{renderer.height()} widget)");

        renderer.SetAnimation(false);
        Check(!renderer.TimerActive(), "setAnimation(false) stops the timer");

        // --- the pen surface ----------------------------------------------------------------
        // A fresh renderer: its seven control points are still the deterministic initial circle,
        // so the pixel counts below do not depend on how long the animation above ran.
        using var penArea = new ApiPathStrokeRenderer();
        penArea.Show();

        QImage Shot(double width, Qt.PenStyle style, Qt.PenCapStyle cap, Qt.PenJoinStyle join,
                    ApiPathStrokeRenderer.PathMode mode)
        {
            penArea.PenWidth = width;
            penArea.PenStyle = style;
            penArea.CapStyle = cap;
            penArea.JoinStyle = join;
            penArea.Mode = mode;
            penArea.Repaint();
            using var pixmap = penArea.Grab();
            return pixmap.ToImage();
        }

        static int Differences(QImage a, QImage b)
        {
            int different = 0;
            for (int y = 0; y < a.Height(); y++)
                for (int x = 0; x < a.Width(); x++)
                    if (a.Pixel(x, y) != b.Pixel(x, y))
                        different++;
            return different;
        }

        int Stroke(double width, Qt.PenStyle style, Qt.PenCapStyle cap, Qt.PenJoinStyle join,
                   ApiPathStrokeRenderer.PathMode mode)
        {
            using var image = Shot(width, style, cap, join, mode);
            return CountInk(image, Red);
        }

        int solid = Stroke(5.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.FlatCap,
                           Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
        Check(solid > 0, $"the solid pen strokes the path ({solid} red pixels)");

        int thin = Stroke(1.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.FlatCap,
                          Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
        Check(solid > thin * 2,
            $"a wider pen puts down more ink ({thin} at width 1 -> {solid} at width 5)");

        int dashed = Stroke(5.0, Qt.PenStyle.DashLine, Qt.PenCapStyle.FlatCap,
                            Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
        Check(dashed < solid && dashed > 0,
            $"a dashed pen leaves gaps ({solid} -> {dashed} red pixels)");

        int dots = Stroke(5.0, Qt.PenStyle.DotLine, Qt.PenCapStyle.FlatCap,
                          Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
        Check(dots < dashed,
            $"dots leave more gaps than dashes ({dashed} -> {dots} red pixels)");

        // The "Custom" style goes through QPainterPathStroker::createStroke + fillPath instead.
        int custom = Stroke(5.0, Qt.PenStyle.NoPen, Qt.PenCapStyle.FlatCap,
                            Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
        Check(custom > 0 && custom != solid,
            $"the custom dash pattern is stroked through QPainterPathStroker ({custom} red pixels)");

        // Cap and join styles really change the stroke. The joins are compared by the pixels they
        // differ in rather than by an ink count: a bevel cuts a corner where a miter extends it,
        // and the two areas nearly cancel out.
        {
            int flatCap = Stroke(15.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.FlatCap,
                                 Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
            int roundCap = Stroke(15.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap,
                                  Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
            int squareCap = Stroke(15.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.SquareCap,
                                   Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
            Check(roundCap > flatCap && squareCap > flatCap,
                $"round and square caps extend past the flat one ({flatCap}/{squareCap}/{roundCap})");

            using var bevel = Shot(15.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.FlatCap,
                                   Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.LineMode);
            using var miter = Shot(15.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.FlatCap,
                                   Qt.PenJoinStyle.MiterJoin, ApiPathStrokeRenderer.PathMode.LineMode);
            using var round = Shot(15.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.FlatCap,
                                   Qt.PenJoinStyle.RoundJoin, ApiPathStrokeRenderer.PathMode.LineMode);
            int bevelMiter = Differences(bevel, miter);
            int bevelRound = Differences(bevel, round);
            int miterRound = Differences(miter, round);
            Check(bevelMiter > 50 && bevelRound > 50 && miterRound > 50,
                $"the join style changes the corners of the polyline "
                + $"(bevel/miter {bevelMiter}, bevel/round {bevelRound}, miter/round {miterRound} differing pixels)");
        }

        // CurveMode joins the points with cubics, LineMode with straight lines.
        int curves = Stroke(5.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.FlatCap,
                            Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.CurveMode);
        int lines = Stroke(5.0, Qt.PenStyle.SolidLine, Qt.PenCapStyle.FlatCap,
                           Qt.PenJoinStyle.BevelJoin, ApiPathStrokeRenderer.PathMode.LineMode);
        Check(lines != curves && lines > 0,
            $"LineMode draws straight segments instead of cubics ({curves} -> {lines} red pixels)");

        // --- the tiled ArthurFrame background -------------------------------------------------
        {
            renderer.PenStyle = Qt.PenStyle.SolidLine;
            renderer.PenWidth = 1.0;
            renderer.Repaint();
            using var image = renderer.Grab().ToImage();
            // The tile is 128x128 with 64x64 quadrants of white and of (230,230,230), so a scan
            // finds both - while the rounded corner is not tiled at all and shows the widget's own
            // background colour instead.
            int light = CountInk(image, (r, g, b) => r == 230 && g == 230 && b == 230);
            uint corner = image.Pixel(0, 0);
            Check(light > 1000,
                $"the ArthurFrame tile is drawn behind the path ({light} tile pixels at (230,230,230))");
            Check(corner != 0xFFFFFFFFu && corner != 0xFFE6E6E6u,
                $"the rounded corner is clipped out of the tiled background (corner 0x{corner:X8})");
        }

        // --- the mouse ------------------------------------------------------------------------
        {
            using var area = new ApiPathStrokeRenderer();
            area.Show();
            area.Resize(400, 400);
            // Grab rather than repaint: a widget that has just been shown is not mapped yet, and
            // paintEvent is what builds the control points.
            using (var forced = area.Grab()) { }
            area.SetAnimation(true);

            var target = new QPointF(area.Points[3].X(), area.Points[3].Y());
            int pressX = (int)target.X();
            int pressY = (int)target.Y();
            Check(area.ActivePoint == -1, "no control point is active before a press");

            // A managed widget cannot construct a QMouseEvent, so the drag is driven through the
            // runtime's press/move/release probes - Qt delivers a real event each time.
            ApiRuntime.SendMousePress(area.Handle, pressX, pressY);
            Check(area.ActivePoint == 3,
                $"a press near a control point selects it (ActivePoint {area.ActivePoint})");
            Check(!area.TimerActive(), "selecting a point stops the animation while dragging");
            Check(area.MouseDrag, "a desktop press always arms the drag");
            // The official calls mouseMoveEvent() from mousePressEvent() while m_mouseDrag is
            // still false, but the press position is compared against a default QPoint(0,0) -
            // far more than 25 pixels away - so the point lands on the cursor immediately.
            Check(Math.Abs(area.Points[3].X() - pressX) < 0.01
                  && Math.Abs(area.Points[3].Y() - pressY) < 0.01,
                $"the press pulls the control point onto the cursor "
                + $"({area.Points[3].X():F0},{area.Points[3].Y():F0})");

            ApiRuntime.SendMouseMove(area.Handle, pressX + 40, pressY + 20);
            Check(Math.Abs(area.Points[3].X() - (pressX + 40)) < 0.01
                  && Math.Abs(area.Points[3].Y() - (pressY + 20)) < 0.01,
                $"dragging moves the selected point to {area.Points[3].X():F0},{area.Points[3].Y():F0}");

            // A second press-and-move more than 25 pixels away is what arms the drag in the
            // official code; moving without a press must not touch the points.
            var untouched = new QPointF(area.Points[5].X(), area.Points[5].Y());
            ApiRuntime.SendMouseRelease(area.Handle, pressX + 40, pressY + 20);
            Check(area.ActivePoint == -1, "the release clears the active point");
            Check(area.TimerActive(), "the release restores the animation");
            area.SetAnimation(false);

            ApiRuntime.SendMouseMove(area.Handle, 5, 5);
            Check(Math.Abs(area.Points[5].X() - untouched.X()) < 0.01
                  && Math.Abs(area.Points[5].Y() - untouched.Y()) < 0.01,
                "a move without an active point leaves the points alone");

        }

        // --- the touch half of event() --------------------------------------------------------
        {
            using var area = new ApiPathStrokeRenderer();
            area.Show();
            area.Resize(400, 400);
            using (var forced = area.Grab()) { }
            area.SetAnimation(true);

            double x = area.Points[2].X(), y = area.Points[2].Y();
            var begin = new ApiPathStrokeRenderer.TouchPoint(7, QEventPoint.State.Pressed, x, y);
            Check(area.HandleTouchPoints(QEvent.Type.TouchBegin, new[] { begin }),
                "a touch press that lands on a control point is handled");
            Check(area.MappedFingerCount() == 1, "the finger is mapped to a control point");
            Check(!area.TimerActive(), "the first touch press pauses the animation");

            double x2 = x + 30, y2 = y + 30;
            var moved2 = new ApiPathStrokeRenderer.TouchPoint(7, QEventPoint.State.Updated, x2, y2);
            area.HandleTouchPoints(QEvent.Type.TouchUpdate, new[] { moved2 });
            Check(Math.Abs(area.Points[2].X() - x2) < 0.01 && Math.Abs(area.Points[2].Y() - y2) < 0.01,
                $"the touch update drags the point to {area.Points[2].X():F0},{area.Points[2].Y():F0}");

            // A second finger takes the next free point, not the one already held.
            double x3 = area.Points[6].X(), y3 = area.Points[6].Y();
            var second = new ApiPathStrokeRenderer.TouchPoint(8, QEventPoint.State.Pressed, x3, y3);
            area.HandleTouchPoints(QEvent.Type.TouchUpdate, new[] { second });
            Check(area.MappedFingerCount() == 2,
                $"a second finger grabs another point ({area.MappedFingerCount()} mapped)");

            var released = new ApiPathStrokeRenderer.TouchPoint(7, QEventPoint.State.Released, x2, y2);
            var released2 = new ApiPathStrokeRenderer.TouchPoint(8, QEventPoint.State.Released, x3, y3);
            Check(area.HandleTouchPoints(QEvent.Type.TouchUpdate, new[] { released }),
                "a touch release is handled while points are still held");
            Check(area.MappedFingerCount() == 1 && area.TimerActive() == false,
                $"the animation stays paused while a finger remains ({area.MappedFingerCount()} mapped)");
            Check(area.HandleTouchPoints(QEvent.Type.TouchEnd, new[] { released2 }),
                "the touch end is handled while a finger is mapped");
            Check(area.MappedFingerCount() == 0, "the finger mapping is cleared");
            Check(area.TimerActive(), "releasing the last finger restores the animation");
            area.SetAnimation(false);

            var unrelated = new ApiPathStrokeRenderer.TouchPoint(9, QEventPoint.State.Pressed, 1, 1);
            Check(!area.HandleTouchPoints(QEvent.Type.TouchUpdate, new[] { unrelated }),
                "a touch that lands nowhere is ignored (the official e->ignore())");
            Check(!area.HandleTouchPoints(QEvent.Type.TouchEnd, Array.Empty<ApiPathStrokeRenderer.TouchPoint>()),
                "a touch end with nothing mapped is ignored");
        }

        // --- the controls ---------------------------------------------------------------------
        {
            using var widget = new ApiPathStrokeWidget();
            widget.Show();
            QCoreApplication.ProcessEvents();

            var controls = widget.Controls;
            Check(controls.CapButtons.Length == 3 && controls.JoinButtons.Length == 4
                  && controls.StyleButtons.Length == 6 && controls.PathModeButtons.Length == 2,
                "the four radio-button groups are built "
                + $"({controls.CapGroup.title()}, {controls.JoinGroup.title()}, "
                + $"{controls.StyleGroup.title()}, {controls.PathModeGroup.title()})");
            Check(controls.AnimatedButton.IsChecked(), "the Animate button starts checked");
            Check(controls.PenWidthSlider.minimum() == 0 && controls.PenWidthSlider.maximum() == 500
                  && controls.PenWidthSlider.value() == 50,
                $"the pen width slider runs 0..500 and starts at 50 "
                + $"({controls.PenWidthSlider.value()})");
            Check(controls.CapButtons[0].IsChecked() && controls.JoinButtons[0].IsChecked()
                  && controls.PathModeButtons[0].IsChecked() && controls.StyleButtons[0].IsChecked(),
                "the official defaults are checked (flat cap, bevel join, curves, solid)");

            Check(widget.Renderer.TimerActive(), "the controls started the animation through the button");

            // The slider drives setPenWidth(int) -> penWidth/10.
            controls.PenWidthSlider.SetValue(120);
            Check(Math.Abs(widget.Renderer.PenWidth - 12.0) < 0.001,
                $"the pen width slider sets penWidth/10 (got {widget.Renderer.PenWidth})");

            // A radio button click runs the slot the official connects it to.
            controls.JoinButtons[3].ClickedSignal.Emit(true);
            Check(widget.Renderer.JoinStyle == Qt.PenJoinStyle.RoundJoin,
                $"the Round join radio button reaches the renderer ({widget.Renderer.JoinStyle})");

            controls.StyleButtons[5].ClickedSignal.Emit(true);
            Check(widget.Renderer.PenStyle == Qt.PenStyle.NoPen,
                $"the Custom style radio button selects the stroker path ({widget.Renderer.PenStyle})");

            controls.PathModeButtons[1].ClickedSignal.Emit(true);
            Check(widget.Renderer.Mode == ApiPathStrokeRenderer.PathMode.LineMode,
                "the Lines radio button switches the path mode");

            controls.CapButtons[2].ClickedSignal.Emit(true);
            Check(widget.Renderer.CapStyle == Qt.PenCapStyle.RoundCap,
                "the Round cap radio button reaches the renderer");

            // The Animate button toggles the timer.
            controls.AnimatedButton.SetChecked(false);
            Check(!widget.Renderer.TimerActive(), "unchecking Animate stops the timer");
            controls.AnimatedButton.SetChecked(true);
            Check(widget.Renderer.TimerActive(), "checking Animate starts it again");
            widget.Renderer.SetAnimation(false);

            Check(controls.PenWidthSlider.width() > 0 && widget.Renderer.width() > 0,
                $"the layout sized the renderer and the controls "
                + $"({widget.Renderer.width()}x{widget.Renderer.height()} and "
                + $"{controls.PenWidthSlider.width()}px slider)");
        }

        // The window is left on screen for a moment if asked, but nothing needs the event loop.
        if (int.TryParse(Environment.GetEnvironmentVariable("QTSHARP_DEMO_MS"), out var ms) && ms > 0)
        {
            var stop = new QTimer();
            stop.SetSingleShot(true);
            stop.TimeoutSignal.Connect(() => app.Quit());
            stop.Start(ms);
            app.Exec();
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
