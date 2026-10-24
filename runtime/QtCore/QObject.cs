using System;
using System.Runtime.CompilerServices;
using QtSharp.Runtime;

// The QObject half of the runtime: the signal API that belongs to the generated QObject wrapper,
// and the emit helpers a user-declared signal calls. A partial class cannot span assemblies, so
// this half lives beside the generated QObject in the QtCore assembly; the rest of the runtime
// (handles, attributes, the wrapper cache, marshalling) is Qt-type-free and lives in
// QtSharp.Runtime.
namespace QtSharp.QtCore
{
    /// <summary>
    /// Emits a signal declared by <see cref="QtSignalAttribute"/> on an event. Pass the event
    /// itself: the compiler supplies its name to the runtime, which takes the signature and the
    /// argument types from the declaration.
    ///
    ///     this.EmitSignal(ValueChanged, value);
    ///
    /// Calling the event directly (`ValueChanged?.Invoke(value)`) is legal inside the declaring
    /// class, but it only runs the C# handlers: it never reaches QMetaObject::activate, so QML and
    /// native connections would not see the emission. This method is the emit that crosses into Qt.
    /// </summary>
    public static class QtSignals
    {
        public static void EmitSignal(this QObject sender, string eventName)
            => ApiRuntime.EmitDeclared(sender, eventName, Array.Empty<object>());

        public static void EmitSignal<T1>(this QObject sender, string eventName, T1 a1)
            => ApiRuntime.EmitDeclared(sender, eventName, new object[] { a1 });

        public static void EmitSignal<T1, T2>(this QObject sender, string eventName, T1 a1, T2 a2)
            => ApiRuntime.EmitDeclared(sender, eventName, new object[] { a1, a2 });

        public static void EmitSignal<T1, T2, T3>(this QObject sender, string eventName, T1 a1, T2 a2, T3 a3)
            => ApiRuntime.EmitDeclared(sender, eventName, new object[] { a1, a2, a3 });

        public static void EmitSignal<T1, T2, T3, T4>(this QObject sender, string eventName, T1 a1, T2 a2, T3 a3, T4 a4)
            => ApiRuntime.EmitDeclared(sender, eventName, new object[] { a1, a2, a3, a4 });

        // The delegate overloads exist for the call site, not for the value: a field-like event
        // can be read inside its own class, and CallerArgumentExpression hands the runtime the
        // event's name without a nameof. The handler list itself is ignored (the emit goes through
        // Qt, which then feeds the list back).
        public static void EmitSignal(this QObject sender, Delegate signal,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(sender, EventName(expression), Array.Empty<object>());

        public static void EmitSignal<T1>(this QObject sender, Delegate signal, T1 a1,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(sender, EventName(expression), new object[] { a1 });

        public static void EmitSignal<T1, T2>(this QObject sender, Delegate signal, T1 a1, T2 a2,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(sender, EventName(expression), new object[] { a1, a2 });

        public static void EmitSignal<T1, T2, T3>(this QObject sender, Delegate signal, T1 a1, T2 a2, T3 a3,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(sender, EventName(expression), new object[] { a1, a2, a3 });

        public static void EmitSignal<T1, T2, T3, T4>(this QObject sender, Delegate signal, T1 a1, T2 a2, T3 a3, T4 a4,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(sender, EventName(expression), new object[] { a1, a2, a3, a4 });

        // The expression is whatever the call site wrote - "ValueChanged" or "this.ValueChanged";
        // the event itself is the last name in it.
        internal static string EventName(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException(
                    "pass the declared event itself, e.g. EmitSignal(ValueChanged, value)");
            int dot = expression.LastIndexOf('.');
            return (dot < 0 ? expression : expression.Substring(dot + 1)).Trim();
        }
    }

    // The same emit as a protected instance method, so the declaring class writes
    // EmitSignal(ValueChanged, value) - the C# event itself, no nameof and no `this.`.
    // (The generated part of QObject defines ==/!= without overriding Equals/GetHashCode; the
    // generated file disables the same two warnings.)
#pragma warning disable 660, 661
    public partial class QObject
    {
        protected void EmitSignal(Delegate signal,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(this, QtSignals.EventName(expression), Array.Empty<object>());

        protected void EmitSignal<T1>(Delegate signal, T1 a1,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(this, QtSignals.EventName(expression), new object[] { a1 });

        protected void EmitSignal<T1, T2>(Delegate signal, T1 a1, T2 a2,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(this, QtSignals.EventName(expression), new object[] { a1, a2 });

        protected void EmitSignal<T1, T2, T3>(Delegate signal, T1 a1, T2 a2, T3 a3,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(this, QtSignals.EventName(expression), new object[] { a1, a2, a3 });

        protected void EmitSignal<T1, T2, T3, T4>(Delegate signal, T1 a1, T2 a2, T3 a3, T4 a4,
            [CallerArgumentExpression(nameof(signal))] string expression = null)
            => ApiRuntime.EmitDeclared(this, QtSignals.EventName(expression), new object[] { a1, a2, a3, a4 });
    }
#pragma warning restore 660, 661
}
