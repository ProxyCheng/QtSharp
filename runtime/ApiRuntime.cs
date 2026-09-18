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
    // A Qt signal, with the arity in the type so both halves are typed: `Connect(Action<int,string>)`
    // and `Emit(42, "x")`. The generated binding supplies the two closures (which marshal each
    // parameter the way Qt declared it); a user-declared signal is built by ApiRuntime.NewSignal.
    //
    // The generated binding is what produces the native argument storage, because only it knows
    // whether a parameter is a bound class pointer, a boxed value or a container - the same
    // distinction the virtual dispatch makes.
    public sealed class QSignal
    {
        readonly Action<Action> connector;
        readonly Action emitter;

        public QSignal(Action<Action> connector, Action emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action handler) => connector(handler);
        public void Emit() => emitter();
    }

    public sealed class QSignal<T1>
    {
        readonly Action<Action<T1>> connector;
        readonly Action<T1> emitter;

        public QSignal(Action<Action<T1>> connector, Action<T1> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action<T1> handler) => connector(handler);
        public void Emit(T1 a1) => emitter(a1);
    }

    public sealed class QSignal<T1, T2>
    {
        readonly Action<Action<T1, T2>> connector;
        readonly Action<T1, T2> emitter;

        public QSignal(Action<Action<T1, T2>> connector, Action<T1, T2> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action<T1, T2> handler) => connector(handler);
        public void Emit(T1 a1, T2 a2) => emitter(a1, a2);
    }

    public sealed class QSignal<T1, T2, T3>
    {
        readonly Action<Action<T1, T2, T3>> connector;
        readonly Action<T1, T2, T3> emitter;

        public QSignal(Action<Action<T1, T2, T3>> connector, Action<T1, T2, T3> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action<T1, T2, T3> handler) => connector(handler);
        public void Emit(T1 a1, T2 a2, T3 a3) => emitter(a1, a2, a3);
    }

    public sealed class QSignal<T1, T2, T3, T4>
    {
        readonly Action<Action<T1, T2, T3, T4>> connector;
        readonly Action<T1, T2, T3, T4> emitter;

        public QSignal(Action<Action<T1, T2, T3, T4>> connector, Action<T1, T2, T3, T4> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action<T1, T2, T3, T4> handler) => connector(handler);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4) => emitter(a1, a2, a3, a4);
    }

    public sealed class QSignal<T1, T2, T3, T4, T5>
    {
        readonly Action<Action<T1, T2, T3, T4, T5>> connector;
        readonly Action<T1, T2, T3, T4, T5> emitter;

        public QSignal(Action<Action<T1, T2, T3, T4, T5>> connector, Action<T1, T2, T3, T4, T5> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action<T1, T2, T3, T4, T5> handler) => connector(handler);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5) => emitter(a1, a2, a3, a4, a5);
    }

    public sealed class QSignal<T1, T2, T3, T4, T5, T6>
    {
        readonly Action<Action<T1, T2, T3, T4, T5, T6>> connector;
        readonly Action<T1, T2, T3, T4, T5, T6> emitter;

        public QSignal(Action<Action<T1, T2, T3, T4, T5, T6>> connector, Action<T1, T2, T3, T4, T5, T6> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action<T1, T2, T3, T4, T5, T6> handler) => connector(handler);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6) => emitter(a1, a2, a3, a4, a5, a6);
    }

    public sealed class QSignal<T1, T2, T3, T4, T5, T6, T7>
    {
        readonly Action<Action<T1, T2, T3, T4, T5, T6, T7>> connector;
        readonly Action<T1, T2, T3, T4, T5, T6, T7> emitter;

        public QSignal(Action<Action<T1, T2, T3, T4, T5, T6, T7>> connector, Action<T1, T2, T3, T4, T5, T6, T7> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action<T1, T2, T3, T4, T5, T6, T7> handler) => connector(handler);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7) => emitter(a1, a2, a3, a4, a5, a6, a7);
    }

    public sealed class QSignal<T1, T2, T3, T4, T5, T6, T7, T8>
    {
        readonly Action<Action<T1, T2, T3, T4, T5, T6, T7, T8>> connector;
        readonly Action<T1, T2, T3, T4, T5, T6, T7, T8> emitter;

        public QSignal(Action<Action<T1, T2, T3, T4, T5, T6, T7, T8>> connector, Action<T1, T2, T3, T4, T5, T6, T7, T8> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public void Connect(Action<T1, T2, T3, T4, T5, T6, T7, T8> handler) => connector(handler);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8) => emitter(a1, a2, a3, a4, a5, a6, a7, a8);
    }

    // A managed view of a Qt object. Qt owns the lifetime (a parent, a scene, the application),
    // so the wrapper cannot free it - but it has to stop working the moment Qt destroys it,
    // instead of calling into native memory that is gone. Every generated class implements this
    // through its root class.
    internal interface IQtObjectView
    {
        void MarkDestroyed();
    }

    // The generated root class implements this, which is what lets a *user-declared* signal or
    // property carry a bound class (as its native pointer) without the runtime having to know the
    // type at compile time.
    public interface IQtHandle
    {
        IntPtr Handle { get; }
    }

    // ------------------------------------------------------------------ user-declared members
    // A C# class can declare its own signals and properties (the pyqtSignal/Property equivalent):
    //
    //     public sealed class Counter : QObject
    //     {
    //         public static readonly QMetaSignal<int> ValueChanged = new("valueChanged");
    //         public static readonly QMetaProperty<int> Value = new("value", o => ((Counter)o)._value, ...);
    //     }
    //
    // ApiRuntime.AttachMetaObject (called by every generated constructor) reflects over the static
    // fields of the concrete type, builds a meta-object with the extra members, and installs it on
    // the object. From then on they are ordinary Qt members: indexOfSignal finds them, connect()
    // works, and the property read/write goes through QObject::property/setProperty.

    public abstract class QMetaSignalBase
    {
        internal string Name;
        internal string Signature;
    }

    public sealed class QMetaSignal : QMetaSignalBase
    {
        public QMetaSignal(string name)
        {
            Name = name;
            Signature = name + "()";
        }

        public void Connect(QObject sender, Action handler)
            => ApiRuntime.Connect(sender.Handle, Signature, (argv, _) => handler());

        public void Emit(QObject sender) => ApiRuntime.EmitUser(sender.Handle, Signature, Type.EmptyTypes, Array.Empty<object>());
    }

    public sealed class QMetaSignal<T1> : QMetaSignalBase
    {
        public QMetaSignal(string name)
        {
            Name = name;
            Signature = name + "(" + MetaType.QtName(typeof(T1)) + ")";
        }

        public void Connect(QObject sender, Action<T1> handler)
            => ApiRuntime.Connect(sender.Handle, Signature, (argv, _) => handler(MetaType.Read<T1>(argv, 0)));

        public void Emit(QObject sender, T1 a1) => ApiRuntime.EmitUser(sender.Handle, Signature, new[] { typeof(T1) }, new object[] { a1 });
    }

    public sealed class QMetaSignal<T1, T2> : QMetaSignalBase
    {
        public QMetaSignal(string name)
        {
            Name = name;
            Signature = name + "(" + MetaType.QtName(typeof(T1)) + "," + MetaType.QtName(typeof(T2)) + ")";
        }

        public void Connect(QObject sender, Action<T1, T2> handler)
            => ApiRuntime.Connect(sender.Handle, Signature,
                (argv, _) => handler(MetaType.Read<T1>(argv, 0), MetaType.Read<T2>(argv, 1)));

        public void Emit(QObject sender, T1 a1, T2 a2)
            => ApiRuntime.EmitUser(sender.Handle, Signature, new[] { typeof(T1), typeof(T2) }, new object[] { a1, a2 });
    }

    public sealed class QMetaSignal<T1, T2, T3> : QMetaSignalBase
    {
        public QMetaSignal(string name)
        {
            Name = name;
            Signature = name + "(" + MetaType.QtName(typeof(T1)) + "," + MetaType.QtName(typeof(T2))
                      + "," + MetaType.QtName(typeof(T3)) + ")";
        }

        public void Connect(QObject sender, Action<T1, T2, T3> handler)
            => ApiRuntime.Connect(sender.Handle, Signature,
                (argv, _) => handler(MetaType.Read<T1>(argv, 0), MetaType.Read<T2>(argv, 1), MetaType.Read<T3>(argv, 2)));

        public void Emit(QObject sender, T1 a1, T2 a2, T3 a3)
            => ApiRuntime.EmitUser(sender.Handle, Signature, new[] { typeof(T1), typeof(T2), typeof(T3) }, new object[] { a1, a2, a3 });
    }

    public sealed class QMetaSignal<T1, T2, T3, T4> : QMetaSignalBase
    {
        public QMetaSignal(string name)
        {
            Name = name;
            Signature = name + "(" + MetaType.QtName(typeof(T1)) + "," + MetaType.QtName(typeof(T2))
                      + "," + MetaType.QtName(typeof(T3)) + "," + MetaType.QtName(typeof(T4)) + ")";
        }

        public void Connect(QObject sender, Action<T1, T2, T3, T4> handler)
            => ApiRuntime.Connect(sender.Handle, Signature, (argv, _) => handler(
                MetaType.Read<T1>(argv, 0), MetaType.Read<T2>(argv, 1),
                MetaType.Read<T3>(argv, 2), MetaType.Read<T4>(argv, 3)));

        public void Emit(QObject sender, T1 a1, T2 a2, T3 a3, T4 a4)
            => ApiRuntime.EmitUser(sender.Handle, Signature, new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) }, new object[] { a1, a2, a3, a4 });
    }

    // A user-declared property: the getter/setter are ordinary C# delegates, and the runtime calls
    // them when Qt asks for the value (QObject::property, a QML binding, a data widget mapper...).
    public abstract class QMetaPropertyBase
    {
        internal string Name;
        internal string TypeName;
        internal abstract void Get(object target, IntPtr value);
        internal abstract void Set(object target, IntPtr value);
    }

    public sealed class QMetaProperty<T> : QMetaPropertyBase
    {
        readonly Func<object, T> read;
        readonly Action<object, T> write;

        public QMetaProperty(string name, Func<object, T> read, Action<object, T> write = null)
        {
            Name = name;
            TypeName = MetaType.QtName(typeof(T));
            this.read = read;
            this.write = write;
        }

        internal override void Get(object target, IntPtr value) => MetaType.Write(TypeName, value, read(target));

        internal override void Set(object target, IntPtr value)
        {
            if (write != null)
                write(target, MetaType.Read<T>(value));
        }
    }

    // The C# type -> Qt type name map the declared members use, plus the conversions between a
    // managed value and the native storage moc's qt_metacall would have used.
    public static class MetaType
    {
        public static string QtName(Type t)
        {
            if (t == typeof(string)) return "QString";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int)) return "int";
            if (t == typeof(uint)) return "uint";
            if (t == typeof(short)) return "short";
            if (t == typeof(ushort)) return "ushort";
            if (t == typeof(long)) return "qint64";
            if (t == typeof(ulong)) return "quint64";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(byte)) return "uchar";
            if (t == typeof(sbyte)) return "char";
            if (t == typeof(IntPtr)) return "void*";
            if (t.IsEnum)
            {
                // A nested enum surfaces as Qt::ItemDataRole / QAbstractItemDelegate::EndEditHint,
                // which is the name QMetaType registered it under.
                var scope = t.DeclaringType;
                return scope == null ? t.Name : scope.Name + "::" + t.Name;
            }
            // A bound class crosses as its pointer; the runtime wraps it with the generated
            // FromHandle the type publishes.
            if (typeof(IQtHandle).IsAssignableFrom(t))
                return t.Name + "*";
            return t.Name;
        }

        /// <summary>Reads the i-th signal argument out of moc's argv.</summary>
        public static T Read<T>(IntPtr argv, int index)
            => (T)FromStorage(typeof(T), Marshal.ReadIntPtr(argv, index * IntPtr.Size));

        /// <summary>Reads a value out of a property value slot.</summary>
        internal static T Read<T>(IntPtr value) => (T)FromStorage(typeof(T), value);

        static readonly Dictionary<Type, System.Reflection.MethodInfo> fromHandle = new();

        // The generated wrapper's FromHandle (an object pointer) or FromTransientHandle (a boxed
        // value), looked up once per type.
        static System.Reflection.MethodInfo WrapperFactory(Type t, bool pointer)
        {
            lock (fromHandle)
            {
                if (fromHandle.TryGetValue(t, out var method))
                    return method;
                method = pointer
                    ? t.GetMethod("FromHandle", new[] { typeof(IntPtr) })
                    : t.GetMethod("FromTransientHandle",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                      ?? t.GetMethod("FromHandle", new[] { typeof(IntPtr) });
                fromHandle[t] = method;
                return method;
            }
        }

        static object FromStorage(Type t, IntPtr storage)
        {
            if (t == typeof(string)) return ApiRuntime.ToManagedTransient(storage);
            if (t == typeof(bool)) return Marshal.ReadByte(storage) != 0;
            if (t == typeof(int)) return Marshal.ReadInt32(storage);
            if (t == typeof(uint)) return (uint)Marshal.ReadInt32(storage);
            if (t == typeof(short)) return Marshal.ReadInt16(storage);
            if (t == typeof(ushort)) return (ushort)Marshal.ReadInt16(storage);
            if (t == typeof(long)) return Marshal.ReadInt64(storage);
            if (t == typeof(ulong)) return (ulong)Marshal.ReadInt64(storage);
            if (t == typeof(float)) return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(storage));
            if (t == typeof(double)) return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(storage));
            if (t == typeof(byte)) return Marshal.ReadByte(storage);
            if (t == typeof(sbyte)) return (sbyte)Marshal.ReadByte(storage);
            if (t == typeof(IntPtr)) return Marshal.ReadIntPtr(storage);
            if (t.IsEnum) return Enum.ToObject(t, Marshal.ReadInt32(storage));
            if (typeof(IQtHandle).IsAssignableFrom(t))
            {
                // A pointer argument: the storage holds the pointer itself.
                IntPtr handle = Marshal.ReadIntPtr(storage);
                if (handle == IntPtr.Zero)
                    return null;
                return WrapperFactory(t, true).Invoke(null, new object[] { handle });
            }
            // A boxed value (QModelIndex): the storage *is* the object.
            return WrapperFactory(t, false).Invoke(null, new object[] { storage });
        }

        /// <summary>Writes a managed value into a native value slot (a property read).</summary>
        public static void Write(string qtType, IntPtr destination, object value)
        {
            if (qtType == "QString") { ApiRuntime.WriteString(destination, (string)value); return; }
            if (qtType == "bool") { Marshal.WriteByte(destination, (bool)value ? (byte)1 : (byte)0); return; }
            if (qtType == "int" || qtType == "uint" || qtType == "short" || qtType == "ushort" || qtType == "uchar")
            { Marshal.WriteInt32(destination, Convert.ToInt32(value)); return; }
            if (qtType == "double") { Marshal.WriteInt64(destination, BitConverter.DoubleToInt64Bits((double)value)); return; }
            if (qtType == "float") { Marshal.WriteInt32(destination, BitConverter.SingleToInt32Bits((float)value)); return; }
            if (qtType == "qint64" || qtType == "quint64") { Marshal.WriteInt64(destination, Convert.ToInt64(value)); return; }
            if (qtType == "void*") { Marshal.WriteIntPtr(destination, (value as IQtHandle)?.Handle ?? (IntPtr)value); return; }
            if (value is IQtHandle handle) { Marshal.WriteIntPtr(destination, handle.Handle); return; }
            if (value != null && value.GetType().IsEnum) { Marshal.WriteInt32(destination, Convert.ToInt32(value)); return; }
            throw new NotSupportedException($"A QMetaProperty of type {qtType} is not supported");
        }
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
                // A C#-declared member table is only meaningful while its object is alive.
                metaByHandle.Remove(handle);
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

        /// <summary>
        /// Delivers a synthetic wheel turn to a widget, <paramref name="degrees"/> being how far
        /// the wheel turned (Qt carries eighths of a degree in the event, and so does this).
        /// </summary>
        public static bool SendWheel(IntPtr widgetHandle, int x, int y, int degrees, int modifiers = 0)
            => ProbeWheel(widgetHandle, x, y, degrees, modifiers) != 0;

        /// <summary>Delivers a synthetic key press (the key code is a Qt::Key value).</summary>
        public static bool SendKeyPress(IntPtr widgetHandle, int key, int modifiers = 0)
            => ProbeKeyPress(widgetHandle, key, modifiers) != 0;

        /// <summary>A string into a native value slot (a property read hands Qt the value).</summary>
        public static void WriteString(IntPtr destination, string value)
            => WriteQStringNative(destination, value ?? string.Empty);

        // ------------------------------------------------------------------ user-declared members
        static readonly Dictionary<Type, MetaObjectSpec> metaSpecs = new();
        static readonly Dictionary<IntPtr, MetaObjectSpec> metaByHandle = new();
        static bool metaPropertyCallbackInstalled;

        sealed class MetaObjectSpec
        {
            public string Name;
            public List<QMetaSignalBase> Signals = new();
            public List<QMetaPropertyBase> Properties = new();
        }

        /// <summary>
        /// Gives an object the signals and properties its C# class declared (see QMetaSignal /
        /// QMetaProperty). Called by every generated constructor; it costs a cached type lookup for
        /// the overwhelming majority of types, which declare nothing.
        /// </summary>
        public static void AttachMetaObject(object target)
        {
            // Only a QObject has a meta-object to extend; the interface alone is not enough (the
            // generated value types implement IQtHandle too).
            if (shuttingDown || target is not QObject || target is not IQtHandle handle)
                return;
            var type = target.GetType();
            MetaObjectSpec spec;
            lock (metaSpecs)
            {
                if (!metaSpecs.TryGetValue(type, out spec))
                {
                    spec = BuildSpec(type);
                    metaSpecs[type] = spec;
                }
            }
            if (spec == null)
                return;

            if (spec.Properties.Count > 0 && !metaPropertyCallbackInstalled)
            {
                unsafe
                {
                    SetMetaPropertyCallback(
                        (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, void>)&OnMetaProperty);
                }
                metaPropertyCallbackInstalled = true;
            }

            var signatures = new string[spec.Signals.Count];
            for (int i = 0; i < signatures.Length; i++)
                signatures[i] = spec.Signals[i].Signature;
            var names = new string[spec.Properties.Count];
            var types = new string[spec.Properties.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = spec.Properties[i].Name;
                types[i] = spec.Properties[i].TypeName;
            }
            if (AttachNative(handle.Handle, spec.Name, signatures, signatures.Length, names, types, types.Length) != 0)
            {
                lock (metaByHandle)
                    metaByHandle[handle.Handle] = spec;
            }
        }

        static MetaObjectSpec BuildSpec(Type type)
        {
            var spec = new MetaObjectSpec { Name = type.Name };
            foreach (var field in type.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.FlattenHierarchy))
            {
                var value = field.GetValue(null);
                if (value is QMetaSignalBase signal)
                    spec.Signals.Add(signal);
                else if (value is QMetaPropertyBase property)
                    spec.Properties.Add(property);
            }
            return spec.Signals.Count == 0 && spec.Properties.Count == 0 ? null : spec;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void OnMetaProperty(IntPtr handle, int index, int write, IntPtr value)
        {
            MetaObjectSpec spec;
            lock (metaByHandle)
            {
                if (!metaByHandle.TryGetValue(handle, out spec))
                    return;
            }
            if (index < 0 || index >= spec.Properties.Count)
                return;
            var target = LookupWrapper(handle);
            if (target == null)
                return;
            if (write != 0)
                spec.Properties[index].Set(target, value);
            else
                spec.Properties[index].Get(target, value);
        }

        internal static object LookupWrapper(IntPtr handle)
        {
            if (wrappers.TryGetValue(handle, out var list))
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].Target is object alive)
                        return alive;
            return null;
        }

        /// <summary>
        /// Activates a signal a C# class declared. The managed values become the native argument
        /// storage the moc-generated signal function would have had on its stack.
        /// </summary>
        public static unsafe bool EmitUser(IntPtr sender, string signature, Type[] types, object[] args)
        {
            int count = args.Length;
            var storage = new IntPtr[count];
            // A QString comes from NewQString (a heap QString) and the rest from AllocHGlobal, so
            // the two kinds are released differently.
            var qstrings = new List<IntPtr>();
            var blocks = new List<IntPtr>();
            IntPtr argv = IntPtr.Zero;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (types[i] == typeof(string))
                    {
                        var text = NewQString((string)args[i]);
                        storage[i] = text;
                        qstrings.Add(text);
                    }
                    else if (typeof(IQtHandle).IsAssignableFrom(types[i]))
                    {
                        // A pointer argument: argv[i] points at a pointer variable.
                        var slot = Marshal.AllocHGlobal(IntPtr.Size);
                        Marshal.WriteIntPtr(slot, args[i] == null ? IntPtr.Zero : ((IQtHandle)args[i]).Handle);
                        storage[i] = slot;
                        blocks.Add(slot);
                    }
                    else
                    {
                        var slot = Marshal.AllocHGlobal(8);
                        Marshal.WriteInt64(slot, 0);
                        MetaType.Write(MetaType.QtName(types[i]), slot, args[i]);
                        storage[i] = slot;
                        blocks.Add(slot);
                    }
                }
                argv = Marshal.AllocHGlobal(Math.Max(count, 1) * IntPtr.Size);
                for (int i = 0; i < count; i++)
                    Marshal.WriteIntPtr(argv, i * IntPtr.Size, storage[i]);
                return Emit(sender, signature, argv, count);
            }
            finally
            {
                if (argv != IntPtr.Zero)
                    Marshal.FreeHGlobal(argv);
                foreach (var text in qstrings)
                    FreeQString(text);
                foreach (var block in blocks)
                    Marshal.FreeHGlobal(block);
            }
        }

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_attach_metaobject", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AttachNative(IntPtr obj, [MarshalAs(UnmanagedType.LPStr)] string className,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] signals, int signalCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] propertyNames,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] propertyTypes,
            int propertyCount);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_set_meta_property_callback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetMetaPropertyCallback(IntPtr callback);

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

        // The native forwarder hands over moc's raw argument array; the generated binding reads
        // each slot with the type it knows, so nothing has to be guessed from a meta-type id.
        // The callback has to stay alive for as long as Qt holds the connection.
        public delegate void SignalSlot(IntPtr argv, IntPtr typeIds);

        public static bool Connect(IntPtr sender, string signature, SignalSlot handler)
        {
            SignalCallback callback = (userData, argc, argv, typeIds) => handler(argv, typeIds);
            keepAlive.Add(callback);
            return ConnectNative(sender, signature, callback, IntPtr.Zero, 0) != 0;
        }

        /// <summary>
        /// Activates a signal. <paramref name="args"/> points at one slot per argument, each
        /// holding the address of the argument storage - exactly the argv a moc-generated signal
        /// function would pass.
        /// </summary>
        public static bool Emit(IntPtr sender, string signature, IntPtr args, int argc)
            => EmitNative(sender, signature, args, argc) != 0;

        // A native QString built from a managed string, for the duration of an emit. The caller
        // frees it (the generated emit closure does so in a finally).
        public static IntPtr NewQString(string value)
            => QStringFromUtf16(value ?? string.Empty);

        public static void FreeQString(IntPtr value)
        {
            if (value != IntPtr.Zero)
                StringFree(value);
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

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_emit", CallingConvention = CallingConvention.Cdecl)]
        private static extern int EmitNative(IntPtr sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string signature,
            IntPtr args, int argc);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_qstring_from_utf16", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QStringFromUtf16([MarshalAs(UnmanagedType.LPWStr)] string text);

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

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_wheel", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeWheel(IntPtr widgetHandle, int x, int y, int degrees, int modifiers);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_key_press", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeKeyPress(IntPtr widgetHandle, int key, int modifiers);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_app_create", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AppCreate();

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_app_exec", CallingConvention = CallingConvention.Cdecl)]
        private static extern int AppExec();

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_app_quit", CallingConvention = CallingConvention.Cdecl)]
        private static extern void AppQuit();
    }
}

