using System;
using System.Collections.Generic;

// Port of examples/widgets/painting/painterpaths on the new pipeline.
//
// The example is the QPainterPath tour: nine paths built by hand (rect, round rect, ellipse, pie,
// polygon, a two-subpath "group", text outlines, a cubic bezier and a star), each drawn by its own
// RenderArea that scales the path into the widget, rotates it, strokes it with a pen and fills it
// with a QLinearGradient.
//
// Faithful to the official source with one mechanical difference: QPainterPath has no bound copy
// constructor, so `RenderArea(const QPainterPath &path)` is expressed as "append into an empty
// path", which reproduces the geometry and the fill rule.
public sealed class ApiPathRenderArea : QWidget
{
    public readonly QPainterPath Path;
    public int PaintCount;

    public int PenWidth = 1;
    public int RotationAngle;
    public QColor PenColor;
    public QColor FillColor1;
    public QColor FillColor2;

    public ApiPathRenderArea(QPainterPath path, QWidget parent = null) : base(parent)
    {
        Path = new QPainterPath();
        Path.AddPath(path);

        PenColor = new QColor(Qt.GlobalColor.black);
        FillColor1 = new QColor(255, 255, 255);
        FillColor2 = new QColor(255, 255, 255);

        // The official sets the background role; auto-filling it is what makes the widget render
        // the same off-screen as it does on screen.
        SetBackgroundRole(QPalette.ColorRole.Base);
        SetAutoFillBackground(true);

        Resize(120, 120);
    }

    // The official slots (minus the update() plumbing, which they also do). The colours are
    // copied, not aliased, so the caller may dispose the ones it passed - QColor is a value type.
    public void SetFillRule(Qt.FillRule rule) { Path.SetFillRule(rule); Update(); }
    public void SetFillGradient(QColor color1, QColor color2)
    {
        FillColor1 = Copy(color1);
        FillColor2 = Copy(color2);
        Update();
    }
    public void SetPenWidth(int width) { PenWidth = width; Update(); }
    public void SetPenColor(QColor color) { PenColor = Copy(color); Update(); }
    public void SetRotationAngle(int degrees) { RotationAngle = degrees; Update(); }

    public static QColor Copy(QColor color)
        => new QColor(color.Red(), color.Green(), color.Blue(), color.Alpha());

    public override QSize MinimumSizeHint() => new QSize(50, 50);
    public override QSize SizeHint() => new QSize(100, 100);

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;

        using var painter = new QPainter(AsQPaintDevice());
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);

        painter.Scale(width() / 100.0, height() / 100.0);
        painter.Translate(50.0, 50.0);
        painter.Rotate(-RotationAngle);
        painter.Translate(-50.0, -50.0);

        using (var brush = new QBrush(PenColor))
        using (var pen = new QPen(brush, PenWidth, Qt.PenStyle.SolidLine,
                                  Qt.PenCapStyle.RoundCap, Qt.PenJoinStyle.RoundJoin))
            painter.SetPen(pen);

        using (var gradient = new QLinearGradient(0, 0, 0, 100))
        {
            gradient.SetColorAt(0.0, FillColor1);
            gradient.SetColorAt(1.0, FillColor2);
            using var gradientBrush = new QBrush(gradient);
            painter.SetBrush(gradientBrush);
            painter.DrawPath(Path);
        }

        painter.End();
    }

    public QImage RenderToImage()
    {
        Repaint();
        using var rendered = Grab();
        return rendered.ToImage();
    }
}

// The official Window: nine render areas in a 3x3 grid plus the fill-rule / fill-gradient /
// pen / rotation controls.
public sealed class ApiPainterPathsWindow : QWidget
{
    public readonly ApiPathRenderArea[] Areas;
    public readonly QComboBox FillRuleComboBox;
    public readonly QComboBox FillColor1ComboBox;
    public readonly QComboBox FillColor2ComboBox;
    public readonly QComboBox PenColorComboBox;
    public readonly QSpinBox PenWidthSpinBox;
    public readonly QSpinBox RotationAngleSpinBox;

