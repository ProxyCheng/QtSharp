using System;

// Port of examples/widgets/painting/basicdrawing on the new pipeline.
//
// The example exists to put the whole QPainter surface on screen at once: thirteen shapes, pen
// width/style/cap/join, brush styles, antialiasing and a composed translation/rotation/scale. The
// port keeps the RenderArea and the control window (so the signals that drive them are exercised
// too) and asserts on the *rendered pixels* rather than on a human looking at the window: the
// widget is painted into a QImage and the ink is counted.
//
// Two deliberate differences from the official source, both for the sake of a deterministic test:
//   * paintEvent first fills the widget with a known light grey, so "background" is a colour this
//     test can name instead of whatever the style happened to draw;
//   * the pixmap is filled with a colour and given an ellipse. The official one is created with
//     `fill(Qt::transparent)` and never painted into, so its Pixmap case renders nothing at all.
public sealed class ApiRenderArea : QWidget
{
    // The official enum, in the official order.
    public enum ShapeKind
    {
        Polygon, Rect, RoundedRect, Ellipse, Arc, Chord, Pie, Path, Text, Pixmap, Line, Points, Polyline,
    }

    public ShapeKind Shape = ShapeKind.Polygon;
    public bool Antialiased;
    public bool Transformed;
    public int PaintCount;

    public readonly QPen Pen;
    public readonly QBrush Brush;

    readonly QPixmap pixmap;

    public ApiRenderArea(QWidget parent = null) : base(parent)
    {
        var black = new QColor(Qt.GlobalColor.black);
            Pen = new QPen(black);
        Pen.SetWidth(1);
        Pen.SetStyle(Qt.PenStyle.SolidLine);
        Pen.SetCapStyle(Qt.PenCapStyle.RoundCap);
        Pen.SetJoinStyle(Qt.PenJoinStyle.RoundJoin);

        var white = new QColor(Qt.GlobalColor.white);
            Brush = new QBrush(white);

        using (var image = new QImage(70, 70, QImage.Format.Format_ARGB32))
        {
            var blue = new QColor(0, 90, 200);
                image.Fill(blue);
            using (var p = new QPainter(image))
            {
                p.SetPen(Qt.PenStyle.NoPen);
                 var orange = new QColor(230, 120, 0);
                p.SetBrush(orange);
                p.DrawEllipse(10, 10, 50, 50);
                p.End();
            }
            pixmap = QPixmap.FromImage(image);
        }

        Resize(400, 200);
    }

    /// <summary>The official setters are slots; these are the same operations.</summary>
    public void SetShape(ShapeKind shape) { Shape = shape; Update(); }
    public void SetAntialiased(bool on) { Antialiased = on; Update(); }
    public void SetTransformed(bool on) { Transformed = on; Update(); }

    public override QSize sizeHint() => new QSize(400, 200);
    public override QSize minimumSizeHint() => new QSize(100, 100);

    // The four points the official example uses for Points/Polyline/Polygon.
    static QPolygon Poly(params (int X, int Y)[] points)
    {
        var raw = new QPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
            raw[i] = new QPoint(points[i].X, points[i].Y);
        var polygon = new QPolygon(raw);
        return polygon;
    }

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;

        using var painter = new QPainter(AsQPaintDevice());
        painter.SetPen(Pen);
        painter.SetBrush(Brush);
        if (Antialiased)
            painter.SetRenderHint(QPainter.RenderHint.Antialiasing, true);

        // A known background (the difference called out above).
        var full = new QRect(0, 0, width(), height());
            painter.FillRect(full, Qt.GlobalColor.lightGray);

        var rect = new QRect(10, 20, 80, 60);

        if (Transformed)
        {
            // `painter.translate(50, 50); painter.rotate(60); painter.scale(0.6, 0.9);
            //  painter.translate(-50, -50);`
            painter.Translate(50.0, 50.0);
            painter.Rotate(60.0);
            painter.Scale(0.6, 0.9);
            painter.Translate(-50.0, -50.0);
        }

