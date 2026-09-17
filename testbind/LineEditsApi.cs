using System;
using System.Collections.Generic;
using QtSharp.Api;

// The official widgets/lineedits example, ported onto the *new* pipeline: group boxes, grid
// layouts, labels, combo boxes, line edits and validators all come from
// model/qtsharp.api.json. Nothing here is hand-written glue.
//
// Differences from the C++ original, both forced by what the model can express today:
//   * the combo boxes are filled with addItem() in a loop, because QStringList (a container)
//     is not a modelled type yet;
//   * the demo drives the combo boxes through currentIndexChanged rather than activated,
//     because activated is only emitted for real user interaction (which a test cannot
//     produce) - the handlers themselves are unchanged.
public sealed class ApiLineEditsWindow : QWidget
{
    readonly QLineEdit echoLineEdit;
    readonly QLineEdit validatorLineEdit;
    readonly QLineEdit alignmentLineEdit;
    readonly QLineEdit inputMaskLineEdit;
    readonly QLineEdit accessLineEdit;

    public int PaintCount;
    public string LastTextChanged;

    public ApiLineEditsWindow()
    {
        var echoGroup = new QGroupBox("Echo");
        var echoLabel = new QLabel("Mode:");
        var echoComboBox = new QComboBox();
        Fill(echoComboBox, "Normal", "Password", "PasswordEchoOnEdit", "No Echo");
        echoLineEdit = new QLineEdit();
        echoLineEdit.SetPlaceholderText("Placeholder Text");
        echoLineEdit.SetFocus();

        var validatorGroup = new QGroupBox("Validator");
        var validatorLabel = new QLabel("Type:");
        var validatorComboBox = new QComboBox();
        Fill(validatorComboBox, "No validator", "Integer validator", "Double validator");
        validatorLineEdit = new QLineEdit();
        validatorLineEdit.SetPlaceholderText("Placeholder Text");

        var alignmentGroup = new QGroupBox("Alignment");
        var alignmentLabel = new QLabel("Type:");
        var alignmentComboBox = new QComboBox();
        Fill(alignmentComboBox, "Left", "Centered", "Right");
        alignmentLineEdit = new QLineEdit();
        alignmentLineEdit.SetPlaceholderText("Placeholder Text");

        var inputMaskGroup = new QGroupBox("Input mask");
        var inputMaskLabel = new QLabel("Type:");
        var inputMaskComboBox = new QComboBox();
        Fill(inputMaskComboBox, "No mask", "Phone number", "ISO date", "License key");
        inputMaskLineEdit = new QLineEdit();
        inputMaskLineEdit.SetPlaceholderText("Placeholder Text");

        var accessGroup = new QGroupBox("Access");
        var accessLabel = new QLabel("Read-only:");
        var accessComboBox = new QComboBox();
        Fill(accessComboBox, "False", "True");
        accessLineEdit = new QLineEdit();
        accessLineEdit.SetPlaceholderText("Placeholder Text");

        echoComboBox.CurrentIndexChangedSignal.Connect(EchoChanged);
        validatorComboBox.CurrentIndexChangedSignal.Connect(ValidatorChanged);
        alignmentComboBox.CurrentIndexChangedSignal.Connect(AlignmentChanged);
        inputMaskComboBox.CurrentIndexChangedSignal.Connect(InputMaskChanged);
        accessComboBox.CurrentIndexChangedSignal.Connect(AccessChanged);

        var echoLayout = new QGridLayout();
        echoLayout.AddWidget(echoLabel, 0, 0);
        echoLayout.AddWidget(echoComboBox, 0, 1);
        echoLayout.AddWidget(echoLineEdit, 1, 0, 1, 2);
        echoGroup.SetLayout(echoLayout);

        var validatorLayout = new QGridLayout();
        validatorLayout.AddWidget(validatorLabel, 0, 0);
        validatorLayout.AddWidget(validatorComboBox, 0, 1);
        validatorLayout.AddWidget(validatorLineEdit, 1, 0, 1, 2);
        validatorGroup.SetLayout(validatorLayout);

        var alignmentLayout = new QGridLayout();
        alignmentLayout.AddWidget(alignmentLabel, 0, 0);
        alignmentLayout.AddWidget(alignmentComboBox, 0, 1);
        alignmentLayout.AddWidget(alignmentLineEdit, 1, 0, 1, 2);
        alignmentGroup.SetLayout(alignmentLayout);

        var inputMaskLayout = new QGridLayout();
        inputMaskLayout.AddWidget(inputMaskLabel, 0, 0);
        inputMaskLayout.AddWidget(inputMaskComboBox, 0, 1);
        inputMaskLayout.AddWidget(inputMaskLineEdit, 1, 0, 1, 2);
        inputMaskGroup.SetLayout(inputMaskLayout);

        var accessLayout = new QGridLayout();
        accessLayout.AddWidget(accessLabel, 0, 0);
        accessLayout.AddWidget(accessComboBox, 0, 1);
        accessLayout.AddWidget(accessLineEdit, 1, 0, 1, 2);
        accessGroup.SetLayout(accessLayout);

        var layout = new QGridLayout();
        layout.AddWidget(echoGroup, 0, 0);
        layout.AddWidget(validatorGroup, 1, 0);
        layout.AddWidget(alignmentGroup, 2, 0);
        layout.AddWidget(inputMaskGroup, 0, 1);
        layout.AddWidget(accessGroup, 1, 1);
        SetLayout(layout);

        SetWindowTitle("Line Edits (new pipeline)");

        // Kept so the boxes can be driven from the test below.
        Boxes = new List<QComboBox> { echoComboBox, validatorComboBox, alignmentComboBox, inputMaskComboBox, accessComboBox };
    }

