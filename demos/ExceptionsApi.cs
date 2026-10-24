using System;

// The exception policy at the native boundary, plus the liveness guard that goes with it.
//
// A signal handler or a virtual override runs inside Qt's C++ frames, so an exception that unwinds
// out of one has nowhere to land: it takes the whole process down, and in a GUI app that usually
// looks like an access violation with nothing on the console to say why. The runtime now catches it
// at the boundary and reports it through ApiRuntime.UnhandledCallbackException - the place PyQt
// prints a traceback and calls qFatal() from, and PySide prints and carries on from. Ours reports
// and carries on, so subscribing to the hook is how an application fails loudly instead.
//
// The guard that keeps the exception from being thrown at all is `!= null`: a wrapper whose Qt
// object is gone compares equal to null (IsAlive / operator == on the generated classes), which is
// what this demo's first half exercises - the report that started it was a handler calling
// DeleteLater() on an object Qt had already destroyed, clicked twice.
public static class ApiExceptionsDemo
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

        int reported = 0;
        string lastWhat = null;
        Action<string, Exception> hook = (what, _) => { reported++; lastWhat = what; };
        ApiRuntime.UnhandledCallbackException += hook;
        try
        {
            // ---------------------------------------------------------------- unguarded: it throws
            // Exactly the reported code, with the guard left out on purpose: the second click calls
            // DeleteLater() on a wrapper whose object the first click destroyed.
            var window = new QWidget();
            var button = new QPushButton(window);
            var layout = new QVBoxLayout(window);
            var editor = new QLineEdit();

            int clicks = 0;
            button.Clicked += (bool _) =>
            {
                clicks++;
                layout.RemoveWidget(button);
                editor.DeleteLater();
            };

            layout.AddWidget(button);
            layout.AddWidget(editor);
            window.SetLayout(layout);
            window.Show();
            QCoreApplication.ProcessEvents();

            for (int i = 1; i <= 3; i++)
            {
                ApiRuntime.SendMousePress(button.Handle, 10, 10);
                ApiRuntime.SendMouseRelease(button.Handle, 10, 10);
                QCoreApplication.ProcessEvents();
            }

            Check(clicks == 3, $"the handler ran for every click (got {clicks})");
            Check(reported == 2, $"the two later throws were reported, not fatal (got {reported})");
            Check(lastWhat == "a signal handler", $"and they are attributed to the handler (got '{lastWhat}')");
            Check(window != null && window.IsAlive, "the window is still alive after the throws");
            Check(editor == null && !editor.IsAlive,
                "and the destroyed line edit reads as null (the guard that was missing)");

            // ------------------------------------------------------------------ guarded: it does not
            reported = 0;
            var guarded = new QLineEdit();
            var guardedButton = new QPushButton();
            guardedButton.Clicked += (bool _) =>
            {
                if (guarded != null)            // the check the operator exists for
                    guarded.DeleteLater();
            };
            guardedButton.Show();
            QCoreApplication.ProcessEvents();

            for (int i = 0; i < 3; i++)
            {
                ApiRuntime.SendMousePress(guardedButton.Handle, 5, 5);
                ApiRuntime.SendMouseRelease(guardedButton.Handle, 5, 5);
                QCoreApplication.ProcessEvents();
            }

            Check(reported == 0, $"the guard means nothing is thrown at all (got {reported})");
            Check(guarded == null, "and the deleted object still reads as null");
            Check(guardedButton != null && guardedButton.IsAlive, "while the button stays alive");

            // ---------------------------------------------------- a virtual override goes the same way
            reported = 0;
            using (var thrower = new ApiThrowingWidget())
            {
                thrower.Show();
                QCoreApplication.ProcessEvents();
                thrower.Repaint();
                Check(reported >= 1, $"an exception in a virtual override is reported too (got {reported})");
                Check(lastWhat == "a virtual override", $"and attributed to the override (got '{lastWhat}')");
                Check(thrower.IsAlive, "the widget that threw is still usable");
            }
        }
        finally
        {
            ApiRuntime.UnhandledCallbackException -= hook;
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}

// A managed override that throws on purpose, to prove the native dispatch entry point reports
// instead of letting the exception unwind into Qt.
sealed class ApiThrowingWidget : QWidget
{
    public override void PaintEvent(QPaintEvent e)
    {
        throw new InvalidOperationException("the override threw on purpose");
    }
}