    // The colour of each combo box entry. The official keeps it in the item's QVariant data;
    // QVariant has no QColor conversion bound, so this parallel table stands in for it.
    readonly Dictionary<QComboBox, List<QColor>> colors = new();

    public ApiPainterPathsWindow(QWidget parent = null) : base(parent)
    {
        Areas = new ApiPathRenderArea[9];
        for (int i = 0; i < Areas.Length; i++)
            Areas[i] = new ApiPathRenderArea(BuildPath(i), this);

        FillRuleComboBox = new QComboBox(this);
        FillRuleComboBox.AddItems(new[] { "Odd Even", "Winding" });
        FillRuleComboBox.CurrentIndexChangedSignal.Connect(index =>
        {
            var rule = (Qt.FillRule)index;
            foreach (var area in Areas)
                area.SetFillRule(rule);
        });

        PopulateWithColors(FillColor1ComboBox = new QComboBox(this));
        PopulateWithColors(FillColor2ComboBox = new QComboBox(this));

        PenWidthSpinBox = new QSpinBox(this);
        PenWidthSpinBox.SetRange(0, 20);

        PopulateWithColors(PenColorComboBox = new QComboBox(this));

        RotationAngleSpinBox = new QSpinBox(this);
        RotationAngleSpinBox.SetRange(0, 359);
        RotationAngleSpinBox.SetWrapping(true);
        RotationAngleSpinBox.SetSuffix("\u00b0");

        // The official wiring: the combos drive the gradient/pen, the spin boxes drive the pen
        // width and the rotation.
        FillColor1ComboBox.CurrentIndexChangedSignal.Connect(_ => ApplyGradient());
        FillColor2ComboBox.CurrentIndexChangedSignal.Connect(_ => ApplyGradient());
        PenColorComboBox.CurrentIndexChangedSignal.Connect(_ => ApplyPenColor());
        PenWidthSpinBox.ValueChangedSignal.Connect(width =>
        {
            foreach (var area in Areas)
                area.SetPenWidth(width);
        });
        RotationAngleSpinBox.ValueChangedSignal.Connect(angle =>
        {
            foreach (var area in Areas)
                area.SetRotationAngle(angle);
        });

        // The official defaults.
        FillColor1ComboBox.SetCurrentIndex(FillColor1ComboBox.FindText("mediumslateblue"));
        FillColor2ComboBox.SetCurrentIndex(FillColor2ComboBox.FindText("cornsilk"));
        PenColorComboBox.SetCurrentIndex(PenColorComboBox.FindText("darkslateblue"));
        PenWidthSpinBox.SetValue(2);

        var topLayout = new QGridLayout();
        for (int i = 0; i < Areas.Length; i++)
            topLayout.AddWidget(Areas[i], i / 3, i % 3);

        var mainLayout = new QGridLayout();
        mainLayout.AddLayout(topLayout, 0, 0, 1, 4);
        mainLayout.AddWidget(new QLabel("Fill Rule:", this), 1, 0);
        mainLayout.AddWidget(FillRuleComboBox, 1, 1, 1, 3);
        mainLayout.AddWidget(new QLabel("Fill Gradient:", this), 2, 0);
        mainLayout.AddWidget(FillColor1ComboBox, 2, 1);
        mainLayout.AddWidget(new QLabel("to", this), 2, 2);
        mainLayout.AddWidget(FillColor2ComboBox, 2, 3);
        mainLayout.AddWidget(new QLabel("Pen Width:", this), 3, 0);
        mainLayout.AddWidget(PenWidthSpinBox, 3, 1, 1, 3);
        mainLayout.AddWidget(new QLabel("Pen Color:", this), 4, 0);
        mainLayout.AddWidget(PenColorComboBox, 4, 1, 1, 3);
        mainLayout.AddWidget(new QLabel("Rotation Angle:", this), 5, 0);
        mainLayout.AddWidget(RotationAngleSpinBox, 5, 1, 1, 3);
        SetLayout(mainLayout);

        SetWindowTitle("Painter Paths");
        Resize(640, 700);
    }

