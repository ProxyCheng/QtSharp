using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using QtSharp.Runtime;

namespace QtSharp.WebView
{
    /// <summary>
    /// A QWidget that hosts Microsoft Edge WebView2.
    ///
    /// This is the managed-adapter answer to web content in a QtSharp application, the way
    /// QtSharp.Sql is the answer for QtSql: the browser comes from .NET and the operating system
    /// (the Evergreen WebView2 runtime, which Edge updates), not from QtWebEngine and not from a
    /// Chromium this package would have to ship. The widget is a plain QWidget in the Qt layout;
    /// WebView2 is a native child window inside its HWND, so the web content scrolls, resizes and
    /// accepts input the way the rest of the window does.
    ///
    /// WebView2 has no Linux implementation, so the widget is Windows-only. Qt's own Qt WebView
    /// module is the cross-platform route (it falls back to QtWebEngine on Linux).
    /// </summary>
    /// <remarks>
    /// Threading: WebView2 and Qt are both single-threaded here. Call the methods and subscribe to
    /// the events on the thread that created the QApplication; use <see cref="RunOnUiThread"/> to
    /// get back there from a worker thread.
    ///
    /// Airspace: the browser is a native child window, so Qt-drawn content cannot paint over it.
    /// Child widgets, stylesheet backgrounds and translucent overlays placed on top of it are
    /// hidden behind it; menus, tooltips and dialogs are separate top-level windows and are not
    /// affected.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class WebView2Widget : QWidget
    {
        // The environment is process-wide and shared by every widget: WebView2 owns Chromium's
        // browser process per user data folder, and two environments for the same folder would
        // compete for it. Controllers (and their event handlers) are per widget.
        static CoreWebView2Environment sharedEnvironment;
        static string sharedEnvironmentFolder;

        readonly int uiThreadId;
        readonly QtThreadDispatcher dispatcher;
        CoreWebView2Controller controller;
        CoreWebView2 browser;
        bool initializing;
        bool closing;

        /// <summary>
        /// Creates the widget. <paramref name="parent"/> takes Qt ownership as usual; the browser
        /// starts on the first <see cref="Navigate(string)"/>, <see cref="NavigateToString"/>,
        /// <see cref="ExecuteScriptAsync"/> or <see cref="Initialize"/> call.
        /// </summary>
        public WebView2Widget(QWidget parent = null)
            : base(parent)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(
                    "QtSharp.WebView hosts Microsoft Edge WebView2, which is Windows-only. "
                    + "On Linux, Qt WebView (backed by QtWebEngine) is the equivalent route.");

            uiThreadId = Environment.CurrentManagedThreadId;
            dispatcher = new QtThreadDispatcher { IsClosed = () => closing };
            // Qt deletes a child widget when its window closes; closing the browser there is the
            // one cleanup path that also covers a consumer that never calls Dispose.
            Destroyed += _ => CloseBrowser();
        }

        /// <summary>Whether the browser is created and the first document can be loaded.</summary>
        public bool IsReady => browser != null;

        /// <summary>
        /// The underlying WebView2 browser, or null before the first navigation. It is UI-thread
        /// affine: use <see cref="RunOnUiThread"/> before touching it from another thread.
        /// </summary>
        public CoreWebView2 Browser => browser;

        /// <summary>The current document URL, or an empty string before the first navigation.</summary>
        public string Url
        {
            get => browser?.Source ?? string.Empty;
            set => Navigate(value);
        }

        /// <summary>The document title WebView2 reports, or an empty string.</summary>
        public string Title => browser?.DocumentTitle ?? string.Empty;

        public bool CanGoBack => browser?.CanGoBack ?? false;

        public bool CanGoForward => browser?.CanGoForward ?? false;

        /// <summary>
        /// The folder WebView2 keeps its profile and cache in. Defaults to
        /// <c>%LOCALAPPDATA%\QtSharp\WebView2\&lt;executable name&gt;</c>. Set it before the first
        /// widget navigates; later changes are ignored by an environment that already exists.
        /// </summary>
        public static string UserDataFolder { get; set; }

        /// <summary>Whether the Evergreen WebView2 runtime is installed.</summary>
        public static bool IsRuntimeAvailable
        {
            get
            {
                if (!OperatingSystem.IsWindows())
                    return false;
                try
                {
                    CoreWebView2Environment.GetAvailableBrowserVersionString(null);
                    return true;
                }
                catch (WebView2RuntimeNotFoundException)
                {
                    return false;
                }
            }
        }

        /// <summary>The installed runtime version, e.g. <c>156.0.4314.50</c>.</summary>
        public static string GetRuntimeVersion()
            => CoreWebView2Environment.GetAvailableBrowserVersionString(null);

        /// <summary>
        /// Creates the browser now instead of on the first navigation. Idempotent. This pumps the
        /// Qt event loop while WebView2 completes its asynchronous start-up, so it returns once
        /// the browser is ready (or throws if it cannot start).
        /// </summary>
        public void Initialize()
        {
            EnsureInitialized();
        }

