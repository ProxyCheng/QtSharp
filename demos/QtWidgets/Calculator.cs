using System;
using System.Collections.Generic;
using System.Globalization;

// Port of examples/widgets/widgets/calculator on the new pipeline.
//
// The two things this demo exists for are the ones the legacy binding could not express:
//   * CalcButton overrides sizeHint(), and Qt's own layout code reads it back (a managed widget
//     saying how big it wants to be);
//   * Calculator overrides keyPressEvent() and reads the key code out of the QKeyEvent - input
//     events used to arrive as an opaque pointer, so a handler could tell that a key was pressed
//     but not which one.
public sealed class CalcButtonApi : QToolButton
{
    public CalcButtonApi(string text, QWidget parent = null) : base(parent)
    {
        SetSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred);
        SetText(text);
    }

    // QSize CalcButton::sizeHint() const { QSize s = QToolButton::sizeHint(); s.rheight() += 20;
    //                                         s.rwidth() = qMax(s.width(), s.height()); return s; }
    public override QSize sizeHint()
    {
        var size = base.sizeHint();
        int height = size.Height() + 20;
        int width = Math.Max(size.Width(), height);
        return new QSize(width, height);
    }
}

public sealed class CalculatorApi : QWidget
{
    const int NumDigitButtons = 10;

    readonly QLineEdit display;
    readonly CalcButtonApi[] digitButtons = new CalcButtonApi[NumDigitButtons];
    readonly Dictionary<string, CalcButtonApi> buttons = new();
    readonly CalcButtonApi backspaceButton, clearButton, clearAllButton;
    readonly CalcButtonApi divisionButton, timesButton, minusButton, plusButton;

    double sumInMemory;
    double sumSoFar;
    double factorSoFar;
    string pendingAdditiveOperator = string.Empty;
    string pendingMultiplicativeOperator = string.Empty;
    bool waitingForOperand = true;

    public CalculatorApi(QWidget parent = null) : base(parent)
    {
        display = new QLineEdit("0");
        display.ReadOnly = true;
        display.Alignment = Qt.Alignment.AlignRight;
        display.MaxLength = 15;

        var font = display.Font;
        font.SetPointSize(font.PointSize() + 8);
        display.Font = font;

        for (int i = 0; i < NumDigitButtons; ++i)
        {
            int digitValue = i;
            digitButtons[i] = CreateButton(digitValue.ToString(CultureInfo.InvariantCulture),
                                           () => DigitClicked(digitValue));
        }

        var pointButton = CreateButton(".", PointClicked);
        var changeSignButton = CreateButton("\u00b1", ChangeSignClicked);

        backspaceButton = CreateButton("Backspace", BackspaceClicked);
        clearButton = CreateButton("Clear", Clear);
        clearAllButton = CreateButton("Clear All", ClearAll);

        var clearMemoryButton = CreateButton("MC", ClearMemory);
        var readMemoryButton = CreateButton("MR", ReadMemory);
        var setMemoryButton = CreateButton("MS", SetMemory);
        var addToMemoryButton = CreateButton("M+", AddToMemory);

        divisionButton = CreateButton("\u00f7", () => MultiplicativeOperatorClicked("\u00f7"));
        timesButton = CreateButton("\u00d7", () => MultiplicativeOperatorClicked("\u00d7"));
        minusButton = CreateButton("-", () => AdditiveOperatorClicked("-"));
        plusButton = CreateButton("+", () => AdditiveOperatorClicked("+"));

        var squareRootButton = CreateButton("Sqrt", () => UnaryOperatorClicked("Sqrt"));
        var powerButton = CreateButton("x\u00b2", () => UnaryOperatorClicked("x\u00b2"));
        var reciprocalButton = CreateButton("1/x", () => UnaryOperatorClicked("1/x"));
        var equalButton = CreateButton("=", EqualClicked);

        var mainLayout = new QGridLayout();
        mainLayout.SetSizeConstraint(QLayout.SizeConstraint.SetFixedSize);
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
        SetLayout(mainLayout);

        SetWindowTitle("Calculator");
    }

    public string Display => display.text();

    public QLineEdit DisplayEdit => display;

    /// <summary>Clicks a button by its face text, the way a test can drive the calculator.</summary>
    public void Click(string text) => buttons[text].Click();

    /// <summary>Whether a button of that name exists (a typo in a test would otherwise silently pass).</summary>
    public bool HasButton(string text) => buttons.ContainsKey(text);

