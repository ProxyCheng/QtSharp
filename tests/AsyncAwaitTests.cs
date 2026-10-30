using System;
using System.Threading;
using System.Threading.Tasks;
using QtSharp.QtCore;

// The async/await layer: QtSynchronizationContext makes await resume on the Qt thread, and the two
// task helpers (QThreadPool.RunAsync, QObject.InvokeAsync) complete on the caller's context.
// Everything is pumped by hand, the way the test host runs: the QApplication exists, but nothing
// calls exec(), so ProcessEvents is the event loop.
[Test("test-async-await")]
public static class AsyncAwaitTests
{
    public static int Run(TestContext context)
    {
        IntPtr mainThreadId = QThread.CurrentThreadId();
        QtSynchronizationContext sync = QtSynchronizationContext.Install();
        try
        {
            context.Check(QtSynchronizationContext.Current == sync,
                "Install sets the thread's SynchronizationContext");
            context.Check(sync.IsTargetThread, "the context knows the thread it posts to");
            context.Check(ReferenceEquals(sync.CreateCopy(), sync),
                "a copy is the same context, not a copy of the caller's thread");

            RunContinuationTests(context, mainThreadId, sync);
            RunThreadPoolTests(context, mainThreadId);
            RunInvocationTests(context, mainThreadId);
            RunDestructionTests(context);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }

        return context.ExitCode;
    }

    // ---------------------------------------------------------------- continuations
    static void RunContinuationTests(TestContext context, IntPtr mainThreadId,
        QtSynchronizationContext sync)
    {
        IntPtr resumedOn = IntPtr.Zero;
        bool finished = false;
        async Task Hop()
        {
            await Task.Run(() => Thread.Sleep(5));
            resumedOn = QThread.CurrentThreadId();
            finished = true;
        }
        _ = Hop();
        Pump(() => finished);
        context.Check(finished, "the continuation after Task.Run ran");
        context.Check(resumedOn == mainThreadId,
            $"await resumed on the Qt thread (worker {resumedOn}, main {mainThreadId})");

        // Two hops: the first await captures the Qt context, the second continues from the first
        // continuation's context - which is this one again.
        int value = 0;
        bool nested = false;
        async Task Nested()
        {
            int first = await Task.Run(() => 1);
            int second = await Task.Run(() => first + 1);
            value = second;
            nested = true;
        }
        _ = Nested();
        Pump(() => nested);
        context.Check(nested && value == 2, $"nested awaits keep their order and values ({value})");

        // await with ConfigureAwait(false) must stay on the worker; the context does not leak into
        // code that opted out.
        IntPtr unconfigured = IntPtr.Zero;
        bool offThread = false;
        async Task WithoutContext()
        {
            await Task.Run(() => 1).ConfigureAwait(false);
            unconfigured = QThread.CurrentThreadId();
            offThread = true;
        }
        _ = WithoutContext();
        Pump(() => offThread);
        context.Check(offThread && unconfigured != mainThreadId,
            "ConfigureAwait(false) keeps the continuation off the Qt thread");

        // Send from a worker blocks until the Qt thread runs the callback.
        IntPtr sentOn = IntPtr.Zero;
        Task sender = Task.Run(() => sync.Send(_ => sentOn = QThread.CurrentThreadId(), null));
        Pump(() => sender.IsCompleted);
        context.Check(sender.IsCompleted && sentOn == mainThreadId,
            "Send from a worker runs the callback on the Qt thread");
    }

