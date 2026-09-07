using System;
using System.Runtime.InteropServices;
using System.Security;

namespace QtSharp
{
    public static class QStringMarshaller
    {
        [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QString_from_utf16", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FromUtf16(IntPtr utf16);

        [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QString_utf16", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Utf16(IntPtr qs);

        [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QString_size", CallingConvention = CallingConvention.Cdecl)]
        private static extern long Size(IntPtr qs);

        [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QString_free", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FreeQs(IntPtr qs);

        public static IntPtr ToNative(string s)
        {
            if (s == null)
                return IntPtr.Zero;
            IntPtr utf16 = Marshal.StringToCoTaskMemUni(s);
            IntPtr qs = FromUtf16(utf16);
            Marshal.FreeCoTaskMem(utf16);
            return qs;
        }

        public static string ToManaged(IntPtr qs)
        {
            if (qs == IntPtr.Zero)
                return null;
            IntPtr utf16 = Utf16(qs);
            long len = Size(qs);
            return Marshal.PtrToStringUni(utf16, (int)len);
        }

        public static void Free(IntPtr qs)
        {
            if (qs != IntPtr.Zero)
                FreeQs(qs);
        }
    }
}
