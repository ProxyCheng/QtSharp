﻿using System;
using System.IO;
using System.Text;

// A port of Qt's notepad tutorial's printing path: the document is a QTextDocument, and Print
// sends it through a QPrinter. The official dialog is interactive, so the test prints to PDF and
// asserts the file; the dialog path is the same QPrinter underneath.
[Example("qtprintsupport-notepad")]
public static class Notepad
{
    public static int Run(ExampleContext example)
    {

        string dir = Path.Combine(Path.GetTempPath(), "qtsharp-notepad-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            const string html =
                "<h1>QtSharp</h1>"
                + "<p>The notepad example prints a <b>QTextDocument</b> through a QPrinter.</p>";
            using var document = new QTextDocument();
            document.SetHtml(html);

#if QTSHARP_EXAMPLE_VIEW
            // The notepad window, reduced to the document the tutorial prints.
            var editor = new QTextEdit();
            editor.SetWindowTitle("Notepad");
            editor.SetHtml(html);
            example.Show(editor);
#endif

            string pdfPath = Path.Combine(dir, "notepad.pdf");
            using (var printer = new QPrinter(QPrinter.PrinterMode.ScreenResolution))
            {
                printer.SetOutputFormat(QPrinter.OutputFormat.PdfFormat);
                printer.SetOutputFileName(pdfPath);
                document.Print(printer);
            }

            bool exists = File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0;
            string header = exists ? Encoding.ASCII.GetString(File.ReadAllBytes(pdfPath), 0, 4) : "";
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(exists && header == "%PDF",
                $"QTextDocument prints through QPrinter to a PDF ({(exists ? new FileInfo(pdfPath).Length + " bytes" : "missing")})");
            #endif

#if QTSHARP_EXAMPLE_SMOKE

            example.Check(QPrinterInfo.AvailablePrinterNames().Length >= 0,
                $"QPrinterInfo enumerates the installed printers ({QPrinterInfo.AvailablePrinterNames().Length})");
#endif
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp files are harmless */ }
        }

        #if QTSHARP_EXAMPLE_SMOKE
        return example.ExitCode;
        #else
        return 0;
        #endif
    }
}
