using System;
using System.Runtime.InteropServices;

// QThread::create(Function) is a template, so it has no exported symbol to bind. The native side
// instantiates it around the same managed token the other functor helpers use
// (native/QtSharpApiRuntime.cpp): the action runs on the new thread, and Qt destroys the functor -
// releasing the delegate - when the thread finishes.
//
// The returned QThread is owned by the caller, exactly as Qt documents for create(): start it,
// wait for it, then Dispose it. Disposing a thread that is still running deletes a live QThread,
// which Qt rejects; the wrapper follows Qt's rule rather than inventing a different one.
namespace QtSharp.QtCore
{
    public partial class QThread
    {
        /// <summary>
        /// Creates a thread that runs <paramref name="action"/> once. The caller owns the returned
        /// thread: call <see cref="Start()"/>, wait with <see cref="Wait(int)"/>, then dispose it.
        /// The managed equivalent of Qt's template <c>QThread::create(Function)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        /// <remarks>
        /// The action runs on the new thread; an exception it throws is reported through
        /// <c>ApiRuntime.UnhandledCallbackException</c> and does not unwind into Qt.
        /// </remarks>
        public static QThread Create(Action action)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            FunctorDispatch.Create(action, out IntPtr token, out IntPtr dispatch);
            try
            {
                return FromOwnedHandle(QThreadNative.Create(token, dispatch));
            }
            catch
            {
                FunctorDispatch.Release(token);
                throw;
            }
        }
    }

    static class QThreadNative
    {
        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qthread_create",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Create(IntPtr token, IntPtr dispatch);
    }
}