    /// <summary>The nine QPainterPath objects of the official example, verbatim.</summary>
    public static QPainterPath BuildPath(int index)
    {
        switch (index)
        {
            case 0:
            {
                var p = new QPainterPath();
                p.MoveTo(20.0, 30.0);
                p.LineTo(80.0, 30.0);
                p.LineTo(80.0, 70.0);
                p.LineTo(20.0, 70.0);
                p.CloseSubpath();
                return p;
            }
            case 1:
            {
                var p = new QPainterPath();
                p.MoveTo(80.0, 35.0);
                p.ArcTo(70.0, 30.0, 10.0, 10.0, 0.0, 90.0);
                p.LineTo(25.0, 30.0);
                p.ArcTo(20.0, 30.0, 10.0, 10.0, 90.0, 90.0);
                p.LineTo(20.0, 65.0);
                p.ArcTo(20.0, 60.0, 10.0, 10.0, 180.0, 90.0);
                p.LineTo(75.0, 70.0);
                p.ArcTo(70.0, 60.0, 10.0, 10.0, 270.0, 90.0);
                p.CloseSubpath();
                return p;
            }
            case 2:
            {
                var p = new QPainterPath();
                p.MoveTo(80.0, 50.0);
                p.ArcTo(20.0, 30.0, 60.0, 40.0, 0.0, 360.0);
                return p;
            }
            case 3:
            {
                var p = new QPainterPath();
                p.MoveTo(50.0, 50.0);
                p.ArcTo(20.0, 30.0, 60.0, 40.0, 60.0, 240.0);
                p.CloseSubpath();
                return p;
            }
            case 4:
            {
                var p = new QPainterPath();
                p.MoveTo(10.0, 80.0);
                p.LineTo(20.0, 10.0);
                p.LineTo(80.0, 30.0);
                p.LineTo(90.0, 70.0);
                p.CloseSubpath();
                return p;
            }
            case 5:
            {
                var p = new QPainterPath();
                p.MoveTo(60.0, 40.0);
                p.ArcTo(20.0, 20.0, 40.0, 40.0, 0.0, 360.0);
                p.MoveTo(40.0, 40.0);
                p.LineTo(40.0, 80.0);
                p.LineTo(80.0, 80.0);
                p.LineTo(80.0, 40.0);
                p.CloseSubpath();
                return p;
            }
            case 6:
            {
                var p = new QPainterPath();
                using var font = new QFont("Times", 50);
                // The official sets ForceOutline so the glyphs stay resolution-independent.
                font.SetStyleStrategy(QFont.StyleStrategy.ForceOutline);
                p.AddText(10.0, 70.0, font, "Qt");
                return p;
            }
            case 7:
            {
                var p = new QPainterPath();
                p.MoveTo(20.0, 30.0);
                p.CubicTo(80.0, 0.0, 50.0, 50.0, 80.0, 80.0);
                return p;
            }
            default:
            {
                var p = new QPainterPath();
                p.MoveTo(90.0, 50.0);
                for (int i = 1; i < 5; ++i)
                    p.LineTo(50.0 + 40.0 * Math.Cos(0.8 * i * Math.PI),
                              50.0 + 40.0 * Math.Sin(0.8 * i * Math.PI));
                p.CloseSubpath();
                return p;
            }
        }
    }

    void PopulateWithColors(QComboBox comboBox)
    {
        var list = new List<QColor>();
        colors[comboBox] = list;
        foreach (var name in QColor.ColorNames())
        {
            comboBox.AddItem(name);
            list.Add(new QColor(name));
        }
    }

    QColor ColorAt(QComboBox comboBox) => colors[comboBox][comboBox.currentIndex()];

    void ApplyGradient()
    {
        var color1 = ColorAt(FillColor1ComboBox);
        var color2 = ColorAt(FillColor2ComboBox);
        foreach (var area in Areas)
            area.SetFillGradient(color1, color2);
    }

    void ApplyPenColor()
    {
        var color = ColorAt(PenColorComboBox);
        foreach (var area in Areas)
            area.SetPenColor(color);
    }
}

