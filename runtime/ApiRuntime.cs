using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.CodeDom.Compiler;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

// Hand-written runtime that ships with the generated bindings (not model-derived).
// It provides the pieces the PoCs proved: a signal wrapper on top of the generic
// moc-metadata forwarder, the QString bridge and the object wrapper cache.
namespace QtSharp.Runtime
{
    // A Qt signal, with the arity in the type so both halves are typed: `Connect(Action<int,string>)`
    // and `Emit(42, "x")`. The generated binding supplies the two closures (which marshal each
    // parameter the way Qt declared it); a user-declared signal is built by ApiRuntime.NewSignal.
    //
    // The generated binding is what produces the native argument storage, because only it knows
    // whether a parameter is a bound class pointer, a boxed value or a container - the same
    // distinction the virtual dispatch makes.
    //
    // `Connect` hands back a QtConnection: the binding's closure opens the native connection and
    // remembers the handler as its key, and the handle drops exactly that connection on Dispose.
    // The context/connection-type overloads live in QtSharp.QtCore (QSignalConnections), because
    // naming QObject and Qt::ConnectionType would make this runtime depend on the binding.
    public sealed class QSignal
    {
        readonly Func<Action, IQtObject, int, QtConnection> connector;
        readonly Action emitter;

        public QSignal(Func<Action, IQtObject, int, QtConnection> connector, Action emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit() => emitter();

        internal QtConnection ConnectCore(Action handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    public sealed class QSignal<T1>
    {
        readonly Func<Action<T1>, IQtObject, int, QtConnection> connector;
        readonly Action<T1> emitter;

        public QSignal(Func<Action<T1>, IQtObject, int, QtConnection> connector, Action<T1> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action<T1> handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit(T1 a1) => emitter(a1);

        internal QtConnection ConnectCore(Action<T1> handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    public sealed class QSignal<T1, T2>
    {
        readonly Func<Action<T1, T2>, IQtObject, int, QtConnection> connector;
        readonly Action<T1, T2> emitter;

        public QSignal(Func<Action<T1, T2>, IQtObject, int, QtConnection> connector, Action<T1, T2> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action<T1, T2> handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit(T1 a1, T2 a2) => emitter(a1, a2);

        internal QtConnection ConnectCore(Action<T1, T2> handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    public sealed class QSignal<T1, T2, T3>
    {
        readonly Func<Action<T1, T2, T3>, IQtObject, int, QtConnection> connector;
        readonly Action<T1, T2, T3> emitter;

        public QSignal(Func<Action<T1, T2, T3>, IQtObject, int, QtConnection> connector, Action<T1, T2, T3> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action<T1, T2, T3> handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit(T1 a1, T2 a2, T3 a3) => emitter(a1, a2, a3);

        internal QtConnection ConnectCore(Action<T1, T2, T3> handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    public sealed class QSignal<T1, T2, T3, T4>
    {
        readonly Func<Action<T1, T2, T3, T4>, IQtObject, int, QtConnection> connector;
        readonly Action<T1, T2, T3, T4> emitter;

        public QSignal(Func<Action<T1, T2, T3, T4>, IQtObject, int, QtConnection> connector, Action<T1, T2, T3, T4> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action<T1, T2, T3, T4> handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4) => emitter(a1, a2, a3, a4);

        internal QtConnection ConnectCore(Action<T1, T2, T3, T4> handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    public sealed class QSignal<T1, T2, T3, T4, T5>
    {
        readonly Func<Action<T1, T2, T3, T4, T5>, IQtObject, int, QtConnection> connector;
        readonly Action<T1, T2, T3, T4, T5> emitter;

        public QSignal(Func<Action<T1, T2, T3, T4, T5>, IQtObject, int, QtConnection> connector, Action<T1, T2, T3, T4, T5> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action<T1, T2, T3, T4, T5> handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5) => emitter(a1, a2, a3, a4, a5);

        internal QtConnection ConnectCore(Action<T1, T2, T3, T4, T5> handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    public sealed class QSignal<T1, T2, T3, T4, T5, T6>
    {
        readonly Func<Action<T1, T2, T3, T4, T5, T6>, IQtObject, int, QtConnection> connector;
        readonly Action<T1, T2, T3, T4, T5, T6> emitter;

        public QSignal(Func<Action<T1, T2, T3, T4, T5, T6>, IQtObject, int, QtConnection> connector, Action<T1, T2, T3, T4, T5, T6> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action<T1, T2, T3, T4, T5, T6> handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6) => emitter(a1, a2, a3, a4, a5, a6);

        internal QtConnection ConnectCore(Action<T1, T2, T3, T4, T5, T6> handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    public sealed class QSignal<T1, T2, T3, T4, T5, T6, T7>
    {
        readonly Func<Action<T1, T2, T3, T4, T5, T6, T7>, IQtObject, int, QtConnection> connector;
        readonly Action<T1, T2, T3, T4, T5, T6, T7> emitter;

        public QSignal(Func<Action<T1, T2, T3, T4, T5, T6, T7>, IQtObject, int, QtConnection> connector, Action<T1, T2, T3, T4, T5, T6, T7> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action<T1, T2, T3, T4, T5, T6, T7> handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7) => emitter(a1, a2, a3, a4, a5, a6, a7);

        internal QtConnection ConnectCore(Action<T1, T2, T3, T4, T5, T6, T7> handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    public sealed class QSignal<T1, T2, T3, T4, T5, T6, T7, T8>
    {
        readonly Func<Action<T1, T2, T3, T4, T5, T6, T7, T8>, IQtObject, int, QtConnection> connector;
        readonly Action<T1, T2, T3, T4, T5, T6, T7, T8> emitter;

        public QSignal(Func<Action<T1, T2, T3, T4, T5, T6, T7, T8>, IQtObject, int, QtConnection> connector, Action<T1, T2, T3, T4, T5, T6, T7, T8> emitter)
        {
            this.connector = connector;
            this.emitter = emitter;
        }

        public QtConnection Connect(Action<T1, T2, T3, T4, T5, T6, T7, T8> handler) => connector(handler, null, ApiRuntime.AutoConnection);
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8) => emitter(a1, a2, a3, a4, a5, a6, a7, a8);

        internal QtConnection ConnectCore(Action<T1, T2, T3, T4, T5, T6, T7, T8> handler, IQtObject context, int connectionType)
            => connector(handler, context, connectionType);
    }

    // The disposable handle `QSignal.Connect` returns. It owns one connection record, so Dispose
    // disconnects that exact connection - not "the most recent one with this handler" - and drops
    // the record, which releases the managed thunk Qt was calling. Disposing twice is a no-op.
    // A connection Qt has already broken (the sender or the context was destroyed) still disposes
    // cleanly: QObject::disconnect reports false and the native handle is freed either way.
    public sealed class QtConnection : IDisposable
    {
        ApiRuntime.SignalConnection connection;

        internal QtConnection(ApiRuntime.SignalConnection connection) => this.connection = connection;

        /// <summary>Whether this handle still refers to a connection Qt has not dropped.</summary>
        public bool IsConnected => connection != null && !connection.Dropped && connection.Native != IntPtr.Zero;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref connection, null);
            if (current != null)
                ApiRuntime.DisconnectConnection(current);
        }
    }

    // A managed view of a Qt object. Qt owns the lifetime (a parent, a scene, the application),
    // so the wrapper cannot free it - but it has to stop working the moment Qt destroys it,
    // instead of calling into native memory that is gone. Every generated class implements this
    // through its root class.
    // Public, not internal: a separately packaged extension module (QtSharp.Charts and the ones
    // after it) is a different assembly whose generated classes implement this too.
    public interface IQtObjectView
    {
        void MarkDestroyed();

        /// <summary>Whether this wrapper is already known to be dead (Qt destroyed it, or it was disposed).</summary>
        bool IsInvalidated { get; }
    }

    // The generated root class implements this, which is what lets a *user-declared* signal or
    // property carry a bound class (as its native pointer) without the runtime having to know the
    // type at compile time.
    public interface IQtHandle
    {
        IntPtr Handle { get; }
    }

    // Implemented by the generated value types Qt itself converts into a QVariant (QColor, QBrush,
    // QIcon, ...). The runtime QVariant dispatch asks the value for its C++ type name and hands
    // the pointer to QMetaType; there is no per-type constructor to bind. The name is the
    // qualified C++ spelling, because that is what QMetaType::type() resolves.
    public interface IQtVariantValue : IQtHandle
    {
        string QtTypeName { get; }
    }

    // Implemented by QObject and its subclasses (the generated root adds it), not by a plain
    // wrapper such as QGraphicsItem: only the former has a meta-object to extend. It lives in the
    // runtime so the runtime can recognize a QObject without referencing the QtCore binding.
    public interface IQtObject : IQtHandle
    {
    }

    // ------------------------------------------------------------------ user-declared members
    // A C# class declares its own signals and properties (the pyqtSignal/Property equivalent) by
    // putting an attribute on the member. The member is the declaration:
    //
    //     public sealed class Counter : QObject
    //     {
    //         [QtSignal] public event Action<int> ValueChanged;
    //         [QtProperty] public int Value { get; set; }
    //     }
    //
    // ApiRuntime.AttachMetaObject (called by every generated constructor) reflects over the
    // attributed members of the concrete type and its user base classes, builds a meta-object with
    // the extra members, and installs it on the object. From then on they are ordinary Qt
    // members: indexOfSignal finds them, connect() works, and the property read/write goes
    // through QObject::property/setProperty.

    // The QMetaProperty flags, as the native meta-object builder takes them. The C# names match
    // QMetaProperty's is*() accessors; Readable and Writable mirror READ/WRITE, and the rest are
    // the Q_PROPERTY keywords of the same names.
    internal static class MetaPropertyFlags
    {
        public const int Readable = 1 << 0;
        public const int Writable = 1 << 1;
        public const int Resettable = 1 << 2;
        public const int Designable = 1 << 3;
        public const int Scriptable = 1 << 4;
        public const int Stored = 1 << 5;
        public const int User = 1 << 6;
        public const int Constant = 1 << 7;
        public const int Final = 1 << 8;
        public const int Required = 1 << 9;

        public const int Defaults = Readable | Writable | Designable | Scriptable | Stored;
    }

    // A declared property: the C# PropertyInfo in AttributedProperty is the accessor pair, and the
    // runtime calls it when Qt asks for the value (QObject::property, a QML binding, a data widget
    // mapper...).
    internal abstract class QMetaPropertyBase
    {
        internal string Name;
        internal string TypeName;

        // The rest of the Q_PROPERTY surface, handed to the native builder. NotifyIndex is
        // resolved from the declaration while the spec is built.
        internal virtual int Flags => MetaPropertyFlags.Defaults;
        internal virtual int Revision => 0;
        internal int NotifyIndex = -1;

        internal abstract void Get(object target, IntPtr value);
        internal abstract void Set(object target, IntPtr value);
        internal virtual void Reset(object target) { }
    }

    // Declaring a member: put the attribute on the C# member whose accessors (or handler list) are
    // already the property (or signal). The member *is* the declaration, so there is no second
    // place to keep in sync:
    //
    //     [QtProperty] public double MinValue { get => GetMinValue(); set => SetMinValue(value); }
    //     [QtSignal]   public event Action<double> ValueChanged;
    //
    // AttachMetaObject discovers them while it builds the meta-object. The Qt name is the member's
    // name with a lower-case first letter ("minValue", "valueChanged"); the constructor argument
    // overrides it when a name differs.
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class QtPropertyAttribute : Attribute
    {
        public QtPropertyAttribute() { }
        public QtPropertyAttribute(string name) => Name = name;

        /// <summary>The Qt property name; null derives it from the C# member name.</summary>
        public string Name { get; }

        /// <summary>
        /// The signal that tells Qt the property changed (Q_PROPERTY's NOTIFY). Name a declared
        /// signal by its C# member name or its Qt name; when omitted, a signal named
        /// "&lt;Property&gt;Changed" is linked if the class declares one. The property's setter
        /// still emits the signal - NOTIFY only tells bindings and QML which signal to watch.
        /// </summary>
        public string Notify { get; set; }

        /// <summary>
        /// The parameterless method QMetaProperty::reset() calls (Q_PROPERTY's RESET). When
        /// omitted, a method named "Reset&lt;Property&gt;" is used if one exists. It is not run
        /// automatically; the method puts the property back to its default.
        /// </summary>
        public string Reset { get; set; }

        /// <summary>Q_PROPERTY's DESIGNABLE (default true).</summary>
        public bool Designable { get; set; } = true;

        /// <summary>Q_PROPERTY's SCRIPTABLE (default true).</summary>
        public bool Scriptable { get; set; } = true;

        /// <summary>Q_PROPERTY's STORED (default true).</summary>
        public bool Stored { get; set; } = true;

        /// <summary>Q_PROPERTY's USER.</summary>
        public bool User { get; set; }

        /// <summary>Q_PROPERTY's CONSTANT.</summary>
        public bool Constant { get; set; }

        /// <summary>Q_PROPERTY's FINAL.</summary>
        public bool Final { get; set; }

        /// <summary>Q_PROPERTY's REQUIRED.</summary>
        public bool Required { get; set; }

        /// <summary>Q_PROPERTY's REVISION; 0 means the property carries no revision.</summary>
        public int Revision { get; set; }
    }

    [AttributeUsage(AttributeTargets.Event, AllowMultiple = false, Inherited = true)]
    public sealed class QtSignalAttribute : Attribute
    {
        public QtSignalAttribute() { }
        public QtSignalAttribute(string name) => Name = name;

        /// <summary>The Qt signal name; null derives it from the C# member name.</summary>
        public string Name { get; }
    }

    // The method half of the same idea: the C# method is the implementation, and the attribute
    // puts it in the meta-object as a real Qt slot, so QMetaObject::invokeMethod and
    // QMetaMethod::invoke reach it, connectSlotsByName can connect it, and a name-based connect
    // sees it like any moc-declared slot. (Not a `[QtNativeSlot]`: this is a *declaration*, the
    // opposite of the read-side attributes below.)
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class QtSlotAttribute : Attribute
    {
        public QtSlotAttribute() { }
        public QtSlotAttribute(string name) => Name = name;

        /// <summary>The Qt slot name; null derives it from the C# member name.</summary>
        public string Name { get; }
    }

    // The *generated* members already exist in moc's meta-object, so they are not declarations and
    // must not be passed to AttachMetaObject. These attributes are the read side of that: tooling,
    // documentation and a caller reflecting over a generated wrapper can tell a native Q_PROPERTY
    // or signal from one a C# subclass declared with [QtProperty]/[QtSignal].
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class QtNativePropertyAttribute : Attribute
    {
        public QtNativePropertyAttribute(string name, string type, bool writable)
        {
            Name = name;
            Type = type;
            Writable = writable;
        }

        /// <summary>The Q_PROPERTY name as Qt spells it (the C# member is PascalCase).</summary>
        public string Name { get; }

        /// <summary>The Qt type as the model spells it (QString, Qt::Alignment, QList&lt;...&gt;).</summary>
        public string Type { get; }

        /// <summary>Whether the generated property has a setter (Q_PROPERTY's WRITE).</summary>
        public bool Writable { get; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
    public sealed class QtNativeSignalAttribute : Attribute
    {
        public QtNativeSignalAttribute(string signature) => Signature = signature;

        /// <summary>The moc signature (clicked(bool), valueChanged(QString)).</summary>
        public string Signature { get; }
    }

    // An [QtProperty] C# property: the PropertyInfo is the accessor pair, so an auto-property and
    // a hand-written one (get => GetMinValue(), set => SetMinValue(value)) work the same.
    sealed class AttributedProperty : QMetaPropertyBase
    {
        readonly PropertyInfo property;
        readonly QtPropertyAttribute declaration;

        public AttributedProperty(PropertyInfo property, string name, QtPropertyAttribute declaration)
        {
            this.property = property;
            this.declaration = declaration;
            Name = name;
            TypeName = MetaType.QtName(property.PropertyType);
        }

        public PropertyInfo Info => property;
        public string NotifyName => declaration.Notify;
        public string ResetName => declaration.Reset;
        public MethodInfo ResetMethod;

        internal override int Flags
        {
            get
            {
                var flags = MetaPropertyFlags.Readable
                            | (property.CanWrite ? MetaPropertyFlags.Writable : 0)
                            | (ResetMethod != null ? MetaPropertyFlags.Resettable : 0)
                            | (declaration.Designable ? MetaPropertyFlags.Designable : 0)
                            | (declaration.Scriptable ? MetaPropertyFlags.Scriptable : 0)
                            | (declaration.Stored ? MetaPropertyFlags.Stored : 0)
                            | (declaration.User ? MetaPropertyFlags.User : 0)
                            | (declaration.Constant ? MetaPropertyFlags.Constant : 0)
                            | (declaration.Final ? MetaPropertyFlags.Final : 0)
                            | (declaration.Required ? MetaPropertyFlags.Required : 0);
                return flags;
            }
        }

        internal override int Revision => declaration.Revision;

        internal override void Get(object target, IntPtr value)
            => MetaType.Write(TypeName, value, property.GetValue(target));

        internal override void Set(object target, IntPtr value)
            => property.SetValue(target, MetaType.ReadValue(property.PropertyType, value));

        internal override void Reset(object target) => ResetMethod?.Invoke(target, null);
    }

    // An [QtSignal] C# event. A field-like event keeps its subscribers in a compiler-generated
    // delegate field that has the event's name; the native connection installed at attach time
    // reads that field, so a C# emit, a C++ emit and a QML emit all reach the same handlers.
    //
    // The field also starts with a trampoline (see SignalTrampoline), so `ValueChanged?.Invoke(v)`
    // inside the declaring class activates the Qt signal instead of only running the C# side.
    // EmitSignal stays as the explicit spelling of the same activation.
    sealed class AttributedSignal
    {
        public EventInfo Info;
        public string Name;          // the Qt signal name (the event name or the attribute's)
        public string Signature;
        public Type[] Parameters;
        FieldInfo subscribers;
        Type trampolineType;
        MethodInfo trampolineEmit;

        public static AttributedSignal Create(EventInfo info, string name)
        {
            var invoke = info.EventHandlerType?.GetMethod("Invoke");
            if (invoke == null)
                throw new InvalidOperationException(
                    $"[QtSignal] event '{info.DeclaringType}.{info.Name}' is not a delegate type");
            if (invoke.ReturnType != typeof(void))
                throw new InvalidOperationException(
                    $"[QtSignal] event '{info.DeclaringType}.{info.Name}' returns a value, "
                    + "but a Qt signal carries no result");

            var declared = invoke.GetParameters();
            var parameters = new Type[declared.Length];
            for (int i = 0; i < declared.Length; i++)
            {
                if (declared[i].ParameterType.IsByRef || declared[i].IsOut)
                    throw new InvalidOperationException(
                        $"[QtSignal] event '{info.DeclaringType}.{info.Name}' has a ref/out parameter, "
                        + "which a Qt signal cannot carry");
                parameters[i] = declared[i].ParameterType;
            }

            // A field-like event's backing field is private and has the event's name; a
            // hand-written add/remove leaves no handler list to read, so it cannot be a Qt signal.
            var field = info.DeclaringType.GetField(info.Name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null || !typeof(Delegate).IsAssignableFrom(field.FieldType))
                throw new InvalidOperationException(
                    $"[QtSignal] event '{info.DeclaringType}.{info.Name}' must be a field-like event "
                    + "(hand-written add/remove leaves the runtime no subscriber list to call)");

            var signature = new System.Text.StringBuilder(name);
            signature.Append('(');
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                    signature.Append(',');
                signature.Append(MetaType.QtName(parameters[i]));
            }
            signature.Append(')');

            var trampolineType = TrampolineType(info, parameters);
            return new AttributedSignal
            {
                Info = info,
                Name = name,
                Signature = signature.ToString(),
                Parameters = parameters,
                subscribers = field,
                trampolineType = trampolineType,
                trampolineEmit = trampolineType.GetMethod("Emit"),
            };
        }

        // The arity classes SignalTrampoline<...> publish, the same limit QSignal stops at.
        static Type TrampolineType(EventInfo info, Type[] parameters)
        {
            Type definition;
            switch (parameters.Length)
            {
                case 0: return typeof(SignalTrampoline0);
                case 1: definition = typeof(SignalTrampoline<>); break;
                case 2: definition = typeof(SignalTrampoline<,>); break;
                case 3: definition = typeof(SignalTrampoline<,,>); break;
                case 4: definition = typeof(SignalTrampoline<,,,>); break;
                case 5: definition = typeof(SignalTrampoline<,,,,>); break;
                case 6: definition = typeof(SignalTrampoline<,,,,,>); break;
                case 7: definition = typeof(SignalTrampoline<,,,,,,>); break;
                case 8: definition = typeof(SignalTrampoline<,,,,,,,>); break;
                default:
                    throw new InvalidOperationException(
                        $"[QtSignal] event '{info.DeclaringType}.{info.Name}' has {parameters.Length} "
                        + "parameters; the runtime supports at most 8");
            }
            return definition.MakeGenericType(parameters);
        }

        // Seeds the emit trampoline into the object's event field, in front of whatever handlers
        // are already there. Called once per object while its meta-object is attached.
        public void Seed(object target, IQtObject sender)
        {
            var current = subscribers.GetValue(target) as Delegate;
            if (HasTrampoline(current))
                return;
            var trampoline = Delegate.CreateDelegate(Info.EventHandlerType,
                Activator.CreateInstance(trampolineType, this, sender), trampolineEmit);
            subscribers.SetValue(target, Delegate.Combine(trampoline, current));
        }

        static bool HasTrampoline(Delegate handlers)
        {
            if (handlers == null)
                return false;
            foreach (var handler in handlers.GetInvocationList())
                if (handler.Target is SignalTrampoline)
                    return true;
            return false;
        }

        // What the trampoline calls. The activation marker tells the native connection that this
        // emission already ran the C# handlers (the delegate chain that invoked the trampoline runs
        // them next), so it must not deliver them a second time.
        internal void EmitFromTrampoline(IQtObject sender, object[] args)
        {
            if (args.Length != Parameters.Length)
                throw new ArgumentException(
                    $"'{sender.GetType().Name}.{Info.Name}' carries {Parameters.Length} argument(s), "
                    + $"but {args.Length} were emitted", nameof(args));
            ApiRuntime.ActivateDeclared(sender.Handle, Signature, Parameters, args);
        }

        public void Invoke(object target, IntPtr argv)
        {
            if (subscribers.GetValue(target) is not Delegate handlers)
                return;
            // The trampoline is the emit side, not a subscriber: a signal Qt activates calls only
            // the handlers the C# side added.
            var invocation = handlers.GetInvocationList();
            if (Array.Exists(invocation, handler => handler.Target is SignalTrampoline))
            {
                var subscribed = Array.FindAll(invocation, handler => handler.Target is not SignalTrampoline);
                if (subscribed.Length == 0)
                    return;
                handlers = Delegate.Combine(subscribed);
            }
            var args = new object[Parameters.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = MetaType.ReadArgument(Parameters[i], argv, i);
            handlers.DynamicInvoke(args);
        }
    }

    // An [QtSlot] C# method. The MethodInfo is both the declaration and the implementation, so
    // overloads stay ordinary C# overloads and the normalized signature is what tells them apart
    // in the meta-object. Everything a meta-object cannot carry (a static or generic method, a
    // by-reference or params parameter, an argument with no Qt type name) is rejected here rather
    // than quietly left out: a declaration that does nothing is worse than an error.
    sealed class AttributedSlot
    {
        public MethodInfo Method;
        public string Name;                      // the Qt slot name (the method's, or the attribute's)
        public string Signature;                 // name(type,type), the normalized Qt spelling
        public Type[] Parameters;
        public string ReturnTypeName;            // null for a void slot

        public AttributedSlot(MethodInfo method, string name)
        {
            Method = method;
            Name = name;
            string member = $"{method.DeclaringType?.Name}.{method.Name}";

            if (method.IsStatic || !method.IsPublic)
                throw new InvalidOperationException(
                    $"[QtSlot] member '{member}' must be a public instance method");
            if (method.IsAbstract)
                throw new InvalidOperationException(
                    $"[QtSlot] member '{member}' is abstract; a slot needs an implementation to invoke");
            if (method.IsGenericMethod || method.ContainsGenericParameters)
                throw new InvalidOperationException(
                    $"[QtSlot] member '{member}' is generic; a Qt slot cannot carry type parameters");

            var declared = method.GetParameters();
            Parameters = new Type[declared.Length];
            for (int i = 0; i < declared.Length; i++)
            {
                var parameter = declared[i];
                if (parameter.ParameterType.IsByRef || parameter.IsOut || parameter.IsIn)
                    throw new InvalidOperationException(
                        $"[QtSlot] member '{member}' has a ref/out/in parameter, which a Qt slot cannot carry");
                if (parameter.GetCustomAttribute<ParamArrayAttribute>() != null)
                    throw new InvalidOperationException(
                        $"[QtSlot] member '{member}' has a params parameter, which a Qt slot cannot write into");
                Parameters[i] = parameter.ParameterType;
                QtName(Parameters[i], member, "parameter");
            }

            if (method.ReturnType.IsByRef)
                throw new InvalidOperationException(
                    $"[QtSlot] member '{member}' returns by reference, which a Qt slot cannot carry");
            ReturnTypeName = method.ReturnType == typeof(void)
                ? null
                : QtName(method.ReturnType, member, "return type");

            var signature = new System.Text.StringBuilder(name);
            signature.Append('(');
            for (int i = 0; i < Parameters.Length; i++)
            {
                if (i > 0)
                    signature.Append(',');
                signature.Append(MetaType.QtName(Parameters[i]));
            }
            signature.Append(')');
            Signature = signature.ToString();
        }

        // The failure names the member and the offending type: "no Qt type name" on its own would
        // not say which declaration asked for one.
        static string QtName(Type type, string member, string role)
        {
            try
            {
                return MetaType.QtName(type);
            }
            catch (NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"[QtSlot] {role} of member '{member}' has the type '{type.FullName}', for which "
                    + "there is no Qt type name");
            }
        }
    }

    // A delegate of an [QtSignal] event's own type, seeded into the event's backing field. The
    // field is what `ValueChanged?.Invoke(v)` reads, so invoking the event calls this first: it
    // activates the Qt signal (Qt connections, QML and C++ see it), and the subscribers the C#
    // side added are the rest of the field's invocation list, invoked after it as usual.
    internal abstract class SignalTrampoline
    {
        readonly AttributedSignal signal;
        readonly IQtObject sender;

        protected SignalTrampoline(AttributedSignal signal, IQtObject sender)
        {
            this.signal = signal;
            this.sender = sender;
        }

        protected void EmitSignal(object[] args) => signal.EmitFromTrampoline(sender, args);
    }

    internal sealed class SignalTrampoline0 : SignalTrampoline
    {
        public SignalTrampoline0(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit() => EmitSignal(Array.Empty<object>());
    }

    internal sealed class SignalTrampoline<T1> : SignalTrampoline
    {
        public SignalTrampoline(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit(T1 a1) => EmitSignal(new object[] { a1 });
    }

    internal sealed class SignalTrampoline<T1, T2> : SignalTrampoline
    {
        public SignalTrampoline(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit(T1 a1, T2 a2) => EmitSignal(new object[] { a1, a2 });
    }

    internal sealed class SignalTrampoline<T1, T2, T3> : SignalTrampoline
    {
        public SignalTrampoline(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit(T1 a1, T2 a2, T3 a3) => EmitSignal(new object[] { a1, a2, a3 });
    }

    internal sealed class SignalTrampoline<T1, T2, T3, T4> : SignalTrampoline
    {
        public SignalTrampoline(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4) => EmitSignal(new object[] { a1, a2, a3, a4 });
    }

    internal sealed class SignalTrampoline<T1, T2, T3, T4, T5> : SignalTrampoline
    {
        public SignalTrampoline(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5) => EmitSignal(new object[] { a1, a2, a3, a4, a5 });
    }

    internal sealed class SignalTrampoline<T1, T2, T3, T4, T5, T6> : SignalTrampoline
    {
        public SignalTrampoline(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6)
            => EmitSignal(new object[] { a1, a2, a3, a4, a5, a6 });
    }

    internal sealed class SignalTrampoline<T1, T2, T3, T4, T5, T6, T7> : SignalTrampoline
    {
        public SignalTrampoline(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7)
            => EmitSignal(new object[] { a1, a2, a3, a4, a5, a6, a7 });
    }

    internal sealed class SignalTrampoline<T1, T2, T3, T4, T5, T6, T7, T8> : SignalTrampoline
    {
        public SignalTrampoline(AttributedSignal signal, IQtObject sender) : base(signal, sender) { }
        public void Emit(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8)
            => EmitSignal(new object[] { a1, a2, a3, a4, a5, a6, a7, a8 });
    }

    // How a bound type crosses the native boundary, as far as its Qt type name is concerned.
    // Qt spells a QObject-derived class as a pointer metatype ("QWidget*"), while a value class
    // (QFont, QByteArray), a trivially copyable struct (QColor) and an enum keep their bare name.
    public enum QtTypeKind
    {
        ObjectPointer,
        ValueClass,
        Pod,
        Enum,
    }

    // The C# type -> Qt type name map the declared members use, plus the conversions between a
    // managed value and the native storage moc's qt_metacall would have used.
    //
    // QtName is the one canonical mapper: it has an intrinsic half (the C# primitives and the BCL
    // types the binding crosses as a Qt value) and the generated half the module assemblies
    // register at load time (every bound class, struct and enum, with the exact C++ spelling and
    // its QtTypeKind). QMetaType.FromType<T> asks the same mapper before QMetaType.FromName, so
    // a declared member and a metatype can never disagree about a type's name.
    public static class MetaType
    {
        static readonly Dictionary<Type, string> intrinsic = new()
        {
            [typeof(string)] = "QString",
            [typeof(bool)] = "bool",
            [typeof(int)] = "int",
            [typeof(uint)] = "uint",
            [typeof(short)] = "short",
            [typeof(ushort)] = "ushort",
            [typeof(long)] = "qint64",
            [typeof(ulong)] = "quint64",
            [typeof(float)] = "float",
            [typeof(double)] = "double",
            [typeof(byte)] = "uchar",
            [typeof(sbyte)] = "char",
            [typeof(IntPtr)] = "void*",
            // The BCL types that mean the same value as a Qt one, the way BclInterop spells the
            // conversions: a declared member may use either spelling.
            [typeof(char)] = "QChar",
            [typeof(DateOnly)] = "QDate",
            [typeof(TimeOnly)] = "QTime",
            [typeof(DateTime)] = "QDateTime",
            [typeof(Guid)] = "QUuid",
            [typeof(Uri)] = "QUrl",
            [typeof(byte[])] = "QByteArray",
        };

        // The generated table's half, filled by the module initializers the generator emits. A
        // module is loaded before any of its types can be named, so registration has happened by
        // the time a declared member or FromType<T> asks about one.
        static readonly ConcurrentDictionary<Type, (string Name, QtTypeKind Kind)> bound = new();

        /// <summary>
        /// Registers a bound type's Qt name and kind. The generated module initializers call this;
        /// user code does not need to.
        /// </summary>
        public static void Register(Type managedType, string qtName, QtTypeKind kind)
        {
            if (managedType == null)
                throw new ArgumentNullException(nameof(managedType));
            if (string.IsNullOrEmpty(qtName))
                throw new ArgumentException("A Qt type name cannot be empty.", nameof(qtName));
            bound[managedType] = (qtName, kind);
        }

        public static string QtName(Type t)
        {
            if (t == null)
                throw new ArgumentNullException(nameof(t));
            if (intrinsic.TryGetValue(t, out string name))
                return name;
            if (bound.TryGetValue(t, out var entry))
                return entry.Kind == QtTypeKind.ObjectPointer ? entry.Name + "*" : entry.Name;
            // A QObject subclass written in C# is not in the generated table: AttachMetaObject
            // names its meta-object after the C# type (BuildSpec), and a QObject crosses as a
            // pointer, so the metatype follows that same spelling.
            if (typeof(IQtObject).IsAssignableFrom(t))
                return t.Name + "*";
            // No guess for anything else: a name invented from t.Name would be a metatype Qt has
            // never heard of, and the failure would surface far away (in a signal signature or a
            // Q_PROPERTY). Failing here says which type is unsupported.
            throw new NotSupportedException(
                $"There is no Qt type name for '{t.FullName}'. A [QtSignal]/[QtProperty] type has " +
                "to be a C# primitive, one of the BCL types the binding maps (DateTime, DateOnly, " +
                "TimeOnly, Guid, Uri, byte[]), a bound Qt type, or a QObject subclass.");
        }

        /// <summary>Reads the i-th signal argument out of moc's argv.</summary>
        public static T Read<T>(IntPtr argv, int index)
            => (T)FromStorage(typeof(T), Marshal.ReadIntPtr(argv, index * IntPtr.Size));

        /// <summary>The non-generic property read an attributed property uses (the type is its PropertyInfo's).</summary>
        internal static object ReadValue(Type t, IntPtr value) => FromStorage(t, value);

        /// <summary>The non-generic signal-argument read an attributed event uses.</summary>
        internal static object ReadArgument(Type t, IntPtr argv, int index)
            => FromStorage(t, Marshal.ReadIntPtr(argv, index * IntPtr.Size));

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

    public static unsafe partial class ApiRuntime
    {
        public const string Library = "QtSharp.Api";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void SignalCallback(IntPtr userData, int argc, IntPtr argv, IntPtr typeIds);

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
                  SetDestroyedCallback((IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, long, void>)&OnDestroyed);
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                // Qt is torn down first, and deliberately while managed code is still callable:
                // destroying the application destroys the widgets Qt still owns, which runs their
                // vtable hooks and signal connections back into C#. Doing it later - from a native
                // static destructor - happens after the CLR is gone, which either trips
                // "Attempt to execute managed code after the .NET runtime thread state has been
                // destroyed" or runs Qt's teardown with its platform integration already gone.
                try
                {
                    AppDestroy();
                }
                catch
                {
                    // Nothing useful can be reported while the process is exiting.
                }
                shuttingDown = true;
                SetDestroyedCallback(IntPtr.Zero);
            };
        }

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_set_destroyed_callback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetDestroyedCallback(IntPtr callback);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_watch_destroyed", CallingConvention = CallingConvention.Cdecl)]
        private static extern void WatchDestroyed(IntPtr handle, long generation);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_watched_count", CallingConvention = CallingConvention.Cdecl)]
        private static extern int WatchedCountNative();

        // A native object is reachable through several managed views (a QWidget is also a
        // QPaintDevice, and a managed subclass is itself a view of its base), so each handle
        // keeps a list of live wrappers and a lookup takes the newest assignable one.
        static readonly Dictionary<IntPtr, List<WeakReference>> wrappers = new();

        // Everything keyed by a native address is guarded by this: Qt's destroy notification
        // arrives on whatever thread destroyed the object, while wrappers are registered from the
        // thread that created them.
        static readonly object gate = new();

        /// <summary>
        /// The lifetime of whatever occupies an address. A native address is *not* a stable
        /// identity - the allocator hands a freed block to the next object of the same size - so
        /// "the object at 0x... is dead" cannot be remembered by address alone. Each address
        /// carries a generation that is bumped when a new object takes it over, and a destroy
        /// notification carries the generation it was delivered for. A notification that arrives
        /// after the address has been reused therefore cannot touch the new occupant's wrappers.
        /// </summary>
        sealed class Lifetime
        {
            public long Generation;
            public bool Alive;
        }

        static readonly Dictionary<IntPtr, Lifetime> lifetimes = new();
        static long nextGeneration;
        static long registrations;

        /// <summary>
        /// How many wrappers have been registered since the process started. A Qt *object* costs one
        /// of these (and one entry in the native watch table); a Qt **value type** - QPointF, QRect,
        /// QSize, QLine and the rest of the trivially copyable ones - costs none, because it is a
        /// struct and never reaches this registry at all. That is the whole point of the struct
        /// treatment: a chart that samples and replaces a hundred thousand points allocates a
        /// hundred thousand structs and nothing else, where a wrapper each meant a native
        /// allocation, an entry here, an entry in g_watchedObjects and a finalizer per point.
        ///
        /// Read it around a bulk call in a test: the number must not move. A leak shows up here as a
        /// count that keeps climbing.
        /// </summary>
        public static long WrappersRegistered => Interlocked.Read(ref registrations);

        /// <summary>How many objects the native runtime is watching for destruction.</summary>
        public static int WatchedObjects => WatchedCountNative();

        public static void Register(IntPtr handle, object wrapper)
        {
            if (handle == IntPtr.Zero)
                return;
            Interlocked.Increment(ref registrations);
            lock (gate)
            {
                // A live registration is evidence that an object occupies this address now. If the
                // address belonged to an object Qt destroyed, this is the allocator reusing the
                // block: the address starts a new generation and is alive again. Without this the
                // dead marking would stick to the address forever, and the new object would never
                // be watched - silently pointing at freed memory once Qt destroys it.
                if (!lifetimes.TryGetValue(handle, out var lifetime))
                    lifetimes[handle] = lifetime = new Lifetime { Generation = ++nextGeneration, Alive = true };
                else if (!lifetime.Alive)
                {
                    lifetime.Generation = ++nextGeneration;
                    lifetime.Alive = true;
                }
                AddWrapper(handle, wrapper);
            }
        }

        static void AddWrapper(IntPtr handle, object wrapper)
        {
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
            if (handle == IntPtr.Zero)
                return;
            lock (gate)
            {
                if (!wrappers.TryGetValue(handle, out var list))
                    return;
                list.RemoveAll(w => !w.IsAlive || ReferenceEquals(w.Target, wrapper));
                if (list.Count == 0)
                    wrappers.Remove(handle);
            }
        }

        public static T Wrap<T>(IntPtr handle, Func<IntPtr, T> factory) where T : class
        {
            if (handle == IntPtr.Zero)
                return null;
            lock (gate)
            {
                if (wrappers.TryGetValue(handle, out var list))
                {
                    // Newest first: an address that was freed and reused belongs to the newer object,
                    // and only the newer wrapper owns it. A wrapper Qt already invalidated is skipped
                    // even while it is still reachable - it no longer stands for anything.
                    for (int i = list.Count - 1; i >= 0; i--)
                        if (list[i].Target is T alive && IsUsable(alive))
                            return alive;
                }
                var created = factory(handle);
                // Register, not AddWrapper: a wrapping constructor does not register itself (only
                // the constructors that make an object do), and the lifetime entry has to exist
                // before anything can be watched or owned at this address.
                Register(handle, created);
                return created;
            }
        }

        /// <summary>
        /// The live wrapper registered for a native address, whatever its type, or null. This is
        /// the read side of a QVariant holding a QObject*: the pointer identifies an object the
        /// program may already wrap, and the caller wants that wrapper back rather than a new
        /// base-class one.
        /// </summary>
        public static object TryGetWrapper(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return null;
            lock (gate)
            {
                if (!wrappers.TryGetValue(handle, out var list))
                    return null;
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].Target is object alive && IsUsable(alive))
                        return alive;
                return null;
            }
        }

        static bool IsUsable(object wrapper)
            => wrapper is not IQtObjectView view || !view.IsInvalidated;

        // ------------------------------------------------------------------ destruction
        // Which items belong to which owner. An item is not a QObject (a QGraphicsRectItem has no
        // destroyed signal), so its owner's destruction is the only lifetime signal there is. Each
        // item is remembered with the generation it was recorded in: an address that has been handed
        // to a different object since must not be invalidated along with the owner.
        static readonly Dictionary<IntPtr, List<(IntPtr Item, long Generation)>> ownedItems = new();

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        static void OnDestroyed(IntPtr handle, long generation)
        {
            // Called from Qt, on whatever thread destroyed the object.
            lock (gate)
                HandleDestroyed(handle, generation);
        }

        static void HandleDestroyed(IntPtr handle, long generation)
        {
            if (!lifetimes.TryGetValue(handle, out var lifetime))
                lifetimes[handle] = lifetime = new Lifetime { Generation = generation };
            // A notification for an object that no longer occupies this address - the allocator has
            // already handed the block to a new one - must not touch the new object's wrappers. This
            // is the late DeleteLater / parent-teardown / scroll-removal notification, and it is why
            // the generation travels with the notification.
            if (lifetime.Generation != generation)
                return;
            lifetime.Alive = false;
            ForgetConnections(handle);
            MarkWrappersDestroyed(handle);
            // Everything the object owned goes with it, but only the items that are still the ones
            // recorded there.
            if (ownedItems.TryGetValue(handle, out var items))
            {
                ownedItems.Remove(handle);
                foreach (var (item, recorded) in items)
                    MarkItemDestroyed(item, recorded);
            }
            // A C#-declared member table is only meaningful while its object is alive.
            metaByHandle.Remove(handle);
        }

        static void MarkWrappersDestroyed(IntPtr handle)
        {
            if (!wrappers.TryGetValue(handle, out var list))
                return;
            foreach (var weak in list)
                if (weak.Target is IQtObjectView view)
                    view.MarkDestroyed();
            wrappers.Remove(handle);
        }

        /// <summary>
        /// Invalidates the wrappers at <paramref name="item"/>. When the caller knows the generation
        /// the item was recorded in, the address must still be that object's - otherwise it has been
        /// reused and whatever lives there now is someone else's.
        /// </summary>
        static void MarkItemDestroyed(IntPtr item, long? recordedGeneration = null)
        {
            if (recordedGeneration.HasValue)
            {
                if (!lifetimes.TryGetValue(item, out var recorded) || recorded.Generation != recordedGeneration.Value)
                    return;
                recorded.Alive = false;
            }
            else if (lifetimes.TryGetValue(item, out var lifetime))
                lifetime.Alive = false;
            else
                lifetimes[item] = new Lifetime { Generation = ++nextGeneration, Alive = false };
            MarkWrappersDestroyed(item);
        }

        /// <summary>Whether Qt has already destroyed this object.</summary>
        internal static bool IsDestroyed(IntPtr handle)
        {
            lock (gate)
                return lifetimes.TryGetValue(handle, out var lifetime) && !lifetime.Alive;
        }

        /// <summary>
        /// The generation the runtime currently has for an address. Exposed so a test can tell a
        /// notification for the current occupant from one for a previous one.
        /// </summary>
        internal static long GenerationOf(IntPtr handle)
        {
            lock (gate)
                return lifetimes.TryGetValue(handle, out var lifetime) ? lifetime.Generation : 0;
        }

        /// <summary>
        /// Delivers a destroy notification exactly as the native side does. Exposed so a test can
        /// reproduce a *late* notification - one for an object that has already been replaced at
        /// that address - which cannot be provoked reliably through Qt itself.
        /// </summary>
        internal static void ProbeDestroyed(IntPtr handle, long generation)
        {
            lock (gate)
                HandleDestroyed(handle, generation);
        }

        /// <summary>
        /// Starts watching a QObject: one native slot per object, and the managed hook tells every
        /// wrapper of it that Qt destroyed it. Idempotent, and a no-op for an object that is
        /// already gone. The address's current generation goes along, so the notification can be
        /// recognised as this occupant's rather than a later one's.
        /// </summary>
        public static void Watch(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;
            long generation;
            lock (gate)
            {
                if (!lifetimes.TryGetValue(handle, out var lifetime))
                    lifetimes[handle] = lifetime = new Lifetime { Generation = ++nextGeneration, Alive = true };
                else if (!lifetime.Alive)
                    return;              // Qt already destroyed this object
                generation = lifetime.Generation;
            }
            WatchDestroyed(handle, generation);
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
            lock (gate)
            {
                if (!lifetimes.TryGetValue(item, out var itemLifetime))
                    lifetimes[item] = itemLifetime = new Lifetime { Generation = ++nextGeneration, Alive = true };
                if (lifetimes.TryGetValue(owner, out var ownerLifetime) && !ownerLifetime.Alive)
                {
                    MarkItemDestroyed(item, itemLifetime.Generation);
                    return;
                }
                if (!ownedItems.TryGetValue(owner, out var items))
                    ownedItems[owner] = items = new List<(IntPtr Item, long Generation)>();
                items.Add((item, itemLifetime.Generation));
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
        /// Delivers a synthetic mouse move at (x, y) with <paramref name="buttons"/> held (Qt's
        /// mouse-button mask, LeftButton = 1 by default), so a managed mouseMoveEvent override can
        /// be driven - press, move, release is what a drag looks like.
        /// </summary>
        public static bool SendMouseMove(IntPtr widgetHandle, int x, int y, int buttons = 1)
            => ProbeMouseMove(widgetHandle, x, y, buttons) != 0;

        /// <summary>Delivers a synthetic left mouse release at (x, y) to a widget.</summary>
        public static bool SendMouseRelease(IntPtr widgetHandle, int x, int y)
            => ProbeMouseRelease(widgetHandle, x, y) != 0;

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
        // The slot implementation a native meta-object index names, per managed type. Kept apart
        // from metaSpecs because the native callback has only the object to go on: the table is
        // looked up by the object's type, walking up if the declaration lives on a managed base.
        static readonly Dictionary<Type, AttributedSlot[]> metaSlots = new();
        static bool metaPropertyCallbackInstalled;
        static bool metaSlotCallbackInstalled;

        sealed class MetaObjectSpec
        {
            public string Name;
            public List<AttributedSignal> Events = new();
            public List<QMetaPropertyBase> Properties = new();
            public List<AttributedSlot> Slots = new();
        }

        /// <summary>
        /// Gives an object the signals and properties its C# class declared with [QtSignal] /
        /// [QtProperty]. Called by every generated constructor; it costs a cached type lookup for
        /// the overwhelming majority of types, which declare nothing.
        /// </summary>
        public static void AttachMetaObject(object target)
        {
            // Only a QObject has a meta-object to extend; the interface alone is not enough (the
            // generated value types implement IQtHandle too).
            if (shuttingDown || target is not IQtObject handle)
                return;
            var type = target.GetType();
            MetaObjectSpec spec;
            lock (metaSpecs)
            {
                if (!metaSpecs.TryGetValue(type, out spec))
                {
                    spec = BuildSpec(type);
                    metaSpecs[type] = spec;
                    // The native callback resolves a slot by the object's managed type, so the
                    // table is published once for this type (a derived type that inherits the
                    // declaration gets its own entry).
                    if (spec != null && spec.Slots.Count > 0)
                        lock (metaSlots)
                            metaSlots[type] = spec.Slots.ToArray();
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
            if (spec.Slots.Count > 0 && !metaSlotCallbackInstalled)
            {
                unsafe
                {
                    SetMetaSlotCallback(
                        (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&OnMetaSlot);
                }
                metaSlotCallbackInstalled = true;
            }

            var signatures = new string[spec.Events.Count];
            for (int i = 0; i < signatures.Length; i++)
                signatures[i] = spec.Events[i].Signature;
            var names = new string[spec.Properties.Count];
            var types = new string[spec.Properties.Count];
            var flags = new int[spec.Properties.Count];
            var notify = new int[spec.Properties.Count];
            var revisions = new int[spec.Properties.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = spec.Properties[i].Name;
                types[i] = spec.Properties[i].TypeName;
                flags[i] = spec.Properties[i].Flags;
                notify[i] = spec.Properties[i].NotifyIndex;
                revisions[i] = spec.Properties[i].Revision;
            }
            var slotSignatures = new string[spec.Slots.Count];
            var slotReturns = new string[spec.Slots.Count];
            for (int i = 0; i < slotSignatures.Length; i++)
            {
                slotSignatures[i] = spec.Slots[i].Signature;
                slotReturns[i] = spec.Slots[i].ReturnTypeName;      // null means void
            }
            if (AttachNative(handle.Handle, spec.Name, signatures, signatures.Length,
                    names, types, types.Length, flags, notify, revisions,
                    slotSignatures, slotReturns, slotSignatures.Length) != 0)
            {
                bool firstAttach;
                lock (metaByHandle)
                {
                    firstAttach = !metaByHandle.ContainsKey(handle.Handle);
                    metaByHandle[handle.Handle] = spec;
                }
                // An attributed event needs a native connection to feed its C# handlers and a
                // trampoline in its field so an Invoke of the event activates the signal; the
                // generated base constructors each call AttachMetaObject, so only the first one
                // installs them (the others would double every handler and every trampoline).
                if (firstAttach)
                    foreach (var declaredEvent in spec.Events)
                    {
                        ConnectDeclaredSignal(handle.Handle, declaredEvent);
                        declaredEvent.Seed(target, handle);
                    }
            }
        }

        static MetaObjectSpec BuildSpec(Type type)
        {
            var spec = new MetaObjectSpec { Name = type.Name };
            AddAttributedMembers(spec, type);
            ResolveAttributedProperties(spec);
            return spec.Events.Count == 0 && spec.Properties.Count == 0 && spec.Slots.Count == 0
                ? null : spec;
        }

        // [QtProperty]/[QtSignal]/[QtSlot] members live on the C# class itself (or a user base of
        // it) and never on a generated binding class, so the walk stops at the first generated
        // type: the binding's hundreds of inherited properties are not inspected for nothing.
        static void AddAttributedMembers(MetaObjectSpec spec, Type type)
        {
            const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            // A static [QtSlot] has to be found to be rejected, so methods include Static; the
            // property and event walks do not.
            const BindingFlags declaredMethods = declared | BindingFlags.Static;
            var slotSignatures = new HashSet<string>(StringComparer.Ordinal);
            for (var current = type;
                 current != null && current != typeof(object) && current.GetCustomAttribute<GeneratedCodeAttribute>() == null;
                 current = current.BaseType)
            {
                foreach (var property in current.GetProperties(declared))
                {
                    var attribute = property.GetCustomAttribute<QtPropertyAttribute>(false);
                    if (attribute == null)
                        continue;
                    if (property.GetIndexParameters().Length > 0)
                        throw new InvalidOperationException(
                            $"[QtProperty] member '{current.Name}.{property.Name}' is an indexer");
                    if (property.GetMethod == null || !property.GetMethod.IsPublic)
                        throw new InvalidOperationException(
                            $"[QtProperty] member '{current.Name}.{property.Name}' must be a public property with a getter");
                    spec.Properties.Add(new AttributedProperty(
                        property, DeclaredName(property.Name, attribute.Name), attribute));
                }

                foreach (var declaredEvent in current.GetEvents(declared))
                {
                    var attribute = declaredEvent.GetCustomAttribute<QtSignalAttribute>(false);
                    if (attribute == null)
                        continue;
                    if (declaredEvent.AddMethod == null || !declaredEvent.AddMethod.IsPublic)
                        throw new InvalidOperationException(
                            $"[QtSignal] member '{current.Name}.{declaredEvent.Name}' must be a public event");
                    spec.Events.Add(AttributedSignal.Create(
                        declaredEvent, DeclaredName(declaredEvent.Name, attribute.Name)));
                }

                foreach (var method in current.GetMethods(declaredMethods))
                {
                    var attribute = method.GetCustomAttribute<QtSlotAttribute>(false);
                    if (attribute == null)
                        continue;
                    var slot = new AttributedSlot(method, DeclaredName(method.Name, attribute.Name));
                    // Qt identifies a method by name and parameter types; the return type is not
                    // part of that, so two declarations with the same signature could only be one.
                    if (!slotSignatures.Add(slot.Signature))
                        throw new InvalidOperationException(
                            $"[QtSlot] member '{current.Name}.{method.Name}' duplicates the Qt slot "
                            + $"signature '{slot.Signature}'");
                    spec.Slots.Add(slot);
                }
            }
        }

        static string DeclaredName(string member, string declared)
            => string.IsNullOrEmpty(declared)
                ? char.ToLowerInvariant(member[0]) + member.Substring(1)
                : declared;

        // Resolves NOTIFY and RESET for an attributed property. Both are optional: an explicit
        // name must resolve (a typo is an error), otherwise the conventional member is used when
        // it exists - `ValueChanged` for Value's NOTIFY, `ResetValue` for its RESET.
        static void ResolveAttributedProperties(MetaObjectSpec spec)
        {
            foreach (var property in spec.Properties)
            {
                if (property is not AttributedProperty attributed)
                    continue;

                if (!string.IsNullOrEmpty(attributed.NotifyName))
                {
                    attributed.NotifyIndex = NotifySignalIndex(spec, attributed.NotifyName);
                    if (attributed.NotifyIndex < 0)
                        throw new InvalidOperationException(
                            $"[QtProperty] '{attributed.Info.DeclaringType.Name}.{attributed.Info.Name}' names "
                            + $"a notify signal '{attributed.NotifyName}' that the class does not declare "
                            + "(use the C# event/field name or the Qt name)");
                }
                else
                {
                    // The Qt convention: the property `value` is notified by `valueChanged`.
                    string changed = attributed.Info.Name + "Changed";
                    attributed.NotifyIndex = NotifySignalIndex(spec, changed);
                    if (attributed.NotifyIndex < 0)
                        attributed.NotifyIndex = NotifySignalIndex(spec, DeclaredName(changed, null));
                }

                if (!string.IsNullOrEmpty(attributed.ResetName))
                {
                    attributed.ResetMethod = ResetMethod(attributed, attributed.ResetName, generated: false);
                    if (attributed.ResetMethod == null)
                        throw new InvalidOperationException(
                            $"[QtProperty] '{attributed.Info.DeclaringType.Name}.{attributed.Info.Name}' names "
                            + $"a RESET method '{attributed.ResetName}' that is not a parameterless instance "
                            + "method returning void");
                }
                else
                {
                    // The Qt convention: the property `value` is reset by `resetValue`; in C# that
                    // method is called ResetValue.
                    attributed.ResetMethod = ResetMethod(attributed, "Reset" + attributed.Info.Name, generated: true);
                }
            }
        }

        static int NotifySignalIndex(MetaObjectSpec spec, string name)
        {
            for (int i = 0; i < spec.Events.Count; i++)
            {
                var declared = spec.Events[i];
                if (name == declared.Info.Name || name == declared.Name || name == declared.Signature)
                    return i;
            }
            return -1;
        }

        static MethodInfo ResetMethod(AttributedProperty property, string name, bool generated)
        {
            var method = property.Info.DeclaringType.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void))
                return null;
            // Only an explicit name may reach a generated Qt method; the convention never does
            // (resetFoo exists on some QObjects and is not ours).
            return generated && method.DeclaringType.GetCustomAttribute<GeneratedCodeAttribute>() != null ? null : method;
        }

        // Feeds an attributed event from its Qt signal. The connection's userData is the sender
        // handle, not the wrapper, so a native connection never roots the managed object; the
        // wrapper is looked up when the signal fires, just as the property callback does.
        static void ConnectDeclaredSignal(IntPtr sender, AttributedSignal declared)
        {
            SignalCallback callback = (userData, argc, argv, typeIds) =>
            {
                // `ValueChanged?.Invoke(v)` already activated this emission through the seeded
                // trampoline; the delegate chain that invoked it runs the C# handlers itself.
                if (ConsumeDeclaredActivation(userData, declared.Signature))
                    return;
                var target = LookupWrapper(userData);
                if (target == null)
                    return;
                // The event's C# handlers are about to run, so this is their callback: the sender
                // and the declared signal's signature are visible for the duration. The mark
                // consumed above is the *other* path - a direct event Invoke runs its handlers
                // from the delegate chain after this callback has returned, with no frame.
                PushSender(sender, declared.Signature);
                try
                {
                    declared.Invoke(target, argv);
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    ReportCallbackException("a declared signal handler", exception.InnerException);
                }
                catch (Exception exception)
                {
                    ReportCallbackException("a declared signal handler", exception);
                }
                finally
                {
                    PopSender();
                }
            };
            // Qt::DirectConnection, not Auto: this connection feeds the event's C# handler list, and
            // a C# event runs its handlers synchronously on the raising thread. Auto would queue a
            // raise from another thread and deliver them a second time once the delegate chain has
            // already called them.
            var connection = ConnectNative(sender, declared.Signature, callback, sender, IntPtr.Zero, DirectConnection);
            if (connection == IntPtr.Zero)
                return;
            lock (connections)
                connections.Add(new SignalConnection
                {
                    Sender = sender, Signature = declared.Signature,
                    Callback = callback, Native = connection,
                });
        }

        // How many activations of a (sender, signal) pair came from a [QtSignal] event's own
        // Invoke, and are therefore delivered to the C# handlers by the delegate chain. A count,
        // not a flag: an emission can nest inside another (a Qt slot that re-emits the signal),
        // and each activation consumes its own mark. Thread-static because the connection is
        // direct and Qt dispatches it inside the call that activated it, on the same thread.
        [ThreadStatic] static Dictionary<(IntPtr Handle, string Signature), int> declaredActivations;

        // Activates a declared signal on behalf of its event trampoline. The mark is consumed by
        // ConnectDeclaredSignal's callback, so that one activation leaves the C# handler list to
        // the chain and a nested activation still delivers normally.
        internal static void ActivateDeclared(IntPtr sender, string signature, Type[] types, object[] args)
        {
            var key = (sender, signature);
            var activations = declaredActivations ??= new Dictionary<(IntPtr, string), int>();
            activations.TryGetValue(key, out int pending);
            activations[key] = pending + 1;
            try
            {
                EmitUser(sender, signature, types, args);
            }
            finally
            {
                // The direct connection runs before EmitUser returns, so a mark left here means the
                // connection is gone (or the activation never reached it); it must not linger and
                // swallow a later, unrelated emission.
                activations.Remove(key);
            }
        }

        static bool ConsumeDeclaredActivation(IntPtr sender, string signature)
        {
            if (declaredActivations == null)
                return false;
            var key = (sender, signature);
            if (!declaredActivations.TryGetValue(key, out int pending) || pending == 0)
                return false;
            if (pending == 1)
                declaredActivations.Remove(key);
            else
                declaredActivations[key] = pending - 1;
            return true;
        }

        /// <summary>
        /// Activates the signal an [QtSignal] event declared, by the C# event's name. The native
        /// signature and argument types come from the declaration, so only the values are passed.
        /// </summary>
        public static void EmitDeclared(IQtObject sender, string eventName, object[] args)
        {
            var declared = DeclaredSignal(sender, eventName);
            if (args.Length != declared.Parameters.Length)
                throw new ArgumentException(
                    $"'{sender.GetType().Name}.{eventName}' carries {declared.Parameters.Length} argument(s), "
                    + $"but {args.Length} were emitted", nameof(args));
            EmitUser(sender.Handle, declared.Signature, declared.Parameters, args);
        }

        static AttributedSignal DeclaredSignal(IQtObject sender, string eventName)
        {
            MetaObjectSpec spec;
            lock (metaSpecs)
                metaSpecs.TryGetValue(sender.GetType(), out spec);
            if (spec != null)
                foreach (var declared in spec.Events)
                    if (declared.Info.Name == eventName)
                        return declared;
            throw new InvalidOperationException(
                $"{sender.GetType().FullName} declares no [QtSignal] event named '{eventName}'");
        }

        // The call codes the native metaCall forwards; the native side spells the same numbers.
        const int PropertyRead = 0;
        const int PropertyWrite = 1;
        const int PropertyReset = 2;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void OnMetaProperty(IntPtr handle, int index, int call, IntPtr value)
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
            if (call == PropertyWrite)
            {
                try { spec.Properties[index].Set(target, value); }
                catch (Exception exception) { ReportCallbackException("a declared property setter", exception); }
            }
            else if (call == PropertyReset)
            {
                try { spec.Properties[index].Reset(target); }
                catch (Exception exception) { ReportCallbackException("a declared property reset", exception); }
            }
            else
            {
                try { spec.Properties[index].Get(target, value); }
                catch (Exception exception) { ReportCallbackException("a declared property getter", exception); }
            }
        }

        // Runs an [QtSlot] method on behalf of the dynamic meta-object. `args` is moc's argv: the
        // return storage at [0] and the address of each parameter's native value from [1] on. The
        // table is found by the object's own managed type, walking up so a slot declared on a
        // managed base class runs with the derived instance as its target.
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void OnMetaSlot(IntPtr handle, int slotIndex, IntPtr args)
        {
            var target = LookupWrapper(handle);
            if (target == null)
                return;
            var slots = SlotTable(target.GetType());
            if (slots == null || slotIndex < 0 || slotIndex >= slots.Length)
                return;
            var slot = slots[slotIndex];
            if (args == IntPtr.Zero && (slot.Parameters.Length > 0 || slot.ReturnTypeName != null))
                return;

            try
            {
                var values = new object[slot.Parameters.Length];
                for (int i = 0; i < values.Length; i++)
                    values[i] = MetaType.ReadValue(slot.Parameters[i],
                        Marshal.ReadIntPtr(args, (i + 1) * IntPtr.Size));
                object result = slot.Method.Invoke(target, values);
                if (slot.ReturnTypeName == null)
                    return;
                IntPtr destination = Marshal.ReadIntPtr(args, 0);
                if (destination != IntPtr.Zero)
                    MetaType.Write(slot.ReturnTypeName, destination, result);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                // Qt called this from inside its C++ frames; see ReportCallbackException. The
                // return storage stays as Qt initialised it.
                ReportCallbackException("a declared slot", exception.InnerException);
            }
            catch (Exception exception)
            {
                ReportCallbackException("a declared slot", exception);
            }
        }

        static AttributedSlot[] SlotTable(Type type)
        {
            lock (metaSlots)
            {
                for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                    if (metaSlots.TryGetValue(current, out var slots))
                        return slots;
            }
            return null;
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
            int propertyCount,
            [MarshalAs(UnmanagedType.LPArray)] int[] propertyFlags,
            [MarshalAs(UnmanagedType.LPArray)] int[] propertyNotify,
            [MarshalAs(UnmanagedType.LPArray)] int[] propertyRevisions,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] slotSignatures,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] slotReturnTypes,
            int slotCount);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_set_meta_property_callback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetMetaPropertyCallback(IntPtr callback);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_set_meta_slot_callback", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetMetaSlotCallback(IntPtr callback);

        /// <summary>
        /// Builds the process's one Qt application object from a command line, and returns its
        /// handle. <paramref name="kind"/> selects QCoreApplication (0), QGuiApplication (1) or
        /// QApplication (2). A second call returns the application that already exists rather than
        /// making another one - Qt aborts on a second QApplication. The strings are copied into
        /// native storage that lives as long as the process, because Qt keeps the argv pointers.
        ///
        /// This is what the generated QCoreApplication/QGuiApplication/QApplication constructors
        /// call; a program uses those (`var app = new QApplication(args);`) and then the run loop
        /// and the exit on the instance (`app.Exec()`, `app.Quit()`), the way a PyQt program does.
        /// </summary>
        public static IntPtr CreateApplication(string[] args, int flags = 0, int kind = 2)
        {
            // There is no per-platform preparation here on purpose: Qt finds its plugins through
            // the deployed qt.conf (Prefix=. / Plugins=plugins) on Linux and through the
            // application directory on Windows. Platform-specific code lives in the build and
            // packaging layer, not in the shared managed runtime.
            var argv = args ?? Array.Empty<string>();
            return AppCreateArgs(argv, argv.Length, flags, kind);
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

        // A QString that belongs to Qt - the argument of a virtual override - is read without
        // being freed. (ToManaged releases the copy the native side allocated for us.)
        public static string ToManagedTransient(IntPtr qstring)
            => qstring == IntPtr.Zero ? null
               : Marshal.PtrToStringUni(StringUtf16(qstring), (int)StringSize(qstring));

        /// <summary>
        /// Reads a blittable value type (QPoint, QRect, ...) out of moc's argument storage. The
        /// generated wrappers call this instead of dereferencing a pointer themselves, which is
        /// what lets the generated classes be ordinary (non-unsafe) declarations; extension
        /// modules call it from their own assembly, hence public.
        /// </summary>
        public static unsafe T ReadStruct<T>(IntPtr storage) where T : unmanaged => *(T*)storage;

        /// <summary>
        /// Copies an array Qt passed to a virtual (a <c>const QPoint *points, int count</c>
        /// parameter pair) into managed memory. The override owns its copy, so it may keep it
        /// after the call; writes into it do not reach Qt, which is why only a const input array
        /// is published as an override point.
        /// </summary>
        public static unsafe T[] ReadArray<T>(IntPtr source, int count) where T : unmanaged
            => source == IntPtr.Zero || count <= 0
                ? Array.Empty<T>()
                : new ReadOnlySpan<T>((void*)source, count).ToArray();

        /// <summary>
        /// An exception a user handler threw, caught where native code called into managed code.
        ///
        /// Qt's event loop and its signal machinery are C++ frames: an exception that unwinds into
        /// them has nowhere to land and takes the whole process down - in a GUI app, usually as an
        /// access violation with nothing on the console to say why. PyQt answers this by reporting
        /// the traceback and calling qFatal(); PySide by reporting and carrying on. This reports and
        /// keeps the process alive, and hands the exception to whoever subscribed here (a logger, a
        /// test, a crash reporter). Subscribe to fail loudly instead of quietly.
        /// </summary>
        public static event Action<string, Exception> UnhandledCallbackException;

        // Public for the same reason as IQtObjectView: the generated virtual stubs call it.
        public static void ReportCallbackException(string what, Exception exception)
        {
            var hook = UnhandledCallbackException;
            if (hook != null)
            {
                try { hook(what, exception); }
                catch { }   // a broken hook must not become the crash it was meant to report
            }
            Console.Error.WriteLine($"[qtsharp] unhandled exception in {what}: {exception}");
        }

        // The native forwarder hands over moc's raw argument array; the generated binding reads
        // each slot with the type it knows, so nothing has to be guessed from a meta-type id.
        // The callback has to stay alive for as long as Qt holds the connection.
        public delegate void SignalSlot(IntPtr argv, IntPtr typeIds);

        // The Qt::ConnectionType values the native side forwards. Auto is the default; Direct is
        // what ConnectDeclaredSignal needs (see the comment there).
        internal const int AutoConnection = 0;
        internal const int DirectConnection = 1;

        // ------------------------------------------------------------------ sender context
        // PyQt's/PySide's sender(): while Qt is delivering a signal to a managed handler, the
        // sender handle and the moc signature the connection was made with are on a per-thread
        // stack. It is a stack, not a single slot, because a handler may emit another signal -
        // the inner handler must see the inner sender, and when it returns the outer one is
        // visible again. Thread-static because a queued connection is delivered on the
        // receiver's thread, so each thread has its own view.
        [ThreadStatic] static IntPtr[] senderHandles;
        [ThreadStatic] static string[] senderSignals;
        [ThreadStatic] static int senderDepth;

        /// <summary>The sender handle of the signal being delivered on this thread, or zero.</summary>
        internal static IntPtr CurrentSender
            => senderDepth > 0 ? senderHandles[senderDepth - 1] : IntPtr.Zero;

        /// <summary>The moc signature of the signal being delivered on this thread, or null.</summary>
        internal static string CurrentSenderSignal
            => senderDepth > 0 ? senderSignals[senderDepth - 1] : null;

        static void PushSender(IntPtr sender, string signature)
        {
            var handles = senderHandles;
            int depth = senderDepth;
            if (handles == null || depth == handles.Length)
            {
                int capacity = handles == null ? 4 : handles.Length * 2;
                Array.Resize(ref senderHandles, capacity);
                Array.Resize(ref senderSignals, capacity);
            }
            senderHandles[depth] = sender;
            senderSignals[depth] = signature;
            senderDepth = depth + 1;
        }

        static void PopSender()
        {
            int depth = senderDepth - 1;
            if (depth < 0)
                return;
            senderHandles[depth] = IntPtr.Zero;
            senderSignals[depth] = null;
            senderDepth = depth;
        }

        /// <summary>
        /// Connects a managed handler to a signal without a key: the connection lives until its
        /// sender dies, and Dispose of the returned handle drops it earlier. This is the shape a
        /// QSignal's <c>Connect(handler)</c> uses.
        /// </summary>
        public static QtConnection Connect(IntPtr sender, string signature, SignalSlot handler)
            => Connect(sender, signature, handler, null);

        /// <summary>
        /// Connects a managed handler to a signal, remembering <paramref name="key"/> so that
        /// Disconnect can drop exactly this connection again. That is what the `-=` of a signal
        /// exposed as a C# event needs: `key` is the user's own handler, and comparison is
        /// delegate equality - the same rule C# events use for remove. The returned handle
        /// disconnects the connection by identity, so the same handler may be connected twice.
        /// </summary>
        public static QtConnection Connect(IntPtr sender, string signature, SignalSlot handler, Delegate key)
            => Connect(sender, signature, handler, key, IntPtr.Zero, AutoConnection);

        /// <summary>
        /// Connects a managed handler to a signal with a context object and a Qt::ConnectionType.
        /// A non-zero <paramref name="context"/> is the native QObject whose thread the handler
        /// runs on for a queued connection and whose destruction breaks the connection; zero keeps
        /// the pre-existing behaviour (the sender is the receiver, the handler runs on whichever
        /// thread emits).
        /// </summary>
        public static QtConnection Connect(IntPtr sender, string signature, SignalSlot handler, Delegate key,
                                           IntPtr context, int connectionType)
        {
            SignalCallback callback = (userData, argc, argv, typeIds) =>
            {
                // The handler is the slot's own C# code, so the emitting object and the signal it
                // was connected to are visible through QObject.Sender/SenderSignal while it runs.
                PushSender(sender, signature);
                // Qt called this from inside its C++ frames; see ReportCallbackException.
                try { handler(argv, typeIds); }
                catch (Exception exception) { ReportCallbackException("a signal handler", exception); }
                finally { PopSender(); }
            };
            var connection = ConnectNative(sender, signature, callback, IntPtr.Zero, context, connectionType);
            if (connection == IntPtr.Zero)
                return new QtConnection(null);

            // The record holds the callback: the native slot object points at the thunk the
            // marshaller built for it, which must stay alive for as long as Qt holds the slot.
            var record = new SignalConnection
            {
                Sender = sender, Signature = signature, Key = key, Context = context,
                Callback = callback, Native = connection,
            };
            lock (connections)
                connections.Add(record);
            return new QtConnection(record);
        }

        /// <summary>
        /// Drops the most recent connection made with this key, and reports whether there was one.
        /// This is the `-=` of a signal event; a key that never connected is a no-op, like C#.
        /// </summary>
        public static bool Disconnect(IntPtr sender, string signature, Delegate key)
        {
            SignalConnection found = null;
            lock (connections)
            {
                for (int i = connections.Count - 1; i >= 0; i--)
                {
                    var candidate = connections[i];
                    if (candidate.Sender == sender && candidate.Signature == signature
                        && Equals(candidate.Key, key))
                    {
                        connections.RemoveAt(i);
                        found = candidate;
                        break;
                    }
                }
            }
            return found != null && DisconnectConnection(found);
        }

        /// <summary>
        /// Drops one connection record: removes it from the bookkeeping, disconnects the native
        /// connection it names, and frees the native handle. Idempotent - the record's
        /// <see cref="SignalConnection.Native"/> is cleared under the lock, so a handle disposed
        /// after its sender (or context) was destroyed still frees the handle safely.
        /// </summary>
        internal static bool DisconnectConnection(SignalConnection connection)
        {
            IntPtr native;
            lock (connections)
            {
                connections.Remove(connection);
                native = connection.Native;
                connection.Native = IntPtr.Zero;
            }
            // QObject::disconnect is safe on a connection Qt has already broken (the sender or
            // the context was destroyed); the handle is ours to free either way.
            return native != IntPtr.Zero && DisconnectNative(native) != 0;
        }

        /// <summary>
        /// A live signal connection. <see cref="Callback"/> is what keeps the native thunk alive,
        /// <see cref="Key"/> is the handler a C# event removes by, <see cref="Context"/> is the
        /// receiver/context whose lifetime and thread affinity Qt applies, and
        /// <see cref="Native"/> is the QMetaObject::Connection the runtime disconnects.
        /// </summary>
        internal sealed class SignalConnection
        {
            public IntPtr Sender;
            public string Signature;
            public Delegate Key;
            public IntPtr Context;
            public SignalCallback Callback;
            public IntPtr Native;
            public bool Dropped;     // Qt destroyed the sender/context; the record is no longer live
        }

        static readonly List<SignalConnection> connections = new();

        // The connections of a sender that Qt has destroyed are gone with it; dropping the records
        // releases the managed callbacks, and a handle that still points at one of these records
        // frees its native wrapper when it is disposed.
        //
        // A destroyed *context* is only marked, not removed: Qt has already dropped the connection
        // (so the handler cannot run and IsConnected must say so), but the sender may still be
        // alive and, on another thread, mid-activation of the very slot object whose thunk this
        // record keeps alive. Releasing the callback here would race that call; the record goes
        // when the sender dies or the caller disposes the handle.
        static void ForgetConnections(IntPtr sender)
        {
            lock (connections)
            {
                for (int i = connections.Count - 1; i >= 0; i--)
                {
                    var connection = connections[i];
                    if (connection.Sender == sender)
                    {
                        connection.Dropped = true;
                        connections.RemoveAt(i);
                    }
                    else if (connection.Context == sender)
                    {
                        connection.Dropped = true;
                    }
                }
            }
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

        // Copies a *live* native QByteArray the caller still owns: this is what QByteArray.ToArray
        // uses, so it must not free the wrapper's own object. ToManagedBytes below is for a
        // QByteArray Qt handed over as a return value, which this side must free.
        public static byte[] CopyBytes(IntPtr array)
        {
            if (array == IntPtr.Zero)
                return null;
            int size = ByteArraySize(array);
            var result = new byte[size];
            if (size > 0)
                Marshal.Copy(ByteArrayData(array), result, 0, size);
            return result;
        }

        public static byte[] ToManagedBytes(IntPtr array)
        {
            if (array == IntPtr.Zero)
                return null;
            try
            {
                return CopyBytes(array);
            }
            finally
            {
                ByteArrayFree(array);
            }
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
        private static extern IntPtr ConnectNative(IntPtr sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string signature,
            SignalCallback callback, IntPtr userData, IntPtr context, int connectionType);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_disconnect", CallingConvention = CallingConvention.Cdecl)]
        private static extern int DisconnectNative(IntPtr connection);


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

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_mouse_move", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeMouseMove(IntPtr widgetHandle, int x, int y, int buttons);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_mouse_release", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeMouseRelease(IntPtr widgetHandle, int x, int y);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_wheel", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeWheel(IntPtr widgetHandle, int x, int y, int degrees, int modifiers);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_probe_key_press", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeKeyPress(IntPtr widgetHandle, int key, int modifiers);

        // The marshaller builds a char*[] for the duration of the call; the native side copies
        // every string into process-lifetime storage before returning.
        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_app_create_args", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr AppCreateArgs(
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] argv,
            int argc, int flags, int kind);

        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_app_destroy", CallingConvention = CallingConvention.Cdecl)]
        private static extern void AppDestroy();
    }
}

