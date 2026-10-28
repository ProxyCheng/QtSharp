using System;
using System.Collections.Generic;
using QtSharp.QtGui;
using QtSharp.QtWidgets;

namespace QtSharp.Demos.Hosting
{
    /// <summary>
    /// What a demo gets instead of the whole application: in the Smoke build it counts assertions,
    /// in the View build it registers a window with the host.
    ///
    /// The two surfaces are compiled, not runtime branches: the Smoke assembly has no
    /// <see cref="Show"/> member at all and the View assembly has no <c>Check</c>/<c>ExitCode</c>,
    /// so a demo that mixes the modes does not compile.
    /// </summary>
    public sealed class DemoContext
    {
#if QTSHARP_DEMO_SMOKE
        int _failures;

        /// <summary>The process exit code: zero when every check passed, one otherwise.</summary>
        public int ExitCode => _failures == 0 ? 0 : 1;

        /// <summary>Prints one <c>[PASS]</c>/<c>[FAIL]</c> line and counts a failure.</summary>
        public void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                _failures++;
        }
#else
        // The host registers the windows here and keeps this context alive across app.Exec(), so
        // the GC cannot finalize a native window while it is on screen.
        readonly List<QWidget> _widgets = new();
        readonly List<QWindow> _windows = new();

        /// <summary>Shows a window and keeps it alive until the demo's event loop ends.</summary>
        public void Show(QWidget widget)
        {
            if (widget == null)
                return;
            _widgets.Add(widget);
            widget.Show();
        }

        /// <summary>Shows a window (the Qt3D case) and keeps it alive until the loop ends.</summary>
        public void Show(QWindow window)
        {
            if (window == null)
                return;
            _windows.Add(window);
            window.Show();
        }

        /// <summary>Whether the demo put anything on screen; a headless demo has no event loop.</summary>
        internal bool HasWindows => _widgets.Count > 0 || _windows.Count > 0;
#endif
    }
}
