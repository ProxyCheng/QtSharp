using System;
using System.Collections.Generic;
using QtSharp.Api;

// Port of examples/widgets/painting/transformations on the new pipeline.
//
// Four RenderAreas draw the same QPainterPath. The first one draws it untransformed; the other three
// apply the transformations selected in their combo boxes - cumulatively, so area 2 gets the choices
// of areas 0 and 1 and area 3 gets all three, which is exactly what the official
// Window::operationChanged() does. Every area also paints the red coordinate axes and the dashed
// "unit square" outline: the shape is painted inside a save()/restore() pair, the outline outside it
// - so the outline stays put while the shape moves.
//
// Faithful to the official source with two mechanical differences:
//   * QPainterStateGuard (Qt 6.5+, not bound) is expressed as QPainter::save()/restore();
//   * a shape is copied into a render area by appending into an empty QPainterPath, because
//     QPainterPath has no bound copy constructor (the substitution painterpaths already uses).
public enum ApiTransformOperation
{
    NoTransformation,
    Translate,
    Rotate,
    Scale,
}

// The four QPainterPath objects of the official Window::setupShapes(), verbatim.
public static class ApiTransformationShapes
{
    public static QPainterPath Truck()
    {
        var truck = new QPainterPath();
        truck.SetFillRule(Qt.FillRule.WindingFill);
        truck.MoveTo(0.0, 87.0);
        truck.LineTo(0.0, 60.0);
        truck.LineTo(10.0, 60.0);
        truck.LineTo(35.0, 35.0);
        truck.LineTo(100.0, 35.0);
        truck.LineTo(100.0, 87.0);
        truck.LineTo(0.0, 87.0);
        truck.MoveTo(17.0, 60.0);
        truck.LineTo(55.0, 60.0);
        truck.LineTo(55.0, 40.0);
        truck.LineTo(37.0, 40.0);
        truck.LineTo(17.0, 60.0);
        truck.AddEllipse(17.0, 75.0, 25.0, 25.0);
        truck.AddEllipse(63.0, 75.0, 25.0, 25.0);
        return truck;
    }

    public static QPainterPath Clock()
    {
        var clock = new QPainterPath();
        clock.AddEllipse(-50.0, -50.0, 100.0, 100.0);
        clock.AddEllipse(-48.0, -48.0, 96.0, 96.0);
        clock.MoveTo(0.0, 0.0);
        clock.LineTo(-2.0, -2.0);
        clock.LineTo(0.0, -42.0);
        clock.LineTo(2.0, -2.0);
        clock.LineTo(0.0, 0.0);
        clock.MoveTo(0.0, 0.0);
        clock.LineTo(2.732, -0.732);
        clock.LineTo(24.495, 14.142);
        clock.LineTo(0.732, 2.732);
        clock.LineTo(0.0, 0.0);
        return clock;
    }

    public static QPainterPath House()
    {
        var house = new QPainterPath();
        house.MoveTo(-45.0, -20.0);
        house.LineTo(0.0, -45.0);
        house.LineTo(45.0, -20.0);
        house.LineTo(45.0, 45.0);
        house.LineTo(-45.0, 45.0);
        house.LineTo(-45.0, -20.0);
        house.AddRect(15.0, 5.0, 20.0, 35.0);
        house.AddRect(-35.0, -15.0, 25.0, 25.0);
        return house;
    }

    public static QPainterPath Text()
    {
        var text = new QPainterPath();
        using var font = new QFont();
        font.SetPixelSize(50);
        using var metrics = new QFontMetrics(font);
        using var bounds = metrics.BoundingRect("Qt");
        using var center = bounds.Center();
        text.AddText(new QPointF(-center.X(), -center.Y()), font, "Qt");
        return text;
    }
}

public sealed class ApiTransformRenderArea : QWidget
{
    public readonly QRect XBoundingRect;
    public readonly QRect YBoundingRect;
    public int PaintCount;

    ApiTransformOperation[] operations = Array.Empty<ApiTransformOperation>();
    QPainterPath shape = new QPainterPath();

    public ApiTransformRenderArea(QWidget parent = null) : base(parent)
    {
        using (var newFont = font())
        {
            newFont.SetPixelSize(12);
            SetFont(newFont);
            using var metrics = new QFontMetrics(newFont);
            XBoundingRect = metrics.BoundingRect("x");
            YBoundingRect = metrics.BoundingRect("y");
        }

        Resize(232, 232);
    }

    /// <summary>Replaces the rendered path. The shape is copied (see the file header).</summary>
    public void SetShape(QPainterPath newShape)
    {
        var copy = new QPainterPath();
        copy.AddPath(newShape);
        shape.Dispose();
        shape = copy;
        Update();
    }

