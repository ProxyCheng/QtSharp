using System;
using QtSharp.QtCore;
using QtSharp.QtWidgets;

// Verifies the generic (moc-driven) signal dispatcher PoC.
//
// Everything below uses ONE native forwarder. No per-signal generated connect function is
// involved, which is the whole point: overloaded signals and QPrivateSignal-decorated
// signals work, and parameters are exposed with their real types.
public static class MetaSignalTest
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

        var combo = new QComboBox();
        combo.AddItem("alpha");
        combo.AddItem("beta");

        var edit = new QLineEdit();
        var spin = new QSpinBox();
        var button = new QPushButton("go");
        var completer = new QCompleter(new[] { "aa", "bb" });
        var timer = new QTimer();

        int comboIndex = -1;
        string editText = null;
        int spinValue = -1;
        bool? buttonChecked = null;

        Check(MetaSignal.Connect(combo, "currentIndexChanged(int)", a => comboIndex = (int)a[0]),
            "connect currentIndexChanged(int)");
        Check(MetaSignal.Connect(edit, "textChanged(QString)", a => editText = (string)a[0]),
            "connect textChanged(QString)");
        Check(MetaSignal.Connect(spin, "valueChanged(int)", a => spinValue = (int)a[0]),
            "connect valueChanged(int)  [parameter is unnamed in the header]");
        Check(MetaSignal.Connect(button, "clicked(bool)", a => buttonChecked = (bool)a[0]),
            "connect clicked(bool)");

        // Trigger through the public API.
        QComboBox.__Internal.SetCurrentIndex(combo.__Instance, 1);
        edit.Text = "hi";
        spin.Value = 42;
        button.Click();

        Check(comboIndex == 1, $"currentIndexChanged delivered 1 (got {comboIndex})");
        Check(editText == "hi", $"textChanged delivered \"hi\" (got \"{editText}\")");
        Check(spinValue == 42, $"valueChanged delivered 42 (got {spinValue})");
        Check(buttonChecked == false, $"clicked delivered false (got {buttonChecked})");

        // Overloaded signals: same name, two signatures, resolved independently.
        int stringOverload = MetaSignal.SignalIndex(completer, "activated(QString)");
        int indexOverload = MetaSignal.SignalIndex(completer, "activated(QModelIndex)");
        Check(stringOverload >= 0 && indexOverload >= 0 && stringOverload != indexOverload,
            $"QCompleter::activated overloads resolve to distinct indices ({stringOverload} vs {indexOverload})");
        Check(MetaSignal.Connect(completer, "activated(QString)", _ => { }), "connect activated(QString)");
        Check(MetaSignal.Connect(completer, "activated(QModelIndex)", _ => { }), "connect activated(QModelIndex)");
        Check(MetaSignal.SignalIndex(completer, "activated(QUrl)") < 0, "bogus signature rejected");

        // QPrivateSignal marker: inspect what moc actually records at runtime.
        for (int i = 0; i < MetaSignal.MethodCount(timer); i++)
            if (MetaSignal.MethodKind(timer, i) == 1)
                Console.WriteLine($"[info] QTimer signal({i}) = {MetaSignal.MethodSignature(timer, i)}");

        int timeout = MetaSignal.SignalIndex(timer, "timeout()");
        Check(timeout >= 0, $"QTimer::timeout resolves at runtime ({timeout})");
        Check(MetaSignal.Connect(timer, "timeout()", _ => { }), "connect timeout()");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
