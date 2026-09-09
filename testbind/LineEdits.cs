using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

public class LineEditsWindow : QWidget
{
    QLineEdit echoLineEdit;
    QLineEdit validatorLineEdit;
    QLineEdit alignmentLineEdit;
    QLineEdit inputMaskLineEdit;
    QLineEdit accessLineEdit;

    public LineEditsWindow() : base(null, 0)
    {
        var echoGroup = new QGroupBox("Echo");
        var echoLabel = new QLabel("Mode:");
        var echoComboBox = new QComboBox();
        echoComboBox.AddItems(new[] { "Normal", "Password", "PasswordEchoOnEdit", "No Echo" });
        echoLineEdit = new QLineEdit();
        echoLineEdit.PlaceholderText = "Placeholder Text";
        echoLineEdit.SetFocus();

        var validatorGroup = new QGroupBox("Validator");
        var validatorLabel = new QLabel("Type:");
        var validatorComboBox = new QComboBox();
        validatorComboBox.AddItems(new[] { "No validator", "Integer validator", "Double validator" });
        validatorLineEdit = new QLineEdit();
        validatorLineEdit.PlaceholderText = "Placeholder Text";

        var alignmentGroup = new QGroupBox("Alignment");
        var alignmentLabel = new QLabel("Type:");
        var alignmentComboBox = new QComboBox();
        alignmentComboBox.AddItems(new[] { "Left", "Centered", "Right" });
        alignmentLineEdit = new QLineEdit();
        alignmentLineEdit.PlaceholderText = "Placeholder Text";

        var inputMaskGroup = new QGroupBox("Input mask");
        var inputMaskLabel = new QLabel("Type:");
        var inputMaskComboBox = new QComboBox();
        inputMaskComboBox.AddItems(new[] { "No mask", "Phone number", "ISO date", "License key" });
        inputMaskLineEdit = new QLineEdit();
        inputMaskLineEdit.PlaceholderText = "Placeholder Text";

        var accessGroup = new QGroupBox("Access");
        var accessLabel = new QLabel("Read-only:");
        var accessComboBox = new QComboBox();
        accessComboBox.AddItems(new[] { "False", "True" });
        accessLineEdit = new QLineEdit();
        accessLineEdit.PlaceholderText = "Placeholder Text";

        echoComboBox.ActivatedSignal.Connect(EchoChanged);
        validatorComboBox.ActivatedSignal.Connect(ValidatorChanged);
        alignmentComboBox.ActivatedSignal.Connect(AlignmentChanged);
        inputMaskComboBox.ActivatedSignal.Connect(InputMaskChanged);
        accessComboBox.ActivatedSignal.Connect(AccessChanged);

        var echoLayout = new QGridLayout();
        echoLayout.AddWidget(echoLabel, 0, 0);
        echoLayout.AddWidget(echoComboBox, 0, 1);
        echoLayout.AddWidget(echoLineEdit, 1, 0, 1, 2);
        echoGroup.Layout = echoLayout;

        var validatorLayout = new QGridLayout();
        validatorLayout.AddWidget(validatorLabel, 0, 0);
        validatorLayout.AddWidget(validatorComboBox, 0, 1);
        validatorLayout.AddWidget(validatorLineEdit, 1, 0, 1, 2);
        validatorGroup.Layout = validatorLayout;

        var alignmentLayout = new QGridLayout();
        alignmentLayout.AddWidget(alignmentLabel, 0, 0);
        alignmentLayout.AddWidget(alignmentComboBox, 0, 1);
        alignmentLayout.AddWidget(alignmentLineEdit, 1, 0, 1, 2);
        alignmentGroup.Layout = alignmentLayout;

        var inputMaskLayout = new QGridLayout();
        inputMaskLayout.AddWidget(inputMaskLabel, 0, 0);
        inputMaskLayout.AddWidget(inputMaskComboBox, 0, 1);
        inputMaskLayout.AddWidget(inputMaskLineEdit, 1, 0, 1, 2);
        inputMaskGroup.Layout = inputMaskLayout;

        var accessLayout = new QGridLayout();
        accessLayout.AddWidget(accessLabel, 0, 0);
        accessLayout.AddWidget(accessComboBox, 0, 1);
        accessLayout.AddWidget(accessLineEdit, 1, 0, 1, 2);
        accessGroup.Layout = accessLayout;

        var layout = new QGridLayout();
        layout.AddWidget(echoGroup, 0, 0);
        layout.AddWidget(validatorGroup, 1, 0);
        layout.AddWidget(alignmentGroup, 2, 0);
        layout.AddWidget(inputMaskGroup, 0, 1);
        layout.AddWidget(accessGroup, 1, 1);
        Layout = layout;

        WindowTitle = "Line Edits";
    }

    void EchoChanged(int index)
    {
        switch (index)
        {
            case 0: echoLineEdit.echoMode = QLineEdit.EchoMode.Normal; break;
            case 1: echoLineEdit.echoMode = QLineEdit.EchoMode.Password; break;
            case 2: echoLineEdit.echoMode = QLineEdit.EchoMode.PasswordEchoOnEdit; break;
            case 3: echoLineEdit.echoMode = QLineEdit.EchoMode.NoEcho; break;
        }
    }

    void ValidatorChanged(int index)
    {
        switch (index)
        {
            case 0: validatorLineEdit.Validator = null; break;
            case 1: validatorLineEdit.Validator = new QIntValidator(validatorLineEdit); break;
            case 2: validatorLineEdit.Validator = new QDoubleValidator(-999.0, 999.0, 2, validatorLineEdit); break;
        }
        validatorLineEdit.Clear();
    }

    void AlignmentChanged(int index)
    {
        switch (index)
        {
            case 0: alignmentLineEdit.SetAlignment((int)Qt.AlignmentFlag.AlignLeft); break;
            case 1: alignmentLineEdit.SetAlignment((int)Qt.AlignmentFlag.AlignCenter); break;
            case 2: alignmentLineEdit.SetAlignment((int)Qt.AlignmentFlag.AlignRight); break;
        }
    }

    void InputMaskChanged(int index)
    {
        switch (index)
        {
            case 0:
                inputMaskLineEdit.InputMask = "";
                break;
            case 1:
                inputMaskLineEdit.InputMask = "+99 99 99 99 99;_";
                break;
            case 2:
                inputMaskLineEdit.InputMask = "0000-00-00";
                inputMaskLineEdit.Text = "00000000";
                inputMaskLineEdit.CursorPosition = 0;
                break;
            case 3:
                inputMaskLineEdit.InputMask = ">AAAAA-AAAAA-AAAAA-AAAAA-AAAAA;#";
                break;
        }
    }

    void AccessChanged(int index)
    {
        accessLineEdit.ReadOnly = index == 1;
    }
}
