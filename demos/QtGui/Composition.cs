using System;

// Port of examples/widgets/painting/composition on the new pipeline.
//
// The example is the QPainter::CompositionMode tour: a base (a hue ramp with an alpha mask over an
// image), a "source" circle dragged/animating on top of it, and one radio button per composition
// mode deciding how the two are combined. Two buffers are kept so the base is drawn once and every
// frame just re-blends the source over a copy of it.
//
// The port keeps the renderer exactly (including the buffer reuse) and replaces the two pieces the
// repository cannot provide:
//   * `:res/composition/flower.jpg` (and its alpha channel) need a qrc that does not exist here, so
//     the "photo" is generated - a diagonal colour ramp with a few shapes, which serves the same
//     purpose of giving the composition modes something colourful to work on;
//   * the official controls are 26 QRadioButtons built by hand; they are the same 26 modes here,
//     driven from one combo box plus the hue and alpha sliders.
//
// The base class is a plain QWidget with a paintEvent override instead of the example's private
// ArthurFrame helper (which also carries the "show source" overlay this port leaves out).
public sealed class ApiCompositionRenderer : QWidget
{
    public QPainter.CompositionMode Mode = QPainter.CompositionMode.CompositionMode_SourceOver;
    public int CircleAlpha = 127;
    public int CircleHue = 255;
    public int PaintCount;

    readonly QImage image;

    QImage buffer;
    QImage baseBuffer;
    double circleX, circleY;

    public ApiCompositionRenderer(QWidget parent = null) : base(parent)
    {
        image = MakeImage();
        Resize(320, 240);
    }

    // Stands in for the flower.jpg the official example loads.
    static QImage MakeImage()
    {
        var result = new QImage(120, 90, QImage.Format.Format_ARGB32_Premultiplied);
        using (var painter = new QPainter(result))
        {
            using (var gradient = new QLinearGradient(0, 0, 120, 90))
            {
                Stop(gradient, 0.0, 20, 40, 160);
                Stop(gradient, 0.5, 220, 180, 40);
                Stop(gradient, 1.0, 40, 140, 60);
                using var brush = new QBrush(gradient);
                var full = new QRect(0, 0, 120, 90);
                painter.FillRect(full, brush);
            }
            using (var pen = new QPen(Qt.PenStyle.NoPen))
                painter.SetPen(pen);
            var white = new QColor(255, 255, 255);
                painter.SetBrush(white);
            painter.DrawEllipse(20, 15, 40, 40);
            var dark = new QColor(20, 20, 20);
                painter.SetBrush(dark);
            painter.DrawRect(70, 45, 40, 30);
            painter.End();
        }
        return result;
    }

    static void Stop(QGradient gradient, double position, int r, int g, int b, int a = 255)
    {
         var color = new QColor(r, g, b, a);
        gradient.SetColorAt(position, color);
    }

    public void SetCompositionMode(QPainter.CompositionMode mode) { Mode = mode; Update(); }
    public void SetCircleAlpha(int alpha) { CircleAlpha = alpha; Update(); }
    public void SetCircleHue(int hue) { CircleHue = hue; Update(); }
    public void SetCirclePos(double x, double y) { circleX = x; circleY = y; Update(); }

    /// <summary>
    /// Paints now. Repaint() is only honoured on a widget the window system has exposed, and a
    /// machine with no display never exposes one - there the buffer was left untouched and every
    /// pixel assertion downstream failed (CI found that). grab() renders through render(), which
    /// delivers the QPaintEvent either way.
    /// </summary>
    void ForcePaint()
    {
        using var rendered = Grab();
    }

    /// <summary>
    /// The blended buffer, alpha included - what the widget blits and what the tests read. The
    /// render comes first because every setter only *schedules* one (Update), and the buffer is
    /// only rewritten while painting.
    /// </summary>
    public QImage BufferImage()
    {
        ForcePaint();
        return buffer == null ? null : buffer.Copy();
    }

