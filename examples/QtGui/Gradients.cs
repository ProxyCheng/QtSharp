﻿using System;

// Port of examples/widgets/painting/gradients on the new pipeline.
//
// The example is the tour of Qt's gradient vocabulary: a linear, a radial and a conical gradient,
// their colour stops, the three spread modes and (in the Qt 6 version) the `QGradient::Preset`
// table. The port keeps that content and asserts on the rendered pixels: each gradient's direction
// and each spread mode's behaviour are read back out of the widget.
//
// Everything is drawn in ObjectBoundingMode, so the numbers in the gradient constructors are
// fractions of the widget and the assertions do not depend on its size.
public sealed class ApiGradientWidget : QWidget
{
    public enum GradientMode
    {
        Default1,   // the linear gradient, as a diagonal ramp
        Default2,   // the radial gradient, with its focal point off-centre
        Conical,    // the conical gradient
        Preset,     // QGradient::Preset, filled straight through QPainter::fillRect
    }

    public GradientMode Mode = GradientMode.Default1;
    public int PaintCount;

    public readonly QLinearGradient Linear;
    public readonly QRadialGradient Radial;
    public readonly QConicalGradient Conical;

    public ApiGradientWidget(QWidget parent = null) : base(parent)
    {
        // Linear: top-left to bottom-right, red -> green -> blue.
        Linear = new QLinearGradient(0.0, 0.0, 1.0, 1.0);
        Linear.SetCoordinateMode(QGradient.CoordinateMode.ObjectBoundingMode);
        Stop(Linear, 0.0, 255, 0, 0);
        Stop(Linear, 0.5, 0, 255, 0);
        Stop(Linear, 1.0, 0, 0, 255);

        // Radial: centre yellow -> rim blue, with a radius small enough that the widget's corners
        // fall outside it - which is what makes the spread mode observable.
        Radial = new QRadialGradient(0.5, 0.5, 0.25);
        Radial.SetCoordinateMode(QGradient.CoordinateMode.ObjectBoundingMode);
        Stop(Radial, 0.0, 255, 255, 0);
        Stop(Radial, 1.0, 0, 0, 255);

        // Conical: red at 0 degrees, blue at 180, back to red - so the right edge and the left edge
        // carry different colours.
        Conical = new QConicalGradient(0.5, 0.5, 0.0);
        Conical.SetCoordinateMode(QGradient.CoordinateMode.ObjectBoundingMode);
        Stop(Conical, 0.0, 255, 0, 0);
        Stop(Conical, 0.5, 0, 0, 255);
        Stop(Conical, 1.0, 255, 0, 0);

        Resize(200, 200);
    }

    static void Stop(QGradient gradient, double position, int r, int g, int b)
    {
         var color = new QColor(r, g, b);
        gradient.SetColorAt(position, color);
    }

    public void SetMode(GradientMode mode) { Mode = mode; Update(); }

    public override QSize sizeHint() => new QSize(200, 200);

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;

        using var painter = new QPainter(AsQPaintDevice());
        painter.SetRenderHint(QPainter.RenderHint.Antialiasing);

        var full = new QRect(0, 0, width(), height());

