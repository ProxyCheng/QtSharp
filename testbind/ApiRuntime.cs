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

    // A managed view of a Qt object. Qt owns the lifetime (a parent, a scene, the application),
    // so the wrapper cannot free it - but it has to stop working the moment Qt destroys it,
    // instead of calling into native memory that is gone. Every generated class implements this
    // through its root class.
    internal interface IQtObjectView
    {
        void MarkDestroyed();
    }

    public static unsafe class ApiRuntime
    {
        public const string Library = "QtSharp.Api";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SignalCallback(IntPtr userData, int argc, IntPtr argv, IntPtr typeIds);

        static readonly List<object> keepAlive = new();

        // The destroyed hook is one function pointer for the whole process (never a delegate per
        // object): it points at static code, so nothing has to be kept alive.
        //
        // Shutdown is the one moment it must not be called: the CLR stops accepting managed entry
        // points while Qt is still tearing objects down (and the finalizers of the remaining
        // wrappers are still deleting them), which trips
        // "Attempt to execute managed code after the .NET runtime thread state has been destroyed".
        // Installing the hook's absence first - and refusing to release anything after that -
        // keeps the teardown entirely native.
        static bool shuttingDown;

        /// <summary>Whether the process is past the point where a native callback may enter managed code.</summary>
        public static bool ShuttingDown => shuttingDown;

        static ApiRuntime()
        {
            unsafe
            {
                SetDestroyedCallback((IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&OnDestroyed);
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                shuttingDown = true;
                SetDestroyedCallback(IntPtr.Zero);
            };
        }

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_set_destroyed_callback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetDestroyedCallback(IntPtr callback);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_watch_destroyed", CallingConvention = CallingConvention.Cdecl)]
        private static extern void WatchDestroyed(IntPtr handle);

        // A native object is reachable through several managed views (a QWidget is also a
        // QPaintDevice, and a managed subclass is itself a view of its base), so each handle
        // keeps a list of live wrappers and a lookup takes the newest assignable one.
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

        /// <summary>
        /// Drops a wrapper that is not usable any more (it disposed, so its handle is gone).
        /// Without this a *reused* native address would hand the dead wrapper back: the allocator
        /// hands the same block to the next object of the same size.
        /// </summary>
        public static void Forget(IntPtr handle, object wrapper)
        {
            if (handle == IntPtr.Zero || !wrappers.TryGetValue(handle, out var list))
                return;
            list.RemoveAll(w => !w.IsAlive || ReferenceEquals(w.Target, wrapper));
            if (list.Count == 0)
                wrappers.Remove(handle);
        }

        public static T Wrap<T>(IntPtr handle, Func<IntPtr, T> factory) where T : class
        {
            if (handle == IntPtr.Zero)
                return null;
            if (wrappers.TryGetValue(handle, out var list))
            {
                // Newest first: an address that was freed and reused belongs to the newer object,
                // and only the newer wrapper owns it.
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].Target is T alive)
                        return alive;
            }
            var created = factory(handle);
            Register(handle, created);
            return created;
        }

        // ------------------------------------------------------------------ destruction
        // Which objects Qt has destroyed, and which items belong to which owner. An item is not a
        // QObject (a QGraphicsRectItem has no destroyed signal), so its owner's destruction is the
        // only lifetime signal there is.
        static readonly HashSet<IntPtr> destroyed = new();
        static readonly Dictionary<IntPtr, List<IntPtr>> ownedItems = new();

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        static void OnDestroyed(IntPtr handle)
        {
            // Called from Qt, on whatever thread destroyed the object.
            lock (destroyed)
            {
                destroyed.Add(handle);
                if (wrappers.TryGetValue(handle, out var list))
                {
                    foreach (var weak in list)
                        if (weak.Target is IQtObjectView view)
                            view.MarkDestroyed();
                    wrappers.Remove(handle);
                }
                // Everything the object owned goes with it.
                if (ownedItems.TryGetValue(handle, out var items))
                {
                    ownedItems.Remove(handle);
                    foreach (var item in items)
                        MarkItemDestroyed(item);
                }
            }
        }

        static void MarkItemDestroyed(IntPtr item)
        {
            if (wrappers.TryGetValue(item, out var list))
            {
                foreach (var weak in list)
                    if (weak.Target is IQtObjectView view)
                        view.MarkDestroyed();
                wrappers.Remove(item);
            }
            destroyed.Add(item);
        }

        /// <summary>Whether Qt has already destroyed this object.</summary>
        internal static bool IsDestroyed(IntPtr handle)
        {
            lock (destroyed)
                return destroyed.Contains(handle);
        }

        /// <summary>
        /// Starts watching a QObject: one native slot per object, and the managed hook tells every
        /// wrapper of it that Qt destroyed it. Idempotent, and a no-op for an object that is
        /// already gone.
        /// </summary>
        public static void Watch(IntPtr handle)
        {
            if (handle == IntPtr.Zero || IsDestroyed(handle))
                return;
            WatchDestroyed(handle);
        }

        /// <summary>
        /// Records that <paramref name="owner"/> owns <paramref name="item"/>. When the owner is
        /// destroyed, the item's wrappers are invalidated too - the only lifetime rule a
        /// non-QObject item (a QGraphicsItem) has.
        /// </summary>
        public static void AssociateOwner(IntPtr owner, IntPtr item)
        {
            if (owner == IntPtr.Zero || item == IntPtr.Zero)
                return;
            lock (destroyed)
            {
                if (destroyed.Contains(owner))
                {
                    MarkItemDestroyed(item);
                    return;
                }
                if (!ownedItems.TryGetValue(owner, out var items))
                    ownedItems[owner] = items = new List<IntPtr>();
                items.Add(item);
            }
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

        // A QString that belongs to Qt - the argument of a virtual override - is read without
        // being freed. (ToManaged releases the copy the native side allocated for us.)
        public static string ToManagedTransient(IntPtr qstring)
            => qstring == IntPtr.Zero ? null
               : Marshal.PtrToStringUni(StringUtf16(qstring), (int)StringSize(qstring));

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

        // The caller's slot holds the value itself, so every scalar the model can return needs
        // its own writer (a virtual override's result is copied into it).
        public static void WriteResult(IntPtr destination, long value) => Marshal.WriteInt64(destination, value);
        public static void WriteResult(IntPtr destination, ulong value) => Marshal.WriteInt64(destination, unchecked((long)value));
        public static void WriteResult(IntPtr destination, uint value) => Marshal.WriteInt32(destination, unchecked((int)value));
        public static void WriteResult(IntPtr destination, short value) => Marshal.WriteInt16(destination, value);
        public static void WriteResult(IntPtr destination, ushort value) => Marshal.WriteInt16(destination, unchecked((short)value));
        public static void WriteResult(IntPtr destination, char value) => Marshal.WriteInt16(destination, (short)value);
        public static void WriteResult(IntPtr destination, sbyte value) => Marshal.WriteByte(destination, unchecked((byte)value));
        public static void WriteResult(IntPtr destination, byte value) => Marshal.WriteByte(destination, value);
        public static void WriteResult(IntPtr destination, double value) => Marshal.WriteInt64(destination, BitConverter.DoubleToInt64Bits(value));
        public static void WriteResult(IntPtr destination, float value) => Marshal.WriteInt32(destination, BitConverter.SingleToInt32Bits(value));
        public static void WriteResult(IntPtr destination, IntPtr value) => Marshal.WriteIntPtr(destination, value);

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

        // A NUL-terminated UTF-8 buffer handed to us by a virtual (const char* parameters).
        public static string FromUtf8(IntPtr utf8) => utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);

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

        // A container argument: the managed array becomes a native QStringList for the duration
        // of the call (the generated wrapper frees it in a finally).
        public static IntPtr NewStringList(string[] items)
        {
            var list = StringListNew();
            if (items == null)
                return list;
            foreach (var item in items)
                StringListAppend(list, item);
            return list;
        }

        public static void FreeStringList(IntPtr list)
        {
            if (list != IntPtr.Zero)
                StringListFree(list);
        }

        // A container return: the native side hands over a freshly allocated QStringList.
        public static string[] ToManagedStringList(IntPtr list)
        {
            if (list == IntPtr.Zero)
                return null;
            var result = CopyStringList(list);
            StringListFree(list);
            return result;
        }

        // The same, for a list Qt handed to a virtual override: that one is Qt's, so it is copied
        // out without being freed.
        public static string[] ToManagedStringListTransient(IntPtr list)
            => list == IntPtr.Zero ? null : CopyStringList(list);

        // A virtual override that returns a string writes it into the caller's slot.
        public static void Write(IntPtr destination, string value)
        {
            if (value == null)
                WriteQStringNative(destination, null);
            else
                WriteQStringNative(destination, value);
        }

        // ...and the same for a string list: the managed array becomes a native QStringList that
        // is copied into the slot and then released.
        public static void WriteStringList(IntPtr destination, string[] value)
        {
            var list = NewStringList(value);
            try
            {
                WriteStringListNative(destination, list);
            }
            finally
            {
                FreeStringList(list);
            }
        }

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_write_qstring", CallingConvention = CallingConvention.Cdecl)]
        private static extern void WriteQStringNative(IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string text);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_write_qstringlist", CallingConvention = CallingConvention.Cdecl)]
        private static extern void WriteStringListNative(IntPtr destination, IntPtr list);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstringlist_new", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr StringListNew();

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstringlist_append", CallingConvention = CallingConvention.Cdecl)]
        private static extern void StringListAppend(IntPtr list, [MarshalAs(UnmanagedType.LPWStr)] string text);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstringlist_free", CallingConvention = CallingConvention.Cdecl)]
        private static extern void StringListFree(IntPtr list);

        // A byte buffer argument becomes a QByteArray for the duration of the call; the
        // generated wrapper frees it in a finally. A QByteArrayView return is copied out.
        public static IntPtr NewByteArray(byte[] data)
            => ByteArrayFromBytes(data ?? Array.Empty<byte>(), data?.Length ?? 0);

        public static void FreeByteArray(IntPtr array)
        {
            if (array != IntPtr.Zero)
                ByteArrayFree(array);
        }

        public static byte[] ToManagedBytes(IntPtr array)
        {
            if (array == IntPtr.Zero)
                return null;
            int size = ByteArraySize(array);
            var result = new byte[size];
            if (size > 0)
                Marshal.Copy(ByteArrayData(array), result, 0, size);
            ByteArrayFree(array);
            return result;
        }

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qbytearray_from_bytes", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ByteArrayFromBytes([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data, int length);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qbytearray_size", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ByteArraySize(IntPtr array);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qbytearray_data", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ByteArrayData(IntPtr array);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qbytearray_free", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ByteArrayFree(IntPtr array);

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
