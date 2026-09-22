using System;
using QtSharp.Api;

// A Qt object whose signal and properties are declared *in C#* - the pyqtSignal / pyqtProperty
// equivalent.
//
// The declarations are ordinary static fields. Every generated QObject constructor calls
// ApiRuntime.AttachMetaObject, which reflects over them, builds a meta-object carrying the extra
// members and installs it on the object; after that they are indistinguishable from moc-generated
// members: indexOfSignal finds them, Qt can connect to them, and QObject::property/setProperty (a
// data mapper, a designer, a scripting layer) reach the C# accessors.
//
// One shape difference from PyQt, worth knowing: the declarations must be *static* fields, because
// the base constructor runs AttachMetaObject before an instance field would have been assigned. So
// it reads
//
//     HelloCounter.ValueChanged.Emit(counter, 7);
//
// where PyQt writes `self.valueChanged.emit(7)`. Everything else is the same idea.
//
// (The name is HelloCounter, not Counter, because the demo library has a Counter of its own in
// demos/MetaObjectApi.cs - the fuller version of this example, wired into the test app.)
public sealed class HelloCounter : QObject
{
    /// <summary>pyqtSignal(int): the value changed. Also the `value` property's notify signal.</summary>
    public static readonly QMetaSignal<int> ValueChangedSignal = new("valueChanged");

    /// <summary>pyqtSignal(): the counter was reset (a signal with no arguments).</summary>
    public static readonly QMetaSignal ResetSignal = new("reset");

    /// <summary>
    /// The same signal as an ordinary C# event, which is the shape a C# consumer expects:
    ///     counter.ValueChanged += handler;      counter.ValueChanged -= handler;
    /// A C# event cannot share the name of the static declaration, which is why the declaration
    /// carries the Signal suffix - the same convention the generated binding uses
    /// (`button.ClickedSignal` is the declaration, `button.Clicked` is the event).
    /// </summary>
    public event Action<int> ValueChanged
    {
        add => ValueChangedSignal.Connect(this, value);
        remove => ValueChangedSignal.Disconnect(this, value);
    }

    /// <summary>
    /// pyqtProperty(int): readable and writable. The setter notifies, which is what PyQt's
    /// `notify=` argument does - so setProperty() from Qt runs this C# setter *and* fires the
    /// signal the UI is connected to.
    /// </summary>
    public static readonly QMetaProperty<int> Value = new(
        "value",
        o => ((HelloCounter)o)._value,
        (o, v) => ((HelloCounter)o).SetValue(v));

    /// <summary>pyqtProperty(QString): read-only, the equivalent of `Property(str, fget)`.</summary>
    public static readonly QMetaProperty<string> Label = new("label", o => ((HelloCounter)o).Text);

    int _value;

    /// <summary>The name this counter reports through the read-only `label` property.</summary>
    public string Text { get; set; } = "clicks";

    /// <summary>The value as C# sees it (the declared property's getter reads the same field).</summary>
    public int Current => _value;

    /// <summary>Ordinary C#: the signal travels the same moc machinery a native emit would.</summary>
    public void Bump() => SetValue(_value + 1);

    public void ResetValue()
    {
        _value = 0;
        ResetSignal.Emit(this);
        ValueChangedSignal.Emit(this, 0);
    }

    void SetValue(int value)
    {
        _value = value;
        ValueChangedSignal.Emit(this, value);
    }
}