        switch (Shape)
        {
            case ShapeKind.Line:
                var topLeft = rect.TopLeft();
                var bottomRight = rect.BottomRight();
                    painter.DrawLine(topLeft, bottomRight);
                break;

            case ShapeKind.Points:
                using (var points = Poly((10, 80), (20, 10), (80, 30), (90, 70)))
                    painter.DrawPoints(points);
                break;

            case ShapeKind.Polyline:
                using (var polyline = Poly((10, 80), (20, 10), (80, 30), (90, 70)))
                    painter.DrawPolyline(polyline);
                break;

            case ShapeKind.Polygon:
                using (var polygon = Poly((10, 80), (20, 10), (80, 30), (90, 70)))
                    painter.DrawPolygon(polygon);
                break;

            case ShapeKind.Rect:
                painter.DrawRect(rect);
                break;

            case ShapeKind.RoundedRect:
                painter.DrawRoundedRect(rect, 25.0, 25.0, Qt.SizeMode.RelativeSize);
                break;

            case ShapeKind.Ellipse:
                painter.DrawEllipse(rect);
                break;

            case ShapeKind.Arc:
                painter.DrawArc(rect, 0, 120 * 16);
                break;

            case ShapeKind.Chord:
                painter.DrawChord(rect, 0, 120 * 16);
                break;

            case ShapeKind.Pie:
                painter.DrawPie(rect, 0, 120 * 16);
                break;

            case ShapeKind.Path:
                using (var path = new QPainterPath())
                {
                    path.MoveTo(20.0, 80.0);
                    path.LineTo(20.0, 30.0);
                    path.CubicTo(80.0, 0.0, 50.0, 50.0, 80.0, 80.0);
                    painter.DrawPath(path);
                }
                break;

            case ShapeKind.Text:
                painter.DrawText(rect, (int)Qt.Alignment.AlignCenter, "Qt by\nthe Qt Company");
                break;

            case ShapeKind.Pixmap:
                painter.DrawPixmap(10, 10, pixmap);
                break;
        }

        painter.End();
    }

    /// <summary>Forces a paint and hands back the rendered widget as a QImage.</summary>
    public QImage RenderToImage()
    {
        Repaint();
        using var rendered = Grab();
        return rendered.ToImage();
    }
}

// The control side of the official example: a shape combo box, a pen group (width/style/cap/join),
// a brush style group and the two option check boxes. The wiring is the official wiring, so the
// demo asserts that driving a control really changes what the render area paints.
public sealed class ApiBasicDrawingWindow : QWidget
{
    public readonly ApiRenderArea RenderArea;
    public readonly QComboBox ShapeComboBox;
    public readonly QSpinBox PenWidthSpinBox;
    public readonly QComboBox PenStyleComboBox;
    public readonly QComboBox PenCapComboBox;
    public readonly QComboBox PenJoinComboBox;
    public readonly QComboBox BrushStyleComboBox;
    public readonly QCheckBox AntialiasingCheckBox;
    public readonly QCheckBox TransformationsCheckBox;

    // Index -> shape, in the order the official combo box lists them.
    static readonly ApiRenderArea.ShapeKind[] Shapes =
    {
        ApiRenderArea.ShapeKind.Polygon,
        ApiRenderArea.ShapeKind.Rect,
        ApiRenderArea.ShapeKind.RoundedRect,
        ApiRenderArea.ShapeKind.Ellipse,
        ApiRenderArea.ShapeKind.Arc,
        ApiRenderArea.ShapeKind.Chord,
        ApiRenderArea.ShapeKind.Pie,
        ApiRenderArea.ShapeKind.Path,
        ApiRenderArea.ShapeKind.Text,
        ApiRenderArea.ShapeKind.Pixmap,
        ApiRenderArea.ShapeKind.Line,
        ApiRenderArea.ShapeKind.Points,
        ApiRenderArea.ShapeKind.Polyline,
    };

    static readonly Qt.PenStyle[] PenStyles =
    {
        Qt.PenStyle.SolidLine, Qt.PenStyle.DashLine, Qt.PenStyle.DotLine,
        Qt.PenStyle.DashDotLine, Qt.PenStyle.DashDotDotLine, Qt.PenStyle.CustomDashLine,
    };

    static readonly Qt.PenCapStyle[] PenCaps =
    {
        Qt.PenCapStyle.FlatCap, Qt.PenCapStyle.SquareCap, Qt.PenCapStyle.RoundCap,
    };

    static readonly Qt.PenJoinStyle[] PenJoins =
    {
        Qt.PenJoinStyle.MiterJoin, Qt.PenJoinStyle.BevelJoin, Qt.PenJoinStyle.RoundJoin,
    };

