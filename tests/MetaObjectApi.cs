using System;
using System.Reflection;
using System.Runtime.InteropServices;

// A signal and a property declared *in C#, the pyqtSignal/Property equivalent. The declarations
// are attributes on ordinary members; ApiRuntime.AttachMetaObject (which every generated
// constructor calls) reflects over them, builds a meta-object with the extra members and installs
// it on the object. After that they are indistinguishable from moc-generated members:
// indexOfSignal finds them, connect() works, and QObject::property/setProperty reach them.
public class Counter : QObject
{
    [QtSignal] public event Action ValueReset;
    [QtSignal] public event Action<int> ValueChanged;
    [QtSignal("renamed")] public event Action<string, int> Renamed;
    [QtSignal] public event Action<QObject> ChildAdded;
    [QtSignal] public event Action<int> LevelChanged;

    // Explicit Q_PROPERTY metadata: NOTIFY points at ValueChanged, RESET at ResetValue.
    [QtProperty(Notify = nameof(ValueChanged), Reset = nameof(ResetValue))]
    public int Value { get => _value; set => _value = value; }

    // The Qt name differs from the member's: "label", not "text".
    [QtProperty("label")] public string Text { get => _label; set => _label = value; }

    // No Notify/Reset names: the conventions link LevelChanged and ResetLevel.
    [QtProperty] public int Level { get; set; }

    [QtProperty("caption")] public string Name { get; set; } = "";

    [QtProperty(Constant = true, Final = true)]
    public string ConstantLabel => "attributed";      // get-only: a read-only, constant property

    [QtProperty(Designable = false, Scriptable = false, Stored = false,
                User = true, Required = true, Revision = 3)]
    public string Tag { get; set; } = "";

    int _value;
    string _label = "";

    public int RawValue => _value;
    public string RawLabel => _label;

    // Ordinary C# code, and the signal goes through the same moc machinery a C++ emit would.
    public void Bump()
    {
        _value++;
        EmitSignal(ValueChanged, _value);
    }

    // The emit spelled as an invocation of the event itself. The runtime keeps an emit trampoline
    // in the event's handler field, so this crosses into Qt exactly like EmitSignal does.
    public void BumpThroughEvent()
    {
        _value++;
        ValueChanged?.Invoke(_value);
    }

    public void ResetThroughEvent() => ValueReset?.Invoke();

    public void RenameThroughEvent(string name, int value) => Renamed?.Invoke(name, value);

    public void AddChildThroughEvent(QObject child) => ChildAdded?.Invoke(child);

    public void ResetValue()
    {
        _value = 0;
        EmitSignal(ValueReset);
    }

    public void ResetLevel() => Level = 7;

    public void SetLevel(int level)
    {
        Level = level;
        EmitSignal(LevelChanged, level);
    }

    public void Rename(string name, int value) => EmitSignal(Renamed, name, value);

    public void AddChild(QObject child) => EmitSignal(ChildAdded, child);
}

// The declarations are inherited: a subclass gets its own meta-object with the same members.
public class SpecialCounter : Counter
{
}

