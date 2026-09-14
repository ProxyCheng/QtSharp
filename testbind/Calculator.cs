using System;
using System.Collections.Generic;
using System.Globalization;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// Port of examples/widgets/widgets/calculator/button.h|cpp
public class CalcButton : QToolButton
{
    public CalcButton(string text, QWidget parent = null) : base(parent)
    {
        SetSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred);
        Text = text;
    }

    public override QSize SizeHint
    {
        get
        {
            var size = base.SizeHint;
            size.Height += 20;
            size.Width = Math.Max(size.Width, size.Height);
            return size;
        }
    }
}

// Port of examples/widgets/widgets/calculator/calculator.h|cpp
public class Calculator : QWidget
{
    const int NumDigitButtons = 10;

    readonly List<object> keepAlive = new();

    QLineEdit display;
    double sumInMemory = 0.0;
    double sumSoFar = 0.0;
    double factorSoFar = 0.0;
    string pendingAdditiveOperator = string.Empty;
    string pendingMultiplicativeOperator = string.Empty;
    bool waitingForOperand = true;
    readonly CalcButton[] digitButtons = new CalcButton[NumDigitButtons];

    public Calculator(QWidget parent = null) : base(parent, 0)
    {
        display = keep(new QLineEdit("0"));
        display.ReadOnly = true;
        display.SetAlignment((int)Qt.AlignmentFlag.AlignRight);
        display.MaxLength = 15;

        var font = display.Font;
        font.PointSize = font.PointSize + 8;
        display.Font = font;

        for (int i = 0; i < NumDigitButtons; ++i)
        {
            int digitValue = i;
            digitButtons[i] = CreateButton(digitValue.ToString(), () => DigitClicked(digitValue));
        }

        var pointButton = CreateButton(".", PointClicked);
        var changeSignButton = CreateButton("\u00b1", ChangeSignClicked);

        var backspaceButton = CreateButton("Backspace", BackspaceClicked);
        var clearButton = CreateButton("Clear", Clear);
        var clearAllButton = CreateButton("Clear All", ClearAll);

        var clearMemoryButton = CreateButton("MC", ClearMemory);
        var readMemoryButton = CreateButton("MR", ReadMemory);
        var setMemoryButton = CreateButton("MS", SetMemory);
        var addToMemoryButton = CreateButton("M+", AddToMemory);

        var divisionButton = CreateButton("\u00f7", () => MultiplicativeOperatorClicked("\u00f7"));
        var timesButton = CreateButton("\u00d7", () => MultiplicativeOperatorClicked("\u00d7"));
        var minusButton = CreateButton("-", () => AdditiveOperatorClicked("-"));
        var plusButton = CreateButton("+", () => AdditiveOperatorClicked("+"));

        var squareRootButton = CreateButton("Sqrt", () => UnaryOperatorClicked("Sqrt"));
        var powerButton = CreateButton("x\u00b2", () => UnaryOperatorClicked("x\u00b2"));
        var reciprocalButton = CreateButton("1/x", () => UnaryOperatorClicked("1/x"));
        var equalButton = CreateButton("=", EqualClicked);

        var mainLayout = keep(new QGridLayout());
        mainLayout.sizeConstraint = QLayout.SizeConstraint.SetFixedSize;
        mainLayout.AddWidget(display, 0, 0, 1, 6);
        mainLayout.AddWidget(backspaceButton, 1, 0, 1, 2);
        mainLayout.AddWidget(clearButton, 1, 2, 1, 2);
        mainLayout.AddWidget(clearAllButton, 1, 4, 1, 2);

        mainLayout.AddWidget(clearMemoryButton, 2, 0);
        mainLayout.AddWidget(readMemoryButton, 3, 0);
        mainLayout.AddWidget(setMemoryButton, 4, 0);
        mainLayout.AddWidget(addToMemoryButton, 5, 0);

        for (int i = 1; i < NumDigitButtons; ++i)
        {
            int row = ((9 - i) / 3) + 2;
            int column = ((i - 1) % 3) + 1;
            mainLayout.AddWidget(digitButtons[i], row, column);
        }

        mainLayout.AddWidget(digitButtons[0], 5, 1);
        mainLayout.AddWidget(pointButton, 5, 2);
        mainLayout.AddWidget(changeSignButton, 5, 3);

        mainLayout.AddWidget(divisionButton, 2, 4);
        mainLayout.AddWidget(timesButton, 3, 4);
        mainLayout.AddWidget(minusButton, 4, 4);
        mainLayout.AddWidget(plusButton, 5, 4);

        mainLayout.AddWidget(squareRootButton, 2, 5);
        mainLayout.AddWidget(powerButton, 3, 5);
        mainLayout.AddWidget(reciprocalButton, 4, 5);
        mainLayout.AddWidget(equalButton, 5, 5);
        Layout = mainLayout;

        WindowTitle = "Calculator";
    }

    T keep<T>(T obj) where T : class
    {
        keepAlive.Add(obj);
        return obj;
    }

    void DigitClicked(int digitValue)
    {
        if (display.Text == "0" && digitValue == 0)
            return;

        if (waitingForOperand)
        {
            display.Clear();
            waitingForOperand = false;
        }
        display.Text += digitValue.ToString(CultureInfo.InvariantCulture);
    }

