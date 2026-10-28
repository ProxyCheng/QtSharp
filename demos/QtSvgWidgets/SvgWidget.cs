using System;
using System.IO;

// The QSvgWidget half of Qt's SVG Viewer example: a widget that owns a QSvgRenderer and paints it.
// The test loads the same document as the SvgViewer demo, checks the widget's renderer and size
// hint, and renders it off-screen.
[Demo("qtsvgwidgets-svgwidget")]
public static class SvgWidget
{
    public static int Run(DemoContext demo)
    {

        string dir = Path.Combine(Path.GetTempPath(), "qtsharp-svgwidget-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string svgPath = Path.Combine(dir, "widget.svg");
            File.WriteAllText(svgPath,
                "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
                + "<rect width='32' height='32' fill='#0000ff'/></svg>");

            using var widget = new QSvgWidget(svgPath);
            var hint = widget.SizeHint;
            #if QTSHARP_DEMO_SMOKE
            demo.Check(hint.Width() == 32 && hint.Height() == 32,
                $"QSvgWidget reports the SVG's size hint ({hint.Width()}x{hint.Height()})");
            #endif

            #if QTSHARP_DEMO_SMOKE
            using (var renderer = widget.Renderer())
                demo.Check(renderer.IsValid() && renderer.DefaultSize().Width() == 32,
                    "QSvgWidget owns a valid QSvgRenderer");
            #endif

            widget.Resize(64, 64);
            #if QTSHARP_DEMO_SMOKE
                widget.Show();
            #else
                demo.Show(widget);
            #endif

            QCoreApplication.ProcessEvents();
            using var pixmap = widget.Grab();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(DemoPixels.GrabCovers(pixmap, widget.Width, widget.Height),
                $"QSvgWidget renders off-screen ({pixmap.Width()}x{pixmap.Height()})");
            #endif

            // QSvgWidget::load(QByteArray) is the second entry point the official example uses.
            using var fromBytes = new QSvgWidget();
            fromBytes.Load(new QByteArray(
                "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>"
                + "<rect width='8' height='8' fill='#ff0000'/></svg>"));
            #if QTSHARP_DEMO_SMOKE
            using (var renderer = fromBytes.Renderer())
                demo.Check(renderer.IsValid() && renderer.DefaultSize().Width() == 8,
                    "QSvgWidget loads an in-memory SVG document");
            #endif
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp files are harmless */ }
        }

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        return 0;
        #endif
    }
}
