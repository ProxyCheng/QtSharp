using System;
using QtSharp.Runtime;

// PyQt's/PySide's sender() and senderSignalIndex() idea, spelled the way a C# slot wants it:
// while Qt is delivering a signal to a managed handler, the sender and the connected signal are
// available on the thread that runs the handler. The per-thread stack itself lives in
// ApiRuntime (it owns the connections and the managed trampolines); this half only resolves the
// handle through the same wrapper cache FromHandle uses and exposes the two values on QObject.
namespace QtSharp.QtCore
{
    public partial class QObject
    {
        /// <summary>
        /// The object whose signal is currently being delivered to a managed handler on this
        /// thread - PyQt's/PySide's <c>sender()</c> - or <see langword="null"/> outside a signal
        /// callback.
        ///
        /// The handle is resolved through the wrapper cache <see cref="FromHandle"/> uses, so a
        /// handler connected on a <c>QPushButton</c> sees that very wrapper instance
        /// (<c>button.Clicked += (_, s) => s.Sender == button</c>); no duplicate wrapper is
        /// created for an object the program already wrapped. A sender Qt created and
        /// the program has not wrapped yet gets a plain <see cref="QObject"/> wrapper, which is
        /// then cached like any other.
        ///
        /// The value is per thread and per callback. A nested emission (a handler that emits
        /// another signal) sees the inner sender while it runs and the outer one again when the
        /// inner handler returns; a queued connection sees the sender on the thread that runs the
        /// slot, not on the emitting thread.
        ///
        /// One boundary to know: the handlers of a C#-declared <see cref="QtSignalAttribute"/>
        /// event run from the event's own delegate chain when the event is invoked directly
        /// (<c>ValueChanged?.Invoke(value)</c>). That happens after the trampoline's emission has
        /// returned from Qt, so those handlers have no frame of their own: outside any other
        /// callback <see cref="Sender"/> is null, and an Invoke made from inside another handler
        /// sees that enclosing handler's sender. Signals delivered by Qt - a generated signal, a
        /// native or QML emit, or the same declared event raised with
        /// <c>EmitSignal(ValueChanged, value)</c>, which goes through Qt's connection - do set
        /// it.
        /// </summary>
        public QObject Sender
        {
            get
            {
                IntPtr handle = ApiRuntime.CurrentSender;
                return handle == IntPtr.Zero ? null : FromHandle(handle);
            }
        }

        /// <summary>
        /// The moc signature of the signal currently being delivered to a managed handler on this
        /// thread, for example <c>"clicked(bool)"</c> or <c>"valueChanged(QString)"</c>, or
        /// <see langword="null"/> outside a signal callback.
        ///
        /// This is the signature the connection was made with, so it keeps the parameter types
        /// (and therefore distinguishes overloads) in the same spelling
        /// <see cref="QtNativeSignalAttribute"/> carries. It is the C# counterpart of PyQt's
        /// <c>senderSignalIndex()</c> without needing the protected <c>QObject::senderSignalIndex()</c>.
        ///
        /// It follows <see cref="Sender"/> exactly: the same per-thread stack, the same nesting
        /// behaviour, and the same boundary for a C#-declared event invoked directly
        /// (<c>ValueChanged?.Invoke(value)</c>), whose handlers have no frame of their own and
        /// therefore see null (or an enclosing handler's sender when invoked inside one).
        /// </summary>
        public string SenderSignal => ApiRuntime.CurrentSenderSignal;
    }
}