[Demo("test-metaobject")]
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
        Check(meta.IndexOfSignal("valueChanged(int)") >= 0 && meta.IndexOfSignal("valueReset()") >= 0
              && meta.IndexOfSignal("renamed(QString,int)") >= 0 && meta.IndexOfSignal("childAdded(QObject*)") >= 0,
            "[QtSignal] events are in the meta-object, including the explicit name");
        Check(meta.IndexOfSignal("objectNameChanged(QString)") >= 0, "the inherited Qt signals are still there");
        Check(meta.IndexOfProperty("value") >= 0 && meta.IndexOfProperty("label") >= 0
              && meta.IndexOfProperty("caption") >= 0,
            "[QtProperty] properties are in the meta-object, including the explicit name");
        // Generated members are native: their Qt metadata already exists, so they carry the
        // read-side attributes rather than [QtProperty]/[QtSignal].
        var nativeProperty = typeof(QObject).GetProperty("ObjectName")
            .GetCustomAttribute<QtNativePropertyAttribute>();
        Check(nativeProperty != null && nativeProperty.Name == "objectName"
              && nativeProperty.Type == "QString" && nativeProperty.Writable,
            "a generated Q_PROPERTY carries [QtNativeProperty]");
        var nativeSignal = typeof(QObject).GetEvent("ObjectNameChanged")
            .GetCustomAttribute<QtNativeSignalAttribute>();
        Check(nativeSignal != null && nativeSignal.Signature == "objectNameChanged(QString)",
            "a generated signal carries [QtNativeSignal]");

        // Subscribe to the declared signals and drive them from C#.
        int seen = 0;
        counter.ValueChanged += v => seen = v;
        counter.Bump();
        counter.Bump();
        Check(seen == 2, $"a C#-declared signal reaches its handler (got {seen})");

        // Raising the event itself is also the emit: a native connection sees the activation, and
        // the C# handler still runs exactly once (the runtime keeps its own connection out of that
        // emission; the delegate chain that invoked the trampoline calls the handlers).
        int nativeSeen = -1;
        ApiRuntime.Connect(counter.Handle, "valueChanged(int)", (argv, _) =>
            nativeSeen = Marshal.ReadInt32(Marshal.ReadIntPtr(argv, 0)));
        int eventSeen = 0;
        counter.ValueChanged += v => eventSeen = v;
        counter.BumpThroughEvent();
        Check(nativeSeen == 3 && eventSeen == 3,
            $"an event Invoke activates Qt as well (native={nativeSeen}, event={eventSeen})");

        // An emission that did not start in C# reaches the handlers through the native connection.
        eventSeen = 0;
        ApiRuntime.EmitUser(counter.Handle, "valueChanged(int)", new[] { typeof(int) }, new object[] { 77 });
        Check(eventSeen == 77 && nativeSeen == 77,
            $"a native emission reaches the event's handlers (got {eventSeen})");

        // The trampoline sits in the field before anyone subscribes, so an Invoke with no C#
        // handler still reaches Qt instead of silently doing nothing.
        var unsigned = new Counter();
        int unsignedSeen = -1;
        ApiRuntime.Connect(unsigned.Handle, "valueChanged(int)", (argv, _) =>
            unsignedSeen = Marshal.ReadInt32(Marshal.ReadIntPtr(argv, 0)));
        unsigned.BumpThroughEvent();
        Check(unsignedSeen == 1, $"an event Invoke with no subscribers still reaches Qt (got {unsignedSeen})");

        // A Qt slot that re-emits the signal from inside the activation must still reach the C#
        // handlers for its own emission; only the emission that started in C# is left to the chain.
        var nested = new Counter();
        int nestedSeen = 0;
        nested.ValueChanged += _ => nestedSeen++;
        bool reemitted = false;
        ApiRuntime.Connect(nested.Handle, "valueChanged(int)", (argv, _) =>
        {
            if (!reemitted)
            {
                reemitted = true;
                nested.Bump();               // a Qt slot re-emitting with EmitSignal
            }
        });
        nested.BumpThroughEvent();
        Check(nestedSeen == 2, $"a Qt slot that re-emits during a C# raise is delivered (got {nestedSeen})");

        // `-=` still removes: the trampoline leads the field but is not a subscriber.
        var removed = new Counter();
        int removedSeen = 0;
        Action<int> counted = _ => removedSeen++;
        removed.ValueChanged += counted;
        removed.ValueChanged -= counted;
        removed.BumpThroughEvent();
        Check(removedSeen == 0, $"removing a handler leaves only the emit trampoline (got {removedSeen})");

        // Emit the other arities, including a bound class (marshalled as its pointer). Each also
        // goes through the seeded trampoline once, to cover the arity classes.
        bool reset = false;
        int resetNative = 0;
        ApiRuntime.Connect(counter.Handle, "valueReset()", (argv, _) => resetNative++);
        counter.ValueReset += () => reset = true;
        counter.ResetValue();
        counter.ResetThroughEvent();
        Check(reset && resetNative == 2, $"a parameterless event fires through both spellings (native={resetNative})");

        string renamed = null;
        int renamedTo = 0;
        int renamedNative = 0;
        ApiRuntime.Connect(counter.Handle, "renamed(QString,int)", (argv, _) => renamedNative++);
        counter.Renamed += (n, v) => { renamed = n; renamedTo = v; };
        counter.Rename("counter", 7);
        counter.RenameThroughEvent("again", 8);
        Check(renamed == "again" && renamedTo == 8 && renamedNative == 2,
            $"a two-argument event carries both through both spellings (got '{renamed}', {renamedTo}, native={renamedNative})");

        QObject child = null;
        int childNative = 0;
        ApiRuntime.Connect(counter.Handle, "childAdded(QObject*)", (argv, _) => childNative++);
        counter.ChildAdded += c => child = c;
        var widget = new QWidget();
        counter.AddChild(widget);
        counter.AddChildThroughEvent(widget);
        Check(child != null && child.Handle == widget.Handle && childNative == 2,
            $"a C#-declared signal carries a bound class through both spellings (native={childNative})");

        // The properties read and write through QObject::property/setProperty, which is how Qt
        // (a binding, a designer, a data mapper) would reach them.
        counter.SetProperty("value", new QVariant(42));
        Check(counter.Property("value").ToInt() == 42 && counter.RawValue == 42,
            $"setProperty reaches the C# setter (property={counter.Property("value").ToInt()}, backing={counter.RawValue})");

        counter.SetProperty("label", new QVariant("hello"));
        Check(counter.Property("label").ToString() == "hello" && counter.RawLabel == "hello",
            $"a string property round-trips (got '{counter.Property("label").ToString()}')");

        counter.SetProperty("caption", new QVariant("attributed-name"));
        Check(counter.Property("caption").ToString() == "attributed-name" && counter.Name == "attributed-name",
            $"a property with an explicit Qt name round-trips (got '{counter.Name}')");

        // Q_PROPERTY's NOTIFY, RESET and the rest of its flags are in the table as well.
        var valueProperty = meta.Property(meta.IndexOfProperty("value"));
        Check(valueProperty.HasNotifySignal()
              && valueProperty.NotifySignalIndex() == meta.IndexOfSignal("valueChanged(int)"),
            $"NOTIFY points at the [QtSignal] event (notify index {valueProperty.NotifySignalIndex()})");
        Check(valueProperty.IsReadable() && valueProperty.IsWritable() && valueProperty.IsResettable()
              && valueProperty.IsDesignable() && valueProperty.IsScriptable() && valueProperty.IsStored(),
            "an attributed property carries READ/WRITE/RESET and the default flags");
        counter.Value = 99;
        Check(valueProperty.Reset(counter) && counter.Value == 0,
            $"QMetaProperty::reset() calls the C# ResetValue() (value={counter.Value})");

        var constantProperty = meta.Property(meta.IndexOfProperty("constantLabel"));
        Check(constantProperty.IsConstant() && constantProperty.IsFinal() && !constantProperty.IsWritable(),
            "CONSTANT and FINAL are in the meta-object");

        var tagProperty = meta.Property(meta.IndexOfProperty("tag"));
        Check(!tagProperty.IsDesignable() && !tagProperty.IsScriptable() && !tagProperty.IsStored()
              && tagProperty.IsUser() && tagProperty.IsRequired() && tagProperty.Revision() == 3,
            "DESIGNABLE/SCRIPTABLE/STORED/USER/REQUIRED/REVISION are in the meta-object");

        // With no names given, the Qt conventions are enough: LevelChanged notifies, ResetLevel
        // resets.
        var levelProperty = meta.Property(meta.IndexOfProperty("level"));
        Check(levelProperty.HasNotifySignal()
              && levelProperty.NotifySignalIndex() == meta.IndexOfSignal("levelChanged(int)")
              && levelProperty.IsResettable(),
            "NOTIFY and RESET are inferred from the XChanged event and the ResetX method");
        int levelSeen = 0;
        counter.LevelChanged += level => levelSeen = level;
        counter.SetLevel(11);
        Check(levelSeen == 11, $"the inferred NOTIFY signal fires (level={levelSeen})");
        counter.Level = 5;
        Check(levelProperty.Reset(counter) && counter.Level == 7,
            $"the inferred RESET calls ResetLevel() (level={counter.Level})");

        // A managed subclass of a managed subclass keeps the declarations.
        var special = new SpecialCounter();
        Check(special.MetaObject().ClassName() == "SpecialCounter"
              && special.MetaObject().IndexOfSignal("valueChanged(int)") >= 0
              && special.MetaObject().IndexOfProperty("value") >= 0,
            "an inherited C#-declared member is present on a subclass");
        int specialSeen = 0;
        special.ValueChanged += v => specialSeen = v;
        special.Bump();
        Check(specialSeen == 1, "the subclass's own instance emits with its own handler");
        special.BumpThroughEvent();
        Check(specialSeen == 2, $"the inherited event's Invoke emits for the subclass (got {specialSeen})");

        // A generated class that declares nothing pays nothing and keeps working.
        var plain = new QPushButton();
        Check(plain.MetaObject().ClassName() == "QPushButton"
              && plain.MetaObject().IndexOfSignal("clicked(bool)") >= 0,
            "a class without declared members is untouched");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
