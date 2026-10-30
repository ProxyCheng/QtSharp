using System;
using System.IO;
using System.Linq;

namespace QtSharp.Tests
{
    /// <summary>
    /// The System.IO bridge: QDir/QFileInfo conversions, the QDirIterator enumerators and the
    /// QIODevice -> Stream adapter.
    /// </summary>
    [Test("test-filesystem-interop")]
    public static class FilesystemInteropTests
    {
        public static int Run(TestContext context)
        {
            string root = Path.Combine(Path.GetTempPath(),
                "qtsharp-fs-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string payloadPath = Path.Combine(root, "payload.txt");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(payloadPath, "hello");

            try
            {
                // ---------------------------------------------------------------- QDir -> DirectoryInfo
                using (var dir = new QDir(new DirectoryInfo(root)))
                {
                    context.Check(dir.Exists(), "DirectoryInfo -> QDir sees the directory");
                    DirectoryInfo back = dir.ToDirectoryInfo();
                    context.Check(string.Equals(Path.GetFullPath(back.FullName), Path.GetFullPath(root),
                            StringComparison.OrdinalIgnoreCase),
                        $"QDir -> DirectoryInfo round-trips ({back.FullName})");
                }

                // ---------------------------------------------------------------- QFileInfo <-> FileSystemInfo
                using (var info = new QFileInfo(new FileInfo(payloadPath)))
                {
                    context.Check(info.Exists() && info.Size() == 5,
                        $"FileInfo -> QFileInfo sees the file ({info.Size()} bytes)");
                    context.Check(info.ToFileSystemInfo() is FileInfo,
                        "QFileInfo -> FileSystemInfo is a FileInfo for a file");
                    FileInfo asFile = info.ToFileInfo();
                    context.Check(asFile.Name == "payload.txt"
                          && string.Equals(Path.GetFullPath(asFile.DirectoryName), Path.GetFullPath(root),
                              StringComparison.OrdinalIgnoreCase),
                        $"ToFileInfo carries the path ({asFile.FullName})");

                    bool refused = false;
                    try { info.ToDirectoryInfo(); }
                    catch (InvalidOperationException) { refused = true; }
                    context.Check(refused, "ToDirectoryInfo refuses a file");
                }

                using (var info = new QFileInfo(new DirectoryInfo(root)))
                {
                    context.Check(info.IsDir(), "DirectoryInfo -> QFileInfo is a directory");
                    context.Check(info.ToFileSystemInfo() is DirectoryInfo,
                        "QFileInfo -> FileSystemInfo is a DirectoryInfo for a directory");
                    bool refused = false;
                    try { info.ToFileInfo(); }
                    catch (InvalidOperationException) { refused = true; }
                    context.Check(refused, "ToFileInfo refuses a directory");
                }

                using (var missing = new QFileInfo(Path.Combine(root, "missing.txt")))
                {
                    context.Check(missing.ToFileSystemInfo() is FileInfo && !missing.Exists(),
                        "a path that does not exist yet converts to a FileInfo that also does not exist");
                }

                bool nullRefused = false;
                try { _ = new QFileInfo((FileSystemInfo)null); }
                catch (ArgumentNullException) { nullRefused = true; }
                context.Check(nullRefused, "the QFileInfo(FileSystemInfo) constructor rejects null");

                // ---------------------------------------------------------------- QDirIterator -> foreach
                var paths = new QDirIterator(root, QDir.Filters.Files | QDir.Filters.NoDotAndDotDot)
                    .EnumeratePaths().ToList();
                context.Check(paths.Count == 1 && paths[0].EndsWith("payload.txt", StringComparison.Ordinal),
                    $"EnumeratePaths yields the file ({string.Join(", ", paths)})");

                var iterator = new QDirIterator(root, QDir.Filters.Files | QDir.Filters.NoDotAndDotDot);
                context.Check(iterator.IsAlive, "the iterator is alive before enumeration");
                foreach (string _ in iterator.EnumeratePaths()) { }
                context.Check(!iterator.IsAlive, "EnumeratePaths disposes the iterator it owns");

                iterator = new QDirIterator(root,
                    QDir.Filters.AllEntries | QDir.Filters.NoDotAndDotDot);
                foreach (string _ in iterator.EnumeratePaths()) break;
                context.Check(!iterator.IsAlive, "an early break disposes the iterator too");

                int entries = 0;
                foreach (QFileInfo entry in new QDirIterator(root,
                             QDir.Filters.AllEntries | QDir.Filters.NoDotAndDotDot).EnumerateFileInfos())
                {
                    using (entry) entries++;
                }
                context.Check(entries == 2, $"EnumerateFileInfos walks directories and files ({entries})");

                iterator = new QDirIterator(root, QDir.Filters.Files | QDir.Filters.NoDotAndDotDot);
                foreach (string _ in iterator.EnumeratePaths(disposeIterator: false)) { }
                context.Check(iterator.IsAlive, "disposeIterator: false leaves the iterator to the caller");
                iterator.Dispose();

                // ---------------------------------------------------------------- QIODevice -> Stream
                byte[] payload = { 1, 2, 3, 4, 250 };
                string writtenPath = Path.Combine(root, "written.bin");
                using (var buffer = new QBuffer())
                {
                    buffer.Open(QIODeviceBase.OpenMode.ReadWrite);
                    using (Stream stream = buffer.AsStream())
                        stream.Write(payload, 0, payload.Length);
                    using (QByteArray data = buffer.Data())
                        context.Check(data.ToArray().SequenceEqual(payload),
                            "QBuffer writes non-UTF-8 bytes intact (the buffer hook is not a string)");
                }

                using (var file = new QFile(writtenPath))
                {
                    context.Check(file.Open(QIODeviceBase.OpenMode.WriteOnly | QIODeviceBase.OpenMode.Truncate),
                        "QFile opens for writing");
                    using (Stream stream = file.AsStream())
                    {
                        context.Check(stream.CanWrite && !stream.CanRead,
                            "the adapter reports the device's directions");
                        stream.Write(payload, 0, payload.Length);
                        stream.Flush();
                    }
                    context.Check(file.IsOpen(), "the adapter leaves the device open by default");
                }
                context.Check(File.ReadAllBytes(writtenPath).SequenceEqual(payload),
                    "QIODevice -> Stream writes reach the file");

                using (var file = new QFile(writtenPath))
                {
                    file.Open(QIODeviceBase.OpenMode.ReadOnly);
                    using (Stream stream = file.AsStream())
                    {
                        context.Check(stream.CanSeek && stream.Length == payload.Length,
                            $"a QFile stream is seekable and sized ({stream.Length})");
                        var buffer = new byte[payload.Length];
                        int read = stream.Read(buffer, 0, buffer.Length);
                        context.Check(read == payload.Length && buffer.SequenceEqual(payload),
                            $"Read fills the caller's buffer ({read} bytes)");
                        context.Check(stream.Read(buffer, 0, buffer.Length) == 0,
                            "reading at the end returns 0 (EOF)");
                        stream.Seek(1, SeekOrigin.Begin);
                        context.Check(stream.Position == 1 && stream.ReadByte() == 2,
                            "Seek moves the position and ReadByte follows it");
                    }
                }

                // SetLength reaches QFileDevice::resize; a QBuffer has no resize at all.
                using (var file = new QFile(writtenPath))
                {
                    file.Open(QIODeviceBase.OpenMode.ReadWrite);
                    using (Stream stream = file.AsStream())
                    {
                        stream.SetLength(2);
                        context.Check(stream.Length == 2 && new FileInfo(writtenPath).Length == 2,
                            $"SetLength resizes a QFile ({stream.Length})");
                    }
                }

                using (var buffer = new QBuffer())
                {
                    buffer.Open(QIODeviceBase.OpenMode.ReadWrite);
                    using (Stream stream = buffer.AsStream())
                    {
                        bool refused = false;
                        try { stream.SetLength(1); }
                        catch (NotSupportedException) { refused = true; }
                        context.Check(refused, "SetLength throws NotSupportedException on a QBuffer");
                    }
                }

                using (var file = new QFile(writtenPath))
                {
                    file.Open(QIODeviceBase.OpenMode.ReadOnly);
                    using (Stream stream = file.AsStream(leaveOpen: false)) { }
                    context.Check(!file.IsOpen(), "AsStream(leaveOpen: false) closes the device on dispose");
                }
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { }
            }

            return context.ExitCode;
        }
    }
}