    public void SetOperations(ApiTransformOperation[] newOperations)
    {
        operations = newOperations;
        Update();
    }

    public ApiTransformOperation[] Operations() => operations;

    public override QSize MinimumSizeHint() => new QSize(182, 182);
    public override QSize SizeHint() => new QSize(232, 232);

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;

        using var painter = new QPainter(AsQPaintDevice());
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
        using (var white = new QBrush(new QColor(Qt.GlobalColor.white)))
            painter.FillRect(e.Rect(), white);

        painter.Translate(66.0, 66.0);

        painter.Save();
        TransformPainter(painter);
        DrawShape(painter);
        painter.Restore();

        DrawOutline(painter);

        TransformPainter(painter);
        DrawCoordinates(painter);

        painter.End();
    }

    void DrawCoordinates(QPainter painter)
    {
        using (var red = new QPen(new QColor(Qt.GlobalColor.red)))
            painter.SetPen(red);

        painter.DrawLine(0, 0, 50, 0);
        painter.DrawLine(48, -2, 50, 0);
        painter.DrawLine(48, 2, 50, 0);
        painter.DrawText(60 - XBoundingRect.Width() / 2, 0 + XBoundingRect.Height() / 2, "x");

        painter.DrawLine(0, 0, 0, 50);
        painter.DrawLine(-2, 48, 0, 50);
        painter.DrawLine(2, 48, 0, 50);
        painter.DrawText(0 - YBoundingRect.Width() / 2, 60 + YBoundingRect.Height() / 2, "y");
    }

    void DrawOutline(QPainter painter)
    {
        using (var darkGreen = new QPen(new QColor(Qt.GlobalColor.darkGreen)))
            painter.SetPen(darkGreen);
        // The second setPen replaces the colour too - QPen(Qt::DashLine) is a black, zero-width
        // dashed pen. The official does the same, so the outline really is black and dashed.
        using (var dashed = new QPen(Qt.PenStyle.DashLine))
            painter.SetPen(dashed);
        painter.SetBrush(Qt.BrushStyle.NoBrush);
        painter.DrawRect(0, 0, 100, 100);
    }

    void DrawShape(QPainter painter)
    {
        using var blue = new QBrush(new QColor(Qt.GlobalColor.blue));
        painter.FillPath(shape, blue);
    }

    void TransformPainter(QPainter painter)
    {
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case ApiTransformOperation.Translate:
                    painter.Translate(50.0, 50.0);
                    break;
                case ApiTransformOperation.Scale:
                    painter.Scale(0.75, 0.75);
                    break;
                case ApiTransformOperation.Rotate:
                    painter.Rotate(60.0);
                    break;
            }
        }
    }

    public QImage RenderToImage()
    {
        Repaint();
        using var rendered = Grab();
        return rendered.ToImage();
    }
}

// The official Window: the untransformed render area plus three that each accumulate one more
// transformation, and the shape / operation combo boxes.
public sealed class ApiTransformWindow : QWidget
{
    public readonly ApiTransformRenderArea OriginalRenderArea;
    public readonly ApiTransformRenderArea[] TransformedRenderAreas = new ApiTransformRenderArea[3];
    public readonly QComboBox ShapeComboBox;
    public readonly QComboBox[] OperationComboBoxes = new QComboBox[3];
    public readonly QPainterPath[] Shapes;

    static readonly ApiTransformOperation[] OperationTable =
    {
        ApiTransformOperation.NoTransformation,
        ApiTransformOperation.Rotate,
        ApiTransformOperation.Scale,
        ApiTransformOperation.Translate,
    };

    public ApiTransformWindow(QWidget parent = null) : base(parent)
    {
        OriginalRenderArea = new ApiTransformRenderArea(this);

        ShapeComboBox = new QComboBox(this);
        ShapeComboBox.AddItem("Clock");
        ShapeComboBox.AddItem("House");
        ShapeComboBox.AddItem("Text");
        ShapeComboBox.AddItem("Truck");

        var layout = new QGridLayout();
        layout.AddWidget(OriginalRenderArea, 0, 0);
        layout.AddWidget(ShapeComboBox, 1, 0);

        for (int i = 0; i < TransformedRenderAreas.Length; i++)
        {
            TransformedRenderAreas[i] = new ApiTransformRenderArea(this);

            var combo = new QComboBox(this);
            combo.AddItem("No transformation");
            combo.AddItem("Rotate by 60\u00b0");
            combo.AddItem("Scale to 75%");
            combo.AddItem("Translate by (50, 50)");
            combo.ActivatedSignal.Connect(_ => OperationChanged());
            OperationComboBoxes[i] = combo;

            layout.AddWidget(TransformedRenderAreas[i], 0, i + 1);
            layout.AddWidget(combo, 1, i + 1);
        }

        SetLayout(layout);

        Shapes = new[]
        {
            ApiTransformationShapes.Clock(),
            ApiTransformationShapes.House(),
            ApiTransformationShapes.Text(),
            ApiTransformationShapes.Truck(),
        };

        ShapeComboBox.ActivatedSignal.Connect(index => ShapeSelected(index));

        ShapeSelected(0);
        SetWindowTitle("Transformations");
        Resize(1000, 340);
    }

