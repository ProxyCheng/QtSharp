using System;
using System.IO;
using System.Text;

// The document modules: QtSvg (render an SVG, and paint *to* SVG) and QtPrintSupport (QPrinter,
// which is also how a PDF is produced). Each is its own Qt module with its own runtime DLL, so
// this is also the proof that the extra modules link and load. XML is no longer a Qt module in
// the binding - System.Xml covers it, and QXmlStreamReader/Writer stay in QtCore; the seam is
// exercised by test-xmlseam.
[Test("test-docformats")]
public static class ApiDocFormatsDemo
{
    public static int Run(TestContext context)
    {

        string dir = Path.Combine(Path.GetTempPath(), "qtsharp-doc-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);

        // ---- QtSvg: render an SVG onto an image --------------------------------------------
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>"
                   + "<rect width='16' height='16' fill='#000000'/>"
                   + "<rect x='4' y='4' width='8' height='8' fill='#00ff00'/></svg>";
        var renderer = new QSvgRenderer(new QByteArray(svg));
        context.Check(renderer.IsValid(), "QSvgRenderer accepts an SVG document");
        var image = new QImage(16, 16, QImage.Format.Format_ARGB32);
        var painter = new QPainter(image);
        renderer.Render(painter);
        painter.End();
        var centre = image.PixelColor(8, 8);
        var corner = image.PixelColor(1, 1);
        context.Check(centre.Green() == 255 && centre.Red() == 0 && corner.Red() == 0 && corner.Green() == 0,
            $"the SVG rendered (centre {centre.Red()},{centre.Green()},{centre.Blue()}; corner {corner.Red()},{corner.Green()})");

        // ---- QtSvg / QtSvgGenerator: paint to an SVG file ----------------------------------
        string svgPath = Path.Combine(dir, "out.svg");
        var generator = new QSvgGenerator();
        generator.FileName = svgPath;
        generator.Size = new QSize(32, 32);
        generator.ViewBox = new QRectF(0, 0, 32, 32);
        var svgPainter = new QPainter(generator);
        svgPainter.FillRect(new QRect(0, 0, 32, 32), new QColor(255, 0, 0));
        svgPainter.End();
        svgPainter.Dispose();
        // The generator finalises (and closes) the file when it is destroyed, not on End().
        generator.Dispose();
        context.Check(File.Exists(svgPath) && File.ReadAllText(svgPath).Contains("<svg"),
            "QSvgGenerator paints to an SVG file");

        // ---- QtPrintSupport: QPrinter writes a real PDF ------------------------------------
        string pdfPath = Path.Combine(dir, "out.pdf");
        var printer = new QPrinter(QPrinter.PrinterMode.ScreenResolution);
        printer.SetOutputFormat(QPrinter.OutputFormat.PdfFormat);
        printer.SetOutputFileName(pdfPath);
        var pdfPainter = new QPainter(printer);
        pdfPainter.DrawText(new QRect(50, 50, 400, 60), 0, "QtSharp");
        pdfPainter.End();
        pdfPainter.Dispose();
        printer.Dispose();      // flushes and closes the PDF
        bool pdfExists = File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0;
        string head = pdfExists ? Encoding.ASCII.GetString(File.ReadAllBytes(pdfPath), 0, 4) : "";
        context.Check(pdfExists && head == "%PDF", $"QPrinter writes a PDF ({(pdfExists ? new FileInfo(pdfPath).Length + " bytes" : "missing")})");
        context.Check(QPrinterInfo.AvailablePrinterNames().Length >= 0,
            $"QPrinterInfo::availablePrinterNames ({QPrinterInfo.AvailablePrinterNames().Length} printer(s))");

        try { Directory.Delete(dir, true); } catch { /* the temp files are harmless */ }

        return context.ExitCode;
    }
}
