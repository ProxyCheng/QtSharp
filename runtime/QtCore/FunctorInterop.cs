using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using QtSharp.Runtime;

// Qt's functor-taking APIs are C++ templates, so the model never sees them: there is no exported
// symbol to bind and no `std::function` in any Qt signature. The two that matter in application
// code are QTimer::singleShot(msec, functor) and QMetaObject::invokeMethod(context, functor), and
// they are bound here by instantiating the templates in the native runtime
// (native/QtSharpApiRuntime.cpp) around a managed token:
//
//     QTimer.singleShot(0, () => refresh());
//     QTimer.singleShot(1000, window, () => close());
//     QMetaObject.InvokeMethod(worker, action, Qt.ConnectionType.QueuedConnection);
//
// The token is a GCHandle on the delegate. Native code invokes it (method 0) or releases it
// (method 1) when Qt destroys the functor without calling it - the context object died, or the
// queued event was discarded. That release path is what keeps a cancelled singleShot from rooting
// its delegate until the process ends. The method names follow Qt: `singleShot` is lowercase
// because the Q_PROPERTY already owns `SingleShot` (the generated static overloads do the same).
//
// The functor-based QObject::connect overloads are deliberately not here: a signal connection is
// already a C# event (`button.Clicked += handler`), and a template connection would have to repeat
// every signal signature natively for no new capability.
namespace QtSharp.QtCore
{
    public partial class QTimer
    {
        /// <summary>
        /// Invokes <paramref name="action"/> once after <paramref name="msec"/> milliseconds on the
        /// calling thread's event loop. The managed equivalent of Qt's functor
        /// <c>QTimer::singleShot(int, Functor)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        public static void singleShot(int msec, Action action)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            FunctorDispatch.Create(action, out IntPtr token, out IntPtr dispatch);
            try
            {
                FunctorNative.QTimerSingleShot(msec, token, dispatch);
            }
            catch
            {
                FunctorDispatch.Release(token);
                throw;
            }
        }

        /// <summary>
        /// Invokes <paramref name="action"/> once after <paramref name="msec"/> milliseconds unless
        /// <paramref name="context"/> is destroyed first, in which case the call is cancelled. The
        /// callback runs in the context object's thread. The managed equivalent of Qt's functor
        /// <c>QTimer::singleShot(int, const QObject*, Functor)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> or <paramref name="action"/> is null.
        /// </exception>
        public static void singleShot(int msec, QObject context, Action action)
        {
            if (context is null)
                throw new ArgumentNullException(nameof(context));
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            FunctorDispatch.Create(action, out IntPtr token, out IntPtr dispatch);
            try
            {
                FunctorNative.QTimerSingleShotContext(msec, context.Handle, token, dispatch);
            }
            catch
            {
                FunctorDispatch.Release(token);
                throw;
            }
        }
    }

    public partial class QMetaObject
    {
        /// <summary>
        /// Queues <paramref name="action"/> on <paramref name="context"/>'s thread and returns
        /// whether Qt accepted it. The managed equivalent of Qt's functor
        /// <c>QMetaObject::invokeMethod(QObject*, Functor, Qt::ConnectionType)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> or <paramref name="action"/> is null.
        /// </exception>
        /// <remarks>
        /// The callback takes no arguments and returns nothing. A rejected invocation releases the
        /// managed delegate through native code, exactly like a cancelled singleShot.
        /// </remarks>
        public static bool InvokeMethod(
            QObject context, Action action,
            Qt.ConnectionType type = Qt.ConnectionType.AutoConnection)
        {
            if (context is null)
                throw new ArgumentNullException(nameof(context));
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            FunctorDispatch.Create(action, out IntPtr token, out IntPtr dispatch);
            try
            {
                return FunctorNative.InvokeMethod(context.Handle, (int)type, token, dispatch);
            }
            catch
            {
                FunctorDispatch.Release(token);
                throw;
            }
        }
    }

    /// <summary>
    /// The managed side of the native functor helpers: one rooted delegate per pending call, and
    /// the single native dispatcher both method ids go through.
    /// </summary>
    static unsafe class FunctorDispatch
    {
        // void(IntPtr token, int methodId, IntPtr argv), matching QsApiDispatch.
        internal static readonly IntPtr Pointer =
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch;

        internal static void Create(Action action, out IntPtr token, out IntPtr dispatch)
        {
            token = GCHandle.ToIntPtr(GCHandle.Alloc(action));
            dispatch = Pointer;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Dispatch(IntPtr token, int methodId, IntPtr argv)
        {
            if (methodId == 0)
                Invoke(token);
            else
                Release(token);
        }

        static void Invoke(IntPtr token)
        {
            var handle = GCHandle.FromIntPtr(token);
            var action = (Action)handle.Target;
            handle.Free();
            try
            {
                action();
            }
            catch (Exception exception)
            {
                // A managed exception must not unwind into Qt's C++ frames.
                ApiRuntime.ReportCallbackException("a Qt functor", exception);
            }
        }

        internal static void Release(IntPtr token)
        {
            if (token == IntPtr.Zero)
                return;
            try
            {
                GCHandle.FromIntPtr(token).Free();
            }
            catch (InvalidOperationException)
            {
                // Already released by the invoke path.
            }
        }
    }

    static class FunctorNative
    {
        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qtimer_single_shot_functor",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern void QTimerSingleShot(int msec, IntPtr token, IntPtr dispatch);

        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qtimer_single_shot_functor_context",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern void QTimerSingleShotContext(
            int msec, IntPtr context, IntPtr token, IntPtr dispatch);

        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qmetaobject_invoke_method_functor",
            CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool InvokeMethod(
            IntPtr context, int connectionType, IntPtr token, IntPtr dispatch);
    }
}
