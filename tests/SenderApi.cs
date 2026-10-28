using System;
using System.Threading;

// QObject.Sender / QObject.SenderSignal: PyQt's sender() idea. The runtime pushes the emitting
// object and the connected moc signature onto a per-thread stack around every managed signal
// handler, so a slot can ask who delivered the signal instead of capturing the emitter in a
// closure. These checks cover a generated signal, nested emissions (the stack must restore the
// outer frame), the null answer outside a callback, a cross-thread queued delivery, and the one
// documented boundary: a C#-declared [QtSignal] event invoked directly rather than through Qt.
[Test("test-sender")]
public static class ApiSenderDemo
{
    // A [QtSignal] declared in C#, so both emit paths can be compared: RaiseDirect is the
    // `ValueChanged?.Invoke(...)` spelling the doc calls out, RaiseThroughQt goes through Qt.
    sealed class Emitter : QObject
    {
        [QtSignal] public event Action<int> ValueChanged;

        public void RaiseDirect(int value) => ValueChanged?.Invoke(value);

        public void RaiseThroughQt(int value) => EmitSignal(ValueChanged, value);
    }

    // A QThread whose run() is Qt's event loop; the connection's context object is created inside
    // Run, so its thread affinity is the worker and a queued connection is delivered there. The
    // context hangs off a disposable QTimer that is deleted on the same thread after the loop.
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
            contextOwner.Dispose();          // also deletes the context child
        }
    }

    public static int Run(TestContext context)
    {
        // ---------------------------------------------------------------- outside a callback
        var owner = new QTimer();
        var idle = new QObject(owner);
        context.Check(idle.Sender == null && idle.SenderSignal == null,
            "Sender and SenderSignal are null outside a signal callback");

        // ---------------------------------------------------------------- a generated signal
        // clicked(bool) is a moc signal delivered from Qt. The handler must see the same wrapper
        // instance the signal was connected on - not a second QObject wrapper at the same handle.
        var button = new QPushButton("sender");
        QObject seen = null;
        QObject seenFromAnotherReceiver = null;
        string seenSignature = null;
        int clicked = 0;
        button.Clicked += checkedState =>
        {
            clicked++;
            seen = button.Sender;
            // The value belongs to the executing callback, not to the instance the getter is
            // called on: reading it from a different QObject yields the same sender (PyQt's
            // self.sender() means this, without `self` mattering).
            seenFromAnotherReceiver = idle.Sender;
            seenSignature = button.SenderSignal;
        };
        button.Click();
        context.Check(clicked == 1, $"the click reached the handler (got {clicked})");
        context.Check(ReferenceEquals(seen, button),
            $"Sender is the exact wrapper instance the signal was connected on (got {seen?.GetType().Name ?? "null"})");
        context.Check(seen is QPushButton, "Sender resolves to the bound type, not a base-class duplicate");
        context.Check(ReferenceEquals(seenFromAnotherReceiver, button),
            "Sender is per callback, so reading it from another QObject gives the same answer");
        context.Check(seenSignature == "clicked(bool)",
            $"SenderSignal is the recorded moc signature (got '{seenSignature}')");
        context.Check(button.Sender == null && button.SenderSignal == null,
            "the callback's frame is gone once the handler returns");
        button.Dispose();

        // ---------------------------------------------------------------- nested emissions
        // The outer handler emits an inner signal. While the inner handler runs it must see the
        // inner sender; after it returns the outer frame must be visible again.
        var outer = new QTimer();
        var inner = new QTimer();
        QObject outerBefore = null, innerSeen = null, outerAfter = null;
        string innerSignature = null;
        outer.Timeout += () =>
        {
            outerBefore = outer.Sender;
            inner.TimeoutSignal.Emit();
            outerAfter = outer.Sender;
        };
        inner.Timeout += () =>
        {
            innerSeen = inner.Sender;
            innerSignature = inner.SenderSignal;
        };
        outer.TimeoutSignal.Emit();
        context.Check(ReferenceEquals(outerBefore, outer), "the outer handler sees the outer sender");
        context.Check(ReferenceEquals(innerSeen, inner),
            $"the nested handler sees the inner sender (got {innerSeen?.GetType().Name ?? "null"})");
        context.Check(innerSignature == "timeout()", $"the nested signature is the inner one (got '{innerSignature}')");
        context.Check(ReferenceEquals(outerAfter, outer),
            "the outer sender is restored after the nested emission returns");
        context.Check(outer.Sender == null && inner.Sender == null,
            "both frames are gone after the outer emission returns");
        outer.Dispose();
        inner.Dispose();

        // ---------------------------------------------------------------- declared-event boundary
        // A [QtSignal] event invoked directly runs its handlers from the event's own delegate
        // chain, after the trampoline's emission has already returned from Qt. There is no frame
        // for it, so at top level Sender/SenderSignal are null - the documented boundary.
        var emitter = new Emitter();
        QObject declaredSeen = idle;
        string declaredSignature = "unset";
        int declaredRuns = 0;
        emitter.ValueChanged += _ =>
        {
            declaredRuns++;
            declaredSeen = emitter.Sender;
            declaredSignature = emitter.SenderSignal;
        };
        emitter.RaiseDirect(1);
        context.Check(declaredRuns == 1 && declaredSeen == null && declaredSignature == null,
            $"a directly invoked [QtSignal] handler has no sender frame (got {declaredSeen?.GetType().Name ?? "null"}, '{declaredSignature}')");

        // The same event raised through Qt (EmitSignal -> QMetaObject::activate -> the runtime's
        // connection) does have one: the handlers run inside the connection's callback.
        emitter.RaiseThroughQt(2);
        context.Check(ReferenceEquals(declaredSeen, emitter) && declaredSignature == "valueChanged(int)",
            $"the same event raised through Qt sees its sender and signature (got {declaredSignature})");

        // ---------------------------------------------------------------- queued, other thread
        // The connection is queued to a context living on the worker; the sender is the QTimer on
        // the main thread. The handler must run on the worker and still resolve the same wrapper.
        var worker = new LoopingThread();
        worker.Start();
        context.Check(worker.Ready.Wait(5000), "the worker thread started and created its context");
        IntPtr mainThreadId = QThread.CurrentThreadId();
        context.Check(worker.ThreadId != mainThreadId,
            $"the worker runs on its own thread (worker {worker.ThreadId}, main {mainThreadId})");

        var queuedTimer = new QTimer();
        queuedTimer.SetInterval(5);
        var queuedReceiver = new QObject(owner);
        QObject queuedSender = null;
        string queuedSignature = null;
        IntPtr queuedThreadId = IntPtr.Zero;
        int queuedCalls = 0;
        using (queuedTimer.TimeoutSignal.Connect(
                   () =>
                   {
                       queuedSender = queuedReceiver.Sender;
                       queuedSignature = queuedReceiver.SenderSignal;
                       queuedThreadId = QThread.CurrentThreadId();
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
        context.Check(Volatile.Read(ref queuedCalls) > 0,
            $"the queued connection was delivered (got {queuedCalls})");
        context.Check(queuedThreadId == worker.ThreadId,
            $"the queued handler ran on the context's thread (handler {queuedThreadId}, context {worker.ThreadId})");
        context.Check(ReferenceEquals(queuedSender, queuedTimer),
            $"a cross-thread queued handler resolves the same sender wrapper (got {queuedSender?.GetType().Name ?? "null"})");
        context.Check(queuedSignature == "timeout()",
            $"a cross-thread queued handler sees the signature (got '{queuedSignature}')");

        worker.Quit();
        context.Check(worker.Wait(5000), "the worker thread stopped");
        worker.Dispose();
        queuedTimer.Dispose();
        owner.Dispose();

        return context.ExitCode;
    }
}
