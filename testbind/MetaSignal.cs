using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using QtSharp.QtCore;

// PoC: a single generic signal dispatcher driven by moc metadata.
//
// Contrast with GeneratedSignals.cs, which emits one compile-time connect function per
// signal and therefore cannot express overloaded signals or signals that carry a
// QPrivateSignal marker. Here every signal goes through the same native forwarder.
public static class MetaSignal
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeCallback(IntPtr userData, int argc, IntPtr argv, IntPtr typeIds);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_meta_signal_index", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SignalIndexNative(IntPtr sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string signature);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_meta_signal_parameter_count", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SignalParameterCountNative(IntPtr sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string signature);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_meta_connect", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ConnectNative(IntPtr sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string signature,
        NativeCallback callback, IntPtr userData, int connectionType);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_meta_method_count", CallingConvention = CallingConvention.Cdecl)]
    private static extern int MethodCountNative(IntPtr sender);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_meta_method_kind", CallingConvention = CallingConvention.Cdecl)]
    private static extern int MethodKindNative(IntPtr sender, int index);

    [SuppressUnmanagedCodeSecurity, DllImport("QtSharp", EntryPoint = "qs_meta_method_signature", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr MethodSignatureNative(IntPtr sender, int index);

    static readonly List<object> keepAlive = new();

    public static int SignalIndex(QObject sender, string signature)
        => SignalIndexNative(sender.__Instance, signature);

    public static int SignalParameterCount(QObject sender, string signature)
        => SignalParameterCountNative(sender.__Instance, signature);

    public static int MethodCount(QObject sender)
        => MethodCountNative(sender.__Instance);

    public static int MethodKind(QObject sender, int index)
        => MethodKindNative(sender.__Instance, index);

    public static string MethodSignature(QObject sender, int index)
        => Marshal.PtrToStringUTF8(MethodSignatureNative(sender.__Instance, index));

    // The signature is the moc normalized signature, e.g. "clicked(bool)",
    // "activated(QString)", "timeout(QPrivateSignal)".
    public static bool Connect(QObject sender, string signature, Action<object[]> handler)
    {
        NativeCallback callback = (userData, argc, argv, typeIds) =>
        {
            var args = new object[argc];
            for (int i = 0; i < argc; i++)
            {
                IntPtr value = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                int typeId = Marshal.ReadInt32(typeIds, i * sizeof(int));
                args[i] = Decode(value, typeId);
            }
            handler(args);
        };
        keepAlive.Add(callback);
        return ConnectNative(sender.__Instance, signature, callback, IntPtr.Zero, 0) != 0;
    }

    // QMetaType::Type ids (Qt6).
    static object Decode(IntPtr value, int typeId) => typeId switch
    {
        1 => Marshal.ReadByte(value) != 0,                                  // Bool
        2 => Marshal.ReadInt32(value),                                      // Int
        3 => (uint)Marshal.ReadInt32(value),                                // UInt
        4 => Marshal.ReadInt64(value),                                      // LongLong
        5 => (ulong)Marshal.ReadInt64(value),                               // ULongLong
        6 => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(value)),      // Double
        7 => (char)Marshal.ReadInt16(value),                                // QChar
        10 => QtSharp.QStringMarshaller.ToManaged(value),                   // QString
        _ => value,                                                         // unknown -> raw pointer
    };
}
