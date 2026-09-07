using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using QtSharp;

static class Program
{
    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_QWidget_windowTitle", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr qs_windowTitle(IntPtr w);

    static unsafe int Main()
    {
        try
        {
            int argc = 1;
            byte[] arg0 = Encoding.UTF8.GetBytes("qtsharp\0");
            fixed (byte* pArg0 = arg0)
            {
                sbyte* sArg0 = (sbyte*)pArg0;
                sbyte** argv = &sArg0;

                using var app = new QApplication(ref argc, argv, 0);
                Console.WriteLine($"[OK] QApplication created, argc={argc}");

                using var widget = new QWidget(null, 0);
                Console.WriteLine("[OK] QWidget created");

                widget.WindowTitle = "Hello from C#";
                var titlePtr = qs_windowTitle(widget.__Instance);
                Console.WriteLine($"[OK] windowTitle roundtrip = '{QStringMarshaller.ToManaged(titlePtr)}'");
                QStringMarshaller.Free(titlePtr);

                widget.Resize(640, 480);
                widget.Show();
                Console.WriteLine("[OK] Widget shown; entering event loop (auto-quit 1.5s)...");

                using var timer = new System.Threading.Timer(_ => QCoreApplication.Quit(), null, 1500, System.Threading.Timeout.Infinite);
                int rc = QApplication.Exec();
                Console.WriteLine($"[OK] Event loop exited with code {rc}");
                return rc;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }
}