    static readonly Qt.BrushStyle[] BrushStyles =
    {
        Qt.BrushStyle.SolidPattern, Qt.BrushStyle.HorPattern, Qt.BrushStyle.VerPattern,
        Qt.BrushStyle.CrossPattern, Qt.BrushStyle.BDiagPattern, Qt.BrushStyle.FDiagPattern,
        Qt.BrushStyle.DiagCrossPattern, Qt.BrushStyle.Dense1Pattern, Qt.BrushStyle.Dense2Pattern,
        Qt.BrushStyle.Dense3Pattern, Qt.BrushStyle.Dense4Pattern, Qt.BrushStyle.Dense5Pattern,
        Qt.BrushStyle.Dense6Pattern, Qt.BrushStyle.Dense7Pattern, Qt.BrushStyle.NoBrush,
    };

    public ApiBasicDrawingWindow(QWidget parent = null) : base(parent)
    {
        RenderArea = new ApiRenderArea(this);

        ShapeComboBox = new QComboBox(this);
        ShapeComboBox.AddItems(new[]
        {
            "Polygon", "Rectangle", "Rounded Rectangle", "Ellipse", "Arc", "Chord", "Pie",
            "Path", "Text", "Pixmap", "Line", "Points", "Polyline",
        });
        ShapeComboBox.CurrentIndexChangedSignal.Connect(index =>
        {
            if (index >= 0 && index < Shapes.Length)
                RenderArea.SetShape(Shapes[index]);
        });

        PenWidthSpinBox = new QSpinBox(this);
        PenWidthSpinBox.SetRange(0, 20);
        PenWidthSpinBox.SetValue(1);
        PenWidthSpinBox.ValueChangedSignal.Connect(width =>
        {
            RenderArea.Pen.SetWidth(width);
            RenderArea.Update();
        });

        PenStyleComboBox = new QComboBox(this);
        foreach (var style in PenStyles)
            PenStyleComboBox.AddItem(style.ToString());
        PenStyleComboBox.CurrentIndexChangedSignal.Connect(index =>
        {
            if (index >= 0 && index < PenStyles.Length)
            {
                RenderArea.Pen.SetStyle(PenStyles[index]);
                RenderArea.Update();
            }
        });

        PenCapComboBox = new QComboBox(this);
        foreach (var cap in PenCaps)
            PenCapComboBox.AddItem(cap.ToString());
        PenCapComboBox.CurrentIndexChangedSignal.Connect(index =>
        {
            if (index >= 0 && index < PenCaps.Length)
            {
                RenderArea.Pen.SetCapStyle(PenCaps[index]);
                RenderArea.Update();
            }
        });

        PenJoinComboBox = new QComboBox(this);
        foreach (var join in PenJoins)
            PenJoinComboBox.AddItem(join.ToString());
        PenJoinComboBox.CurrentIndexChangedSignal.Connect(index =>
        {
            if (index >= 0 && index < PenJoins.Length)
            {
                RenderArea.Pen.SetJoinStyle(PenJoins[index]);
                RenderArea.Update();
            }
        });

        BrushStyleComboBox = new QComboBox(this);
        foreach (var style in BrushStyles)
            BrushStyleComboBox.AddItem(style.ToString());
        BrushStyleComboBox.CurrentIndexChangedSignal.Connect(index =>
        {
            if (index >= 0 && index < BrushStyles.Length)
            {
                RenderArea.Brush.SetStyle(BrushStyles[index]);
                RenderArea.Update();
            }
        });

        AntialiasingCheckBox = new QCheckBox("Antialiasing", this);
        AntialiasingCheckBox.ToggledSignal.Connect(on => RenderArea.SetAntialiased(on));

        TransformationsCheckBox = new QCheckBox("Transformations", this);
        TransformationsCheckBox.ToggledSignal.Connect(on => RenderArea.SetTransformed(on));

        var shapeGroup = new QGroupBox("Shape", this);
        var shapeLayout = new QVBoxLayout();
        shapeLayout.AddWidget(ShapeComboBox);
        shapeGroup.SetLayout(shapeLayout);

        var penGroup = new QGroupBox("Pen", this);
        var penLayout = new QGridLayout();
        penLayout.AddWidget(new QLabel("Width:", penGroup), 0, 0);
        penLayout.AddWidget(PenWidthSpinBox, 0, 1);
        penLayout.AddWidget(new QLabel("Style:", penGroup), 1, 0);
        penLayout.AddWidget(PenStyleComboBox, 1, 1);
        penLayout.AddWidget(new QLabel("Cap:", penGroup), 2, 0);
        penLayout.AddWidget(PenCapComboBox, 2, 1);
        penLayout.AddWidget(new QLabel("Join:", penGroup), 3, 0);
        penLayout.AddWidget(PenJoinComboBox, 3, 1);
        penGroup.SetLayout(penLayout);

        var brushGroup = new QGroupBox("Brush", this);
        var brushLayout = new QVBoxLayout();
        brushLayout.AddWidget(BrushStyleComboBox);
        brushGroup.SetLayout(brushLayout);

        var optionsGroup = new QGroupBox("Options", this);
        var optionsLayout = new QVBoxLayout();
        optionsLayout.AddWidget(AntialiasingCheckBox);
        optionsLayout.AddWidget(TransformationsCheckBox);
        optionsGroup.SetLayout(optionsLayout);

        var otherLayout = new QVBoxLayout();
        otherLayout.AddWidget(shapeGroup);
        otherLayout.AddWidget(penGroup);
        otherLayout.AddWidget(brushGroup);
        otherLayout.AddWidget(optionsGroup);
        otherLayout.AddStretch(1);

        var mainLayout = new QGridLayout();
        mainLayout.AddWidget(RenderArea, 0, 0, 1, 2);
        mainLayout.AddLayout(otherLayout, 1, 0);
        SetLayout(mainLayout);

        SetWindowTitle("Basic Drawing");
        Resize(700, 500);
    }
}

