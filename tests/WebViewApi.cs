using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using QtSharp.WebView;

namespace QtSharp.Tests
{
    /// <summary>
    /// The WebView2 adapter: the QWidget hosts a real browser window, loads a document and runs
    /// script in it. The test needs a native window and the Evergreen WebView2 runtime, so it only
    /// asserts under the <c>windows</c> QPA platform; the offscreen test run reports it as skipped.
    /// Run it directly with
    /// <c>QTSHARP_EXAMPLE_PLATFORM=windows testapp test-webview2</c>.
    /// </summary>
    [Test("test-webview2")]
    [SupportedOSPlatform("windows")]
    public static class WebViewApi
    {
        public static int Run(TestContext context)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("[SKIP] WebView2 is Windows-only");
                return context.ExitCode;
            }
            if (QGuiApplication.platformName() != "windows")
            {
                Console.WriteLine(
                    $"[SKIP] the {QGuiApplication.platformName()} platform has no native window to host WebView2; "
                    + "run with QTSHARP_EXAMPLE_PLATFORM=windows");
                return context.ExitCode;
            }
            if (!WebView2Widget.IsRuntimeAvailable)
            {
                Console.WriteLine("[SKIP] the Evergreen WebView2 runtime is not installed");
                return context.ExitCode;
            }

            var host = new QWidget();
            host.Resize(640, 480);
            host.Show();
            var view = new WebView2Widget(host);
            view.Resize(600, 420);
            view.Show();

            bool ready = false;
            view.Ready += (_, _) => ready = true;
            bool completed = false;
            bool succeeded = false;
            view.NavigationCompleted += (_, e) =>
            {
                completed = true;
                succeeded = e.IsSuccess;
            };

            string html = "<html><head><title>QtSharp</title></head>"
                          + "<body><h1 id=\"headline\">hello webview2</h1></body></html>";
            view.NavigateToString(html);

            PumpUntil(() => completed, 20000);
            context.Check(ready, "Ready is raised while the first navigation starts");
            context.Check(completed && succeeded,
                $"the document loads (completed={completed}, success={succeeded})");
            context.Check(view.Title == "QtSharp", $"the document title is reported (got '{view.Title}')");
            context.Check(view.Url == "about:blank", $"the document URL is about:blank (got '{view.Url}')");

            string headline = view.ExecuteScript("document.getElementById('headline').innerText");
            context.Check(headline == "\"hello webview2\"",
                $"ExecuteScript runs in the page (got {headline})");

            // A worker thread has to come back through Qt's queued invocation; this is the path a
            // consumer's background work uses.
            bool posted = false;
            Task.Run(() => view.RunOnUiThread(() => posted = true));
            PumpUntil(() => posted, 5000);
            context.Check(posted, "RunOnUiThread executes a worker's action on the Qt thread");

            view.Dispose();
            host.Dispose();
            return context.ExitCode;
        }

        static void PumpUntil(Func<bool> condition, int timeoutMilliseconds)
        {
            long deadline = Environment.TickCount64 + timeoutMilliseconds;
            while (!condition() && Environment.TickCount64 < deadline)
                QCoreApplication.ProcessEvents(QEventLoop.ProcessEventsFlags.AllEvents, 10);
        }
    }
}
