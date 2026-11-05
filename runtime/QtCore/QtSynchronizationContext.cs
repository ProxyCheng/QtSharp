using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using QtSharp.Runtime;

// C# has the thing QtAsyncio has to build for Python: a language-level await, and one hook -
// SynchronizationContext - that decides where a continuation runs. Installing this context on the
// thread that owns the widgets is all it takes for `await` in an event handler to resume on that
// thread, the way `QtAsyncio.run` lets `await` work next to a QEventLoop.
//
//     QtSynchronizationContext.Install();     // on the GUI thread, after the QApplication
//     async void OnClicked()
//     {
//         var rows = await Task.Run(() => ReadRows());   // worker thread
//         list.AddItems(rows);                            // back on the GUI thread
//     }
//
// Post goes through QMetaObject::invokeMethod with QueuedConnection, so a continuation is an
// ordinary queued event: it needs a running event loop, and it is dropped when the context object
// dies - the same way Qt drops a queued functor whose context is gone. That drop is what keeps a
// closed window from running its handlers afterwards.
namespace QtSharp.QtCore
{
    /// <summary>
    /// A <see cref="SynchronizationContext"/> that posts continuations to a Qt thread. The
    /// C# counterpart of PySide's <c>QtAsyncio</c> scheduler: <c>await</c> resumes on the thread
    /// the context object lives on, including after a <see cref="System.Threading.Tasks.Task.Run(Func{System.Threading.Tasks.Task})"/>
    /// hop to a worker thread.
    /// </summary>
    /// <remarks>
    /// Install one per thread that runs Qt code, usually just the GUI thread, and do it after the
    /// <see cref="QCoreApplication"/> exists. The thread must be pumping an event loop for
    /// continuations to run. Thread affinity and object lifetime are the caller's job: a context
    /// object that is destroyed while continuations are pending drops them, so a handler awaiting
    /// on a dead window simply never resumes.
    /// </remarks>
    public sealed class QtSynchronizationContext : SynchronizationContext
    {
        readonly QObject _context;
        readonly IntPtr _threadId;

        QtSynchronizationContext(QObject context)
        {
            _context = context;
            _threadId = QThread.CurrentThreadId();
        }

        /// <summary>
        /// Installs a context for the calling thread and returns it. When <paramref name="context"/>
        /// is null the application instance is used, or - before a QApplication exists - a plain
        /// <see cref="QObject"/> created on this thread. Calling it twice on the same thread returns
        /// the installed context.
        /// </summary>
        /// <remarks>
        /// The context object pins the target thread and its lifetime: continuations are queued
        /// against it, so destroying it drops them. It must live on the calling thread.
        /// </remarks>
        public static QtSynchronizationContext Install(QObject context = null)
        {
            if (Current is QtSynchronizationContext installed && installed.IsTargetThread)
                return installed;

            if (context is null)
                context = QCoreApplication.Instance();
            if (context is null)
                context = new QObject();

            var created = new QtSynchronizationContext(context);
            SetSynchronizationContext(created);
            return created;
        }

        /// <summary>The context installed for the calling thread, or null when there is none.</summary>
        public static new QtSynchronizationContext Current => SynchronizationContext.Current as QtSynchronizationContext;

        /// <summary>The Qt object whose thread this context posts to.</summary>
        public QObject Context => _context;

        /// <summary>Whether the calling thread is the one this context posts to.</summary>
        public bool IsTargetThread => QThread.CurrentThreadId() == _threadId;

        /// <summary>
        /// Queues <paramref name="callback"/> on the target thread. A continuation posted to a
        /// destroyed context is dropped: the task that posted it never resumes, exactly like a
        /// queued Qt functor whose context object died.
        /// </summary>
        public override void Post(SendOrPostCallback callback, object state)
        {
            if (callback is null)
                throw new ArgumentNullException(nameof(callback));

            TryPostCore(() => RunGuarded(callback, state));
        }

        /// <summary>
        /// Runs <paramref name="callback"/> on the target thread and waits for it. Called on the
        /// target thread it runs inline; from another thread it blocks until the target thread has
        /// run it, so the target must be pumping its event loop.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The context object was destroyed, so the callback could never run.
        /// </exception>
        public override void Send(SendOrPostCallback callback, object state)
        {
            if (callback is null)
                throw new ArgumentNullException(nameof(callback));

            if (IsTargetThread)
            {
                callback(state);
                return;
            }

            ExceptionDispatchInfo error = null;
            using (var done = new ManualResetEventSlim())
            {
                bool queued = TryPostCore(() =>
                {
                    try
                    {
                        callback(state);
                    }
                    catch (Exception exception)
                    {
                        error = ExceptionDispatchInfo.Capture(exception);
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                if (!queued)
                    throw new ObjectDisposedException(nameof(QtSynchronizationContext),
                        "The context object was destroyed; the callback can never run on its thread.");

                done.Wait();
            }

            error?.Throw();
        }

        /// <summary>
        /// The copy is the same context: a copy represents the same target thread, so it must post
        /// there rather than inherit the caller's thread.
        /// </summary>
        public override SynchronizationContext CreateCopy() => this;

        bool TryPostCore(Action action)
        {
            if (!_context.IsAlive)
                return false;

            // Queued even from the target thread: Post promises not to run the callback inline.
            return QueuedInvocation.Post(_context.Handle, action, null, Qt.ConnectionType.QueuedConnection);
        }

        static void RunGuarded(SendOrPostCallback callback, object state)
        {
            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                // An async void handler must not unwind into Qt's event loop.
                ApiRuntime.ReportCallbackException("a QtSynchronizationContext continuation", exception);
            }
        }
    }
}
