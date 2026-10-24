using System;
using System.IO;

// A port of Qt's examples/svg/svgviewer: load an SVG through QSvgRenderer, render it into an
// image, and export both a PNG and an SVG through QSvgGenerator. The official window is not
// needed to exercise the module; the assertions are on the rendered pixels and the files.
[Demo("qtsvg-svgviewer")]
public static class SvgViewer
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

        string dir = Path.Combine(Path.GetTempPath(), "qtsharp-svg-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            // The svgviewer's test document: a black canvas with a green square in the middle.
            string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
                       + "<rect width='32' height='32' fill='#000000'/>"
                       + "<rect x='8' y='8' width='16' height='16' fill='#00ff00'/></svg>";
            string svgPath = Path.Combine(dir, "viewer.svg");
            File.WriteAllText(svgPath, svg);

            using var renderer = new QSvgRenderer(new QByteArray(svg));
            Check(renderer.IsValid(), "QSvgRenderer accepts the svgviewer document");
            Check(renderer.DefaultSize().Width() == 32 && renderer.DefaultSize().Height() == 32,
                $"the SVG's default size is 32x32 (got {renderer.DefaultSize().Width()}x{renderer.DefaultSize().Height()})");

            // SvgView::renderAsImage in the official example.
            var image = new QImage(64, 64, QImage.Format.Format_ARGB32);
            var painter = new QPainter(image);
            renderer.Render(painter, new QRectF(new QPointF(0, 0), new QSizeF(64, 64)));
            painter.End();
            painter.Dispose();
            var centre = image.PixelColor(32, 32);
            var corner = image.PixelColor(1, 1);
            Check(centre.Green() == 255 && centre.Red() == 0 && centre.Blue() == 0,
                $"the green square rendered at the centre ({centre.Red()},{centre.Green()},{centre.Blue()})");
            Check(corner.Red() == 0 && corner.Green() == 0 && corner.Blue() == 0,
                "the canvas stays black outside the square");

            string pngPath = Path.Combine(dir, "viewer.png");
            Check(image.Save(pngPath), "the rendered image exports as PNG");
            Check(File.Exists(pngPath) && new FileInfo(pngPath).Length > 0,
                "the PNG file exists and is not empty");

            // ExportDialog's SVG path: QSvgGenerator writes the document back to disk.
            string exportedSvg = Path.Combine(dir, "export.svg");
            var generator = new QSvgGenerator();
            generator.FileName = exportedSvg;
            generator.Size = new QSize(64, 64);
            generator.ViewBox = new QRectF(0, 0, 64, 64);
            var exportPainter = new QPainter(generator);
            exportPainter.FillRect(new QRect(0, 0, 64, 64), new QColor(255, 0, 0));
            exportPainter.End();
            exportPainter.Dispose();
            generator.Dispose();
            Check(File.Exists(exportedSvg) && File.ReadAllText(exportedSvg).Contains("<svg"),
                "QSvgGenerator exports an SVG file");

            // The SVG Viewer's file load path (SvgView::load).
            using var fromFile = new QSvgRenderer(svgPath);
            Check(fromFile.IsValid(), "QSvgRenderer loads the saved SVG by file name");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp files are harmless */ }
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
