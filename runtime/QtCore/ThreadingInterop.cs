using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using QtSharp.Runtime;

// The task layer the binding does not get from Qt. QFuture<T> is a template, so there is nothing
// to bind; in C# its role is Task, and these are the two spots where Qt's threading primitives and
// Task meet:
//
//   await pool.RunAsync(() => Decode(path));          // work on Qt's thread pool
//   await window.InvokeAsync(() => title = text);     // work on the object's thread
//
// Both hand the native side a managed token (a GCHandle) and one dispatcher, exactly like the
// functor helpers in FunctorInterop.cs: methodId 0 runs the work, methodId 1 releases the token
// when Qt drops the task without running it. The dropped path is what turns QThreadPool::clear()
// (or a destroyed context object) into a cancelled Task instead of one that never completes.
namespace QtSharp.QtCore
{
    /// <summary>
    /// Asynchronous counterparts of the Qt threading primitives: a delegate-based task for
    /// <see cref="QThreadPool"/> and a queued invocation for a <see cref="QObject"/>.
    /// </summary>
    public static class QThreadPoolExtensions
    {
        /// <summary>
        /// Runs <paramref name="action"/> on the pool and returns a task that completes when it
        /// has. The C# spelling of QtConcurrent::run / QFuture, without the template.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="pool"/> or <paramref name="action"/> is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
        public static Task RunAsync(this QThreadPool pool, Action action, int priority = 0)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));
            return Queue(pool, () => { action(); return true; }, CancellationToken.None, priority);
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the pool unless <paramref name="cancellationToken"/>
        /// is already cancelled when a worker picks the task up. The check is made at the start of
        /// the work; cancelling afterwards needs cooperation inside <paramref name="action"/>.
        /// </summary>
        public static Task RunAsync(this QThreadPool pool, Action action,
            CancellationToken cancellationToken, int priority = 0)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));
            return Queue(pool, () => { action(); return true; }, cancellationToken, priority);
        }

        /// <summary>Runs <paramref name="func"/> on the pool and completes with its result.</summary>
        public static Task<T> RunAsync<T>(this QThreadPool pool, Func<T> func, int priority = 0)
        {
            if (func is null)
                throw new ArgumentNullException(nameof(func));
            return Queue(pool, func, CancellationToken.None, priority);
        }

        /// <summary>Runs <paramref name="func"/> on the pool unless the token is already cancelled.</summary>
        public static Task<T> RunAsync<T>(this QThreadPool pool, Func<T> func,
            CancellationToken cancellationToken, int priority = 0)
        {
            if (func is null)
                throw new ArgumentNullException(nameof(func));
            return Queue(pool, func, cancellationToken, priority);
        }

        static Task<T> Queue<T>(QThreadPool pool, Func<T> work,
            CancellationToken cancellationToken, int priority)
        {
            if (pool is null)
                throw new ArgumentNullException(nameof(pool));
            if (!pool.IsAlive)
                throw new ObjectDisposedException(nameof(pool));

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = new ThreadPoolWork(
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }
                    try
                    {
                        completion.TrySetResult(work());
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                },
                () => completion.TrySetCanceled(cancellationToken));

            ThreadPoolTasks.Enqueue(pool.Handle, priority, task);
            return completion.Task;
        }
    }

    /// <summary>
    /// Queues a delegate on a <see cref="QObject"/>'s thread and returns a task for its completion.
    /// </summary>
    public static class QObjectThreadingExtensions
    {
        /// <summary>
        /// Queues <paramref name="action"/> on <paramref name="target"/>'s thread. The task
        /// completes when the action has run, faults with its exception, or is cancelled when the
        /// target is destroyed before Qt dispatches it.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="action"/> is null.
        /// </exception>
        public static Task InvokeAsync(this QObject target, Action action,
            Qt.ConnectionType type = Qt.ConnectionType.QueuedConnection)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));
            return InvokeAsync(target, () => { action(); return true; }, type);
        }

        /// <summary>
        /// Queues <paramref name="func"/> on <paramref name="target"/>'s thread and completes with
        /// its result.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="func"/> is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The target has already been destroyed.</exception>
        public static Task<T> InvokeAsync<T>(this QObject target, Func<T> func,
            Qt.ConnectionType type = Qt.ConnectionType.QueuedConnection)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (func is null)
                throw new ArgumentNullException(nameof(func));
            if (!target.IsAlive)
                throw new ObjectDisposedException(nameof(target),
                    "The Qt object behind this wrapper has been destroyed.");

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            QueuedInvocation.Post(target.Handle,
                () =>
                {
                    try
                    {
                        completion.TrySetResult(func());
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                },
                () => completion.TrySetCanceled(),
                type);

            // A refused invocation runs the dropped path synchronously inside the native call, so
            // the task is already cancelled here; nothing else to do.
            return completion.Task;
        }
    }

    /// <summary>
    /// The token contract shared by the queued invocation and the pool task: one rooted record per
    /// pending call, and one native dispatcher both method ids go through.
    /// </summary>
    static unsafe class QueuedInvocation
    {
        // void(IntPtr token, int methodId, IntPtr argv), matching QsApiDispatch.
        static readonly IntPtr DispatchPointer =
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch;

        internal static bool Post(IntPtr context, Action run, Action dropped, Qt.ConnectionType type)
        {
            var work = new Work { Run = run, Dropped = dropped };
            GCHandle handle = GCHandle.Alloc(work);
            try
            {
                return FunctorNative.InvokeMethod(
                    context, (int)type, GCHandle.ToIntPtr(handle), DispatchPointer);
            }
            catch
            {
                handle.Free();
                throw;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Dispatch(IntPtr token, int methodId, IntPtr argv)
        {
            GCHandle handle = GCHandle.FromIntPtr(token);
            var work = (Work)handle.Target;
            try { handle.Free(); } catch (InvalidOperationException) { }

            if (methodId == 0)
            {
                try
                {
                    work.Run();
                }
                catch (Exception exception)
                {
                    // A managed exception must not unwind into Qt's C++ frames.
                    ApiRuntime.ReportCallbackException("a queued invocation", exception);
                }
                return;
            }

            // Qt refused the invocation or destroyed the context first: the callback never runs.
            work.Dropped?.Invoke();
        }

        sealed class Work
        {
            public Action Run;
            public Action Dropped;
        }
    }

    static unsafe class ThreadPoolTasks
    {
        static readonly IntPtr DispatchPointer =
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch;

        internal static void Enqueue(IntPtr pool, int priority, ThreadPoolWork work)
        {
            GCHandle handle = GCHandle.Alloc(work);
            try
            {
                QThreadPoolNative.RunFunctor(pool, priority, GCHandle.ToIntPtr(handle), DispatchPointer);
            }
            catch
            {
                handle.Free();
                throw;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Dispatch(IntPtr token, int methodId, IntPtr argv)
        {
            GCHandle handle = GCHandle.FromIntPtr(token);
            var work = (ThreadPoolWork)handle.Target;
            try { handle.Free(); } catch (InvalidOperationException) { }

            if (methodId == 0)
            {
                try
                {
                    work.Run();
                }
                catch (Exception exception)
                {
                    ApiRuntime.ReportCallbackException("a thread-pool task", exception);
                }
                return;
            }

            work.Discarded();
        }
    }

    sealed class ThreadPoolWork
    {
        internal ThreadPoolWork(Action run, Action discarded)
        {
            Run = run;
            Discarded = discarded;
        }

        internal Action Run { get; }
        internal Action Discarded { get; }
    }

    static class QThreadPoolNative
    {
        [DllImport(ApiNative.Library, EntryPoint = "qs_api_threadpool_run_functor",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern void RunFunctor(
            IntPtr pool, int priority, IntPtr token, IntPtr dispatch);
    }
}
