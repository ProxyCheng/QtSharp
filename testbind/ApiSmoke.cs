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
    public static int Run()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var obj = new QObject();
        Check(obj.Handle != IntPtr.Zero, "QObject created through the generated binding");

        obj.SetObjectName("hello");
        Check(obj.objectName() == "hello", $"generated accessor round-trip (got '{obj.objectName()}')");

        string seen = null;
        Check(obj.ObjectNameChangedSignal != null, "generated signal property exists");
        obj.ObjectNameChangedSignal.Connect(v => seen = v);
        obj.SetObjectName("world");
        Check(seen == "world", $"generated signal delivered (got '{seen}')");

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