    /// <summary>Combo box index -> operation, then every area gets the prefix up to its own index.</summary>
    public void OperationChanged()
    {
        var operations = new List<ApiTransformOperation>();
        for (int i = 0; i < TransformedRenderAreas.Length; i++)
        {
            int index = OperationComboBoxes[i].currentIndex();
            operations.Add(OperationTable[index]);
            TransformedRenderAreas[i].SetOperations(operations.ToArray());
        }
    }

    public void ShapeSelected(int index)
    {
        var shape = Shapes[index];
        OriginalRenderArea.SetShape(shape);
        foreach (var area in TransformedRenderAreas)
            area.SetShape(shape);
    }
}

public static class ApiTransformationsDemo
{
    static bool IsBlue(int r, int g, int b) => b > 150 && r < 100 && g < 100;
    static bool IsRed(int r, int g, int b) => r > 150 && g < 100 && b < 100;
    static bool IsWhite(int r, int g, int b) => r > 240 && g > 240 && b > 240;
    // The outline is the only neutral (grey) thing on the canvas: a zero-width dashed pen puts the
    // 1px line on a pixel boundary, so with antialiasing it reads as two rows of ~50% grey rather
    // than as pure black. Blends against the blue shape or the red axes are not neutral.
    static bool IsOutline(int r, int g, int b) => r == g && g == b && r <= 220;

