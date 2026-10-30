using System;
using System.Threading;

namespace QtSharp.Tests
{
    /// <summary>
    /// Qt's functor-taking template APIs: QTimer::singleShot and QMetaObject::invokeMethod. The
    /// callback is a managed delegate whose token native code releases either when it fires or when
    /// Qt destroys the functor (context object died).
    /// </summary>
    [Test("test-functor-interop")]
    public static class FunctorInteropTests
    {
        public static int Run(TestContext context)
        {
            // ---------------------------------------------------------------- singleShot
            int fired = 0;
            QTimer.singleShot(0, () => fired++);
            Pump(() => fired == 1);
            context.Check(fired == 1, $"singleShot(msec, Action) fires once ({fired})");

            int contextFired = 0;
            var alive = new QTimer();
            QTimer.singleShot(0, alive, () => contextFired++);
            Pump(() => contextFired == 1);
            context.Check(contextFired == 1, "singleShot with a live context fires");
            alive.Dispose();

            // Destroying the context destroys the pending functor; the managed token is released
            // on that path instead of running.
            int cancelled = 0;
            var doomed = new QTimer();
            QTimer.singleShot(5000, doomed, () => cancelled++);
            doomed.Dispose();
            PumpFor(50);
            context.Check(cancelled == 0, "destroying the context cancels the pending call");

            // ---------------------------------------------------------------- invokeMethod
            int invoked = 0;
            using (var target = new QTimer())
            {
                bool accepted = QMetaObject.InvokeMethod(target, () => invoked++,
                    Qt.ConnectionType.QueuedConnection);
                context.Check(accepted, "invokeMethod accepts the functor");
                Pump(() => invoked == 1);
                context.Check(invoked == 1, $"the queued functor ran ({invoked})");
            }

            // ---------------------------------------------------------------- argument checks
            bool nullActionRefused = false;
            try { QTimer.singleShot(0, (Action)null); }
            catch (ArgumentNullException) { nullActionRefused = true; }
            context.Check(nullActionRefused, "a null Action is rejected");

            bool nullContextRefused = false;
            try { QTimer.singleShot(0, (QObject)null, () => { }); }
            catch (ArgumentNullException) { nullContextRefused = true; }
            context.Check(nullContextRefused, "a null context is rejected");

            return context.ExitCode;
        }

        static void Pump(Func<bool> done)
        {
            for (int i = 0; i < 200 && !done(); i++)
            {
                QCoreApplication.ProcessEvents(QEventLoop.ProcessEventsFlags.AllEvents, 10);
                Thread.Sleep(1);
            }
        }

        static void PumpFor(int milliseconds)
        {
            for (int i = 0; i < milliseconds; i += 10)
            {
                QCoreApplication.ProcessEvents(QEventLoop.ProcessEventsFlags.AllEvents, 10);
                Thread.Sleep(1);
            }
        }
    }
}