    /// <summary>The button with that face text, for tests that want to look at it.</summary>
    public QToolButton Button(string text) => buttons[text];

    CalcButtonApi CreateButton(string text, Action clicked)
    {
        var button = new CalcButtonApi(text, this);
        button.ClickedSignal.Connect(_ => clicked());
        buttons[text] = button;
        return button;
    }

    // QWidget::keyPressEvent is Qt's keyboard route into the calculator; the key code comes out of
    // the bound QKeyEvent (Qt::Key values are published by the model).
    public override void KeyPressEvent(QKeyEvent e)
    {
        if (e.Key() >= (int)Qt.Key.Key_0 && e.Key() <= (int)Qt.Key.Key_9)
        {
            digitButtons[e.Key() - (int)Qt.Key.Key_0].Click();
            return;
        }

        switch (e.Key())
        {
            case (int)Qt.Key.Key_Backspace: backspaceButton.Click(); break;
            case (int)Qt.Key.Key_Escape:
            case (int)Qt.Key.Key_Delete: clearAllButton.Click(); break;
            case (int)Qt.Key.Key_Slash: divisionButton.Click(); break;
            case (int)Qt.Key.Key_Asterisk: timesButton.Click(); break;
            case (int)Qt.Key.Key_Minus: minusButton.Click(); break;
            case (int)Qt.Key.Key_Plus: plusButton.Click(); break;
            case (int)Qt.Key.Key_Equal:
            case (int)Qt.Key.Key_Return:
            case (int)Qt.Key.Key_Enter: EqualClicked(); break;
            case (int)Qt.Key.Key_Period:
            case (int)Qt.Key.Key_Comma: PointClicked(); break;
            default: base.KeyPressEvent(e); break;
        }
    }

    void DigitClicked(int digitValue)
    {
        if (display.text() == "0" && digitValue == 0)
            return;

        if (waitingForOperand)
        {
            display.Clear();
            waitingForOperand = false;
        }
        display.SetText(display.text() + digitValue.ToString(CultureInfo.InvariantCulture));
    }

    void UnaryOperatorClicked(string clickedOperator)
    {
        double operand = ToDouble(display.text());
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
        display.SetText(FormatNumber(result));
        waitingForOperand = true;
    }