[Demo("qtgui-painterpaths")]
public static class PainterPaths
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

        // "Ink" is any pixel that differs from the widget's (auto-filled) background at (1,1).
        (int Ink, int Total) InkOf(QImage image)
        {
            uint reference = image.Pixel(1, 1);
            int ink = 0;
            for (int y = 0; y < image.Height(); y++)
            {
                for (int x = 0; x < image.Width(); x++)
                {
                    uint p = image.Pixel(x, y);
                    if (p == reference)
                        continue;
                    int dr = Math.Abs((int)((p >> 16) & 0xFF) - (int)((reference >> 16) & 0xFF));
                    int dg = Math.Abs((int)((p >> 8) & 0xFF) - (int)((reference >> 8) & 0xFF));
                    int db = Math.Abs((int)(p & 0xFF) - (int)(reference & 0xFF));
                    if (dr > 24 || dg > 24 || db > 24)
                        ink++;
                }
            }
            return (ink, image.Width() * image.Height());
        }

        // The nine paths of the official example, each in its own render area. The source path is
        // disposed straight after the area is built: the area has copied it, and rendering below
        // proves the copy is a real one.
        string[] names = { "rect", "roundRect", "ellipse", "pie", "polygon", "group", "text", "bezier", "star" };
        var areas = new ApiPathRenderArea[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var path = ApiPainterPathsWindow.BuildPath(i);
            areas[i] = new ApiPathRenderArea(path);
            areas[i].Show();
            path.Dispose();
        }

        for (int i = 0; i < areas.Length; i++)
        {
            using var image = areas[i].RenderToImage();
            var ink = InkOf(image);
            // The text path is outline glyphs and is comparatively thin, so the bar is a small
            // fraction of the widget rather than an absolute count.
            Check(ink.Ink > 40,
                $"path {names[i]} renders into its area ({ink.Ink}/{ink.Total} pixels carry ink)");
        }

        // The paths themselves: bounding rectangles computed by Qt from the geometry.
        using (var rectPath = ApiPainterPathsWindow.BuildPath(0))
        {
            var bounds = rectPath.BoundingRect();
            Check(Math.Abs(bounds.Left() - 20.0) < 0.01 && Math.Abs(bounds.Top() - 30.0) < 0.01
                  && Math.Abs(bounds.Width() - 60.0) < 0.01 && Math.Abs(bounds.Height() - 40.0) < 0.01,
                $"the rectangle path's bounding rect is (20,30,60,40) (got "
                + $"({bounds.Left()},{bounds.Top()},{bounds.Width()},{bounds.Height()}))");
        }

        using (var ellipsePath = ApiPainterPathsWindow.BuildPath(2))
        {
            var bounds = ellipsePath.BoundingRect();
            Check(Math.Abs(bounds.Left() - 20.0) < 0.01 && Math.Abs(bounds.Top() - 30.0) < 0.01
                  && Math.Abs(bounds.Width() - 60.0) < 0.01 && Math.Abs(bounds.Height() - 40.0) < 0.01,
                $"the ellipse path's bounding rect is (20,30,60,40) (got "
                + $"({bounds.Left()},{bounds.Top()},{bounds.Width()},{bounds.Height()}))");
        }

        // The virtual size hints.
        var hint = areas[0].SizeHint();
        var minimum = areas[0].MinimumSizeHint();
            Check(hint.Width() == 100 && hint.Height() == 100
                  && minimum.Width() == 50 && minimum.Height() == 50,
                $"sizeHint()/minimumSizeHint() report the official sizes "
                + $"({hint.Width()}x{hint.Height()} / {minimum.Width()}x{minimum.Height()})");

        // Fill rules: setFillRule reaches the path, and winding vs odd-even change what is filled.
        // The star path is the one that shows it - it is a pentagram, so its centre is crossed
        // twice and only WindingFill paints it (that is what the combo box in the example is for).
        areas[8].SetFillRule(Qt.FillRule.OddEvenFill);
        Check(areas[8].Path.FillRule() == Qt.FillRule.OddEvenFill,
            $"setFillRule reached the path (got {areas[8].Path.FillRule()})");

        // QPainterPath::contains() answers "is this point inside?" under the path's own fill rule,
        // so the pentagram's twice-crossed centre is the direct way to see the rule take effect.
        var centre = new QPointF(50.0, 50.0);
        var outside = new QPointF(3.0, 3.0);
        {
            using var oddEvenPath = ApiPainterPathsWindow.BuildPath(8);
            oddEvenPath.SetFillRule(Qt.FillRule.OddEvenFill);
            bool oddEvenCentre = oddEvenPath.Contains(centre);
            bool oddEvenOutside = oddEvenPath.Contains(outside);

            using var windingPath = ApiPainterPathsWindow.BuildPath(8);
            windingPath.SetFillRule(Qt.FillRule.WindingFill);
            bool windingCentre = windingPath.Contains(centre);

            Check(!oddEvenCentre && windingCentre,
                $"the star's centre is crossed twice, so only WindingFill contains it "
                + $"(oddEven={oddEvenCentre}, winding={windingCentre})");
            Check(!oddEvenOutside,
                $"a point outside the star is outside under OddEvenFill too (got {oddEvenOutside})");
        }

        using var windingArea = new ApiPathRenderArea(ApiPainterPathsWindow.BuildPath(8));
        windingArea.Resize(120, 120);
        windingArea.Show();
        windingArea.SetFillRule(Qt.FillRule.WindingFill);
        Check(windingArea.Path.FillRule() == Qt.FillRule.WindingFill,
            $"the winding fill rule round-trips (got {windingArea.Path.FillRule()})");

        // Pen width: the slider of the example, 0..20.
        using var widthArea = new ApiPathRenderArea(ApiPainterPathsWindow.BuildPath(0));
        widthArea.Resize(120, 120);
        widthArea.Show();
        widthArea.SetPenWidth(0);
        int thinInk;
        using (var image = widthArea.RenderToImage())
            thinInk = InkOf(image).Ink;
        widthArea.SetPenWidth(9);
        int thickInk;
        using (var image = widthArea.RenderToImage())
            thickInk = InkOf(image).Ink;
        Check(widthArea.PenWidth == 9 && thickInk > thinInk,
            $"the pen width reaches the pen ({thinInk} ink pixels at width 0, {thickInk} at width 9)");

        // Rotation: the same area, turned by 45 degrees.
        using var rotationArea = new ApiPathRenderArea(ApiPainterPathsWindow.BuildPath(4));
        rotationArea.Resize(120, 120);
        rotationArea.Show();
        byte[] before = null;
        rotationArea.SetRotationAngle(0);
        using (var image = rotationArea.RenderToImage())
            before = Row(image, 45);
        rotationArea.SetRotationAngle(45);
        Check(rotationArea.RotationAngle == 45,
            $"the rotation angle is stored (got {rotationArea.RotationAngle})");
        byte[] after = null;
        using (var image = rotationArea.RenderToImage())
            after = Row(image, 45);
        Check(!Same(before, after),
            "rotating by 45 degrees changes the rendered scanline");

        // The fill gradient: the two colours are painted at the top and the bottom of the widget.
        using var gradientArea = new ApiPathRenderArea(ApiPainterPathsWindow.BuildPath(0));
        gradientArea.Resize(120, 120);
        gradientArea.Show();
        using (var color1 = new QColor(Qt.GlobalColor.red))
        using (var color2 = new QColor(Qt.GlobalColor.blue))
            gradientArea.SetFillGradient(color1, color2);
        using (var image = gradientArea.RenderToImage())
        {
            // The painter scales the 100-unit path space into the 120px widget, so logical y=40
            // and y=66 land at device y=48 and y=79 - both inside the rectangle path (y 36..84).
            uint top = image.Pixel(image.Width() / 2, (int)Math.Round(48.0 * image.Height() / 120.0));
            uint bottom = image.Pixel(image.Width() / 2, (int)Math.Round(79.0 * image.Height() / 120.0));
            Check(((top >> 16) & 0xFF) > ((bottom >> 16) & 0xFF)
                  && (bottom & 0xFF) > (top & 0xFF),
                $"the fill gradient runs from the first colour to the second "
                + $"(top 0x{top:X8}, bottom 0x{bottom:X8})");
        }

        // The window: nine areas, the official controls and their ranges.
        using var window = new ApiPainterPathsWindow();
        window.Show();

        Check(window.Areas.Length == 9, $"the window has nine render areas (got {window.Areas.Length})");
        Check(window.FillRuleComboBox.count() == 2,
            $"the fill rule combo box has Odd Even and Winding (got {window.FillRuleComboBox.count()})");
        foreach (var combo in new[] { window.FillColor1ComboBox, window.FillColor2ComboBox, window.PenColorComboBox })
            Check(combo.count() > 100, $"a colour combo box is filled from QColor::colorNames() (got {combo.count()})");
        Check(window.FillColor1ComboBox.currentText() == "mediumslateblue"
              && window.FillColor2ComboBox.currentText() == "cornsilk"
              && window.PenColorComboBox.currentText() == "darkslateblue",
            $"the official default colours were selected "
            + $"({window.FillColor1ComboBox.currentText()}/{window.FillColor2ComboBox.currentText()}/"
            + $"{window.PenColorComboBox.currentText()})");
        Check(window.PenWidthSpinBox.minimum() == 0 && window.PenWidthSpinBox.maximum() == 20
              && window.PenWidthSpinBox.value() == 2,
            $"the pen width spin box has the official range and default "
            + $"({window.PenWidthSpinBox.minimum()}..{window.PenWidthSpinBox.maximum()}, value {window.PenWidthSpinBox.value()})");
        Check(window.RotationAngleSpinBox.minimum() == 0 && window.RotationAngleSpinBox.maximum() == 359
              && window.RotationAngleSpinBox.Wrapping,
            $"the rotation spin box wraps 0..359 "
            + $"({window.RotationAngleSpinBox.minimum()}..{window.RotationAngleSpinBox.maximum()}, "
            + $"wrapping={window.RotationAngleSpinBox.Wrapping})");
        Check(window.RotationAngleSpinBox.suffix() == "\u00b0",
            $"the rotation spin box carries the degree suffix (got '{window.RotationAngleSpinBox.suffix()}')");

        // Driving a control reaches every area, the way the official connect()s do.
        window.PenWidthSpinBox.SetValue(7);
        bool allAreas = true;
        foreach (var area in window.Areas)
            allAreas &= area.PenWidth == 7;
        Check(allAreas, "the pen width spin box drives all nine render areas");

        window.RotationAngleSpinBox.SetValue(30);
        allAreas = true;
        foreach (var area in window.Areas)
            allAreas &= area.RotationAngle == 30;
        Check(allAreas, "the rotation spin box drives all nine render areas");

        window.FillRuleComboBox.SetCurrentIndex((int)Qt.FillRule.WindingFill);
        allAreas = window.Areas[0].Path.FillRule() == Qt.FillRule.WindingFill
                   && window.Areas[8].Path.FillRule() == Qt.FillRule.WindingFill;
        Check(allAreas, "the fill rule combo box drives all nine paths");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>One scanline, given in the painter's 100-unit path space.</summary>
    static byte[] Row(QImage image, int logicalY)
    {
        int y = Math.Min(Math.Max((int)Math.Round(logicalY * (double)image.Height() / 100.0), 0), image.Height() - 1);
        var row = new byte[image.Width() * 4];
        for (int x = 0; x < image.Width(); x++)
        {
            uint p = image.Pixel(x, y);
            row[x * 4 + 0] = (byte)(p & 0xFF);
            row[x * 4 + 1] = (byte)((p >> 8) & 0xFF);
            row[x * 4 + 2] = (byte)((p >> 16) & 0xFF);
            row[x * 4 + 3] = (byte)((p >> 24) & 0xFF);
        }
        return row;
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }
}
