using System;
using QtSharp.Demos;

// A Qt object whose signal and properties are declared *in C#* - the pyqtSignal / pyqtProperty
// equivalent.
//
// The declarations are attributes on ordinary members. Every generated QObject constructor calls
// ApiRuntime.AttachMetaObject, which reflects over them, builds a meta-object carrying the extra
// members and installs it on the object; after that they are indistinguishable from moc-generated
// members: indexOfSignal finds them, Qt can connect to them, and QObject::property/setProperty (a
// data mapper, a designer, a scripting layer) reach the C# accessors.
//
// A declared event is a field-like C# event, so it is also the handler list:
// `counter.ValueChanged += h` subscribes, and the runtime feeds it from the Qt signal. Emitting
// uses EmitSignal(ValueChanged, value) - the equivalent of PyQt's self.valueChanged.emit(value).
//
// (The name is HelloCounter, not Counter, because the demo library has a Counter of its own in
// demos/MetaObjectApi.cs - the fuller version of this example, wired into the test app.)
public sealed class HelloCounter : QObject
{
    /// <summary>pyqtSignal(int): the value changed. Also the `value` property's notify signal.</summary>
    [QtSignal]
    public event Action<int> ValueChanged;

    /// <summary>pyqtSignal(): the counter was reset (a signal with no arguments).</summary>
    [QtSignal]
    public event Action Reset;

    /// <summary>
    /// pyqtProperty(int): readable and writable. The setter emits, which is what PyQt's `notify=`
    /// does - so setProperty() from Qt runs this C# setter *and* fires the signal the UI is
    /// connected to. NOTIFY and RESET are inferred from ValueChanged and ResetValue.
    /// </summary>
    [QtProperty]
    public int Value { get => _value; set => SetValue(value); }

    /// <summary>pyqtProperty(QString): read-only, the equivalent of `Property(str, fget)`.</summary>
    [QtProperty("label")]
    public string Text => "clicks";

    int _value;

    /// <summary>The value as C# sees it.</summary>
    public int Current => _value;

    /// <summary>Ordinary C#: the signal travels the same moc machinery a native emit would.</summary>
    public void Bump() => SetValue(_value + 1);

    public void ResetValue()
    {
        _value = 0;
        EmitSignal(Reset);
        EmitSignal(ValueChanged, 0);
    }

    void SetValue(int value)
    {
        _value = value;
        EmitSignal(ValueChanged, value);
    }
}
