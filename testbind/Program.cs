using System;
using System.Globalization;
using QtSharp.QtWidgets;

static class Program
{
    static QLineEdit display;
    static string current = "";
    static double accumulator = 0;
    static string pendingOp = "";

    static int Main()
    {
        try
        {
            using var app = new QApplication(new[] { "calc" });

            using var window = new QWidget(null, 0);
            window.WindowTitle = "QtSharp Calculator";
            window.Resize(300, 360);

            var layout = new QGridLayout();
            display = new QLineEdit(null);
            display.Text = "0";
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
                var b = new QPushButton(null);
                b.Text = text;
                layout.AddWidget(b, row, col);
                b.ClickedSignal.Connect(_ => OnKey(text));
            }

            var clear = new QPushButton(null);
            clear.Text = "C";
            layout.AddWidget(clear, 5, 0, 1, 4);
            clear.ClickedSignal.Connect(_ => Reset());

            window.Layout = layout;
            window.Show();
            Console.WriteLine("[OK] Calculator shown; close it to exit...");

            return QApplication.Exec();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            return 1;
        }
    }

    static void Reset()
    {
        current = "";
        accumulator = 0;
        pendingOp = "";
        display.Text = "0";
    }

    static void OnKey(string key)
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
