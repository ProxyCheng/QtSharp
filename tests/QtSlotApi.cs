using System;

// A method a C# class declares as a real Qt slot. [QtSlot] puts it in the dynamic meta-object, so
// it is reachable the way Qt itself reaches a slot: by name through QMetaObject::invokeMethod, by
// index through QMetaMethod::invoke, and by the on_<objectName>_<signal> convention through
// QMetaObject::connectSlotsByName. The calls below go through those entry points only; nothing here
// calls the C# method directly.
[Test("test-qtslot")]
public static class QtSlotApi
{
    public sealed class SlotCalculator : QObject
    {
        public int AddCalls;
        public int ResetCalls;
        public int FooInt;
        public string FooText;

        [QtSlot] public int Add(int a, int b)
        {
            AddCalls++;
            return a + b;
        }

        [QtSlot] public void MarkReset() => ResetCalls++;

        // Two overloads: the normalized signature is what tells them apart in the meta-object.
        [QtSlot] public void Foo(int value) => FooInt = value;
        [QtSlot] public void Foo(string value) => FooText = value;

        // The Qt name is not the C# name; the overloaded signature is unaffected.
        [QtSlot("multiply")] public int Times(int a, int b) => a * b;
    }

    // One signal and one slot: the test asserts their method ids land where the dynamic meta-object
    // arithmetic says (signals first, slots after them).
    public sealed class SlotLayoutProbe : QObject
    {
        [QtSignal] public event Action Ready;

        public void FireReady() => Ready?.Invoke();

        [QtSlot] public int Probe() => 7;
    }

    // The moc naming convention connectSlotsByName looks for: on_<objectName>_<signal signature>.
    // The host is a widget so the button can be parented through the ordinary constructor.
    public sealed class ClickHost : QWidget
    {
        public int Clicks;
        public bool LastChecked = true;

        [QtSlot] public void on_slotButton_clicked(bool isChecked)
        {
            Clicks++;
            LastChecked = isChecked;
        }
    }

    // A slot that throws: the exception must be reported through the callback path and must not
    // unwind into Qt's C++ frames.
    public sealed class ThrowingSlot : QObject
    {
        [QtSlot] public void Explode() => throw new InvalidOperationException("slot exploded");
    }

    // ------------------------------------------------ invalid declarations (each must be rejected)
    class StaticSlotHolder : QObject
    {
        [QtSlot] public static void NotAnInstance() { }
    }

    class RefSlotHolder : QObject
    {
        [QtSlot] public void NotByRef(ref int value) => value++;
    }

    class UnsupportedSlotHolder : QObject
    {
        [QtSlot] public void NotSupported(object value) { }
    }

    class DuplicateSlotHolder : QObject
    {
        [QtSlot("same")] public void FirstOfPair(int value) { }
        [QtSlot("same")] public void SecondOfPair(int value) { }
    }

