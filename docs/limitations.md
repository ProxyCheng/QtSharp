# Known limitations

The supported release targets are .NET 10 with Qt 6.12.0 on Windows x64 and Linux x64/glibc.

## Platforms and deployment

* **Windows x64 and Linux x64 (glibc), Qt 6.12.0 only** — the model is extracted from the MSVC
  kit, and the Linux build compiles the same generated C++ with g++. ARM64 and musl are not
  packaged.
* **The addon `.Qt6` packages have no separate Linux payload.** On Linux the base
  `QtSharp.Qt6` package deploys the whole Qt tree (`lib/` + `plugins/` + `qt.conf`, addon modules
  included), because every addon package depends on it anyway; the addon packages still carry the
  addon's native glue for both RIDs.
* **Single-file publish is not supported on Linux.** The deployment relies on `$ORIGIN` rpaths
  between the application directory, `lib/` and `plugins/`; a single-file bundle extracts the
  native libraries elsewhere and breaks that layout.
* **`QtSharp.Tools` is Windows x64 only** — its `rcc`/`uic`/`lrelease`/`lupdate` payload is the
  MSVC build. On Linux use the tools from the Qt kit directly.
* **`QtSharp.WebView` is Windows x64 only** — it hosts Microsoft Edge WebView2, which has no
  Linux implementation. The machine also needs the Evergreen WebView2 Runtime (preinstalled on
  Windows 11 and most updated Windows 10 systems);
  `WebView2Widget.IsRuntimeAvailable` reports whether it is present.
* **QtMultimedia on Linux needs the distribution's FFmpeg runtime.** Qt's ffmpeg backend plugin
  links `libavcodec`/`libavformat`/`libavutil`/`libswresample`/`libswscale`; install the ffmpeg
  runtime packages that match the kit's build (on Ubuntu 24.04, the `libavcodec60` family). The
  binding and the plugin load without them, but `QMediaPlayer` reports no media.

## API and marshalling

* **`long`-typed Qt APIs stay 32-bit on Linux.** The model comes from the MSVC headers, where
  `long` is 32 bits, so the C ABI pins it to `int`/`uint` on both platforms and the wrapper
  converts at the Qt call. That keeps one C# API across platforms, but `QByteArray.toLong`,
  `QLocale.toLong`, the `QEventPoint` timestamps and the `QThread`/`QWaitCondition` timeout
  parameters truncate to 32 bits on Linux (the timestamps wrap after ~49.7 days of uptime). Use the
  `QByteArray::toLongLong` / `QLocale::toLongLong` overloads when the full range matters.
* **Windows-only Qt members are not bound.** `QProcess.nativeArguments`/`setNativeArguments` are
  declared by Qt only on Windows; the generator filters them out so one API surface compiles on
  both platforms. Use `ProcessStartInfo.Arguments` instead.
* **`QAudioFormat` has no constructor overloads in the binding.** Qt 6.12 declares none beyond the
  implicit default (the AST does not list it), so there is no `QAudioFormat(...)`; as a struct its
  default (`new QAudioFormat()` / `default`) is Qt's default format, and the setters fill it in.
  `QAudioDevice.PreferredFormat()` is still the useful starting point.
* An optional parameter whose C++ default is a *bare enumerator that is not an enumerator of the
  parameter's type* still truncates the overload (the C# default would be 0, which Qt often
  reserves for "invalid"). A boxed-value default is fine now: the parameter defaults to `null` and
  the native glue substitutes the declared expression, which is what unlocked
  `QPixmap.fill(QColor)`, `QWidget.grab(QRect)`, `QComboBox.addItem(QString, QVariant)` and
  `QTcpServer.listen(address, port)`.
* A `const uchar *` *return* is dropped (there is no length to go with it), so `QResource.data()`
  and `QImage.bits()` are absent; `QResource.uncompressedData()`, `QByteArray` and the `byte[]`
  parameters cover the same ground. Nested classes (`QFont::Tag`, `QTextOption::Tab`) are not
  bound either — the model publishes top-level classes and enums only.

## Module coverage

* **Intentionally omitted**: QtXml, QtSql and QtWebSockets use .NET alternatives; see
  [module scope and .NET integration](../README.md#module-scope-and-net-integration).
* **Not currently bound**: QtQml/QtQuick, QtPdf, QtWebEngine and QtWebView. The measured cost of
  the web view modules is in [web view options](webview.md); `QtSharp.WebView` is the managed
  WebView2 substitute for the Windows case.
* **Not ported**: the RHI examples (they need `QRhi` and prebuilt shaders) and Window Embedding.
  PDF *output* works through `QPrinter` (see `test-docformats`); PDF *reading* needs the QtPdf
  module, which is not bound.

## Runtime behavior

* **`QtSharp.WebView` has airspace limits.** WebView2 renders into a native child window, so
  Qt-painted content cannot cover it: child widgets, stylesheet backgrounds and translucent
  overlays above it are hidden. Menus, tooltips and dialogs are separate top-level windows and
  work normally. The widget is also UI-thread bound; use `RunOnUiThread` after background work.
* **`QtSharp.Sql` materializes a whole result set.** Qt's `QSqlTableModel` fetches in 256-row
  batches; the substitute loads everything up front, so page very large tables with `WHERE`/`LIMIT`.
  Its per-cell `Data()` also crosses the managed/native boundary (a per-call cost for visible cells).
* [`WindowFlagsApi.cs`](../tests/WindowFlagsApi.cs) is excluded from the dispatcher because native window teardown is unstable.
  Passing its assertions does not establish a clean process exit.
