using System;
using System.IO;

// The missing direction of the IO seam: StreamIODevice makes a BCL Stream usable where Qt wants a
// QIODevice*, and QIODeviceStream makes an open QIODevice usable where the BCL wants a Stream
// (XmlReader, JsonDocument, image decoders, CopyTo, ...). The device stays the caller's; the
// adapter only calls its public API, so Qt's own buffering and error state are preserved:
//
//     using var file = new QFile(path);
//     file.Open(QIODeviceBase.OpenMode.ReadOnly);
//     using Stream stream = file.AsStream();
//
// Read goes through QIODevice::read, not the protected readData, so a sequential device still gets
// its QIODevice buffer. The cost is one QByteArray per read; QIODevice's read(char*, qint64)
// overload is not published by the binding. Write goes through QIODevice::write.
//
// Position/Length/Seek exist only for seekable devices (QFile, QBuffer); a sequential device
// reports CanSeek == false and those members throw NotSupportedException, as Stream requires.
// Read on a sequential device returns what is buffered right now and 0 when nothing is - which a
// Stream consumer reads as end-of-stream. This adapter is aimed at synchronous, seekable devices;
// for a socket or pipe use Qt's readyRead/waitForReadyRead instead of pretending it is a Stream.
//
// Flush reaches QFileDevice::flush for a QFile/QSaveFile. SetLength reaches QFileDevice::resize,
// and throws NotSupportedException for a QBuffer, which cannot grow or shrink. Disposing the
// adapter closes the Qt device only when leaveOpen is false; it never disposes the QFile/QBuffer
// wrapper, whose lifetime stays with the caller.
namespace QtSharp.QtCore
{
    public static class QIODeviceStreamExtensions
    {
        /// <summary>
        /// Wraps an open <see cref="QIODevice"/> as a BCL <see cref="Stream"/>.
        /// </summary>
        /// <param name="device">The device to read from or write to.</param>
        /// <param name="leaveOpen">
        /// Whether disposing the stream leaves the Qt device open. The default is true; false makes
        /// the stream dispose call <see cref="QIODevice.Close"/> on the device.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The device is not open.</exception>
        public static Stream AsStream(this QIODevice device, bool leaveOpen = true)
            => new QIODeviceStream(device, leaveOpen);
    }

    /// <summary>
    /// A BCL <see cref="Stream"/> over an open <see cref="QIODevice"/>. See
    /// <see cref="QIODeviceStreamExtensions.AsStream"/> for the conversion rules.
    /// </summary>
    public sealed class QIODeviceStream : Stream
    {
        readonly QIODevice device;
        readonly bool leaveOpen;

        /// <summary>
        /// Bridges <paramref name="device"/>; the device must already be open.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The device is not open.</exception>
        public QIODeviceStream(QIODevice device, bool leaveOpen = true)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.leaveOpen = leaveOpen;
            if (!device.IsOpen())
                throw new InvalidOperationException(
                    "The QIODevice must be open before it can be wrapped as a Stream; "
                    + "call Open() first.");
        }

        /// <summary>The Qt device behind the stream.</summary>
        public QIODevice Device => device;

        public override bool CanRead => device.IsAlive && device.IsOpen() && device.IsReadable();

        public override bool CanSeek => device.IsAlive && device.IsOpen() && !device.IsSequential();

        public override bool CanWrite => device.IsAlive && device.IsOpen() && device.IsWritable();

        public override long Length
            => CanSeek ? device.Size() : throw new NotSupportedException(
                "The QIODevice is sequential; it has no length.");

        public override long Position
        {
            get => CanSeek ? device.Pos() : throw new NotSupportedException(
                "The QIODevice is sequential; it has no position.");
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));
            ValidateBuffer(buffer, offset, count);
            return Read(new Span<byte>(buffer, offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0)
                return 0;
            if (!CanRead)
                throw new NotSupportedException("The QIODevice is not open for reading.");

            using QByteArray chunk = device.Read(buffer.Length);
            if (chunk is null)
                return 0;
            byte[] bytes = chunk.ToArray();
            int count = Math.Min(bytes.Length, buffer.Length);
            bytes.AsSpan(0, count).CopyTo(buffer);
            return count;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));
            ValidateBuffer(buffer, offset, count);
            Write(new ReadOnlySpan<byte>(buffer, offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length == 0)
                return;
            if (!CanWrite)
                throw new NotSupportedException("The QIODevice is not open for writing.");

            using var bytes = new QByteArray(buffer.ToArray());
            long written = device.Write(bytes);
            if (written != buffer.Length)
                throw new IOException(
                    $"QIODevice::write accepted {written} of {buffer.Length} bytes "
                    + $"({device.ErrorString()}).");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!CanSeek)
                throw new NotSupportedException(
                    "The QIODevice is sequential and cannot be seeked.");

            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => device.Pos() + offset,
                SeekOrigin.End => device.Size() + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0)
                throw new IOException("An attempt was made to seek before the beginning of the device.");
            if (!device.Seek(target))
                throw new IOException(
                    $"QIODevice::seek({target}) failed ({device.ErrorString()}).");
            return target;
        }

        public override void Flush()
        {
            // QIODevice has no flush. A QFileDevice does, and that is the one case where a BCL
            // caller expects Flush to reach the OS.
            if (device.IsAlive && device is QFileDevice file)
                file.Flush();
        }

        public override void SetLength(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (device.IsAlive && device is QFileDevice file)
            {
                if (!file.Resize(value))
                    throw new IOException(
                        $"QFileDevice::resize({value}) failed ({device.ErrorString()}).");
                return;
            }
            throw new NotSupportedException("This QIODevice cannot be resized.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen && device.IsAlive && device.IsOpen())
                device.Close();
            base.Dispose(disposing);
        }

        static void ValidateBuffer(byte[] buffer, int offset, int count)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));
        }
    }
}