        /// <summary>Loads <paramref name="url"/>; creates the browser first when needed.</summary>
        public void Navigate(string url)
        {
            if (url == null)
                throw new ArgumentNullException(nameof(url));
            EnsureInitialized();
            browser.Navigate(url);
        }

        /// <summary>Loads <paramref name="html"/> as the document.</summary>
        public void NavigateToString(string html)
        {
            if (html == null)
                throw new ArgumentNullException(nameof(html));
            EnsureInitialized();
            browser.NavigateToString(html);
        }

        public void Reload() => browser?.Reload();

        public void Stop() => browser?.Stop();

        public void GoBack()
        {
            if (browser?.CanGoBack == true)
                browser.GoBack();
        }

        public void GoForward()
        {
            if (browser?.CanGoForward == true)
                browser.GoForward();
        }

        /// <summary>
        /// Runs <paramref name="script"/> in the page and returns its JSON-encoded result. The task
        /// completes when the page answers; the result is a string, so it is safe to read from any
        /// thread, but the next WebView2 call still belongs on the UI thread.
        /// </summary>
        public Task<string> ExecuteScriptAsync(string script)
        {
            if (script == null)
                throw new ArgumentNullException(nameof(script));
            EnsureInitialized();
            return browser.ExecuteScriptAsync(script);
        }

        /// <summary>
        /// Runs <paramref name="script"/> and waits for the result, pumping Qt's event loop while
        /// the page answers. Handy from a Qt slot; prefer the async overload otherwise.
        /// </summary>
        public string ExecuteScript(string script)
            => Pump(ExecuteScriptAsync(script));

        /// <summary>Posts a string to the page as a <c>chrome.webview</c> message.</summary>
        public void PostWebMessage(string message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            EnsureInitialized();
            browser.PostWebMessageAsString(message);
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the thread that created this widget, immediately when
        /// already there and through Qt's queued invocation otherwise.
        /// </summary>
        public void RunOnUiThread(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (closing)
                throw new ObjectDisposedException(nameof(WebView2Widget));
            if (Environment.CurrentManagedThreadId == uiThreadId)
                action();
            else
                dispatcher.Post(action);
        }

        /// <summary>Raised once the browser exists and the first navigation can start.</summary>
        public event EventHandler Ready;

        public event EventHandler<CoreWebView2NavigationStartingEventArgs> NavigationStarting;
        public event EventHandler<CoreWebView2NavigationCompletedEventArgs> NavigationCompleted;
        public event EventHandler<CoreWebView2SourceChangedEventArgs> SourceChanged;
        /// <summary>Raised when the document title changes; read <see cref="Title"/> for it.</summary>
        public event EventHandler DocumentTitleChanged;
        public event EventHandler<CoreWebView2WebMessageReceivedEventArgs> WebMessageReceived;
        public event EventHandler<CoreWebView2ProcessFailedEventArgs> ProcessFailed;

        void EnsureInitialized()
        {
            if (browser != null)
                return;
            if (closing)
                throw new ObjectDisposedException(nameof(WebView2Widget));
            if (initializing)
                throw new InvalidOperationException(
                    "WebView2Widget is already starting; do not navigate from an event it raises while initializing.");
            if (Environment.CurrentManagedThreadId != uiThreadId)
                throw new InvalidOperationException(
                    "WebView2Widget.Initialize/Navigate must run on the thread that created the widget; use RunOnUiThread.");

            initializing = true;
            try
            {
                // WinId() creates the native window on demand; WebView2 parents its browser window
                // to it, so the widget must have one before the controller exists.
                ulong nativeId = WinId();
                if (nativeId == 0)
                    throw new InvalidOperationException("The widget has no native window to host WebView2.");

                CoreWebView2Environment environment = GetSharedEnvironment();
                controller = Pump(environment.CreateCoreWebView2ControllerAsync(new IntPtr((long)nativeId)));
                browser = controller.CoreWebView2;
                if (browser == null)
                    throw new InvalidOperationException(
                        "WebView2 created a controller without a browser; the runtime may be incomplete.");

                browser.NavigationStarting += (_, e) => NavigationStarting?.Invoke(this, e);
                browser.NavigationCompleted += (_, e) => NavigationCompleted?.Invoke(this, e);
                browser.SourceChanged += (_, e) => SourceChanged?.Invoke(this, e);
                browser.DocumentTitleChanged += (_, _) => DocumentTitleChanged?.Invoke(this, EventArgs.Empty);
                browser.WebMessageReceived += (_, e) => WebMessageReceived?.Invoke(this, e);
                browser.ProcessFailed += (_, e) => ProcessFailed?.Invoke(this, e);

                UpdateBounds();
                Ready?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                initializing = false;
            }
        }

        static CoreWebView2Environment GetSharedEnvironment()
        {
            if (sharedEnvironment != null)
                return sharedEnvironment;

            string folder = ResolveUserDataFolder();
            Directory.CreateDirectory(folder);
            sharedEnvironment = Pump(CoreWebView2Environment.CreateAsync(null, folder));
            sharedEnvironmentFolder = folder;
            return sharedEnvironment;
        }

        static string ResolveUserDataFolder()
        {
            if (!string.IsNullOrEmpty(UserDataFolder))
                return UserDataFolder;

            string name = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
            if (string.IsNullOrEmpty(name))
                name = "QtSharpApp";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "QtSharp", "WebView2", name);
        }