        switch (Mode)
        {
            case GradientMode.Default1:
                using (var brush = new QBrush(Linear))
                    painter.FillRect(full, brush);
                break;

            case GradientMode.Default2:
                using (var brush = new QBrush(Radial))
                    painter.FillRect(full, brush);
                break;

            case GradientMode.Conical:
                using (var brush = new QBrush(Conical))
                    painter.FillRect(full, brush);
                break;

            case GradientMode.Preset:
                painter.FillRect(full, QGradient.Preset.WarmFlame);
                break;
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

// The control side: the official example's combo box that switches between the gradients.
public sealed class ApiGradientWindow : QWidget
{
    public readonly ApiGradientWidget GradientWidget;
    public readonly QComboBox ModeComboBox;

    public ApiGradientWindow(QWidget parent = null) : base(parent)
    {
        GradientWidget = new ApiGradientWidget(this);

        ModeComboBox = new QComboBox(this);
        ModeComboBox.AddItems(new[] { "Default 1", "Default 2", "Conical", "Preset" });
        ModeComboBox.CurrentIndexChangedSignal.Connect(index =>
        {
            if (index >= 0 && index <= (int)ApiGradientWidget.GradientMode.Preset)
                GradientWidget.SetMode((ApiGradientWidget.GradientMode)index);
        });

        var layout = new QVBoxLayout();
        layout.AddWidget(ModeComboBox);
        layout.AddWidget(GradientWidget, 1);
        SetLayout(layout);

        SetWindowTitle("Gradients");
        Resize(300, 300);
    }
}

[Example("qtgui-gradients")]
public static class Gradients
{
    public static int Run(ExampleContext example)
    {

        using var widget = new ApiGradientWidget();
        #if QTSHARP_EXAMPLE_SMOKE
            widget.Show();
        #else
            example.Show(widget);
        #endif

        // Logical (x,y) -> device pixel, since grab() renders at the device pixel ratio.
        uint Px(QImage image, int x, int y)
        {
            int px = (int)Math.Round(x * (double)image.Width() / 200.0);
            int py = (int)Math.Round(y * (double)image.Height() / 200.0);
            px = Math.Min(Math.Max(px, 0), image.Width() - 1);
            py = Math.Min(Math.Max(py, 0), image.Height() - 1);
            return image.Pixel(px, py);
        }

        static (int R, int G, int B) Rgb(uint p) => ((int)((p >> 16) & 0xFF), (int)((p >> 8) & 0xFF), (int)(p & 0xFF));
        #if QTSHARP_EXAMPLE_SMOKE
        static bool Near((int R, int G, int B) c, int r, int g, int b, int tolerance = 60)
            => Math.Abs(c.R - r) <= tolerance && Math.Abs(c.G - g) <= tolerance && Math.Abs(c.B - b) <= tolerance;
        #endif

        // The gradient objects themselves: QGradient::type() is right, and the accessors
        // round-trip what the constructors were given.
#if QTSHARP_EXAMPLE_SMOKE

        // The gradient objects themselves: QGradient::type() is right, and the accessors
        // round-trip what the constructors were given.
        example.Check(widget.Linear.type() == QGradient.Type.LinearGradient
              && widget.Radial.type() == QGradient.Type.RadialGradient
              && widget.Conical.type() == QGradient.Type.ConicalGradient,
            $"the three gradients report their type "
            + $"({widget.Linear.type()}, {widget.Radial.type()}, {widget.Conical.type()})");
#endif

        var start = widget.Linear.Start();
        var stop = widget.Linear.FinalStop();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math.Abs(start.X() - 0.0) < 1e-6 && Math.Abs(start.Y() - 0.0) < 1e-6
                  && Math.Abs(stop.X() - 1.0) < 1e-6 && Math.Abs(stop.Y() - 1.0) < 1e-6,
                $"QLinearGradient::start()/finalStop() round-trip "
                + $"(({start.X()},{start.Y()}) -> ({stop.X()},{stop.Y()}))");
            #endif

        var center = widget.Radial.Center();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math.Abs(center.X() - 0.5) < 1e-6 && Math.Abs(center.Y() - 0.5) < 1e-6
                  && Math.Abs(widget.Radial.Radius() - 0.25) < 1e-6,
                $"QRadialGradient::center()/radius() round-trip "
                + $"(({center.X()},{center.Y()}), r={widget.Radial.Radius()})");
            #endif

#if QTSHARP_EXAMPLE_SMOKE

        example.Check(Math.Abs(widget.Conical.Angle() - 0.0) < 1e-6,
            $"QConicalGradient::angle() round-trip (got {widget.Conical.Angle()})");
#endif

        // A focal point that is not the centre really reaches the object.
        using (var focal = new QRadialGradient(0.5, 0.5, 0.5, 0.25, 0.25))
        {
            // A QPointF is a struct now, so the point comes back by value and owns nothing.
            var focalCenter = focal.Center();
            var focalPoint = focal.FocalPoint();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math.Abs(focalPoint.X() - 0.25) < 1e-6 && Math.Abs(focalPoint.Y() - 0.25) < 1e-6
                  && focalPoint.X() != focalCenter.X(),
                $"a QRadialGradient with a focal point keeps it "
                + $"(centre {focalCenter.X()},{focalCenter.Y()} vs focal {focalPoint.X()},{focalPoint.Y()})");
            #endif
        }

        // The linear gradient runs along the diagonal: red at the top-left, blue at the bottom-right.
        widget.SetMode(ApiGradientWidget.GradientMode.Default1);
        using (var image = widget.RenderToImage())
        {
            var topLeft = Rgb(Px(image, 2, 2));
            var middle = Rgb(Px(image, 100, 100));
            var bottomRight = Rgb(Px(image, 197, 197));
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(topLeft.R > 200 && topLeft.B < 80,
                $"the linear gradient starts red (got {topLeft})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(bottomRight.B > 200 && bottomRight.R < 80,
                $"the linear gradient ends blue (got {bottomRight})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(middle.G > 150 && middle.R < 120 && middle.B < 120,
                $"the mid stop is green (got {middle})");
            #endif
        }