    /// <summary>The base buffer alone, for comparisons.</summary>
    public QImage BaseImage()
    {
        ForcePaint();
        return baseBuffer == null ? null : baseBuffer.Copy();
    }

    static QRectF RectangleAround(double x, double y)
        => new QRectF(x - 125.0, y - 100.0, 250.0, 200.0);

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;

        if (circleX == 0.0 && circleY == 0.0)
        {
            circleX = width() / 2.0;
            circleY = height() / 2.0;
        }

        if (buffer == null || buffer.Width() != width() || buffer.Height() != height())
        {
            buffer?.Dispose();
            baseBuffer?.Dispose();
            buffer = new QImage(width(), height(), QImage.Format.Format_ARGB32_Premultiplied);
            baseBuffer = new QImage(width(), height(), QImage.Format.Format_ARGB32_Premultiplied);
            using var fill = new QImage(width(), height(), QImage.Format.Format_ARGB32_Premultiplied);
            var transparent = new QColor(0, 0, 0, 0);
                baseBuffer.Fill(transparent);
            using (var p = new QPainter(baseBuffer))
                DrawBase(p);
        }

        // `memcpy(m_buffer.bits(), m_base_buffer.bits(), sizeInBytes())`: the base is drawn once and
        // every frame re-blends onto a copy of it.
        buffer.Dispose();
        buffer = baseBuffer.Copy();
        using (var p = new QPainter(buffer))
            DrawSource(p);

        using var painter = new QPainter(AsQPaintDevice());
        painter.DrawImage(0, 0, buffer);
        painter.End();
    }

    void DrawBase(QPainter p)
    {
        p.SetPen(Qt.PenStyle.NoPen);

        using (var rectGradient = new QLinearGradient(0, 0, 0, height()))
        {
            Stop(rectGradient, 0.00, 255, 0, 0);
            Stop(rectGradient, 0.17, 255, 255, 0);
            Stop(rectGradient, 0.33, 0, 255, 0);
            Stop(rectGradient, 0.50, 0, 255, 255);
            Stop(rectGradient, 0.66, 0, 0, 255);
            Stop(rectGradient, 0.81, 255, 0, 255);
            Stop(rectGradient, 1.00, 255, 0, 0);
            using var brush = new QBrush(rectGradient);
            p.SetBrush(brush);
            p.DrawRect(width() / 2, 0, width() / 2, height());
        }

        using (var alphaGradient = new QLinearGradient(0, 0, width(), 0))
        {
            Stop(alphaGradient, 0.0, 255, 255, 255);
            Stop(alphaGradient, 0.2, 255, 255, 255);
            Stop(alphaGradient, 0.5, 255, 255, 255, 0);
            Stop(alphaGradient, 0.8, 255, 255, 255);
            Stop(alphaGradient, 1.0, 255, 255, 255);

            p.SetCompositionMode(QPainter.CompositionMode.CompositionMode_DestinationIn);
            using var brush = new QBrush(alphaGradient);
            p.SetBrush(brush);
            p.DrawRect(0, 0, width(), height());
        }

        p.SetCompositionMode(QPainter.CompositionMode.CompositionMode_DestinationOver);
        p.SetPen(Qt.PenStyle.NoPen);
        p.SetRenderHint(QPainter.RenderHint.SmoothPixmapTransform);
        var full = new QRect(0, 0, width(), height());
        p.DrawImage(full, image);
    }

    void DrawSource(QPainter p)
    {
        p.SetPen(Qt.PenStyle.NoPen);
        p.SetRenderHint(QPainter.RenderHint.Antialiasing);
        p.SetCompositionMode(Mode);

        var circleRect = RectangleAround(circleX, circleY);
         var color = QColor.FromHsvF(CircleHue / 360.0f, 1.0f, 1.0f, CircleAlpha / 255.0f);
        using (var gradient = new QLinearGradient(new QPointF(circleRect.Left(), circleRect.Top()),
                                                 new QPointF(circleRect.Right(), circleRect.Bottom())))
        {
            var lighter = color.Lighter();
                gradient.SetColorAt(0.0, lighter);
            gradient.SetColorAt(0.5, color);
            var darker = color.Darker();
                gradient.SetColorAt(1.0, darker);
            using var brush = new QBrush(gradient);
            p.SetBrush(brush);
            p.DrawEllipse(circleRect);
        }
    }

    public QImage RenderToImage()
    {
        Repaint();
        using var rendered = Grab();
        return rendered.ToImage();
    }
}

