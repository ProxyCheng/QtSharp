using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using QtSharp.QtWidgets;

public static class Signals
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void ClickedCallback(IntPtr userData, int isChecked);

    [DllImport("QtSharp", EntryPoint = "qs_QAbstractButton_connect_clicked", CallingConvention = CallingConvention.Cdecl)]
    static extern void ConnectClickedNative(IntPtr button, ClickedCallback cb, IntPtr userData);

    static readonly List<GCHandle> s_handles = new();
    static readonly List<Delegate> s_delegates = new();

    public static void ConnectClicked(QAbstractButton button, Action<bool> handler)
    {
        var handle = GCHandle.Alloc(handler);
        var cb = new ClickedCallback((ud, isChecked) =>
        {
            var target = (Action<bool>)GCHandle.FromIntPtr(ud).Target;
            target(isChecked != 0);
        });
        s_handles.Add(handle);
        s_delegates.Add(cb);
        ConnectClickedNative(button.__Instance, cb, GCHandle.ToIntPtr(handle));
    }
}
