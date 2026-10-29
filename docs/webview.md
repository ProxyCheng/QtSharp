# Web view options: QtWebEngine, Qt WebView and .NET

Status: research note and decision, 2026-10-29. **QtWebEngine and Qt WebView are not bound.**
`QtSharp.WebView` hosts Microsoft Edge WebView2 in a `QWidget` (Windows x64). This note records
the measurements, the .NET alternatives and why the WebView2 adapter was the one chosen.

## The question

Web content inside a QtSharp application can come from three kinds of source:

1. bind Qt's Chromium-based module (QtWebEngine);
2. bind Qt's thin wrapper over the operating system's web view (Qt WebView);
3. skip Qt and host a .NET web view control in a QWidget (the `QtSharp.Sql` pattern).

## What Qt ships in 6.12

### QtWebEngine is a separate product now

Starting with Qt 6.12.0 there are no QtWebEngine or QtPdf binaries in the Qt 6.12 online
installer package. Qt WebEngine is released separately and versioned by its Chromium baseline:
the first release is **Qt WebEngine 6.140.0** (Chromium 140), published under

```
online/qtsdkrepository/windows_x86/extensions/qtwebengine/6120/61400/msvc2022_64/
```

Measured on the Windows x64 MSVC 2022 package:

| piece | size |
|---|---|
| download archive | 252 MB |
| extracted package | 1.2 GB (includes debug binaries and debug `.pak` files) |
| `Qt6WebEngineCore.dll` | 203 MB |
| `Qt6WebEngineWidgets.dll` | 187 KB |
| `QtWebEngineProcess.exe` | 802 KB |
| `qtwebengine_resources.pak` + `_100p` + `_200p` | 2.6 MB |
| `qtwebengine_devtools_resources.pak` | 11.6 MB |
| `icudtl.dat` | 10.5 MB |
| `translations/qtwebengine_locales` (53 locales) | 44 MB |

That is roughly **263 MB of runtime payload** for the release files alone (338 MB with the debug
DLLs and debug `.pak` files the package also carries), before the libraries it imports. The
dependency walk (`Qt6WebEngineCore.dll`, `Qt6WebEngineWidgets.dll`) additionally needs:

```
Qt6Qml, Qt6Quick, Qt6QuickWidgets, Qt6WebChannel, Qt6Positioning
```

none of which the repository's Qt kit carries today. A binding would therefore own a second
large optional download, the deployment of `QtWebEngineProcess.exe`, the resource and locale
files, and the Qml/Quick runtime — the same deployment surface as any Chromium application.

The version split also has a maintenance consequence: Chromium security releases drive
WebEngine point releases, and a WebEngine build sets the minimum Qt version it runs against.
Every such update would require a QtSharp package release.

### Qt WebView is the lightweight Qt-native route

Qt WebView is a small module (`qt.qt6.6120.addons.qtwebview`, 337 KB archive): the Windows binary
package ships the Microsoft Edge WebView2 backend, macOS/iOS use WKWebView, and the Linux backend
is **QtWebEngine** (there is no system WebKitGTK backend in the official matrix).

Measured payload on Windows x64:

| piece | size |
|---|---|
| `Qt6WebView.dll` | 60 KB |
| `plugins/webview/qtwebview_webview2.dll` | 132 KB |

The C++ API was introduced in Qt 6.11 as a **Technology Preview**; the module documentation is
still QML-oriented and does not describe the C++ entry points. It consists of three types:

- `QWebView : QWindow` — `setUrl`, `loadHtml`, `goBack`/`goForward`/`reload`/`stop`,
  `runJavaScript`, cookies, user agent, plus `titleChanged`, `urlChanged`,
  `loadingChanged(QWebViewLoadingInfo)`, `loadProgressChanged` and cookie signals;
- `QWebViewSettings` — `WebAttribute` (`LocalStorageEnabled`, `JavaScriptEnabled`,
  `AllowFileAccess`, `LocalContentCanAccessFileUrls`);
- `QWebViewLoadingInfo` — URL, `LoadStatus` and error string.

It requires `QtWebView::initialize()` before the application object is created. Because
`QWebView` is a `QWindow`, a widget application wraps it in
`QWidget.createWindowContainer(...)`, an API this binding already publishes. On Windows the
Chromium binary is the Edge runtime the operating system already updates; on Linux the same
binding would need the QtWebEngine runtime, so the Linux package story is as heavy as option 1.

## .NET and third-party alternatives

| option | platforms | engine | notes |
|---|---|---|---|
| **WebView2** (`Microsoft.Web.WebView2`) | Windows only | installed Edge | Official .NET SDK; Evergreen runtime auto-updates with Edge; Microsoft confirmed there is no Linux/macOS roadmap. Hosting inside a QWidget works by passing `QWidget.WinId()` to `CreateCoreWebView2Controller`, setting `Bounds` and `IsVisible`, and updating the bounds on resize — but native child windows paint above Qt content (airspace) and input/focus/IME handling bypasses Qt. |
| **Qt WebView** | Windows (WebView2), Linux (WebEngine) | OS / Chromium | Gives the same WebView2 on Windows with Qt-owned hosting and a cross-platform API; 192 KB of Qt code; C++ API is Technology Preview. |
| **CefSharp** | Windows only | bundled Chromium | Actively maintained; ships its own Chromium (~250–300 MB); works on .NET 10 via roll-forward; no Linux/macOS. |
| **DotNetBrowser** | Windows, Linux, macOS | bundled Chromium | Commercial license; ships Chromium; useful when a cross-platform bundled engine is required. |
| **Avalonia WebView, Photino, MAUI BlazorWebView** | per UI stack | OS web view | Each is tied to its own windowing/UI stack and cannot be placed inside a QWidget. |

