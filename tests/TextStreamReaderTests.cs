using System;
using System.IO;
using System.Text;

namespace QtSharp.Tests
{
    /// <summary>
    /// QTextStream as a BCL TextReader: Qt's decoding stays authoritative, Peek uses a one-character
    /// lookahead, and the adapter decides who owns the QTextStream on dispose.
    /// </summary>
    [Test("test-textstream-reader")]
    public static class TextStreamReaderTests
    {
        public static int Run(TestContext context)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes("alpha\nbeta\ngamma");

            using (var bytes = new QByteArray(utf8))
            using (var buffer = new QBuffer(bytes))
            {
                buffer.Open(QIODeviceBase.OpenMode.ReadOnly);
                using (var text = new QTextStream(buffer))
                using (TextReader reader = text.ToTextReader())
                {
                    context.Check(reader.Peek() == 'a',
                        $"Peek sees the first character ({(char)reader.Peek()})");
                    context.Check(reader.ReadLine() == "alpha", "ReadLine reads the first line");
                    var chunk = new char[4];
                    int read = reader.Read(chunk, 0, chunk.Length);
                    context.Check(new string(chunk, 0, read) == "beta",
                        $"Read fills the caller's buffer ({new string(chunk, 0, read)})");
                    context.Check(reader.ReadToEnd() == "\ngamma", "ReadToEnd returns the rest");
                }
            }

            // leaveOpen: false disposes the QTextStream; the QIODevice stays the caller's.
            using (var bytes = new QByteArray(utf8))
            using (var buffer = new QBuffer(bytes))
            {
                buffer.Open(QIODeviceBase.OpenMode.ReadOnly);
                var text = new QTextStream(buffer);
                using (TextReader reader = text.ToTextReader(leaveOpen: false))
                    context.Check(reader.Read() == 'a', "the reader reads through the adapter");
                context.Check(!text.IsAlive, "leaveOpen: false disposes the QTextStream");
                context.Check(buffer.IsOpen(), "the QIODevice behind it stays open");
            }

            // An empty stream is end-of-stream, not an error.
            using (var empty = new QByteArray(Array.Empty<byte>()))
            using (var buffer = new QBuffer(empty))
            {
                buffer.Open(QIODeviceBase.OpenMode.ReadOnly);
                using (var text = new QTextStream(buffer))
                using (TextReader reader = text.ToTextReader())
                {
                    context.Check(reader.Peek() == -1 && reader.Read() == -1 && reader.ReadLine() == null,
                        "an empty stream reports end-of-stream");
                    context.Check(reader.ReadToEnd() == "", "ReadToEnd on an empty stream is empty");
                }
            }

            bool nullRefused = false;
            try { QTextStreamReaderExtensions.ToTextReader(null); }
            catch (ArgumentNullException) { nullRefused = true; }
            context.Check(nullRefused, "ToTextReader rejects null");

            return context.ExitCode;
        }
    }
}
