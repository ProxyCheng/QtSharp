using System;
using QtSharp.Api;

// A signal and a property declared *in C#*, the pyqtSignal/Property equivalent. The declarations
// are ordinary static fields on the managed subclass; ApiRuntime.AttachMetaObject (which every
// generated constructor calls) reflects over them, builds a meta-object with the extra members and
// installs it on the object. After that they are indistinguishable from moc-generated members:
// indexOfSignal finds them, connect() works, and QObject::property/setProperty reach them.
public class Counter : QObject
{
    public static readonly QMetaSignal ValueReset = new("valueReset");
    public static readonly QMetaSignal<int> ValueChanged = new("valueChanged");
    public static readonly QMetaSignal<string, int> Renamed = new("renamed");
    public static readonly QMetaSignal<QObject> ChildAdded = new("childAdded");
    public static readonly QMetaProperty<int> Value =
        new("value", o => ((Counter)o)._value, (o, v) => ((Counter)o)._value = v);
    public static readonly QMetaProperty<string> Label =
        new("label", o => ((Counter)o)._label, (o, v) => ((Counter)o)._label = v);

    int _value;
    string _label = "";

    public int RawValue => _value;
    public string RawLabel => _label;

    // Ordinary C# code, and the signal goes through the same moc machinery a C++ emit would.
    public void Bump()
    {
        _value++;
        ValueChanged.Emit(this, _value);
    }
}

// The declarations are inherited: a subclass gets its own meta-object with the same members.
public class SpecialCounter : Counter
{
}

public static class ApiMetaObjectDemo
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


        var counter = new Counter();
        var meta = counter.MetaObject();
        Check(meta.ClassName() == "Counter", $"the object's meta-object is the C# class (got '{meta.ClassName()}')");
        Check(meta.IndexOfSignal("valueChanged(int)") >= 0, "a C#-declared signal is in the meta-object");
        Check(meta.IndexOfSignal("objectNameChanged(QString)") >= 0, "the inherited Qt signals are still there");
        Check(meta.IndexOfProperty("value") >= 0 && meta.IndexOfProperty("label") >= 0,
            "C#-declared properties are in the meta-object");

        // Connect to the declared signal and drive it from C#.
        int seen = 0;
        Counter.ValueChanged.Connect(counter, v => seen = v);
        counter.Bump();
        counter.Bump();
        Check(seen == 2, $"a C#-declared signal reaches its handler (got {seen})");

        // Emit the other arities, including a bound class (marshalled as its pointer).
        bool reset = false;
        Counter.ValueReset.Connect(counter, () => reset = true);
        Counter.ValueReset.Emit(counter);
        Check(reset, "a parameterless C#-declared signal fires");

        string renamed = null;
        int renamedTo = 0;
        Counter.Renamed.Connect(counter, (n, v) => { renamed = n; renamedTo = v; });
        Counter.Renamed.Emit(counter, "counter", 7);
        Check(renamed == "counter" && renamedTo == 7, $"a two-argument C#-declared signal carries both (got '{renamed}', {renamedTo})");

        QObject child = null;
        Counter.ChildAdded.Connect(counter, c => child = c);
        var widget = new QWidget();
        Counter.ChildAdded.Emit(counter, widget);
        Check(child != null && child.Handle == widget.Handle, "a C#-declared signal can carry a bound class");

        // The properties read and write through QObject::property/setProperty, which is how Qt
        // (a binding, a designer, a data mapper) would reach them.
        counter.SetProperty("value", new QVariant(42));
        Check(counter.Property("value").ToInt() == 42 && counter.RawValue == 42,
            $"setProperty reaches the C# setter (property={counter.Property("value").ToInt()}, backing={counter.RawValue})");

        counter.SetProperty("label", new QVariant("hello"));
        Check(counter.Property("label").ToString() == "hello" && counter.RawLabel == "hello",
            $"a string property round-trips (got '{counter.Property("label").ToString()}')");

        // A property reading straight from managed state - no setter involved.
        counter.SetProperty("value", new QVariant(5));
        Check(counter.Property("value").ToInt() == 5, "property() reads back what setProperty stored");

        // A managed subclass of a managed subclass keeps the declarations.
        var special = new SpecialCounter();
        Check(special.MetaObject().ClassName() == "SpecialCounter"
              && special.MetaObject().IndexOfSignal("valueChanged(int)") >= 0,
            "an inherited C#-declared signal is present on a subclass");
        int specialSeen = 0;
        SpecialCounter.ValueChanged.Connect(special, v => specialSeen = v);
        special.Bump();
        Check(specialSeen == 1, "the subclass's own instance emits with its own handler");

        // A generated class that declares nothing pays nothing and keeps working.
        var plain = new QPushButton();
        Check(plain.MetaObject().ClassName() == "QPushButton"
              && plain.MetaObject().IndexOfSignal("clicked(bool)") >= 0,
            "a class without declared members is untouched");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}