## Decision

**Do not bind QtWebEngine. Host the operating system's web view from a managed QWidget instead.**

- QtWebEngine's capability set (request interception, custom URL schemes, profiles, DevTools,
  QWebChannel, off-the-record profiles) has no consumer in the repository today, while its cost
  is the largest of any addon: ~263 MB of release runtime (338 MB with the debug payload), a
  Qml/Quick dependency tree, a separate Chromium-driven version line, and a large generated API
  surface full of values the model cannot express (`std::function` callbacks,
  `QList<QWebEngineScript>`, cross-module `QWebChannel`).
- `QtSharp.WebView` follows the `QtSharp.Sql` pattern instead: a managed adapter that gives a Qt
  widget the capability while a .NET/OS library does the work. The widget is ~15 KB of managed
  code; Chromium is the Edge WebView2 runtime the operating system already updates, so the
  package carries no browser, no resources and no separate version line.
- Windows-only is the accepted trade-off. On Linux, Qt WebView (backed by QtWebEngine) remains
  the cross-platform Qt route, and binding it would still be the first step if Linux web content
  becomes a requirement.
- A hand-written WebView2 host was chosen over binding Qt WebView because it needs no Qt addon,
  no plugin deployment and no Technology Preview API; the hosting edge cases (bounds, visibility,
  DPI, focus) are the same either way.

## What was built

`nuget/QtSharp.WebView/WebView2Widget.cs` is a `QWidget` subclass that:

- creates the WebView2 environment once per process (profile under
  `%LOCALAPPDATA%\QtSharp\WebView2\<exe>`), then a controller per widget, parented to the
  widget's `WinId()`;
- pumps Qt's event loop while WebView2 completes its asynchronous start-up (`CreateAsync` would
  otherwise deadlock the UI thread waiting on a message-queue completion);
- keeps the controller's bounds, rasterization scale and visibility in step with Qt's resize,
  show, hide and DPI events;
- exposes `Navigate`/`NavigateToString`/`Reload`/`Stop`/`GoBack`/`GoForward`,
  `ExecuteScript(Async)`, `PostWebMessage`, `Url`/`Title`, the WebView2 navigation and process
  events, and `RunOnUiThread` (a queued `[QtSlot]` invocation for worker threads);
- closes the browser from the Qt `destroyed` signal, and ignores late Win32 messages once the
  wrapper is disposed (WebView2's child windows send a few during teardown).

`tests/WebViewApi.cs` asserts load, title, `about:blank`, script execution and the cross-thread
dispatch under a real window; the offscreen gate reports it as skipped. The package is packed as
`QtSharp.WebView.win-x64`, depends on `QtSharp.Api.win-x64` and `Microsoft.Web.WebView2`, and the
package-consumer test checks that the WebView2 loader lands in the consumer's `runtimes/` folder.

Airspace is the main limitation: the browser is a native child window, so Qt-drawn content cannot
paint over it. The package README documents that, the threading rule and the user data folder.

## Reproducing the measurement

```bash
base=https://download.qt.io/online/qtsdkrepository/windows_x86/extensions/qtwebengine/6120/61400/msvc2022_64/extensions.qtwebengine.6120.61400.win64_msvc2022_64
curl -fL -o qtwebengine.7z "$base/6.12.0-0-202609281005qtwebengine-Windows-Windows_11_24H2-MSVC2022-Windows-Windows_11_24H2-X86_64.7z"
7z x qtwebengine.7z -o<empty-dir>

# Qt WebView (needs the 6.12.0 kit and, on Windows, the WebView2 runtime)
curl -fL -o qtwebview.7z "https://download.qt.io/online/qtsdkrepository/windows_x86/desktop/qt6_6120/qt6_6120_msvc2022_64/qt.qt6.6120.addons.qtwebview.win64_msvc2022_64/6.12.0-0-202609280346qtwebview-Windows-Windows_11_24H2-MSVC2022-Windows-Windows_11_24H2-X86_64.7z"
```

Both archives carry `LGPL-3.0-only OR GPL-2.0-only OR GPL-3.0-only` headers. Any future binding
belongs in its own optional package, as Qt Charts already is, so a `QtSharp.Api` consumer does not
inherit the license or the payload.

## Sources

- [Qt WebEngine Separate Release](https://www.qt.io/blog/qt-webengine-will-release-separate) —
  the 6.12 split, 6.140.0 versioning, Chromium baseline and CRA motivation.
- [Using the WebView module with C++ and Qt Widgets](https://www.qt.io/blog/using-the-webview-module-with-c-and-qt-widgets) —
  `QWebView`, the platform backend table, `createWindowContainer`, WebView2 on Windows and
  WebEngine on Linux.
- [Qt WebView documentation](https://doc.qt.io/qt-6/qtwebview-index.html) — `QtWebView::initialize`,
  platform backends and the WebView2 overlap limitation.
- [Microsoft Edge WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/landing/) —
  Windows-only support and Evergreen runtime.
- [Hosting WebView2 in a Qt QWidget](https://stackoverflow.com/questions/76114269/hosting-msft-webview2-browser-control-in-a-qt-qwidget) —
  `winId()`, bounds and `IsVisible` gotchas.
- [CefSharp](https://github.com/cefsharp/CefSharp) and [DotNetBrowser](https://teamdev.com/dotnetbrowser/) —
  the bundled-Chromium alternatives.
