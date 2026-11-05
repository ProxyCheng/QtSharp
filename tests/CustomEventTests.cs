using System;
using System.Threading;
using QtSharp.QtCore;
using QtSharp.QtWidgets;

// Custom QEvent instances from managed code: QEvent's constructor is bound, a C# subclass supplies
// the payload, and QCoreApplication.PostEvent transfers the instance to Qt, which deletes it after
// delivery. The type id comes from QEvent.RegisterEventType, the same call a C++ application makes.
//
// The receiver sees an ordinary QEvent wrapper (Qt hands it the pointer, and the posted wrapper is
// already dead), so the event *type* is what crosses the boundary: dispatch on type(), and keep the
// payload in the receiver or in a type-keyed table. This is the same contract PyQt has: a posted
// event is a QEvent on the other side, not your Python object.
[Test("test-custom-events")]
public static class CustomEventTests
{
    static int customType;

    sealed class PayloadEvent : QEvent
    {
        public int Payload;

        public PayloadEvent(QEvent.Type type, int payload) : base(type)
        {
            Payload = payload;
        }
    }

    sealed class Receiver : QWidget
    {
        public int SeenType = -1;

        public override bool Event(QEvent e)
        {
            if ((int)e.type() == customType)
            {
                SeenType = (int)e.type();
                return true;
            }
            return base.Event(e);
        }
    }

    public static int Run(TestContext context)
    {
        customType = QEvent.RegisterEventType();
        context.Check(customType >= (int)QEvent.Type.User,
            $"registerEventType returns a user type ({customType})");

        using var receiver = new Receiver();
        var posted = new PayloadEvent((QEvent.Type)customType, 7);

        context.Check(posted.IsAlive, "a managed QEvent subclass constructs");
        QCoreApplication.PostEvent(receiver, posted);
        context.Check(!posted.IsAlive,
            "posting transfers ownership: the wrapper is invalidated, so Dispose cannot double-free");

        Pump(() => receiver.SeenType != -1);
        context.Check(receiver.SeenType == customType,
            $"the receiver's Event override saw the custom type ({receiver.SeenType})");

        bool nullRefused = false;
        try { QCoreApplication.PostEvent(receiver, (QEvent)null); }
        catch (ArgumentNullException) { nullRefused = true; }
        context.Check(nullRefused, "posting a null event is rejected");

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
}