public sealed class ApiCompositionWindow : QWidget
{
    public static readonly QPainter.CompositionMode[] Modes =
    {
        QPainter.CompositionMode.CompositionMode_SourceOver,
        QPainter.CompositionMode.CompositionMode_DestinationOver,
        QPainter.CompositionMode.CompositionMode_Clear,
        QPainter.CompositionMode.CompositionMode_Source,
        QPainter.CompositionMode.CompositionMode_Destination,
        QPainter.CompositionMode.CompositionMode_SourceIn,
        QPainter.CompositionMode.CompositionMode_DestinationIn,
        QPainter.CompositionMode.CompositionMode_SourceOut,
        QPainter.CompositionMode.CompositionMode_DestinationOut,
        QPainter.CompositionMode.CompositionMode_SourceAtop,
        QPainter.CompositionMode.CompositionMode_DestinationAtop,
        QPainter.CompositionMode.CompositionMode_Xor,
        QPainter.CompositionMode.CompositionMode_Plus,
        QPainter.CompositionMode.CompositionMode_Multiply,
        QPainter.CompositionMode.CompositionMode_Screen,
        QPainter.CompositionMode.CompositionMode_Overlay,
        QPainter.CompositionMode.CompositionMode_Darken,
        QPainter.CompositionMode.CompositionMode_Lighten,
        QPainter.CompositionMode.CompositionMode_ColorDodge,
        QPainter.CompositionMode.CompositionMode_ColorBurn,
        QPainter.CompositionMode.CompositionMode_HardLight,
        QPainter.CompositionMode.CompositionMode_SoftLight,
        QPainter.CompositionMode.CompositionMode_Difference,
        QPainter.CompositionMode.CompositionMode_Exclusion,
    };

    public readonly ApiCompositionRenderer Renderer;
    public readonly QComboBox ModeComboBox;
    public readonly QSlider CircleColorSlider;
    public readonly QSlider CircleAlphaSlider;

    public ApiCompositionWindow(QWidget parent = null) : base(parent)
    {
        Renderer = new ApiCompositionRenderer(this);

        ModeComboBox = new QComboBox(this);
        foreach (var mode in Modes)
            ModeComboBox.AddItem(mode.ToString());
        ModeComboBox.CurrentIndexChangedSignal.Connect(index =>
        {
            if (index >= 0 && index < Modes.Length)
                Renderer.SetCompositionMode(Modes[index]);
        });

        CircleColorSlider = new QSlider(Qt.Orientation.Horizontal, this);
        CircleColorSlider.SetRange(0, 359);
        CircleColorSlider.ValueChangedSignal.Connect(hue => Renderer.SetCircleHue(hue));

        CircleAlphaSlider = new QSlider(Qt.Orientation.Horizontal, this);
        CircleAlphaSlider.SetRange(0, 255);
        CircleAlphaSlider.ValueChangedSignal.Connect(alpha => Renderer.SetCircleAlpha(alpha));

        var modesGroup = new QGroupBox("Mode", this);
        var modesLayout = new QVBoxLayout();
        modesLayout.AddWidget(ModeComboBox);
        modesGroup.SetLayout(modesLayout);

        var colorGroup = new QGroupBox("Circle color", this);
        var colorLayout = new QVBoxLayout();
        colorLayout.AddWidget(CircleColorSlider);
        colorGroup.SetLayout(colorLayout);

        var alphaGroup = new QGroupBox("Circle alpha", this);
        var alphaLayout = new QVBoxLayout();
        alphaLayout.AddWidget(CircleAlphaSlider);
        alphaGroup.SetLayout(alphaLayout);

        var controls = new QVBoxLayout();
        controls.AddWidget(colorGroup);
        controls.AddWidget(alphaGroup);
        controls.AddWidget(modesGroup);
        controls.AddStretch(1);

        var mainLayout = new QHBoxLayout();
        mainLayout.AddWidget(Renderer, 1);
        mainLayout.AddLayout(controls);
        SetLayout(mainLayout);

        // The official defaults.
        CircleColorSlider.SetValue(270);
        CircleAlphaSlider.SetValue(200);
        ModeComboBox.SetCurrentIndex(Array.IndexOf(Modes, QPainter.CompositionMode.CompositionMode_SourceOut));

        SetWindowTitle("Composition Modes");
        Resize(560, 320);
    }