    static (int Count, int MinX, int MinY, int MaxX, int MaxY) Sample(QImage image,
                                                                    Func<int, int, int, bool> match)
    {
        int count = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = 0; y < image.Height(); y++)
        {
            for (int x = 0; x < image.Width(); x++)
            {
                uint p = image.Pixel(x, y);
                if (!match((int)((p >> 16) & 0xFF), (int)((p >> 8) & 0xFF), (int)(p & 0xFF)))
                    continue;
                count++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        // RenderToImage() grabs, and grab() works in device pixels, so the bounds are reported in
        // the logical coordinates the assertions and the painting code talk about. The counts stay
        // in device pixels, which is harmless: they only ever feed lower-bound / ratio checks.
        double dpr = image.DevicePixelRatio();
        return (count, (int)(minX / dpr), (int)(minY / dpr), (int)(maxX / dpr), (int)(maxY / dpr));
    }

    static bool Px(QImage image, int x, int y, Func<int, int, int, bool> match)
    {
        // The callers pass logical coordinates; the image is in device pixels.
        double dpr = image.DevicePixelRatio();
        x = (int)Math.Round(x * dpr);
        y = (int)Math.Round(y * dpr);
        if (x < 0 || y < 0 || x >= image.Width() || y >= image.Height())
            return false;
        uint p = image.Pixel(x, y);
        return match((int)((p >> 16) & 0xFF), (int)((p >> 8) & 0xFF), (int)(p & 0xFF));
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

        // The render area's hints and the font metrics it uses to place the axis labels.
        using (var probe = new ApiTransformRenderArea())
        {
            using var hint = probe.SizeHint();
            using var minimum = probe.MinimumSizeHint();
            Check(hint.Width() == 232 && hint.Height() == 232,
                $"the render area asks for 232x232 (got {hint.Width()}x{hint.Height()})");
            Check(minimum.Width() == 182 && minimum.Height() == 182,
                $"the render area accepts 182x182 (got {minimum.Width()}x{minimum.Height()})");
            Check(probe.XBoundingRect.Width() > 0 && probe.XBoundingRect.Height() > 0
                  && probe.YBoundingRect.Width() > 0 && probe.YBoundingRect.Height() > 0,
                $"QFontMetrics::boundingRect sizes the \"x\"/\"y\" labels "
                + $"({probe.XBoundingRect.Width()}x{probe.XBoundingRect.Height()}, "
                + $"{probe.YBoundingRect.Width()}x{probe.YBoundingRect.Height()})");
        }

        // The four shapes really are the ones the official setupShapes() builds.
        using (var clock = ApiTransformationShapes.Clock())
        using (var house = ApiTransformationShapes.House())
        using (var text = ApiTransformationShapes.Text())
        using (var truck = ApiTransformationShapes.Truck())
        {
            using (var bounds = clock.BoundingRect())
                Check(Math.Abs(bounds.Width() - 100) <= 1 && Math.Abs(bounds.Height() - 100) <= 1,
                    $"the clock path spans 100x100 (got {bounds.Width()}x{bounds.Height()})");

            using (var bounds = house.BoundingRect())
                Check(Math.Abs(bounds.Width() - 90) <= 1 && Math.Abs(bounds.Height() - 90) <= 1,
                    $"the house path spans 90x90 (got {bounds.Width()}x{bounds.Height()})");

            using (var bounds = truck.BoundingRect())
                Check(Math.Abs(bounds.Width() - 100) <= 1 && Math.Abs(bounds.Height() - 65) <= 1,
                    $"the truck path spans 100x65 (got {bounds.Width()}x{bounds.Height()})");

            using (var bounds = text.BoundingRect())
            using (var center = bounds.Center())
                // The tolerance is a font one rather than a geometry one: the outlines are centred by
                // construction, but which font "Qt" resolves to - and so the exact ink bounds -
                // differs between machines (CI's differs by a couple of pixels).
                Check(bounds.Width() > 20 && bounds.Height() > 20
                      && Math.Abs(center.X()) <= 8 && Math.Abs(center.Y()) <= 8,
                    $"the text path holds \"Qt\" outlines centred on the origin "
                    + $"(got {bounds.Width():F1}x{bounds.Height():F1} at {center.X():F1},{center.Y():F1})");
        }

        // Rendering the clock: a ring, because the default OddEvenFill leaves the inner disc empty.
        using (var area = new ApiTransformRenderArea())
        {
            area.Show();
            using (var clock = ApiTransformationShapes.Clock())
                area.SetShape(clock);

            using var image = area.RenderToImage();
            Check(area.PaintCount >= 1, $"the render area painted (PaintCount {area.PaintCount})");

            Check(Px(image, 66, 17, IsBlue),
                "the clock ring is filled on the -y radius (blue at 66,17)");
            Check(Px(image, 41, 91, IsWhite),
                "OddEvenFill leaves the inner disc empty (white at 41,91)");
            Check(Px(image, 1, 1, IsWhite), "the widget background is white outside the shape");

            var outline = Sample(image, IsOutline);
            var axes = Sample(image, IsRed);
            Check(outline.Count > 100,
                $"the dashed outline is painted on top ({outline.Count} grey pixels)");
            Check(axes.Count > 100,
                $"the coordinate axes are painted ({axes.Count} red pixels)");
        }

        // The transformations, on a standalone area sized by its own sizeHint so the pixel
        // coordinates below are the ones the code uses. The truck is not rotationally symmetric,
        // which is what makes each transformation observable.
        using (var area = new ApiTransformRenderArea())
        {
            area.Show();
            using (var truck = ApiTransformationShapes.Truck())
                area.SetShape(truck);

            (int Count, int MinX, int MinY, int MaxX, int MaxY) identity, rotated, rotatedScaled, moved;
            (int MinX, int MinY) outlineAtIdentity;
            using (var image = area.RenderToImage())
            {
                identity = Sample(image, IsBlue);
                outlineAtIdentity = (Sample(image, IsOutline).MinX, Sample(image, IsOutline).MinY);
                Check(identity.Count > 3500,
                    $"the identity truck fills the unit square ({identity.Count} blue pixels)");
                Check(identity.MinX <= 68 && identity.MaxX >= 162,
                    $"the truck spans the unit square in x ({identity.MinX}-{identity.MaxX})");
            }

            area.SetOperations(new[] { ApiTransformOperation.Rotate });
            using (var image = area.RenderToImage())
            {
                rotated = Sample(image, IsBlue);
                var outline = Sample(image, IsOutline);
                Check(area.Operations().Length == 1,
                    "the area records a single operation");
                Check(Math.Abs(outline.MinX - outlineAtIdentity.MinX) <= 1
                      && Math.Abs(outline.MinY - outlineAtIdentity.MinY) <= 1,
                    $"the outline is painted outside save()/restore(), so it stays put "
                    + $"(outline {outline.MinX},{outline.MinY} vs {outlineAtIdentity.MinX},{outlineAtIdentity.MinY})");
                Check(Math.Abs(rotated.MinX - identity.MinX) + Math.Abs(rotated.MaxY - identity.MaxY) > 10,
                    $"rotating by 60 degrees moves the truck "
                    + $"({identity.MinX}-{identity.MaxX},{identity.MinY}-{identity.MaxY} -> "
                    + $"{rotated.MinX}-{rotated.MaxX},{rotated.MinY}-{rotated.MaxY})");
            }

            area.SetOperations(new[] { ApiTransformOperation.Rotate, ApiTransformOperation.Scale });
            using (var image = area.RenderToImage())
            {
                rotatedScaled = Sample(image, IsBlue);
                Check(rotatedScaled.Count < rotated.Count * 0.8,
                    $"scaling to 75% shrinks the painted shape "
                    + $"({rotated.Count} -> {rotatedScaled.Count} blue pixels)");
            }

            area.SetOperations(new[]
            {
                ApiTransformOperation.Rotate, ApiTransformOperation.Scale, ApiTransformOperation.Translate,
            });
            using (var image = area.RenderToImage())
            {
                moved = Sample(image, IsBlue);
                int shift = Math.Abs(moved.MinX - rotatedScaled.MinX) + Math.Abs(moved.MinY - rotatedScaled.MinY)
                            + Math.Abs(moved.MaxX - rotatedScaled.MaxX) + Math.Abs(moved.MaxY - rotatedScaled.MaxY);
                Check(shift > 10,
                    $"translating by (50,50) moves the shape "
                    + $"({rotatedScaled.MinX}-{rotatedScaled.MaxX},{rotatedScaled.MinY}-{rotatedScaled.MaxY} -> "
                    + $"{moved.MinX}-{moved.MaxX},{moved.MinY}-{moved.MaxY})");
            }
        }

        // The window: four areas over a grid of combo boxes, the official wiring.
        using var window = new ApiTransformWindow();
        window.Show();
        QCoreApplication.ProcessEvents();

        Check(window.ShapeComboBox.count() == 4
              && window.OperationComboBoxes[0].count() == 4
              && window.OperationComboBoxes[1].count() == 4
              && window.OperationComboBoxes[2].count() == 4,
            "the shape and operation combo boxes are populated "
            + $"({window.ShapeComboBox.count()} shapes, {window.OperationComboBoxes[0].count()} operations)");

        Check(window.Shapes.Length == 4 && window.TransformedRenderAreas.Length == 3
              && window.ShapeComboBox.currentIndex() == 0
              && window.TransformedRenderAreas[2].Operations().Length == 0,
            $"the window starts on the clock, with no transformation applied "
            + $"(shape index {window.ShapeComboBox.currentIndex()})");

        // The combo boxes drive the areas: each area gets the prefix of the operation table up to
        // and including its own index.
        window.OperationComboBoxes[0].SetCurrentIndex(1);   // Rotate by 60
        window.OperationComboBoxes[1].SetCurrentIndex(2);   // Scale to 75%
        window.OperationComboBoxes[2].SetCurrentIndex(3);   // Translate by (50, 50)
        window.OperationChanged();

        var ops0 = window.TransformedRenderAreas[0].Operations();
        var ops1 = window.TransformedRenderAreas[1].Operations();
        var ops2 = window.TransformedRenderAreas[2].Operations();
        Check(ops0.Length == 1 && ops0[0] == ApiTransformOperation.Rotate
              && ops1.Length == 2 && ops1[1] == ApiTransformOperation.Scale
              && ops2.Length == 3 && ops2[2] == ApiTransformOperation.Translate,
            "the operation combo boxes accumulate into the three areas, in order "
            + $"(area 0: [{string.Join(",", ops0)}], area 1: [{string.Join(",", ops1)}], "
            + $"area 2: [{string.Join(",", ops2)}])");

        // Switching the shape reaches all four areas.
        int paintBefore = window.OriginalRenderArea.PaintCount;
        window.ShapeSelected(3);   // Truck
        window.OriginalRenderArea.Repaint();

        using (var original = window.OriginalRenderArea.RenderToImage())
        {
            var truck = Sample(original, IsBlue);
            Check(truck.Count > 3500 && truck.MinX <= 68,
                $"the original area switched to the truck ({truck.Count} blue pixels, x from {truck.MinX})");
            Check(window.OriginalRenderArea.PaintCount > paintBefore,
                "the shape switch repainted the original area");
        }

        foreach (var area in window.TransformedRenderAreas)
            area.Repaint();
        using (var area2 = window.TransformedRenderAreas[2].RenderToImage())
        {
            var truck = Sample(area2, IsBlue);
            Check(truck.Count > 0,
                $"the transformed areas switched to the truck too ({truck.Count} blue pixels)");
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
