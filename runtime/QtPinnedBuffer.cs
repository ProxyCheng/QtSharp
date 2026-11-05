using System;
using System.Runtime.InteropServices;

// Qt's buffer-plus-cleanup constructors (QImage's uchar* ones) do not copy what they are handed:
// the image reads that memory for as long as any copy of it lives and calls the cleanup function
// when the last one is destroyed. A managed byte[] moves when the GC compacts, so the binding pins
// it for exactly that window and installs a cleanup that releases the pin. This is the C# shape of
// what PySide6 does with Py_INCREF plus a decref cleanup (PyQt6 uses SIP's /KeepReference/): the
// buffer stays the caller's, the image reads it in place, and nothing is copied.
namespace QtSharp.Runtime
{
    /// <summary>
    /// A pinned <see cref="byte"/> array whose address and cleanup callback are handed to Qt
    /// together, for the APIs that keep a buffer until their last copy is destroyed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Pointer"/> is the address of the first byte; it stays valid until
    /// <see cref="Cleanup"/> runs (Qt destroys the last copy) or <see cref="Dispose"/> is called
    /// because the native call failed. <see cref="Cleanup"/> first runs the callback the caller
    /// passed (which receives Qt's own cleanup argument), then releases the pin and the registry
    /// entry of the thunk behind it, in a <c>finally</c>: a throwing callback cannot leak the pin.
    /// </para>
    /// <para>
    /// Pinning keeps the GC from moving the array, not from collecting it: the image does not own
    /// the buffer, exactly as in Qt, so the managed array stays reachable through the pin until the
    /// image lets go. A caller that wants the image to own its pixels should copy: QImage.Copy()
    /// detaches the image from the buffer.
    /// </para>
    /// </remarks>
    public sealed class PinnedBuffer : IDisposable
    {
        GCHandle pin;
        bool released;

        PinnedBuffer(GCHandle pin, IntPtr pointer, Action<IntPtr> cleanup)
        {
            this.pin = pin;
            Pointer = pointer;
            Cleanup = cleanup;
        }

        /// <summary>The address of the first byte, valid until the cleanup runs.</summary>
        public IntPtr Pointer { get; }

        /// <summary>
        /// The cleanup function to hand Qt, or <c>null</c> after <see cref="Dispose"/>.
        /// </summary>
        public Action<IntPtr> Cleanup { get; private set; }

        /// <summary>
        /// Pins <paramref name="data"/> and composes the cleanup Qt will call with
        /// <paramref name="userCleanup"/>, which may be <c>null</c>.
        /// </summary>
        public static PinnedBuffer Pin(byte[] data, Action<IntPtr> userCleanup)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr pointer = pin.AddrOfPinnedObject();
                PinnedBuffer buffer = null;
                // The cleanup is a delegate over this buffer, so it has to name itself to release
                // its own registry entry; assignment happens before Qt can ever call it.
                Action<IntPtr> cleanup = info =>
                {
                    try
                    {
                        userCleanup?.Invoke(info);
                    }
                    finally
                    {
                        buffer.Release();
                    }
                };
                buffer = new PinnedBuffer(pin, pointer, cleanup);
                return buffer;
            }
            catch
            {
                pin.Free();
                throw;
            }
        }

        /// <summary>Releases the pin without running the caller's cleanup (the native call failed).</summary>
        public void Dispose() => Release();

        void Release()
        {
            if (released)
                return;
            released = true;
            pin.Free();
            if (Cleanup != null)
                QtCallbackRegistry.Release(Cleanup);
            Cleanup = null;
        }
    }
}