[Demo("qtgui-basicdrawing")]
public static class BasicDrawing
{
    public static int Run(DemoContext demo)
    {

        // Ink counters. The background painted by ApiRenderArea::paintEvent is light grey
        // (192,192,192); "dark" is the black pen, "white" the white brush, and everything else
        // (the pixmap's blue/orange, antialiased edges) lands in "other".
        (int Dark, int White, int Other) Scan(QImage image)
        {
            int dark = 0, white = 0, other = 0;
            for (int y = 0; y < image.Height(); y++)
            {
                for (int x = 0; x < image.Width(); x++)
                {
                    uint p = image.Pixel(x, y);
                    int r = (int)((p >> 16) & 0xFF);
                    int g = (int)((p >> 8) & 0xFF);
                    int b = (int)(p & 0xFF);
                    if (r > 240 && g > 240 && b > 240)
                        white++;
                    else if (r + g + b < 200)
                        dark++;
                    else if (Math.Abs(r - 192) > 25 || Math.Abs(g - 192) > 25 || Math.Abs(b - 192) > 25)
                        other++;
                }
            }
            return (dark, white, other);
        }

        using var area = new ApiRenderArea();
        #if QTSHARP_DEMO_SMOKE
            area.Show();
        #else
            demo.Show(area);
        #endif

        // Every shape of the official enum has to reach QPainter and leave ink behind.
        foreach (ApiRenderArea.ShapeKind shape in Enum.GetValues(typeof(ApiRenderArea.ShapeKind)))
        {
            area.SetShape(shape);
            using var image = area.RenderToImage();
            var ink = Scan(image);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(ink.Dark + ink.White + ink.Other > 0,
                $"shape {shape} paints something (dark={ink.Dark}, white={ink.White}, other={ink.Other})");
            #endif
        }

#if QTSHARP_DEMO_SMOKE

        demo.Check(area.PaintCount >= 13,
            $"each setShape() repainted ({area.PaintCount} paintEvent calls reached C#)");
#endif

        // The virtual overrides, read back through the generated accessors.
        var hint = area.SizeHint;
        var minimum = area.MinimumSizeHint;
            #if QTSHARP_DEMO_SMOKE
            demo.Check(hint.Width() == 400 && hint.Height() == 200
                  && minimum.Width() == 100 && minimum.Height() == 100,
                $"sizeHint()/minimumSizeHint() report the official sizes "
                + $"({hint.Width()}x{hint.Height()} / {minimum.Width()}x{minimum.Height()})");
            #endif

        // Pen width: an 8px line covers far more pixels than a 1px one.
        area.SetShape(ApiRenderArea.ShapeKind.Line);
        area.Pen.SetWidth(1);
        int thin;
        using (var image = area.RenderToImage())
            thin = Scan(image).Dark;
        area.Pen.SetWidth(8);
        int thick;
        using (var image = area.RenderToImage())
            thick = Scan(image).Dark;
        #if QTSHARP_DEMO_SMOKE
        demo.Check(thin > 0 && thick > thin,
            $"pen width reaches QPainter (1px={thin} dark pixels, 8px={thick})");
        #endif

        // Pen style: NoPen draws nothing at all.
        area.Pen.SetStyle(Qt.PenStyle.NoPen);
        using (var image = area.RenderToImage())
        {
            var ink = Scan(image);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(ink.Dark == 0 && ink.White == 0 && ink.Other == 0,
                $"PenStyle::NoPen leaves the widget untouched (dark={ink.Dark}, white={ink.White}, other={ink.Other})");
            #endif
        }

        // Pen style: a dashed line covers fewer pixels than a solid one of the same width.
        area.Pen.SetWidth(1);
        area.Pen.SetStyle(Qt.PenStyle.SolidLine);
        int solid;
        using (var image = area.RenderToImage())
            solid = Scan(image).Dark;
        area.Pen.SetStyle(Qt.PenStyle.DashLine);
        int dashed;
        using (var image = area.RenderToImage())
            dashed = Scan(image).Dark;
        #if QTSHARP_DEMO_SMOKE
        demo.Check(dashed > 0 && dashed < solid,
            $"pen style reaches QPainter (solid={solid} dark pixels, dashed={dashed})");
        #endif

        // Brush: the polygon is filled when the brush is solid and empty when it is NoBrush.
        area.Pen.SetStyle(Qt.PenStyle.SolidLine);
        area.SetShape(ApiRenderArea.ShapeKind.Polygon);
        area.Brush.SetStyle(Qt.BrushStyle.SolidPattern);
        int filled;
        using (var image = area.RenderToImage())
            filled = Scan(image).White;
        area.Brush.SetStyle(Qt.BrushStyle.NoBrush);
        int unfilled;
        using (var image = area.RenderToImage())
            unfilled = Scan(image).White;
        #if QTSHARP_DEMO_SMOKE
        demo.Check(filled > 0 && unfilled == 0,
            $"brush style reaches QPainter (solid={filled} white pixels, NoBrush={unfilled})");
        #endif

        // Antialiasing: the oblique line gets blended edge pixels that an aliased one does not have.
        area.Brush.SetStyle(Qt.BrushStyle.SolidPattern);
        area.SetShape(ApiRenderArea.ShapeKind.Line);
        area.SetAntialiased(false);
        int aliased;
        using (var image = area.RenderToImage())
            aliased = Scan(image).Other;
        area.SetAntialiased(true);
        int antialiased;
        using (var image = area.RenderToImage())
            antialiased = Scan(image).Other;
        #if QTSHARP_DEMO_SMOKE
        demo.Check(antialiased > 0 && aliased == 0,
            $"Antialiasing reaches QPainter (blended pixels: off={aliased}, on={antialiased})");
        #endif
        area.SetAntialiased(false);

        // The composed transformation moves the ink: the same shape renders somewhere else.
        area.SetShape(ApiRenderArea.ShapeKind.Rect);
        area.SetTransformed(false);
        int beforeInk;
        using (var before = area.RenderToImage())
            beforeInk = Scan(before).White;
        area.SetTransformed(true);
        int afterInk;
        using (var transformed = area.RenderToImage())
            afterInk = Scan(transformed).White;
        #if QTSHARP_DEMO_SMOKE
        demo.Check(beforeInk != afterInk,
            $"the translate/rotate/scale composition changes the render ({beforeInk} -> {afterInk} white pixels)");
        #endif
        area.SetTransformed(false);

        // Rounded rectangle: the corner is cut away. Rect fills it, RoundedRect (with a 25%
        // relative radius) leaves the background showing at (11,21) - just inside the rectangle's
        // top-left corner and comfortably outside the rounded corner's ellipse.
        uint CornerPixel(QImage image)
        {
            int x = (int)Math.Round(11.0 * image.Width() / 400.0);
            int y = (int)Math.Round(21.0 * image.Height() / 200.0);
            return image.Pixel(Math.Min(x, image.Width() - 1), Math.Min(y, image.Height() - 1));
        }
        #if QTSHARP_DEMO_SMOKE
        bool IsWhite(uint p) => ((p >> 16) & 0xFF) > 240 && ((p >> 8) & 0xFF) > 240 && (p & 0xFF) > 240;
        #endif

        area.SetShape(ApiRenderArea.ShapeKind.Rect);
        int rectInk;
        uint rectCorner;
        using (var image = area.RenderToImage())
        {
            rectInk = Scan(image).White;
            rectCorner = CornerPixel(image);
        }
        area.SetShape(ApiRenderArea.ShapeKind.RoundedRect);
        int roundedInk;
        uint roundedCorner;
        using (var image = area.RenderToImage())
        {
            roundedInk = Scan(image).White;
            roundedCorner = CornerPixel(image);
        }
        #if QTSHARP_DEMO_SMOKE
        demo.Check(IsWhite(rectCorner) && !IsWhite(roundedCorner),
            $"the rounded rect cuts its corner away (rect 0x{rectCorner:X8}, rounded 0x{roundedCorner:X8})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(roundedInk < rectInk,
            $"the rounded rect fills less of the widget than the plain one ({roundedInk} < {rectInk} white pixels)");
        #endif

        // Text and the pixmap render their own kinds of ink.
        area.SetShape(ApiRenderArea.ShapeKind.Text);
        #if QTSHARP_DEMO_SMOKE
        using (var image = area.RenderToImage())
            demo.Check(Scan(image).Dark > 0, "drawText() puts glyphs on the widget");
        #endif

        area.SetShape(ApiRenderArea.ShapeKind.Pixmap);
        #if QTSHARP_DEMO_SMOKE
        using (var image = area.RenderToImage())
            demo.Check(Scan(image).Other > 0, "drawPixmap() puts the pixmap on the widget");
        #endif

        // The control window: the official groups and combo boxes exist, and driving a control
        // really reaches the render area (the signal -> slot wiring of the example).
        using var window = new ApiBasicDrawingWindow();
        #if QTSHARP_DEMO_SMOKE
            window.Show();
        #else
            demo.Show(window);
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.ShapeComboBox.count() == 13,
            $"the shape combo box lists all 13 shapes (got {window.ShapeComboBox.count()})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.PenStyleComboBox.count() == 6 && window.PenCapComboBox.count() == 3
              && window.PenJoinComboBox.count() == 3,
            $"the pen combos list 6 styles / 3 caps / 3 joins "
            + $"({window.PenStyleComboBox.count()}/{window.PenCapComboBox.count()}/{window.PenJoinComboBox.count()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.BrushStyleComboBox.count() == 15,
            $"the brush combo lists 15 styles (got {window.BrushStyleComboBox.count()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.PenWidthSpinBox.minimum() == 0 && window.PenWidthSpinBox.maximum() == 20
              && window.PenWidthSpinBox.value() == 1,
            $"the pen width spin box has the official range and default "
            + $"({window.PenWidthSpinBox.minimum()}..{window.PenWidthSpinBox.maximum()}, value {window.PenWidthSpinBox.value()})");
        #endif

        window.ShapeComboBox.SetCurrentIndex(4);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.RenderArea.Shape == ApiRenderArea.ShapeKind.Arc,
            $"the shape combo box drives the render area (index 4 -> {window.RenderArea.Shape})");
        #endif

        window.PenWidthSpinBox.SetValue(7);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(Math.Abs(window.RenderArea.Pen.Width() - 7.0) < 0.001,
            $"the pen width spin box drives the pen (got {window.RenderArea.Pen.Width()})");
        #endif

        window.PenStyleComboBox.SetCurrentIndex(1);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.RenderArea.Pen.Style() == Qt.PenStyle.DashLine,
            $"the pen style combo box drives the pen (got {window.RenderArea.Pen.Style()})");
        #endif

        window.AntialiasingCheckBox.SetChecked(true);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.RenderArea.Antialiased,
            "the antialiasing check box drives the render area");
        #endif

        window.TransformationsCheckBox.SetChecked(true);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.RenderArea.Transformed,
            "the transformations check box drives the render area");
        #endif

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        return 0;
        #endif
    }
}