    public readonly List<QComboBox> Boxes;

    static void Fill(QComboBox box, params string[] items)
    {
        foreach (var item in items)
            box.AddItem(item);
    }

    public override void PaintEvent(QPaintEvent e) => PaintCount++;

    void EchoChanged(int index)
    {
        switch (index)
        {
            case 0: echoLineEdit.SetEchoMode(QLineEdit.EchoMode.Normal); break;
            case 1: echoLineEdit.SetEchoMode(QLineEdit.EchoMode.Password); break;
            case 2: echoLineEdit.SetEchoMode(QLineEdit.EchoMode.PasswordEchoOnEdit); break;
            case 3: echoLineEdit.SetEchoMode(QLineEdit.EchoMode.NoEcho); break;
        }
    }

    void ValidatorChanged(int index)
    {
        switch (index)
        {
            case 0: validatorLineEdit.SetValidator(null); break;
            case 1: validatorLineEdit.SetValidator(new QIntValidator(validatorLineEdit)); break;
            case 2: validatorLineEdit.SetValidator(new QDoubleValidator(-999.0, 999.0, 2, validatorLineEdit)); break;
        }
        validatorLineEdit.Clear();
    }

    void AlignmentChanged(int index)
    {
        switch (index)
        {
            case 0: alignmentLineEdit.SetAlignment(Qt.Alignment.AlignLeft); break;
            case 1: alignmentLineEdit.SetAlignment(Qt.Alignment.AlignCenter); break;
            case 2: alignmentLineEdit.SetAlignment(Qt.Alignment.AlignRight); break;
        }
    }

    void InputMaskChanged(int index)
    {
        switch (index)
        {
            case 0:
                inputMaskLineEdit.SetInputMask("");
                break;
            case 1:
                inputMaskLineEdit.SetInputMask("+99 99 99 99 99;_");
                break;
            case 2:
                inputMaskLineEdit.SetInputMask("0000-00-00");
                inputMaskLineEdit.SetText("00000000");
                inputMaskLineEdit.SetCursorPosition(0);
                break;
            case 3:
                inputMaskLineEdit.SetInputMask(">AAAAA-AAAAA-AAAAA-AAAAA-AAAAA;#");
                break;
        }
    }

    void AccessChanged(int index) => accessLineEdit.SetReadOnly(index == 1);

    // Exposed for the self-test.
    public QLineEdit Echo => echoLineEdit;
    public QLineEdit ValidatorEdit => validatorLineEdit;
    public QLineEdit AlignmentEdit => alignmentLineEdit;
    public QLineEdit InputMaskEdit => inputMaskLineEdit;
    public QLineEdit AccessEdit => accessLineEdit;

    public void WatchText(QLineEdit edit) => edit.TextChangedSignal.Connect(t => LastTextChanged = t);
}

// Builds the dialog, drives every group through the generated signal layer, and checks the
// widget state that each handler should have produced.
public static class ApiLineEditsDemo
{
    public static int Run()
    {
        ApiRuntime.StartApplication();

        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var window = new ApiLineEditsWindow();
        window.WatchText(window.Echo);
        window.Show();

        // Echo: selecting "Password" must reach the handler and change the line edit.
        window.Boxes[0].SetCurrentIndex(1);
        Check(window.Echo.echoMode() == QLineEdit.EchoMode.Password,
            $"combo signal drove the echo mode (got {window.Echo.echoMode()})");

        // Validator: an int validator must reject letters and accept digits.
        window.Boxes[1].SetCurrentIndex(1);
        Check(window.ValidatorEdit.Validator() != null, "int validator installed on the line edit");
        window.ValidatorEdit.SetText("abc");
        Check(!window.ValidatorEdit.HasAcceptableInput(), "int validator rejects \"abc\"");
        window.ValidatorEdit.SetText("123");
        Check(window.ValidatorEdit.HasAcceptableInput(), "int validator accepts \"123\"");

        // Alignment: a QFlags argument through the generated binding.
        window.Boxes[2].SetCurrentIndex(2);
        Check(window.AlignmentEdit.alignment().HasFlag(Qt.Alignment.AlignRight),
            $"alignment flag applied (got {(int)window.AlignmentEdit.alignment():X})");

        // Input mask: the handler sets the mask and fills the text.
        window.Boxes[3].SetCurrentIndex(2);
        Check(window.InputMaskEdit.inputMask() == "0000-00-00" && window.InputMaskEdit.HasAcceptableInput(),
            $"input mask applied and satisfied (mask='{window.InputMaskEdit.inputMask()}')");

        // Read-only switch.
        window.Boxes[4].SetCurrentIndex(1);
        Check(window.AccessEdit.IsReadOnly(), "read-only flag applied");

        // The line edit's own signal, delivered through the generic moc forwarder.
        window.Echo.SetText("hello");
        Check(window.LastTextChanged == "hello",
            $"textChanged delivered (got '{window.LastTextChanged}')");

        var stop = new QTimer();
        stop.SetSingleShot(true);
        stop.TimeoutSignal.Connect(() => ApiRuntime.Quit());
        stop.Start(int.TryParse(Environment.GetEnvironmentVariable("QTSHARP_DEMO_MS"), out var ms) ? ms : 1200);
        ApiRuntime.Exec();

        Check(window.PaintCount > 0, $"the dialog rendered ({window.PaintCount} paint(s))");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
