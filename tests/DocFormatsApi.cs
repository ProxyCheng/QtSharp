using System;
using System.IO;
using System.Text;

// The document modules: QtXml's DOM, QtSvg (render an SVG, and paint *to* SVG) and QtPrintSupport
// (QPrinter, which is also how a PDF is produced). Each is its own Qt module with its own runtime
// DLL, so this is also the proof that the extra modules link and load.
[Demo("test-docformats")]
public static class ApiDocFormatsDemo
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

        string dir = Path.Combine(Path.GetTempPath(), "qtsharp-doc-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);

        // ---- QtXml: build a DOM, serialise it, parse it back -------------------------------
        var doc = new QDomDocument("dashboard-doc");
        var root = doc.CreateElement("dashboard");
        root.SetAttribute("theme", "dark");
        var panel = doc.CreateElement("panel");
        panel.SetAttribute("id", "1");
        panel.AppendChild(doc.CreateTextNode("hello"));
        root.AppendChild(panel);
        doc.AppendChild(root);

        string xml = doc.ToString();
        Check(xml.Contains("<dashboard theme=\"dark\">") && xml.Contains("<panel id=\"1\">hello</panel>"),
            "QDomDocument builds and serialises a tree");

        var parsed = new QDomDocument();
        Check(parsed.SetContent(xml, false), "QDomDocument::setContent parses the document back");
        var parsedRoot = parsed.DocumentElement();
        Check(parsedRoot.TagName() == "dashboard" && parsedRoot.Attribute("theme") == "dark",
            $"the parsed root carries its tag and attribute ('{parsedRoot.TagName()}')");
        Check(parsedRoot.FirstChild().ToElement().TagName() == "panel",
            "the child element round-trips");

        // ---- QtSvg: render an SVG onto an image --------------------------------------------
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>"
                   + "<rect width='16' height='16' fill='#000000'/>"
                   + "<rect x='4' y='4' width='8' height='8' fill='#00ff00'/></svg>";
        var renderer = new QSvgRenderer(new QByteArray(svg));
        Check(renderer.IsValid(), "QSvgRenderer accepts an SVG document");
        var image = new QImage(16, 16, QImage.Format.Format_ARGB32);
        var painter = new QPainter(image);
        renderer.Render(painter);
        painter.End();
        var centre = image.PixelColor(8, 8);
        var corner = image.PixelColor(1, 1);
        Check(centre.Green() == 255 && centre.Red() == 0 && corner.Red() == 0 && corner.Green() == 0,
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
        Check(File.Exists(svgPath) && File.ReadAllText(svgPath).Contains("<svg"),
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
        Check(pdfExists && head == "%PDF", $"QPrinter writes a PDF ({(pdfExists ? new FileInfo(pdfPath).Length + " bytes" : "missing")})");
        Check(QPrinterInfo.AvailablePrinterNames().Length >= 0,
            $"QPrinterInfo::availablePrinterNames ({QPrinterInfo.AvailablePrinterNames().Length} printer(s))");

        try { Directory.Delete(dir, true); } catch { /* the temp files are harmless */ }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
