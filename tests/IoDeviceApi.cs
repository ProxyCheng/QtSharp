using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace QtSharp.Tests
{
    /// <summary>
    /// A .NET Stream behind Qt's QIODevice: the bridge every Qt API that takes a QIODevice* can use.
    /// Covers the read path (QImage.Load, QXmlStreamReader), the write path, seek/position, and a
    /// leak check over many create/dispose cycles (managed and native memory both bounded).
    /// </summary>
    [Demo("test-iodevice")]
    public static class IoDeviceTests
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

            // A PNG made by Qt itself, so the test does not depend on System.Drawing.
            string tempPath = Path.Combine(Path.GetTempPath(),
                "qtsharp-iodevice-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");
            byte[] png;
            using (var source = new QImage(8, 6, QImage.Format.Format_RGB32))
            {
                source.Fill(new QColor(255, 0, 0));
                Check(source.Save(tempPath, "PNG"), "the test PNG is written");
            }
            png = File.ReadAllBytes(tempPath);
            try { File.Delete(tempPath); } catch { /* temp files are harmless */ }

            // ---- read path: QImage.Load(QIODevice*) ------------------------------------
            using (var memory = new MemoryStream(png))
            using (var device = new StreamIODevice(memory))
            using (var image = new QImage())
            {
                Check(image.Load(device, null), "QImage.load reads a PNG from the StreamIODevice");
                Check(image.Width() == 8 && image.Height() == 6,
                    $"the image has the stream's dimensions ({image.Width()}x{image.Height()})");
                Check(device.Size() == png.Length, "Size() reports the stream length");
                Check(device.IsSequential() == false, "a MemoryStream is a random-access device");
            }

            // ---- seek/position ---------------------------------------------------------
            using (var memory = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }))
            using (var device = new StreamIODevice(memory))
            {
                Check(device.Seek(4) && device.Pos() == 4, "Seek/Pos round-trip through the Stream");
                Check(!device.AtEnd(), "the device is not at the end after a partial seek");
                Check(device.Seek(8) && device.AtEnd(), "AtEnd() follows the stream position");
                Check(device.Reset() && device.Pos() == 0, "Reset() rewinds the stream");
            }

            // ---- write path ------------------------------------------------------------
            using (var sink = new MemoryStream())
            using (var device = new StreamIODevice(sink))
            using (var payload = new QByteArray(new byte[] { 9, 8, 7, 6 }))
            {
                Check(device.Write(payload) == 4, "QIODevice.write reaches the Stream");
                Check(sink.ToArray().Length == 4 && sink.ToArray()[0] == 9,
                    "the bytes arrive in the .NET stream");
            }

            // ---- XML through the same bridge ------------------------------------------
            byte[] xml = Encoding.UTF8.GetBytes("<root><item>hello</item></root>");
            using (var memory = new MemoryStream(xml))
            using (var device = new StreamIODevice(memory))
            using (var reader = new QXmlStreamReader(device))
            {
                bool sawRoot = false;
                while (!reader.AtEnd())
                {
                    var token = reader.ReadNext();
                    if (token == QXmlStreamReader.TokenType.StartElement && reader.Name() == "root")
                    {
                        sawRoot = true;
                        break;
                    }
                }
                Check(sawRoot, "QXmlStreamReader parses the XML from the .NET stream");
            }

            // ---- leak checks -----------------------------------------------------------
            // A create/load/dispose cycle repeated many times: a per-iteration leak of the native
            // QIODevice (or its managed wrapper) would show up as steady growth.
            var process = Process.GetCurrentProcess();
            process.Refresh();
            long managedBefore = GC.GetTotalMemory(true);
            long nativeBefore = process.PrivateMemorySize64;

            int loaded = 0;
            for (int i = 0; i < 1000; i++)
            {
                using (var memory = new MemoryStream(png))
                using (var device = new StreamIODevice(memory))
                using (var image = new QImage())
                    if (image.Load(device, null) && image.Width() == 8)
                        loaded++;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();
            long managedAfter = GC.GetTotalMemory(true);
            long nativeAfter = process.PrivateMemorySize64;

            Check(loaded == 1000, $"every dispose cycle loaded the image ({loaded}/1000)");
            Check(managedAfter - managedBefore < 4L * 1024 * 1024,
                $"managed memory stays bounded ({(managedAfter - managedBefore) / 1024} KB)");
            Check(nativeAfter - nativeBefore < 64L * 1024 * 1024,
                $"native memory stays bounded ({(nativeAfter - nativeBefore) / 1024 / 1024} MB)");

            // Abandoned wrappers: the finalizer has to delete the native QIODevice.
            long nativeBeforeAbandoned = nativeAfter;
            for (int i = 0; i < 500; i++)
            {
                var memory = new MemoryStream(png);
                var device = new StreamIODevice(memory);
                var image = new QImage();
                image.Load(device, null);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();
            long nativeAfterAbandoned = process.PrivateMemorySize64;
            Check(nativeAfterAbandoned - nativeBeforeAbandoned < 64L * 1024 * 1024,
                $"abandoned devices are finalized ({(nativeAfterAbandoned - nativeBeforeAbandoned) / 1024 / 1024} MB)");

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
