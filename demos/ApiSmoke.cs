using System;
using QtSharp.Api;

// Smoke test for the generated bindings (model -> codegen -> build -> run).
// Exercises all three mechanisms through *generated* code, not hand-written glue.
public sealed class ApiDerivedObject : QObject
{
    public int FilterCalls;

    public override bool EventFilter(QObject watched, QEvent e)
    {
        FilterCalls++;
        return true;
    }
}

public static class ApiSmokeTest
{
    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // The application object came from the dispatcher, which built it from the real command
        // line the way a PyQt script does. Constructing it again hands back the same object - Qt
        // allows exactly one - and the runtime keeps the argv alive for as long as the process.
        var again = new QApplication(new[] { "qtsharp-api", "--qtsharp-selftest" });
        Check(app.Handle != IntPtr.Zero, "new QApplication(args) wraps the process's application object");
        Check(again.Handle == app.Handle && QCoreApplication.Instance().Handle == app.Handle,
            "and it is the same object Qt reports through instance()");


        var obj = new QObject();
        Check(obj.Handle != IntPtr.Zero, "QObject created through the generated binding");

        obj.SetObjectName("hello");
        Check(obj.objectName() == "hello", $"generated accessor round-trip (got '{obj.objectName()}')");

        string seen = null;
        Check(obj.ObjectNameChangedSignal != null, "generated signal property exists");
        obj.ObjectNameChangedSignal.Connect(v => seen = v);
        obj.SetObjectName("world");
        Check(seen == "world", $"generated signal delivered (got '{seen}')");

        // Emit: a signal can be activated from managed code as well. The value travels through
        // the same moc machinery a native emit uses, so a handler connected over the generated
        // signal sees it - and so would any C++ connection.
        var button = new QPushButton();
        int clicks = 0;
        button.ClickedSignal.Connect(_ => clicks++);
        button.ClickedSignal.Emit(false);
        Check(clicks == 1, $"a managed emit reaches a connected handler (clicks={clicks})");

        // The same signal as a C# *event*: `+=` connects and `-=` disconnects. The remove really
        // drops the Qt connection (the runtime keeps the handler as the connection's key), which
        // is what makes the event spelling safe on a long-lived object.
        var eventButton = new QPushButton();
        int eventClicks = 0;
        Action<bool> clickHandler = _ => eventClicks++;
        eventButton.Clicked += clickHandler;
        eventButton.ClickedSignal.Emit(false);
        Check(eventClicks == 1, $"a signal exposed as a C# event fires (+= connected it, clicks={eventClicks})");

        eventButton.Clicked -= clickHandler;
        eventButton.ClickedSignal.Emit(false);
        Check(eventClicks == 1, $"and -= disconnects it (still {eventClicks} after the second emit)");
        Check(!ApiRuntime.Disconnect(eventButton.Handle, "clicked(bool)", clickHandler),
            "removing a handler that is not connected reports nothing to remove");

        // Two handlers, one removed: the other keeps firing (C# removes one occurrence at a time).
        int first = 0, second = 0;
        Action<bool> one = _ => first++;
        Action<bool> two = _ => second++;
        eventButton.Clicked += one;
        eventButton.Clicked += two;
        eventButton.Clicked -= one;
        eventButton.ClickedSignal.Emit(false);
        Check(first == 0 && second == 1,
            $"only the removed handler stops (first={first}, second={second})");

        var edit = new QLineEdit();
        string typed = null;
        edit.TextChangedSignal.Connect(v => typed = v);
        edit.TextChangedSignal.Emit("hello");
        Check(typed == "hello", $"a managed emit carries a string argument (got '{typed}')");

        // The two shapes that need real marshalling: a bound-class pointer (argv points at a
        // pointer) and an enum (argv points at an int).
        var itemDelegate = new QStyledItemDelegate();
        QWidget seenEditor = null;
        QAbstractItemDelegate.EndEditHint seenHint = QAbstractItemDelegate.EndEditHint.SubmitModelCache;
        itemDelegate.CloseEditorSignal.Connect((e, h) =>
        {
            seenEditor = e;
            seenHint = h;
        });
        itemDelegate.CloseEditorSignal.Emit(new QLineEdit(), QAbstractItemDelegate.EndEditHint.NoHint);
        Check(seenEditor != null && seenHint == QAbstractItemDelegate.EndEditHint.NoHint,
            $"a managed emit marshals a class pointer and an enum (editor={seenEditor != null}, hint={seenHint})");

        var parent = new QObject();
        parent.SetObjectName("parent");
        Check(obj.Parent() == null, "generated parent accessor returns null for an unparented object");

        // Virtual dispatch: the generated C++ subclass overrides QObject::eventFilter and
        // forwards to the managed override.
        var derived = new ApiDerivedObject();
        var watched = new QObject();
        int nativeResult = ApiRuntime.ProbeEventFilter(derived.Handle, watched.Handle, IntPtr.Zero);
        Check(nativeResult == 1 && derived.FilterCalls == 1,
            $"native call into the generated virtual reached C# (result={nativeResult}, calls={derived.FilterCalls})");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}


