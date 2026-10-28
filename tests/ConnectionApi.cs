using System;
using System.Threading;

// Signal connections as disposable handles: QSignal.Connect returns a QtConnection whose Dispose
// drops that one connection, and the QtCore extension overloads add a context QObject and a
// Qt.ConnectionType. Before this, only a signal exposed as a C# event could be disconnected;
// `TimeoutSignal.Connect(handler)` returned void and lived until its sender died.
//
// The context is also the receiver Qt uses for thread affinity, so a queued connection runs on the
// context's thread. A thread test therefore needs a real event loop on the worker side, which is
// what LoopingThread below provides; the assertion records the native thread id inside the handler.
[Demo("test-connections")]
public static class ApiConnectionDemo
{
    // A QThread whose run() is Qt's own event loop. The context object is created inside Run:
    // a QObject's thread affinity is the thread it was created in, so this is the context the
    // queued connection is delivered on. A bare QObject has no Dispose in this binding (it has no
    // release path of its own), so the context is a child of a disposable QTimer that is deleted
    // on the same thread after the loop exits - no cross-thread deletion.
    sealed class LoopingThread : QThread
    {
        public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
        public QObject Context;
        public IntPtr ThreadId;
        QTimer contextOwner;

        public override void Run()
        {
            ThreadId = QThread.CurrentThreadId();
            contextOwner = new QTimer();
            Context = new QObject(contextOwner);
            Ready.Set();
            Exec();
            contextOwner.Dispose();      // also deletes the context child
        }
    }

    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // A timer's timeout() is an ordinary signal; Emit activates it without an event loop.
        var timer = new QTimer();

        // ---------------------------------------------------------------- the disposable handle
        // `using var` is the shape the issue asks for: no explicit Disconnect call, the handle
        // owns the connection and binds its lifetime to the scope.
        int scopedCalls = 0;
        {
            using var connection = timer.TimeoutSignal.Connect(() => scopedCalls++);
            Check(connection != null && connection.IsConnected, "Connect returns a live QtConnection");
            timer.TimeoutSignal.Emit();
            Check(scopedCalls == 1, $"the connection delivers the emission (got {scopedCalls})");
        }
        timer.TimeoutSignal.Emit();
        Check(scopedCalls == 1,
            $"disposing the handle disconnects it, so the later emission is ignored (got {scopedCalls})");

        // Disposing twice is documented as a no-op, like every other generated Dispose.
        int doubleDisposeCalls = 0;
        var doubleDispose = timer.TimeoutSignal.Connect(() => doubleDisposeCalls++);
        doubleDispose.Dispose();
        doubleDispose.Dispose();
        timer.TimeoutSignal.Emit();
        Check(doubleDisposeCalls == 0, "a second Dispose does not resurrect or double-free anything");

        // ---------------------------------------------------------------- the handler is the key
        // The connector records the handler as the connection's key, which is the same record an
        // event's `-=` looks up - so the key-based Disconnect finds a QSignal connection too.
        int keyedCalls = 0;
        Action keyHandler = () => keyedCalls++;
        var keyedConnection = timer.TimeoutSignal.Connect(keyHandler);
        Check(ApiRuntime.Disconnect(timer.Handle, "timeout()", keyHandler),
            "a QSignal connection is bookkept under its handler key");
        timer.TimeoutSignal.Emit();
        Check(keyedCalls == 0, "the key-based Disconnect dropped it");
        keyedConnection.Dispose();       // already disconnected: safe no-op

        // A handle identifies one connection, not "the newest one with this handler": the same
        // handler can be connected twice and dropped one at a time.
        int repeatedCalls = 0;
        Action repeated = () => repeatedCalls++;
        var first = timer.TimeoutSignal.Connect(repeated);
        var second = timer.TimeoutSignal.Connect(repeated);
        timer.TimeoutSignal.Emit();
        Check(repeatedCalls == 2, $"the same handler connected twice runs twice (got {repeatedCalls})");
        first.Dispose();
        timer.TimeoutSignal.Emit();
        Check(repeatedCalls == 3, $"disposing one handle leaves the other connection (got {repeatedCalls})");
        second.Dispose();
        timer.TimeoutSignal.Emit();
        Check(repeatedCalls == 3, $"disposing the last handle leaves neither (got {repeatedCalls})");

