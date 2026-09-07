using System;
using System.Runtime.InteropServices;

static class Program
{
    [DllImport("qt_sharp_min.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr qtsharp_create_application();

    [DllImport("qt_sharp_min.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr qtsharp_create_widget();

    [DllImport("qt_sharp_min.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void qtsharp_widget_set_title(IntPtr w, [MarshalAs(UnmanagedType.LPStr)] string title);

    [DllImport("qt_sharp_min.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void qtsharp_widget_resize(IntPtr w, int width, int height);

    [DllImport("qt_sharp_min.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void qtsharp_widget_show(IntPtr w);

    [DllImport("qt_sharp_min.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int qtsharp_application_exec(IntPtr app);

    [DllImport("qt_sharp_min.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void qtsharp_delete_qobject(IntPtr o);

    private static int Main()
    {
        IntPtr app = qtsharp_create_application();
        Console.WriteLine($"[OK] QApplication created: {app != IntPtr.Zero}");

        IntPtr widget = qtsharp_create_widget();
        Console.WriteLine($"[OK] QWidget created: {widget != IntPtr.Zero}");

        qtsharp_widget_set_title(widget, "QtSharp PoC - Hello from C#");
        qtsharp_widget_resize(widget, 640, 480);
        qtsharp_widget_show(widget);

        Console.WriteLine("Entering Qt event loop (auto-quit in 1.5s)...");
        int rc = qtsharp_application_exec(app);
        Console.WriteLine($"[OK] Event loop exited with code: {rc}");

        qtsharp_delete_qobject(widget);
        qtsharp_delete_qobject(app);
        Console.WriteLine("[OK] Cleanup done.");
        return rc;
    }
}
