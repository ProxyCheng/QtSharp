using System;
using QtSharp.Runtime;

// The QObject-shaped half of the signal API: a QSignal's Connect overloads that name
// Qt::ConnectionType and a context object.
//
// They live here rather than on QSignal itself because the runtime assembly is Qt-type-free
// (QObject and Qt live in this assembly). Every overload forwards to the same internal
// QSignal.ConnectCore, so a connection opened through one of these has exactly the same record,
// key (the handler) and disposable QtConnection as the plain Connect(handler).
//
// The two shapes mirror Qt's own overloads:
//
//     QObject::connect(sender, signal, functor, type)
//     QObject::connect(sender, signal, context, functor, type)
//
// The context is what a Qt user expects from the second one: a queued connection is delivered on
// the context's thread (which must run an event loop), and destroying the context breaks the
// connection before any later emission. With no context the sender is the receiver, which is the
// historical behaviour. Unlike a member connect, the context does not have to be a QObject
// subclass that declares the slot: any QObject works, the way a functor connect does in Qt.
namespace QtSharp.QtCore
{
    public static class QSignalConnections
    {
        public static QtConnection Connect(this QSignal signal, Action handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect(this QSignal signal, Action handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);

        public static QtConnection Connect<T1>(this QSignal<T1> signal, Action<T1> handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect<T1>(this QSignal<T1> signal, Action<T1> handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);

        public static QtConnection Connect<T1, T2>(this QSignal<T1, T2> signal, Action<T1, T2> handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect<T1, T2>(this QSignal<T1, T2> signal, Action<T1, T2> handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);

        public static QtConnection Connect<T1, T2, T3>(this QSignal<T1, T2, T3> signal, Action<T1, T2, T3> handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect<T1, T2, T3>(this QSignal<T1, T2, T3> signal, Action<T1, T2, T3> handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4>(this QSignal<T1, T2, T3, T4> signal, Action<T1, T2, T3, T4> handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4>(this QSignal<T1, T2, T3, T4> signal, Action<T1, T2, T3, T4> handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4, T5>(this QSignal<T1, T2, T3, T4, T5> signal, Action<T1, T2, T3, T4, T5> handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4, T5>(this QSignal<T1, T2, T3, T4, T5> signal, Action<T1, T2, T3, T4, T5> handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4, T5, T6>(this QSignal<T1, T2, T3, T4, T5, T6> signal, Action<T1, T2, T3, T4, T5, T6> handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4, T5, T6>(this QSignal<T1, T2, T3, T4, T5, T6> signal, Action<T1, T2, T3, T4, T5, T6> handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4, T5, T6, T7>(this QSignal<T1, T2, T3, T4, T5, T6, T7> signal, Action<T1, T2, T3, T4, T5, T6, T7> handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4, T5, T6, T7>(this QSignal<T1, T2, T3, T4, T5, T6, T7> signal, Action<T1, T2, T3, T4, T5, T6, T7> handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4, T5, T6, T7, T8>(this QSignal<T1, T2, T3, T4, T5, T6, T7, T8> signal, Action<T1, T2, T3, T4, T5, T6, T7, T8> handler,
            Qt.ConnectionType type)
            => signal.ConnectCore(handler, null, (int)type);

        public static QtConnection Connect<T1, T2, T3, T4, T5, T6, T7, T8>(this QSignal<T1, T2, T3, T4, T5, T6, T7, T8> signal, Action<T1, T2, T3, T4, T5, T6, T7, T8> handler,
            QObject context, Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
            => signal.ConnectCore(handler, context, (int)type);
    }
}
