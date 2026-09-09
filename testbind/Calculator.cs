using System;
using System.Globalization;
using QtSharp.QtWidgets;

public unsafe partial class Calculator : QWidget
{
    QLineEdit display;
    string current = "";
    double accumulator = 0;
    string pendingOp = "";

    public Calculator() : base(null, 0)
    {
        WindowTitle = "QtSharp Calculator";
        Resize(300, 360);

        var layout = new QGridLayout(this);

        display = new QLineEdit("0", this);
        display.ReadOnly = true;
        layout.AddWidget(display, 0, 0, 1, 4);

        var keys = new (string text, int row, int col)[]
        {
            ("7", 1, 0), ("8", 1, 1), ("9", 1, 2), ("/", 1, 3),
            ("4", 2, 0), ("5", 2, 1), ("6", 2, 2), ("*", 2, 3),
            ("1", 3, 0), ("2", 3, 1), ("3", 3, 2), ("-", 3, 3),
            ("0", 4, 0), (".", 4, 1), ("=", 4, 2), ("+", 4, 3),
        };

        foreach (var (text, row, col) in keys)
        {
            var b = new QPushButton(text, this);
            layout.AddWidget(b, row, col);
            b.ClickedSignal.Connect(_ => OnKey(text));
        }

        var clear = new QPushButton("C", this);
        layout.AddWidget(clear, 5, 0, 1, 4);
        clear.ClickedSignal.Connect(_ => Reset());

        Layout = layout;
    }

    void Reset()
    {
        current = "";
        accumulator = 0;
        pendingOp = "";
        display.Text = "0";
    }

    void OnKey(string key)
    {
        if (key == "=")
        {
            if (pendingOp == "" || current == "")
                return;
            double b = Parse(current);
            double result = accumulator;
            switch (pendingOp)
            {
                case "+": result += b; break;
                case "-": result -= b; break;
                case "*": result *= b; break;
                case "/":
                    if (b == 0) { display.Text = "Error"; Reset(); return; }
                    result /= b; break;
            }
            display.Text = result.ToString(CultureInfo.InvariantCulture);
            current = result.ToString(CultureInfo.InvariantCulture);
            accumulator = 0;
            pendingOp = "";
        }
        else if (key == "+" || key == "-" || key == "*" || key == "/")
        {
            if (current != "")
            {
                accumulator = Parse(current);
                pendingOp = key;
                current = "";
            }
        }
        else
        {
            current += key;
            display.Text = current;
        }
    }

    static double Parse(string s)
    {
        return double.Parse(s, CultureInfo.InvariantCulture);
    }
}
