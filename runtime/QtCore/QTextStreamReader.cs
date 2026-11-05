using System;
using System.IO;
using System.Text;

// QTextStream -> TextReader. Qt's QTextStream is a reader over a QIODevice (or a QByteArray) that
// decodes text with its own encoding and BOM detection; System.IO.TextReader is what the BCL
// accepts (XmlReader.Create, StreamReader-style consumers, ...). This adapter pulls characters
// through QTextStream.Read(maxlen), so Qt's decoding and position stay authoritative.
//
// Only the read direction exists: QTextStream's write side is `operator<<`, which the binding does
// not publish, so there is no TextWriter counterpart. For a writing bridge use a QIODevice with
// QIODevice.AsStream() and a StreamWriter instead.
//
// QTextStream is single-pass from the adapter's point of view: ReadLine/ReadToEnd consume the
// stream and TextReader has no Seek. The adapter owns a one-character lookahead for Peek(); that
// is why ReadLine falls back to the base implementation when the lookahead is non-empty.
// Disposing the adapter disposes the QTextStream only when leaveOpen is false; the QIODevice
// behind it always stays the caller's.
namespace QtSharp.QtCore
{
    public static class QTextStreamReaderExtensions
    {
        /// <summary>
        /// Wraps a <see cref="QTextStream"/> as a BCL <see cref="TextReader"/>.
        /// </summary>
        /// <param name="stream">The Qt text stream to read from.</param>
        /// <param name="leaveOpen">
        /// Whether disposing the reader leaves the QTextStream open. The default is true; false
        /// disposes the QTextStream wrapper (which deletes the native object when it owns it).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        public static TextReader ToTextReader(this QTextStream stream, bool leaveOpen = true)
            => new QTextStreamReader(stream, leaveOpen);
    }

    /// <summary>
    /// A BCL <see cref="TextReader"/> over a <see cref="QTextStream"/>. See
    /// <see cref="QTextStreamReaderExtensions.ToTextReader"/> for the conversion rules.
    /// </summary>
    public sealed class QTextStreamReader : TextReader
    {
        readonly QTextStream stream;
        readonly bool leaveOpen;

        // At most one character: Peek() reads one and Read() consumes it.
        string pending = string.Empty;

        /// <summary>Bridges <paramref name="stream"/>; the stream stays the caller's unless leaveOpen is false.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        public QTextStreamReader(QTextStream stream, bool leaveOpen = true)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.leaveOpen = leaveOpen;
        }

        /// <summary>The Qt stream behind the reader.</summary>
        public QTextStream Stream => stream;

        public override int Peek()
        {
            Fill();
            return pending.Length > 0 ? pending[0] : -1;
        }

        public override int Read()
        {
            Fill();
            if (pending.Length == 0)
                return -1;
            char c = pending[0];
            pending = pending.Substring(1);
            return c;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));
            if (index < 0 || index > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0 || count > buffer.Length - index)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return 0;

            int copied = 0;
            if (pending.Length > 0)
            {
                int n = Math.Min(pending.Length, count);
                pending.CopyTo(0, buffer, index, n);
                pending = pending.Substring(n);
                copied = n;
            }

            if (copied < count)
            {
                string chunk = stream.Read(count - copied);
                if (!string.IsNullOrEmpty(chunk))
                {
                    int n = Math.Min(chunk.Length, count - copied);
                    chunk.CopyTo(0, buffer, index + copied, n);
                    copied += n;
                    if (n < chunk.Length)
                        pending = chunk.Substring(n);
                }
            }

            return copied;
        }

        public override string ReadLine()
        {
            // With no lookahead pending, Qt can hand over the whole line at once; the base
            // implementation would read it one character at a time.
            if (pending.Length == 0)
            {
                if (stream.AtEnd())
                    return null;
                return stream.ReadLine();
            }
            return base.ReadLine();
        }

        public override string ReadToEnd()
        {
            var builder = new StringBuilder();
            if (pending.Length > 0)
            {
                builder.Append(pending);
                pending = string.Empty;
            }
            builder.Append(stream.ReadAll());
            return builder.ToString();
        }

        void Fill()
        {
            if (pending.Length > 0)
                return;
            string chunk = stream.Read(1);
            if (!string.IsNullOrEmpty(chunk))
                pending = chunk;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen && stream.IsAlive)
                stream.Dispose();
            base.Dispose(disposing);
        }
    }
}
