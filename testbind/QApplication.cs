using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QtSharp.QtWidgets
{
    public unsafe partial class QApplication
    {
        public QApplication(string[] args) : this((void*) null)
        {
            if (GetType().FullName != "QtSharp.QtWidgets.QApplication")
                throw new Exception("QtSharp.QtWidgets.QApplication: Can't inherit from classes with disabled NativeToManaged map");
            __Instance = Marshal.AllocHGlobal(sizeof(global::QtSharp.QtWidgets.QApplication.__Internal));
            __ownsNativeInstance = true;

            int argc = args?.Length ?? 0;
            var utf8 = new IntPtr[argc];
            for (int i = 0; i < argc; i++)
                utf8[i] = Marshal.StringToCoTaskMemUTF8(args[i] ?? string.Empty);

            IntPtr argvPtr = Marshal.AllocHGlobal(IntPtr.Size * argc);
            for (int i = 0; i < argc; i++)
                Marshal.WriteIntPtr(argvPtr, i * IntPtr.Size, utf8[i]);

            int* pArgc = &argc;
            __Internal.ctor(__Instance, pArgc, (sbyte**)argvPtr, 0);

            SetupVTables(GetType().FullName == "QtSharp.QtWidgets.QApplication");
        }
    }
}
