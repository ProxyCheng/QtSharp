using System;
using System.Runtime.InteropServices;
using System.Security;

namespace QtSharp
{
    public static class QStringListMarshaller
    {
        [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QStringList_from_strings", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FromStrings(IntPtr strings, int count);

        [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QStringList_size", CallingConvention = CallingConvention.Cdecl)]
        private static extern long Size(IntPtr list);

        [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QStringList_at", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr At(IntPtr list, long index);

        [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QStringList_free", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FreeQs(IntPtr list);

        public static IntPtr ToNative(string[] strings)
        {
            if (strings == null)
                return IntPtr.Zero;

            var utf16s = new IntPtr[strings.Length];
            for (int i = 0; i < strings.Length; i++)
                utf16s[i] = Marshal.StringToCoTaskMemUni(strings[i]);

            IntPtr array = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
            for (int i = 0; i < strings.Length; i++)
                Marshal.WriteIntPtr(array, i * IntPtr.Size, utf16s[i]);

            IntPtr list = FromStrings(array, strings.Length);

            Marshal.FreeHGlobal(array);
            for (int i = 0; i < strings.Length; i++)
                Marshal.FreeCoTaskMem(utf16s[i]);

            return list;
        }

        public static string[] ToManaged(IntPtr list)
        {
            if (list == IntPtr.Zero)
                return null;

            long count = Size(list);
            var result = new string[count];
            for (long i = 0; i < count; i++)
            {
                IntPtr utf16 = At(list, i);
                result[i] = Marshal.PtrToStringUni(utf16);
            }
            return result;
        }

        public static void Free(IntPtr list)
        {
            if (list != IntPtr.Zero)
                FreeQs(list);
        }
    }
}
