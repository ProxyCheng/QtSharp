using System;
using System.IO;

namespace QtSharp.QtCore
{
    /// <summary>
    /// A Qt <see cref="QIODevice"/> backed by a .NET <see cref="Stream"/>, so any Qt API that takes
    /// a <c>QIODevice*</c> can read from or write to managed IO: <c>QImage.Load</c>,
    /// <c>QXmlStreamReader</c>, <c>QDataStream</c>, <c>QMediaPlayer.SetSourceDevice</c>, ...
    ///
    /// The bridge is the QIODevice virtual set published by the generator's bridge registry
    /// (<c>gen/QioDeviceBridge.cs</c>): <see cref="ReadData"/>/<see cref="WriteData"/> receive a raw
    /// byte buffer as an <see cref="IntPtr"/> this class wraps in a span. The Qt object stays a real
    /// QIODevice, owned by this wrapper; disposing the wrapper deletes it. Dispose the stream
    /// separately unless <c>ownsStream</c> is set.
    /// </summary>
    public sealed class StreamIODevice : QIODevice
    {
        readonly Stream stream;

        /// <summary>Bridges <paramref name="stream"/>; the device opens for the directions the
        /// stream supports. The stream is the caller's to dispose.</summary>
        public StreamIODevice(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            var mode = QIODeviceBase.OpenMode.NotOpen;
            if (stream.CanRead)
                mode |= QIODeviceBase.OpenMode.ReadOnly;
            if (stream.CanWrite)
                mode |= QIODeviceBase.OpenMode.WriteOnly;
            Open(mode);
        }

        // The caller's buffer is written in place: a per-read byte[] would allocate once per
        // QIODevice::read() call, which is hot in image/XML/video decoding (and the leak test
        // measures it as process growth).
        public override unsafe long ReadData(IntPtr data, long maxlen)
        {
            if (maxlen <= 0)
                return 0;
            int count = (int)Math.Min(maxlen, int.MaxValue);
            return stream.Read(new Span<byte>((void*)data, count));
        }

        public override unsafe long WriteData(IntPtr data, long len)
        {
            if (len <= 0)
                return 0;
            int count = (int)Math.Min(len, int.MaxValue);
            stream.Write(new ReadOnlySpan<byte>((void*)data, count));
            return count;
        }

        public override bool Seek(long pos)
        {
            if (!stream.CanSeek)
                return false;
            stream.Position = pos;
            return true;
        }

        public override long Pos() => stream.CanSeek ? stream.Position : 0;

        public override long Size() => stream.CanSeek ? stream.Length : 0;

        public override bool AtEnd() => stream.CanSeek && stream.Position >= stream.Length;

        public override bool IsSequential() => !stream.CanSeek;

        public override bool Reset()
        {
            if (!stream.CanSeek)
                return false;
            stream.Position = 0;
            return true;
        }

        /// <summary>The .NET stream behind the device.</summary>
        public Stream Stream => stream;
    }
}