    // ---------------------------------------------------------------- QThreadPool tasks
    static void RunThreadPoolTests(TestContext context, IntPtr mainThreadId)
    {
        using var pool = new QThreadPool();
        pool.MaxThreadCount = 2;

        Task<int> result = pool.RunAsync(() => 6 * 7);
        Pump(() => result.IsCompleted);
        context.Check(result.Status == TaskStatus.RanToCompletion && result.Result == 42,
            $"RunAsync(Func<T>) completes with the result ({result.Status})");

        int ran = 0;
        Task action = pool.RunAsync(() => { Interlocked.Increment(ref ran); });
        Pump(() => action.IsCompleted);
        context.Check(action.Status == TaskStatus.RanToCompletion && ran == 1,
            "RunAsync(Action) completes when the work has run");

        // The work's exception travels through the task, not out of Qt.
        Task<int> failing = pool.RunAsync<int>(() => throw new InvalidOperationException("boom"));
        Pump(() => failing.IsCompleted);
        context.Check(failing.IsFaulted
              && failing.Exception.InnerException is InvalidOperationException,
            "an exception in the work faults the task");

        // A token cancelled before a worker picks the task up means the work never runs.
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            bool started = false;
            Task skipped = pool.RunAsync(() => { started = true; }, cancelled.Token);
            Pump(() => skipped.IsCompleted);
            context.Check(skipped.IsCanceled && !started,
                "a pre-cancelled token skips the work and cancels the task");
        }

        // Awaiting a pool task from the Qt thread resumes on the Qt thread.
        IntPtr afterPool = IntPtr.Zero;
        bool done = false;
        async Task ViaPool()
        {
            await pool.RunAsync(() => { });
            afterPool = QThread.CurrentThreadId();
            done = true;
        }
        _ = ViaPool();
        Pump(() => done);
        context.Check(done && afterPool == mainThreadId,
            "await RunAsync resumes on the Qt thread");

        bool nullRefused = false;
        try { QThreadPoolExtensions.RunAsync(null, () => { }); }
        catch (ArgumentNullException) { nullRefused = true; }
        context.Check(nullRefused, "RunAsync refuses a null pool");
    }

    // ---------------------------------------------------------------- QObject.InvokeAsync
    static void RunInvocationTests(TestContext context, IntPtr mainThreadId)
    {
        using var target = new QTimer();

        IntPtr ranOn = IntPtr.Zero;
        Task<int> invocation = target.InvokeAsync(() =>
        {
            ranOn = QThread.CurrentThreadId();
            return 9;
        });
        Pump(() => invocation.IsCompleted);
        context.Check(invocation.Status == TaskStatus.RanToCompletion && invocation.Result == 9,
            $"InvokeAsync(Func<T>) completes with the result ({invocation.Status})");
        context.Check(ranOn == mainThreadId, "the invocation ran on the object's thread");

        // From a worker thread the call still lands on the object's thread.
        Task<int> fromWorker = Task.Run(async () => await target.InvokeAsync(() => 11));
        Pump(() => fromWorker.IsCompleted);
        context.Check(fromWorker.IsCompleted && fromWorker.Result == 11,
            "InvokeAsync from a worker thread completes with the result");

        Task<IntPtr> workerThread = Task.Run(
            async () => await target.InvokeAsync(() => QThread.CurrentThreadId()));
        Pump(() => workerThread.IsCompleted);
        context.Check(workerThread.IsCompleted && workerThread.Result == mainThreadId,
            "the worker's invocation ran on the Qt thread, not the worker's");
    }

    // ---------------------------------------------------------------- destruction
    static void RunDestructionTests(TestContext context)
    {
        // The queued functor dies with its context object: the task is cancelled, not left pending.
        var doomed = new QTimer();
        Task<int> queued = doomed.InvokeAsync(() => 1);
        doomed.Dispose();
        Pump(() => queued.IsCompleted);
        context.Check(queued.IsCanceled,
            $"a queued invocation whose context died is cancelled ({queued.Status})");

        bool disposedRefused = false;
        try { doomed.InvokeAsync(() => 2); }
        catch (ObjectDisposedException) { disposedRefused = true; }
        context.Check(disposedRefused, "invoking on a disposed object throws ObjectDisposedException");
    }

    static void Pump(Func<bool> done)
    {
        for (int i = 0; i < 400 && !done(); i++)
        {
            QCoreApplication.ProcessEvents(QEventLoop.ProcessEventsFlags.AllEvents, 10);
            Thread.Sleep(1);
        }
    }
}