    public static int Run(TestContext context)
    {
        var calc = new SlotCalculator();
        var meta = calc.MetaObject();

        // ---------------------------------------------------------------- declaration visible
        context.Check(meta.IndexOfMethod("add(int,int)") >= 0
                      && meta.IndexOfMethod("markReset()") >= 0
                      && meta.IndexOfMethod("foo(int)") >= 0
                      && meta.IndexOfMethod("foo(QString)") >= 0
                      && meta.IndexOfMethod("multiply(int,int)") >= 0,
            "[QtSlot] methods (both overloads and the explicit name) are in the meta-object");
        context.Check(meta.IndexOfMethod("times(int,int)") < 0,
            "the explicit Qt name replaces the C# name in the meta-object");

        // ---------------------------------------------------------------- invokeMethod by name
        bool called = QMetaObject.InvokeMethod(calc, "add", Qt.ConnectionType.DirectConnection,
                                               new object[] { 2, 3 }, out object sum);
        context.Check(called && sum is int added && added == 5 && calc.AddCalls == 1,
            $"invokeMethod reaches the slot and reads its result (called={called}, sum={sum}, calls={calc.AddCalls})");

        // ---------------------------------------------------------------- QMetaMethod::invoke
        var addMethod = meta.Method(meta.IndexOfMethod("add(int,int)"));
        bool invoked = addMethod.Invoke(calc, new object[] { 4, 6 }, out object methodSum);
        context.Check(invoked && methodSum is int result && result == 10 && calc.AddCalls == 2,
            $"QMetaMethod::invoke reaches the same slot (invoked={invoked}, result={methodSum})");

        // ---------------------------------------------------------------- a void slot
        bool reset = QMetaObject.InvokeMethod(calc, "markReset", Qt.ConnectionType.DirectConnection,
                                              Array.Empty<object>(), out object resetResult);
        context.Check(reset && resetResult == null && calc.ResetCalls == 1,
            $"a void slot runs and carries no result (reset={reset}, calls={calc.ResetCalls})");

        // ---------------------------------------------------------------- overload resolution
        bool fooInt = QMetaObject.InvokeMethod(calc, "foo(int)", Qt.ConnectionType.DirectConnection,
                                               new object[] { 9 }, out object fooIntResult);
        bool fooText = QMetaObject.InvokeMethod(calc, "foo(QString)", Qt.ConnectionType.DirectConnection,
                                                new object[] { "from the meta-object" }, out object fooTextResult);
        context.Check(fooInt && fooIntResult == null && calc.FooInt == 9 && fooText && fooTextResult == null
                      && calc.FooText == "from the meta-object",
            $"the normalized signature picks the overload (int={calc.FooInt}, text='{calc.FooText}')");

        // ---------------------------------------------------------------- explicit name override
        bool times = QMetaObject.InvokeMethod(calc, "multiply", Qt.ConnectionType.DirectConnection,
                                              new object[] { 6, 7 }, out object product);
        context.Check(times && product is int multiplied && multiplied == 42,
            $"a slot with an explicit Qt name is reachable by it (times={times}, product={product})");

        // ---------------------------------------------------------------- method id layout
        var probe = new SlotLayoutProbe();
        var probeMeta = probe.MetaObject();
        int methodOffset = probeMeta.MethodOffset();
        int signalLocal = probeMeta.IndexOfSignal("ready()") - methodOffset;
        int slotLocal = probeMeta.IndexOfMethod("probe()") - methodOffset;
        context.Check(signalLocal == 0 && slotLocal == 1,
            $"the local signal sits at {signalLocal} and the slot follows it at {slotLocal}");
        bool probed = QMetaObject.InvokeMethod(probe, "probe", Qt.ConnectionType.DirectConnection,
                                               Array.Empty<object>(), out object probeResult);
        context.Check(probed && probeResult is int seven && seven == 7,
            $"the slot id arithmetic reaches the right implementation (probed={probed}, result={probeResult})");

        // ---------------------------------------------------------------- connectSlotsByName
        var host = new ClickHost();
        var button = new QPushButton(host);
        button.SetObjectName("slotButton");
        QMetaObject.ConnectSlotsByName(host);
        button.Click();
        context.Check(host.Clicks == 1 && !host.LastChecked,
            $"connectSlotsByName connected on_slotButton_clicked (clicks={host.Clicks}, checked={host.LastChecked})");

        // ---------------------------------------------------------------- a throwing slot
        string reportedWhat = null;
        Exception reportedError = null;
        Action<string, Exception> hook = (what, error) => { reportedWhat = what; reportedError = error; };
        ApiRuntime.UnhandledCallbackException += hook;
        bool exploded;
        try
        {
            exploded = QMetaObject.InvokeMethod(new ThrowingSlot(), "explode",
                                                Qt.ConnectionType.DirectConnection,
                                                Array.Empty<object>(), out _);
        }
        finally
        {
            ApiRuntime.UnhandledCallbackException -= hook;
        }
        context.Check(exploded && reportedWhat == "a declared slot"
                      && reportedError is InvalidOperationException && reportedError.Message == "slot exploded",
            $"a throwing slot is reported, not unwound into Qt (what='{reportedWhat}', error='{reportedError?.Message}')");

        // ---------------------------------------------------------------- validation
        context.Check(ThrowsInvalidOperation(() => new StaticSlotHolder(), "NotAnInstance", out string staticError),
            $"a static [QtSlot] is rejected at construction ('{staticError}')");
        context.Check(ThrowsInvalidOperation(() => new RefSlotHolder(), "NotByRef", out string refError),
            $"a ref-parameter [QtSlot] is rejected at construction ('{refError}')");
        context.Check(ThrowsInvalidOperation(() => new UnsupportedSlotHolder(), "NotSupported", out string typeError),
            $"an [QtSlot] parameter with no Qt type name is rejected ('{typeError}')");
        context.Check(ThrowsInvalidOperation(() => new DuplicateSlotHolder(), "SecondOfPair", out string duplicateError),
            $"two slots with one Qt signature are rejected ('{duplicateError}')");

        return context.ExitCode;
    }

    static bool ThrowsInvalidOperation(Action action, string member, out string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(member))
        {
            message = exception.Message;
            return true;
        }
        catch (Exception exception)
        {
            message = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
        message = "no exception";
        return false;
    }
}