        // ---------------------------------------------------------------- context lifetime
        // A context the caller owns: destroying it before the signal fires must remove the
        // connection Qt is holding, so the handler is not reached. A bare QObject has no Dispose,
        // so it dies with a disposable parent - one of the ordinary ways a QObject goes away.
        var contextOwner = new QTimer();
        var context = new QObject(contextOwner);
        int contextCalls = 0;
        var contextConnection = timer.TimeoutSignal.Connect(() => contextCalls++, context);
        timer.TimeoutSignal.Emit();
        Check(contextCalls == 1, $"a context connection is live while the context is (got {contextCalls})");
        contextOwner.Dispose();
        Check(!contextConnection.IsConnected, "destroying the context marks the handle disconnected");
        timer.TimeoutSignal.Emit();
        Check(contextCalls == 1,
            $"destroying the context before the signal fires prevents the handler (got {contextCalls})");
        contextConnection.Dispose();     // the connection is gone; the handle still frees cleanly

        // ---------------------------------------------------------------- connection type
        // Direct with no context: the old shape, now with the type spelled out.
        int directCalls = 0;
        using (timer.TimeoutSignal.Connect(() => directCalls++, Qt.ConnectionType.DirectConnection))
        {
            timer.TimeoutSignal.Emit();
            Check(directCalls == 1, $"a direct connection runs on the emitting thread (got {directCalls})");
        }

        // ---------------------------------------------------------------- queued on a context
        // The worker owns an event loop; the context is created inside it (so its thread affinity
        // is the worker), the connection is queued, and the handler must run over there.
        var worker = new LoopingThread();
        worker.Start();
        Check(worker.Ready.Wait(5000), "the worker thread started and created its context");
        IntPtr mainThreadId = QThread.CurrentThreadId();
        Check(worker.ThreadId != mainThreadId,
            $"the worker runs on its own thread (worker {worker.ThreadId}, main {mainThreadId})");

        var queuedTimer = new QTimer();
        queuedTimer.SetInterval(5);
        IntPtr handlerThreadId = IntPtr.Zero;
        int queuedCalls = 0;
        using (queuedTimer.TimeoutSignal.Connect(
                   () =>
                   {
                       handlerThreadId = QThread.CurrentThreadId();
                       Interlocked.Increment(ref queuedCalls);
                   },
                   worker.Context, Qt.ConnectionType.QueuedConnection))
        {
            queuedTimer.Start();
            long deadline = Environment.TickCount64 + 5000;
            while (Volatile.Read(ref queuedCalls) == 0 && Environment.TickCount64 < deadline)
            {
                QCoreApplication.ProcessEvents();
                Thread.Sleep(1);
            }
            queuedTimer.Stop();
        }
        Check(Volatile.Read(ref queuedCalls) > 0,
            $"the queued connection is delivered through the worker's event loop (got {queuedCalls})");
        Check(handlerThreadId == worker.ThreadId,
            $"the queued handler runs on the context's thread (handler {handlerThreadId}, context {worker.ThreadId})");

        worker.Quit();
        Check(worker.Wait(5000), "the worker thread stopped");
        worker.Dispose();
        queuedTimer.Dispose();

        // ---------------------------------------------------------------- the event spelling
        // `+=`/`-=` is unchanged: both go through the same keyed record, and `-=` drops exactly
        // the handler it names.
        int eventCalls = 0;
        Action eventFirst = () => eventCalls++;
        Action eventSecond = () => eventCalls++;
        timer.Timeout += eventFirst;
        timer.Timeout += eventSecond;
        timer.TimeoutSignal.Emit();
        Check(eventCalls == 2, $"event += subscribes both handlers (got {eventCalls})");
        timer.Timeout -= eventFirst;
        timer.TimeoutSignal.Emit();
        Check(eventCalls == 3, $"event -= removes only the handler it names (got {eventCalls})");
        timer.Timeout -= eventSecond;
        timer.TimeoutSignal.Emit();
        Check(eventCalls == 3, $"the last event -= disconnects as well (got {eventCalls})");

        timer.Dispose();

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