    void UnaryOperatorClicked(string clickedOperator)
    {
        double operand = ToDouble(display.Text);
        double result = 0.0;

        if (clickedOperator == "Sqrt")
        {
            if (operand < 0.0)
            {
                AbortOperation();
                return;
            }
            result = Math.Sqrt(operand);
        }
        else if (clickedOperator == "x\u00b2")
        {
            result = Math.Pow(operand, 2.0);
        }
        else if (clickedOperator == "1/x")
        {
            if (operand == 0.0)
            {
                AbortOperation();
                return;
            }
            result = 1.0 / operand;
        }
        display.Text = FormatNumber(result);
        waitingForOperand = true;
    }

    void AdditiveOperatorClicked(string clickedOperator)
    {
        double operand = ToDouble(display.Text);

        if (!string.IsNullOrEmpty(pendingMultiplicativeOperator))
        {
            if (!Calculate(operand, pendingMultiplicativeOperator))
            {
                AbortOperation();
                return;
            }
            display.Text = FormatNumber(factorSoFar);
            operand = factorSoFar;
            factorSoFar = 0.0;
            pendingMultiplicativeOperator = string.Empty;
        }

        if (!string.IsNullOrEmpty(pendingAdditiveOperator))
        {
            if (!Calculate(operand, pendingAdditiveOperator))
            {
                AbortOperation();
                return;
            }
            display.Text = FormatNumber(sumSoFar);
        }
        else
        {
            sumSoFar = operand;
        }

        pendingAdditiveOperator = clickedOperator;
        waitingForOperand = true;
    }

    void MultiplicativeOperatorClicked(string clickedOperator)
    {
        double operand = ToDouble(display.Text);

        if (!string.IsNullOrEmpty(pendingMultiplicativeOperator))
        {
            if (!Calculate(operand, pendingMultiplicativeOperator))
            {
                AbortOperation();
                return;
            }
            display.Text = FormatNumber(factorSoFar);
        }
        else
        {
            factorSoFar = operand;
        }

        pendingMultiplicativeOperator = clickedOperator;
        waitingForOperand = true;
    }

    void EqualClicked()
    {
        double operand = ToDouble(display.Text);

        if (!string.IsNullOrEmpty(pendingMultiplicativeOperator))
        {
            if (!Calculate(operand, pendingMultiplicativeOperator))
            {
                AbortOperation();
                return;
            }
            operand = factorSoFar;
            factorSoFar = 0.0;
            pendingMultiplicativeOperator = string.Empty;
        }
        if (!string.IsNullOrEmpty(pendingAdditiveOperator))
        {
            if (!Calculate(operand, pendingAdditiveOperator))
            {
                AbortOperation();
                return;
            }
            pendingAdditiveOperator = string.Empty;
        }
        else
        {
            sumSoFar = operand;
        }

        display.Text = FormatNumber(sumSoFar);
        sumSoFar = 0.0;
        waitingForOperand = true;
    }

    void PointClicked()
    {
        if (waitingForOperand)
            display.Text = "0";
        if (!display.Text.Contains('.'))
            display.Text += ".";
        waitingForOperand = false;
    }

    void ChangeSignClicked()
    {
        string text = display.Text;
        double value = ToDouble(text);

        if (value > 0.0)
            text = "-" + text;
        else if (value < 0.0)
            text = text.Substring(1);
        display.Text = text;
    }

    void BackspaceClicked()
    {
        if (waitingForOperand)
            return;

        string text = display.Text;
        text = text.Substring(0, Math.Max(0, text.Length - 1));
        if (text.Length == 0)
        {
            text = "0";
            waitingForOperand = true;
        }
        display.Text = text;
    }

    void Clear()
    {
        if (waitingForOperand)
            return;

        display.Text = "0";
        waitingForOperand = true;
    }

    void ClearAll()
    {
        sumSoFar = 0.0;
        factorSoFar = 0.0;
        pendingAdditiveOperator = string.Empty;
        pendingMultiplicativeOperator = string.Empty;
        display.Text = "0";
        waitingForOperand = true;
    }

    void ClearMemory()
    {
        sumInMemory = 0.0;
    }

    void ReadMemory()
    {
        display.Text = FormatNumber(sumInMemory);
        waitingForOperand = true;
    }

    void SetMemory()
    {
        EqualClicked();
        sumInMemory = ToDouble(display.Text);
    }

    void AddToMemory()
    {
        EqualClicked();
        sumInMemory += ToDouble(display.Text);
    }

    CalcButton CreateButton(string text, Action clicked)
    {
        var button = keep(new CalcButton(text, this));
        button.ClickedSignal.Connect(_ => clicked());
        return button;
    }

    void AbortOperation()
    {
        ClearAll();
        display.Text = "####";
    }

    bool Calculate(double rightOperand, string pendingOperator)
    {
        if (pendingOperator == "+")
        {
            sumSoFar += rightOperand;
        }
        else if (pendingOperator == "-")
        {
            sumSoFar -= rightOperand;
        }
        else if (pendingOperator == "\u00d7")
        {
            factorSoFar *= rightOperand;
        }
        else if (pendingOperator == "\u00f7")
        {
            if (rightOperand == 0.0)
                return false;
            factorSoFar /= rightOperand;
        }
        return true;
    }

    static double ToDouble(string s)
        => string.IsNullOrEmpty(s) ? 0.0 : double.Parse(s, CultureInfo.InvariantCulture);

    // Matches QString::number(double) which uses 'g' format with precision 6.
    static string FormatNumber(double value)
        => value.ToString("G6", CultureInfo.InvariantCulture);
}
