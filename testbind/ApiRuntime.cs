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

        // A native object is reachable through several managed views (a QWidget is also a
        // QPaintDevice, and a managed subclass is itself a view of its base), so each handle
        // keeps a list of live wrappers and a lookup takes the first assignable one.
        static readonly Dictionary<IntPtr, List<WeakReference>> wrappers = new();

        public static void Register(IntPtr handle, object wrapper)
        {
            if (handle == IntPtr.Zero)
                return;
            if (!wrappers.TryGetValue(handle, out var list))
                wrappers[handle] = list = new List<WeakReference>();
            list.RemoveAll(w => !w.IsAlive);
            list.Add(new WeakReference(wrapper));
        }

        public static T Wrap<T>(IntPtr handle, Func<IntPtr, T> factory) where T : class
        {
            if (handle == IntPtr.Zero)
                return null;
            if (wrappers.TryGetValue(handle, out var list))
                foreach (var weak in list)
                    if (weak.Target is T alive)
                        return alive;
            var created = factory(handle);
            Register(handle, created);
            return created;
        }

        // Boxed value types (QColor, QTime, ...) are heap-allocated per call and owned by
        // the managed wrapper, so they deliberately skip the cache and are freed by the
        // generated Dispose/finalizer.
        public static int EventType(IntPtr eventPtr) => EventTypeNative(eventPtr);

        /// <summary>
        /// Delivers a user event, which QObject::event routes to the virtual customEvent().
        /// Lets a test check that a managed override of an inherited virtual is reached.
        /// </summary>
        public static bool SendUserEvent(IntPtr objectHandle) => ProbeUserEvent(objectHandle) != 0;

        /// <summary>Delivers a synthetic left mouse press at (x, y) to a widget.</summary>
        public static bool SendMousePress(IntPtr widgetHandle, int x, int y)
            => ProbeMousePress(widgetHandle, x, y) != 0;

        public static void StartApplication() => AppCreate();

        public static int Exec() => AppExec();

        public static void Quit() => AppQuit();

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
            11 => CopyStringList(value),                                    // QStringList
            _ => value,                                                     // unknown -> raw pointer
        };

        static string CopyString(IntPtr qstring)
            => Marshal.PtrToStringUni(StringUtf16(qstring), (int)StringSize(qstring));

        // The list belongs to the signal's caller; the elements are copied out one by one.
        static string[] CopyStringList(IntPtr list)
        {
            int count = StringListCount(list);
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                var element = StringListAt(list, i);
                result[i] = CopyString(element);
                StringFree(element);
            }
            return result;
        }

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstringlist_count", CallingConvention = CallingConvention.Cdecl)]
        private static extern int StringListCount(IntPtr list);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstringlist_at", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr StringListAt(IntPtr list, int index);

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

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_event_type", CallingConvention = CallingConvention.Cdecl)]
        private static extern int EventTypeNative(IntPtr eventPtr);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_user_event", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeUserEvent(IntPtr objectHandle);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_mouse_press", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeMousePress(IntPtr widgetHandle, int x, int y);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_app_create", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AppCreate();

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_app_exec", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AppExec();

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_app_quit", CallingConvention = CallingConvention.Cdecl)]
        private static extern void AppQuit();
    }
}
