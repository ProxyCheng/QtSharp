# QtSharp.WebView

A `QWidget` that hosts **Microsoft Edge WebView2** — web content inside a QtSharp application
without QtWebEngine and without shipping a browser.

The package id carries the platform: **`QtSharp.WebView.win-x64`**. WebView2 has no Linux
implementation; on Linux, Qt's own Qt WebView module (which falls back to QtWebEngine) is the
equivalent route.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.23.0" />
  <PackageReference Include="QtSharp.WebView.win-x64" Version="0.23.0" />
</ItemGroup>
```

The package depends on `Microsoft.Web.WebView2`. The release archive carries that package next to
the QtSharp ones, so a local NuGet source built from the archive restores offline; a consumer
using nuget.org instead needs no extra step.

The Microsoft Edge **WebView2 Evergreen Runtime** must be installed. Windows 11 and most updated
Windows 10 systems already have it; otherwise install it from Microsoft's
[WebView2 download page](https://developer.microsoft.com/microsoft-edge/webview2/).
`WebView2Widget.IsRuntimeAvailable` and `GetRuntimeVersion()` report what is installed.

## Usage

```csharp
using QtSharp.QtWidgets;
using QtSharp.WebView;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var app = new QApplication(args);
        var window = new QWidget();
        window.Resize(1000, 700);

        var view = new WebView2Widget(window);
        view.Resize(980, 660);
        view.NavigationCompleted += (_, e) => Console.WriteLine($"loaded: {e.IsSuccess}");
        view.Navigate("https://doc.qt.io/qt-6/");

        window.Show();
        return app.Exec();
    }
}
```

`Navigate`/`NavigateToString`/`ExecuteScriptAsync` create the browser on first use. Call
`Initialize()` when you want the start-up cost paid up front. The browser is a native child
window inside the widget's HWND, so it participates in the Qt layout like any other widget.

Other members:

| member | purpose |
|---|---|
| `Browser` | the underlying `CoreWebView2` (settings, cookies, add/remove scripts, interceptors) |
| `Url`, `Title`, `CanGoBack`, `CanGoForward` | current document state |
| `Reload`, `Stop`, `GoBack`, `GoForward` | navigation |
| `ExecuteScript(string)` / `ExecuteScriptAsync(string)` | run JavaScript, JSON-encoded result |
| `PostWebMessage(string)` | send a string to the page's `chrome.webview` message channel |
| `RunOnUiThread(Action)` | come back to the Qt thread from a worker thread |
| `NavigationStarting`, `NavigationCompleted`, `SourceChanged`, `DocumentTitleChanged`, `WebMessageReceived`, `ProcessFailed`, `Ready` | events |

`WebView2Widget` is annotated `[SupportedOSPlatform("windows")]`. A project targeting plain
`net10.0` therefore needs an `OperatingSystem.IsWindows()` guard (or
`[SupportedOSPlatform("windows")]`) when it uses the widget.

## Threading

Qt and WebView2 are both UI-thread bound. Call the widget from the thread that created the
`QApplication`; after `await ExecuteScriptAsync(...)` or other background work, return through
`RunOnUiThread`. The queued action runs in Qt's event loop, the same way a `Qt.ConnectionType`
queued slot does.

## Layout and airspace

WebView2 draws into a native child window. Qt-painted content cannot cover it: child widgets,
stylesheet backgrounds and semi-transparent overlays placed above it are hidden behind it.
Menus, tooltips and dialogs are separate top-level windows and appear normally. If an overlay is
required, show it in a separate top-level window or hide the browser while it is open.

The widget creates its native window on demand and does not support being reparented after the
browser starts; create it with its final parent.

## User data folder

WebView2 stores the profile and cache under
`%LOCALAPPDATA%\QtSharp\WebView2\<executable name>`. Set `WebView2Widget.UserDataFolder` before
the first navigation to choose another location (for example, a per-instance folder when several
copies of the application run at once).

## Licensing and payload

The adapter is MIT. It depends on `Microsoft.Web.WebView2` (BSD-3-Clause) for the managed control
and its loader; Chromium itself is the installed Edge WebView2 runtime, not part of this package.
Nothing from QtWebEngine is used or redistributed.