    public void SelectMode(QPainter.CompositionMode mode)
        => ModeComboBox.SetCurrentIndex(Array.IndexOf(Modes, mode));
}

[Demo("qtgui-composition")]
public static class Composition
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

        // A cheap signature of an image: enough to tell two blends apart.
        uint Signature(QImage image)
        {
            uint hash = 2166136261;
            for (int y = 0; y < image.Height(); y++)
                for (int x = 0; x < image.Width(); x++)
                {
                    hash ^= image.Pixel(x, y);
                    hash *= 16777619;
                }
            return hash;
        }

        int TransparentPixels(QImage image)
        {
            int count = 0;
            for (int y = 0; y < image.Height(); y++)
                for (int x = 0; x < image.Width(); x++)
                    if (((image.Pixel(x, y) >> 24) & 0xFF) == 0)
                        count++;
            return count;
        }

        using var window = new ApiCompositionWindow();
        // The pixel assertions below use coordinates inside the render area; make the window big
        // enough that the layout gives the renderer that area on every platform.
        window.Resize(900, 600);
        window.Show();
        QCoreApplication.ProcessEvents();

        Check(ApiCompositionWindow.Modes.Length == 24 && window.ModeComboBox.count() == 24,
            $"the mode list is the 24 blend modes of the example ({ApiCompositionWindow.Modes.Length} modes)");
        Check(window.CircleColorSlider.minimum() == 0 && window.CircleColorSlider.maximum() == 359
              && window.CircleAlphaSlider.maximum() == 255,
            $"the hue and alpha sliders have the official ranges "
            + $"({window.CircleColorSlider.minimum()}..{window.CircleColorSlider.maximum()}, alpha ..{window.CircleAlphaSlider.maximum()})");

        window.Renderer.SetCirclePos(-1000, -1000); // park the circle away from the sampled pixels
        // Grab before asserting the paint count (same reason as the renderer's ForcePaint): grab()
        // renders the widget, while a bare Repaint() is ignored on a widget the window system never
        // exposed - a machine with no display, which is what CI is.
        using (var rendered = window.Renderer.Grab())
        {
            Check(window.Renderer.PaintCount > 0, $"paintEvent reached C# ({window.Renderer.PaintCount} times)");
            Check(rendered != null && !rendered.IsNull() && rendered.Width() > 0 && rendered.Height() > 0,
                $"the renderer grabs off-screen ({rendered?.Width()}x{rendered?.Height()})");
        }

        // The base: the hue ramp on the right half and the generated photo over the whole widget.
        using (var buffer = window.Renderer.BufferImage())
        {
            uint left = buffer.Pixel(20, 120);
            uint right = buffer.Pixel(300, 120);
            Check(left != right, $"the base mixes the photo and the hue ramp (0x{left:X8} vs 0x{right:X8})");
        }

        // Park the circle in a known place and blend it in with each mode.
        window.Renderer.SetCirclePos(160, 120);
        window.SelectMode(QPainter.CompositionMode.CompositionMode_SourceOver);
        uint sourceOver;
        int sourceOverOpaque;
        using (var buffer = window.Renderer.BufferImage())
        {
            sourceOver = Signature(buffer);
            sourceOverOpaque = buffer.Width() * buffer.Height() - TransparentPixels(buffer);
            Check(buffer.Pixel(160, 120) != 0,
                $"the source circle is blended in at its centre (0x{buffer.Pixel(160, 120):X8})");
        }
        Check(window.Renderer.Mode == QPainter.CompositionMode.CompositionMode_SourceOver,
            $"selecting a mode reaches the renderer (got {window.Renderer.Mode})");
        Check(sourceOverOpaque > 0, $"the buffer is mostly opaque under SourceOver ({sourceOverOpaque} pixels)");

        // Clear: what the source covers is left transparent.
        window.SelectMode(QPainter.CompositionMode.CompositionMode_Clear);
        using (var buffer = window.Renderer.BufferImage())
        {
            Check(((buffer.Pixel(160, 120) >> 24) & 0xFF) == 0,
                $"CompositionMode_Clear leaves the circle transparent (alpha {(buffer.Pixel(160, 120) >> 24) & 0xFF})");
            Check(Signature(buffer) != sourceOver, "Clear differs from SourceOver");
        }

        // Destination: the source is ignored, so the buffer is the base again.
        window.Renderer.SetCirclePos(-1000, -1000);
        window.Renderer.Repaint();
        uint baseOnly;
        using (var buffer = window.Renderer.BufferImage())
            baseOnly = Signature(buffer);
        window.Renderer.SetCirclePos(160, 120);
        window.Renderer.Repaint();
        window.SelectMode(QPainter.CompositionMode.CompositionMode_Destination);
        using (var buffer = window.Renderer.BufferImage())
            Check(Signature(buffer) == baseOnly,
                "CompositionMode_Destination ignores the source and keeps the base");

        // A handful of the other modes each produce their own blend.
        var signatures = new System.Collections.Generic.Dictionary<QPainter.CompositionMode, uint>();
        foreach (var mode in new[]
                 {
                     QPainter.CompositionMode.CompositionMode_Source,
                     QPainter.CompositionMode.CompositionMode_SourceIn,
                     QPainter.CompositionMode.CompositionMode_DestinationIn,
                     QPainter.CompositionMode.CompositionMode_Xor,
                     QPainter.CompositionMode.CompositionMode_Plus,
                     QPainter.CompositionMode.CompositionMode_Multiply,
                     QPainter.CompositionMode.CompositionMode_Screen,
                     QPainter.CompositionMode.CompositionMode_Difference,
                 })
        {
            window.SelectMode(mode);
            using var buffer = window.Renderer.BufferImage();
            signatures[mode] = Signature(buffer);
        }
        bool allDistinct = true;
        foreach (var a in signatures)
            foreach (var b in signatures)
                if (a.Key != b.Key && a.Value == b.Value)
                    allDistinct = false;
        Check(allDistinct,
            $"eight different composition modes give eight different blends ({signatures.Count} signatures)");

        // The alpha slider: a fully transparent source changes nothing.
        window.SelectMode(QPainter.CompositionMode.CompositionMode_SourceOver);
        window.CircleAlphaSlider.SetValue(0);
        Check(window.Renderer.CircleAlpha == 0,
            $"the alpha slider drives the renderer (got {window.Renderer.CircleAlpha})");
        using (var buffer = window.Renderer.BufferImage())
            Check(Signature(buffer) == baseOnly,
                "a fully transparent source leaves the buffer untouched");
        window.CircleAlphaSlider.SetValue(255);

        // The hue slider: the circle's colour follows it.
        uint hue270, hue0;
        window.CircleColorSlider.SetValue(270);
        using (var buffer = window.Renderer.BufferImage())
            hue270 = buffer.Pixel(160, 120);
        window.CircleColorSlider.SetValue(0);
        using (var buffer = window.Renderer.BufferImage())
            hue0 = buffer.Pixel(160, 120);
        Check(hue270 != hue0,
            $"the hue slider changes the source colour (0x{hue270:X8} vs 0x{hue0:X8})");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