    void AdditiveOperatorClicked(string clickedOperator)
    {
        double operand = ToDouble(display.text());

        if (pendingMultiplicativeOperator.Length > 0)
        {
            if (!Calculate(operand, pendingMultiplicativeOperator))
            {
                AbortOperation();
                return;
            }
            display.SetText(FormatNumber(factorSoFar));
            operand = factorSoFar;
            factorSoFar = 0.0;
            pendingMultiplicativeOperator = string.Empty;
        }

        if (pendingAdditiveOperator.Length > 0)
        {
            if (!Calculate(operand, pendingAdditiveOperator))
            {
                AbortOperation();
                return;
            }
            display.SetText(FormatNumber(sumSoFar));
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
        double operand = ToDouble(display.text());

        if (pendingMultiplicativeOperator.Length > 0)
        {
            if (!Calculate(operand, pendingMultiplicativeOperator))
            {
                AbortOperation();
                return;
            }
            display.SetText(FormatNumber(factorSoFar));
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
        double operand = ToDouble(display.text());

        if (pendingMultiplicativeOperator.Length > 0)
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
        if (pendingAdditiveOperator.Length > 0)
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

        display.SetText(FormatNumber(sumSoFar));
        sumSoFar = 0.0;
        waitingForOperand = true;
    }

    void PointClicked()
    {
        if (waitingForOperand)
            display.SetText("0");
        if (!display.text().Contains('.'))
            display.SetText(display.text() + ".");
        waitingForOperand = false;
    }

    void ChangeSignClicked()
    {
        string text = display.text();
        double value = ToDouble(text);

        if (value > 0.0)
            text = "-" + text;
        else if (value < 0.0)
            text = text.Substring(1);
        display.SetText(text);
    }

    void BackspaceClicked()
    {
        if (waitingForOperand)
            return;

        string text = display.text();
        text = text.Substring(0, Math.Max(0, text.Length - 1));
        if (text.Length == 0)
        {
            text = "0";
            waitingForOperand = true;
        }
        display.SetText(text);
    }

    void Clear()
    {
        if (waitingForOperand)
            return;

        display.SetText("0");
        waitingForOperand = true;
    }

    void ClearAll()
    {
        sumSoFar = 0.0;
        factorSoFar = 0.0;
        pendingAdditiveOperator = string.Empty;
        pendingMultiplicativeOperator = string.Empty;
        display.SetText("0");
        waitingForOperand = true;
    }

    void ClearMemory() => sumInMemory = 0.0;

    void ReadMemory()
    {
        display.SetText(FormatNumber(sumInMemory));
        waitingForOperand = true;
    }

    void SetMemory()
    {
        EqualClicked();
        sumInMemory = ToDouble(display.text());
    }

    void AddToMemory()
    {
        EqualClicked();
        sumInMemory += ToDouble(display.text());
    }

    void AbortOperation()
    {
        ClearAll();
        display.SetText("####");
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

    // Matches QString::number(double), which uses 'g' format with a precision of 6.
    static string FormatNumber(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
}

[Demo("qtwidgets-calculator")]
public static class Calculator
{
    public static int Run(DemoContext demo)
    {

        var calculator = new CalculatorApi();
        #if QTSHARP_DEMO_SMOKE
            calculator.Show();
        #else
            demo.Show(calculator);
        #endif

        // 1 + 2 = 3, driven by clicking the buttons and reading the display back.
        calculator.Click("1");
        calculator.Click("+");
        calculator.Click("2");
        calculator.Click("=");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "3", $"1 + 2 through the buttons (display '{calculator.Display}')");
        #endif

        // The operator precedence the official calculator implements: 2 + 3 x 4 = 14.
        calculator.Click("Clear All");
        calculator.Click("2");
        calculator.Click("+");
        calculator.Click("3");
        calculator.Click("\u00d7");
        calculator.Click("4");
        calculator.Click("=");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "14", $"2 + 3 x 4 honours precedence (display '{calculator.Display}')");
        #endif

        // Division by zero aborts the operation instead of producing infinity.
        calculator.Click("Clear");
        calculator.Click("5");
        calculator.Click("\u00f7");
        calculator.Click("0");
        calculator.Click("=");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "####", $"division by zero aborts (display '{calculator.Display}')");
        #endif

        // The unary operators, and the memory keys.
        calculator.Click("Clear All");
        calculator.Click("8");
        calculator.Click("1/x");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "0.125", $"1/x (display '{calculator.Display}')");
        #endif

        calculator.Click("Clear All");
        calculator.Click("9");
        calculator.Click("MS");
        calculator.Click("Clear All");
        calculator.Click("MR");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "9", $"memory store/read (display '{calculator.Display}')");
        #endif

        // The keyboard: Qt sends a key event, the managed keyPressEvent reads the code out of the
        // bound QKeyEvent and clicks the matching button.
        calculator.Click("Clear All");
        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_7);
        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_8);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "78", $"the keyboard reached the calculator (display '{calculator.Display}')");
        #endif

        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_Backspace);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "7", $"backspace (display '{calculator.Display}')");
        #endif

        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_Escape);
        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_5);
        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_Plus);
        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_7);
        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_Return);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "12", $"5 + 7 = 12 through the keyboard (display '{calculator.Display}')");
        #endif

        ApiRuntime.SendKeyPress(calculator.Handle, (int)Qt.Key.Key_Escape);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Display == "0", $"escape clears (display '{calculator.Display}')");
        #endif

        // The managed sizeHint override is what Qt lays the button out with: the same text on a
        // plain QToolButton gives a shorter button.
        var plain = new QToolButton();
        plain.SetText("7");
        var managed = calculator.Button("7").SizeHint;
        var widget = plain.SizeHint;
        {
            #if QTSHARP_DEMO_SMOKE
            demo.Check(managed.Height() == widget.Height() + 20 && managed.Width() >= managed.Height(),
                $"the managed sizeHint reached the layout (managed {managed.Width()}x{managed.Height()}, "
                + $"plain {widget.Width()}x{widget.Height()})");
            #endif
        }
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calculator.Height > 0 && calculator.Width > 0,
            $"Qt sized the calculator from the managed buttons ({calculator.Width}x{calculator.Height})");
        #endif

        // And it really draws: an off-screen render of the whole window.
        using (var rendered = calculator.Grab())
        {
            #if QTSHARP_DEMO_SMOKE
            demo.Check(DemoPixels.GrabCovers(rendered, calculator.Width, calculator.Height),
                $"the calculator rendered off-screen (got {rendered.Width()}x{rendered.Height()})");
            #endif
        }

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        return 0;
        #endif
    }
}
