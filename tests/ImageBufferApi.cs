using System;
using System.Runtime.InteropServices;
using QtSharp.QtGui;

// QImage's uchar* constructors do not copy the buffer: the image reads it until its last copy is
// destroyed, then calls the cleanup function. A managed byte[] moves in the GC, so the binding pins
// it for that window (QtSharp.Runtime.PinnedBuffer) and installs a cleanup that first runs the
// caller's callback and then releases the pin. Without the pin the temporary array below would be
// collected and the image would read freed memory.
[Test("test-imagebuffer")]
public static class ImageBufferApi
{
    const int Width = 4;
    const int Height = 3;

    // Bytes 0,1,2,3,…; ARGB32 reads each pixel little-endian as 0xAARRGGBB.
    static byte[] Pattern()
    {
        var data = new byte[Width * Height * 4];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)i;
        return data;
    }

    // Nobody holds the array: only the pin keeps it alive, which is exactly what is under test.
    static QImage MakeImageFromTemporary()
        => new QImage(Pattern(), Width, Height, QImage.Format.Format_ARGB32);

    public static int Run(TestContext context)
    {
        QImage image = MakeImageFromTemporary();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        context.Check(image.Pixel(0, 0) == 0x03020100u,
            $"the pinned buffer survives a collection (pixel 0 is 0x{image.Pixel(0, 0):X8})");
        context.Check(image.Pixel(Width - 1, Height - 1) == 0x2F2E2D2Cu,
            $"and the whole buffer is readable (last pixel is 0x{image.Pixel(Width - 1, Height - 1):X8})");
        image.Dispose();

        // No copy: the image reads the caller's array in place, like Qt and like PyQt/PySide.
        byte[] shared = Pattern();
        using (var fromArray = new QImage(shared, Width, Height, QImage.Format.Format_ARGB32))
        {
            shared[0] = 0xFF;
            context.Check(fromArray.Pixel(0, 0) == 0x030201FFu,
                $"the image shares the caller's memory (pixel 0 is 0x{fromArray.Pixel(0, 0):X8})");
        }

        // The stride overload takes the same pinned path.
        byte[] strided = Pattern();
        using (var withStride = new QImage(strided, Width, Height, Width * 4, QImage.Format.Format_ARGB32))
        {
            context.Check(withStride.Pixel(Width - 1, Height - 1) == 0x2F2E2D2Cu,
                "the bytesPerLine overload pins the buffer too");
        }

        // The caller's cleanup runs when the last copy is destroyed, and it sees cleanupInfo.
        int calls = 0;
        IntPtr seenInfo = IntPtr.Zero;
        Action<IntPtr> cleanup = info =>
        {
            calls++;
            seenInfo = info;
        };
        var owned = new QImage(Pattern(), Width, Height, QImage.Format.Format_ARGB32,
            cleanup, (IntPtr)4242);
        context.Check(calls == 0, "the cleanup has not run while the image is alive");
        owned.Dispose();
        context.Check(calls == 1, $"the cleanup runs once when the image is destroyed (ran {calls})");
        context.Check(seenInfo == (IntPtr)4242,
            "the cleanup receives the cleanupInfo the caller passed");

        // A second Dispose is a no-op on the wrapper, so the pin is not released twice.
        owned.Dispose();
        context.Check(calls == 1, "disposing again does not run the cleanup a second time");

        // An image that outlives a collection and is then destroyed must not double-free the pin.
        var late = MakeImageFromTemporary();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        late.Dispose();
        context.Check(true, "a pinned image can be destroyed after a collection");

        return context.ExitCode;
    }
}
