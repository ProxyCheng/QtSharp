using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

// Hand-written runtime that ships with the generated bindings (not model-derived).
// It provides the pieces the PoCs proved: a signal wrapper on top of the generic
// moc-metadata forwarder, the QString bridge and the object wrapper cache.
namespace QtSharp.Api
{
    // Same shape as the existing testbind QSignal<T>.
    public sealed class QSignal<T>
    {
        readonly Action<T> connector;

        public QSignal(Action<T> connector) => this.connector = connector;

        public void Connect(T handler) => connector(handler);
    }

    public static unsafe class ApiRuntime
    {
        public const string Library = "QtSharp.Api";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SignalCallback(IntPtr userData, int argc, IntPtr argv, IntPtr typeIds);

        static readonly List<object> keepAlive = new();
        static readonly Dictionary<IntPtr, WeakReference> wrappers = new();

        public static void Register(IntPtr handle, object wrapper)
        {
            if (handle != IntPtr.Zero)
                wrappers[handle] = new WeakReference(wrapper);
        }

        public static T Wrap<T>(IntPtr handle, Func<IntPtr, T> factory) where T : class
        {
            if (handle == IntPtr.Zero)
                return null;
            if (wrappers.TryGetValue(handle, out var weak) && weak.Target is T alive)
                return alive;
            var created = factory(handle);
            wrappers[handle] = new WeakReference(created);
            return created;
        }

        // Copies a QString returned by the generated accessors and frees it.
        public static string ToManaged(IntPtr qstring)
        {
            if (qstring == IntPtr.Zero)
                return null;
            try
            {
                return Marshal.PtrToStringUni(StringUtf16(qstring), (int)StringSize(qstring));
            }
            finally
            {
                StringFree(qstring);
            }
        }

        public static bool Connect(IntPtr sender, string signature, Action<object[]> handler)
        {
            SignalCallback callback = (userData, argc, argv, typeIds) =>
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
            return ConnectNative(sender, signature, callback, IntPtr.Zero, 0) != 0;
        }

        public static void WriteResult(IntPtr destination, bool value)
            => Marshal.WriteByte(destination, value ? (byte)1 : (byte)0);

        public static void WriteResult(IntPtr destination, int value)
            => Marshal.WriteInt32(destination, value);

        public static int ProbeEventFilter(IntPtr self, IntPtr watched, IntPtr eventPtr)
            => ProbeEventFilterNative(self, watched, eventPtr);

        // QMetaType::Type ids (Qt6).
        static object Decode(IntPtr value, int typeId) => typeId switch
        {
            1 => Marshal.ReadByte(value) != 0,                              // Bool
            2 => Marshal.ReadInt32(value),                                  // Int
            3 => (uint)Marshal.ReadInt32(value),                            // UInt
            4 => Marshal.ReadInt64(value),                                  // LongLong
            5 => (ulong)Marshal.ReadInt64(value),                           // ULongLong
            6 => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(value)),  // Double
            7 => (char)Marshal.ReadInt16(value),                            // QChar
            10 => CopyString(value),                                        // QString (owned by caller)
            _ => value,                                                     // unknown -> raw pointer
        };

        static string CopyString(IntPtr qstring)
            => Marshal.PtrToStringUni(StringUtf16(qstring), (int)StringSize(qstring));

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_connect", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ConnectNative(IntPtr sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string signature,
            SignalCallback callback, IntPtr userData, int connectionType);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_event_filter", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeEventFilterNative(IntPtr self, IntPtr watched, IntPtr eventPtr);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstring_utf16", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr StringUtf16(IntPtr qstring);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstring_size", CallingConvention = CallingConvention.Cdecl)]
        private static extern long StringSize(IntPtr qstring);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstring_free", CallingConvention = CallingConvention.Cdecl)]
        private static extern void StringFree(IntPtr qstring);
    }
}