        // The radial gradient: yellow where the centre is, and - with PadSpread, the default - the
        // rim colour in the corner, which lies outside the radius.
        widget.SetMode(ApiGradientWidget.GradientMode.Default2);
        widget.Radial.SetSpread(QGradient.Spread.PadSpread);
        (int R, int G, int B) padCenter, padCorner;
        using (var image = widget.RenderToImage())
        {
            padCenter = Rgb(Px(image, 100, 100));
            padCorner = Rgb(Px(image, 2, 2));
        }
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(Near(padCenter, 255, 255, 0), $"the radial gradient is yellow at its centre (got {padCenter})");
        #endif
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(Near(padCorner, 0, 0, 255), $"PadSpread clamps to the rim colour outside the radius (got {padCorner})");
        #endif

        // RepeatSpread makes the ramp start over, so the corner is no longer the rim colour.
        widget.Radial.SetSpread(QGradient.Spread.RepeatSpread);
        (int R, int G, int B) repeatCorner;
        using (var image = widget.RenderToImage())
            repeatCorner = Rgb(Px(image, 2, 2));
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(repeatCorner != padCorner,
            $"RepeatSpread changes what is painted outside the radius ({padCorner} -> {repeatCorner})");
        #endif

        // ReflectSpread is a third mode; rather than betting on one sample point (the corner can
        // coincide under reflection) the whole image is compared against the repeating one.
        widget.Radial.SetSpread(QGradient.Spread.RepeatSpread);
        using (var repeatImage = widget.RenderToImage())
        {
            widget.Radial.SetSpread(QGradient.Spread.ReflectSpread);
            using var reflectImage = widget.RenderToImage();
            int different = 0;
            for (int y = 0; y < repeatImage.Height(); y++)
                for (int x = 0; x < repeatImage.Width(); x++)
                    if (repeatImage.Pixel(x, y) != reflectImage.Pixel(x, y))
                        different++;
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(different > 0,
                $"ReflectSpread renders differently from RepeatSpread ({different} pixels differ)");
            #endif
        }
        widget.Radial.SetSpread(QGradient.Spread.PadSpread);

        // The conical gradient: red at 0 degrees (the 3 o'clock ray) and blue at 180 (9 o'clock).
        widget.SetMode(ApiGradientWidget.GradientMode.Conical);
        using (var image = widget.RenderToImage())
        {
            var right = Rgb(Px(image, 190, 100));
            var left = Rgb(Px(image, 10, 100));
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(right.R > 200 && right.B < 80,
                $"the conical gradient is red on its 0-degree ray (got {right})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(left.B > 200 && left.R < 80,
                $"the conical gradient is blue on its 180-degree ray (got {left})");
            #endif
        }

        // Rotating the conical gradient moves the red ray.
        widget.Conical.SetAngle(180.0);
        using (var image = widget.RenderToImage())
        {
            var right = Rgb(Px(image, 190, 100));
            var left = Rgb(Px(image, 10, 100));
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(right.B > 200 && left.R > 200,
                $"QConicalGradient::setAngle moves the ramp (right {right}, left {left})");
            #endif
        }
        widget.Conical.SetAngle(0.0);

        // The preset table: a QGradient::Preset filled through QPainter::fillRect.
        widget.SetMode(ApiGradientWidget.GradientMode.Preset);
        using (var image = widget.RenderToImage())
        {
            var a = Rgb(Px(image, 10, 10));
            var b = Rgb(Px(image, 190, 10));
            var c = Rgb(Px(image, 10, 190));
            int distinct = (a != b ? 1 : 0) + (b != c ? 1 : 0) + (a != c ? 1 : 0);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(distinct >= 2,
                $"QGradient::Preset paints a multi-colour gradient ({a}, {b}, {c})");
            #endif
        }

        // The window and its combo box: index -> mode -> repaint, the official wiring.
        using var window = new ApiGradientWindow();
        #if QTSHARP_EXAMPLE_SMOKE
            window.Show();
        #else
            example.Show(window);
        #endif

        // No event loop here, so let Qt run the layout out before painting the child.
        QCoreApplication.ProcessEvents();

#if QTSHARP_EXAMPLE_SMOKE

        example.Check(window.ModeComboBox.count() == 4,
            $"the mode combo box is populated (got {window.ModeComboBox.count()})");
#endif
        window.GradientWidget.Repaint();
        int paintsBefore = window.GradientWidget.PaintCount;
        window.ModeComboBox.SetCurrentIndex((int)ApiGradientWidget.GradientMode.Conical);
        window.GradientWidget.Repaint();
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(window.GradientWidget.Mode == ApiGradientWidget.GradientMode.Conical
              && window.GradientWidget.PaintCount > paintsBefore,
            $"the combo box switches the gradient and repaints "
            + $"(mode {window.GradientWidget.Mode}, paints {paintsBefore} -> {window.GradientWidget.PaintCount}, "
            + $"area {window.GradientWidget.width()}x{window.GradientWidget.height()})");
        #endif

        #if QTSHARP_EXAMPLE_SMOKE
        return example.ExitCode;
        #else
        return 0;
        #endif
    }
}