        // WebView2 completes its asynchronous creation through the UI thread's message queue;
        // blocking that thread on the task would deadlock, so the wait pumps Qt's event loop
        // (which dispatches the Win32 messages WebView2's completion needs) until the task ends.
        static T Pump<T>(Task<T> task, int timeoutMilliseconds = 30000)
        {
            long deadline = Environment.TickCount64 + timeoutMilliseconds;
            while (!task.IsCompleted)
            {
                if (Environment.TickCount64 >= deadline)
                    throw new TimeoutException(
                        $"WebView2 did not complete within {timeoutMilliseconds} ms"
                        + (sharedEnvironmentFolder == null ? "." : $" (user data folder '{sharedEnvironmentFolder}')."));
                QCoreApplication.ProcessEvents(QEventLoop.ProcessEventsFlags.AllEvents, 10);
            }
            return task.GetAwaiter().GetResult();
        }

        void UpdateBounds()
        {
            if (controller == null)
                return;
            controller.Bounds = new Rectangle(0, 0, Math.Max(0, width()), Math.Max(0, height()));
            controller.RasterizationScale = Math.Max(1.0, DevicePixelRatioF());
            controller.IsVisible = IsVisible();
        }

        void CloseBrowser()
        {
            if (controller == null)
                return;
            // The controller belongs to the thread that created it; a finalizer or a worker
            // shutting the process down must not touch it. Leaking it there is harmless: the
            // browser process exits with the application.
            if (Environment.CurrentManagedThreadId != uiThreadId)
                return;

            closing = true;
            CoreWebView2Controller current = controller;
            controller = null;
            browser = null;
            try
            {
                current.Close();
            }
            catch (Exception)
            {
                // Shutdown races the browser process; nothing useful is left to do.
            }
        }

        public override void ResizeEvent(QResizeEvent @event)
        {
            if (!IsAlive)
                return;
            base.ResizeEvent(@event);
            UpdateBounds();
        }

        public override void ShowEvent(QShowEvent @event)
        {
            if (!IsAlive)
                return;
            base.ShowEvent(@event);
            UpdateBounds();
        }

        public override void HideEvent(QHideEvent @event)
        {
            if (!IsAlive)
                return;
            base.HideEvent(@event);
            if (controller != null)
                controller.IsVisible = false;
        }

        public override void FocusInEvent(QFocusEvent @event)
        {
            if (!IsAlive)
                return;
            base.FocusInEvent(@event);
            // Keyboard input goes to the browser's own window; hand it the focus Qt just gave us.
            controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }

        public override bool Event(QEvent @event)
        {
            // WebView2's windows are destroyed with the widget and can deliver one more message to
            // the Qt window after the wrapper is gone; the base override would throw on the dead
            // handle, so a disposed widget simply reports "not handled".
            if (!IsAlive)
                return false;
            bool handled = base.Event(@event);
            QEvent.Type type = @event.type();
            if (controller != null
                && (type == QEvent.Type.DevicePixelRatioChange || type == QEvent.Type.ScreenChangeInternal))
                UpdateBounds();
            return handled;
        }

        // WebView2's child windows tear down with the widget, and a Win32 message can still be
        // delivered to the parent after Qt has deleted it. The generated base override reads the
        // wrapper's handle (which throws once disposed); answer "not handled" instead of letting
        // shutdown produce an unhandled-exception report.
        public override bool NativeEvent(QByteArray eventType, IntPtr message, out long result)
        {
            if (!IsAlive)
            {
                result = 0;
                return false;
            }
            return base.NativeEvent(eventType, message, out result);
        }

        // The QMetaObject slot is how a worker thread gets back onto the Qt thread: a queued
        // invocation posts a metacall event to this object's thread, and Qt runs Drain there.
        sealed class QtThreadDispatcher : QObject
        {
            readonly ConcurrentQueue<Action> queue = new();

            /// <summary>Answers whether the widget has closed, so queued work is dropped.</summary>
            public Func<bool> IsClosed;

            public void Post(Action action)
            {
                queue.Enqueue(action);
                QMetaObject.InvokeMethod(this, "drain", Qt.ConnectionType.QueuedConnection);
            }

            [QtSlot]
            public void Drain()
            {
                // Actions queued before the widget closed would touch a dead controller; drop them.
                while (queue.TryDequeue(out Action action))
                {
                    if (IsClosed != null && IsClosed())
                        continue;
                    action();
                }
            }
        }
    }
}
