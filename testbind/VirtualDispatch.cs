using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using QtSharp.QtCore;

// PoC #2: virtual dispatch from C++ into managed code using a stable function pointer.
//
// A generated C++ subclass (see native/QtSharp_vdispatch.cpp) forwards each overridden
// virtual to ONE managed entry point. Because the C++ compiler emits the override, the
// ABI (including return-by-value / sret) is handled by the compiler; the managed side only
// ever sees plain pointers. No per-object vtable patching and no delegate keep-alive.
public static unsafe class VirtualDispatch
{
    public interface IHost
    {
        void SizeHint(out int width, out int height);
        void Paint();
        int RowCount();
        int ColumnCount();
        QVariant Data(QModelIndex index, int role);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void Dispatch(IntPtr userData, int methodId, IntPtr argv)
    {
        var host = (IHost)GCHandle.FromIntPtr(userData).Target;
        switch (methodId)
        {
            case 1: // sizeHint: argv[0] is a QSize*
            {
                host.SizeHint(out int width, out int height);
                QSizeSet(Marshal.ReadIntPtr(argv, 0), width, height);
                break;
            }
            case 2: // paintEvent: void, argv[1] is the QPaintEvent*
                host.Paint();
                break;
            case 11: // rowCount: argv[0] is an int*
                Marshal.WriteInt32(Marshal.ReadIntPtr(argv, 0), host.RowCount());
                break;
            case 12: // columnCount: argv[0] is an int*
                Marshal.WriteInt32(Marshal.ReadIntPtr(argv, 0), host.ColumnCount());
                break;
            case 13: // data: argv[0] QVariant*, argv[1] const QModelIndex*, argv[2] int*
            {
                var index = QModelIndex.__CreateInstance(Marshal.ReadIntPtr(argv, 1 * IntPtr.Size));
                int role = Marshal.ReadInt32(Marshal.ReadIntPtr(argv, 2 * IntPtr.Size));
                var value = host.Data(index, role);
                QVariantAssign(Marshal.ReadIntPtr(argv, 0), value.__Instance);
                break;
            }
        }
    }

    public static IntPtr Dispatcher =>
        (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch;

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_vd_widget_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CreateWidget(IntPtr userData, IntPtr dispatch);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_vd_model_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CreateModel(IntPtr userData, IntPtr dispatch);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_vd_widget_size_hint", CallingConvention = CallingConvention.Cdecl)]
    public static extern void WidgetSizeHint(IntPtr widget, out int width, out int height);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_vd_widget_grab", CallingConvention = CallingConvention.Cdecl)]
    public static extern int WidgetGrab(IntPtr widget);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_vd_model_row_count", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ModelRowCount(IntPtr model);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_vd_model_match_display", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ModelMatchDisplay(IntPtr model, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_vd_qsize_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void QSizeSet(IntPtr size, int width, int height);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_vd_qvariant_assign", CallingConvention = CallingConvention.Cdecl)]
    private static extern void QVariantAssign(IntPtr destination, IntPtr source);
}

public sealed class VirtualDispatchHost : VirtualDispatch.IHost
{
    public int Width = 137;
    public int Height = 42;
    public int PaintCount;
    public bool SizeHintCalled;

    public void SizeHint(out int width, out int height)
    {
        SizeHintCalled = true;
        width = Width;
        height = Height;
    }

    public void Paint() => PaintCount++;
    public int RowCount() => 3;
    public int ColumnCount() => 1;
    public QVariant Data(QModelIndex index, int role) => new QVariant("row" + index.Row);
}

public static class VirtualDispatchTest
{
    public static int Run()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var host = new VirtualDispatchHost();
        var handle = GCHandle.Alloc(host);

        IntPtr widget = VirtualDispatch.CreateWidget(GCHandle.ToIntPtr(handle), VirtualDispatch.Dispatcher);
        Check(widget != IntPtr.Zero, "host widget created (C++ subclass of QWidget)");

        VirtualDispatch.WidgetSizeHint(widget, out int width, out int height);
        Check(host.SizeHintCalled && width == 137 && height == 42,
            $"QWidget::sizeHint() reached C# (got {width}x{height})");

        int grabbed = VirtualDispatch.WidgetGrab(widget);
        Check(grabbed == 1 && host.PaintCount > 0,
            $"QWidget::paintEvent reached C# ({host.PaintCount} paint(s) while rendering)");

        IntPtr model = VirtualDispatch.CreateModel(GCHandle.ToIntPtr(handle), VirtualDispatch.Dispatcher);
        Check(VirtualDispatch.ModelRowCount(model) == 3, "QAbstractItemModel::rowCount() reached C#");

        // The function pointer from [UnmanagedCallersOnly] points at static code, so unlike
        // a delegate it cannot be moved or collected - no keep-alive bookkeeping needed.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        VirtualDispatch.WidgetSizeHint(widget, out int widthAfterGc, out int heightAfterGc);
        Check(widthAfterGc == 137 && heightAfterGc == 42,
            "virtual dispatch still works after GC.Collect()");

        // Qt library code (match()) walks indexes calling the virtual data() itself, and the
        // return is a QVariant by value - the exact path that used to need the sret/RAX fix.
        int hits = VirtualDispatch.ModelMatchDisplay(model, "row0");
        Check(hits == 1, $"Qt-internal call into data() returns the managed QVariant ({hits} match(es))");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
