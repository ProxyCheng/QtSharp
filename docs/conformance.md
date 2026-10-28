# Conformance coverage

The gate treats Qt's own examples as the test suite, the same way a language binding claims
compatibility by running upstream programs. Each module with demos has its own executable, and
each holds exactly one `Main`, one `QApplication` and the module's reflected `[Demo]` classes:

| executable | project | windows | assertions |
|---|---|---|---|
| `QtSharp.Demos.QtCore` | `demos/QtCore/` | 2 | 17 |
| `QtSharp.Demos.QtGui` | `demos/QtGui/` | 6 | 180 |
| `QtSharp.Demos.QtWidgets` | `demos/QtWidgets/` | 19 | 704 |
| `QtSharp.Demos.QtNetwork` | `demos/QtNetwork/` | 1 | 9 |
| `QtSharp.Demos.QtSvg` | `demos/QtSvg/` | 1 | 8 |
| `QtSharp.Demos.QtSvgWidgets` | `demos/QtSvgWidgets/` | 1 | 4 |
| `QtSharp.Demos.QtPrintSupport` | `demos/QtPrintSupport/` | 1 | 2 |
| `QtSharp.Demos.QtMultimedia` | `demos/QtMultimedia/` | 1 | 12 |
| `QtSharp.Charts.Demos` | `demos/Charts/` | 38 | 571 |
| `QtSharp.Qt3D.Demos` | `demos/Qt3D/` | 3 | 37 |
| `testapp` (the binding tests) | `tests/testapp/` | 24 | 779 |

`tools/run-demos.ps1` builds all ten demo executables and hands each to
`tools/run-window-tests.ps1`; the gate, CI and the release build all use that one table. A runner
lists its own names with `--list`, so the gate and the tables cannot drift.

## Smoke and View

The demo sources compile twice, selected by the build configuration:

| configuration | alias | product |
|---|---|---|
| `Smoke` | `Release` | the automated half: builds each scene, checks it, prints `[PASS]`/`[FAIL]` and returns an exit code. No event loop runs; the host picks the offscreen platform before the one `QApplication` is constructed |
| `View` | `Debug` | the interactive half: builds the scene, hands the window to the host and runs the event loop until the last window closes (`--seconds N` closes it early) |

`demos/DemoMode.props` defines `QTSHARP_DEMO_SMOKE`/`QTSHARP_DEMO_VIEW`; those constants compile
the `DemoContext` surface down to `Check`/`ExitCode` (Smoke) or `Show` (View), so a demo that
mixes the two products does not compile. A demo owns its shared construction and has one body per
mode; View contains no assertions and Smoke contains no window registration.

The Smoke host selects `offscreen` by default (with the Windows font directory set, which the
offscreen plugin does not discover itself), so the gate never maps a window. `QTSHARP_DEMO_PLATFORM`
or an already-set `QT_QPA_PLATFORM` overrides that; the host prints one `[platform] ...` line
saying where the choice came from. The Qt3D View product is the one exception: it renders through
OpenGL, which the offscreen plugin on Windows does not provide, so its View runs need a real
display. Its Smoke product never shows the `Qt3DWindow`, so the gate is unaffected.

## Official demos, by module

The demo catalog contains only ports of official Qt examples. The executable, the directory and
the runnable names all name the module the example exercises:

| module | demos |
|---|---|
| QtCore | `qtcore-savegame` — the JSON Save Game example's document model; `qtcore-regexp` — the regexp example's match/capture walk |
| QtGui | `qtgui-basicdrawing`, `qtgui-gradients`, `qtgui-painterpaths`, `qtgui-composition`, `qtgui-transformations`, `qtgui-pathstroke` |
| QtWidgets | `qtwidgets-analogclock`, `qtwidgets-basiclayouts`, `qtwidgets-calculator`, `qtwidgets-calendarwidget`, `qtwidgets-collidingmice`, `qtwidgets-completer`, `qtwidgets-customsortfilter`, `qtwidgets-documentviewer`, `qtwidgets-flowlayout`, `qtwidgets-gallery`, `qtwidgets-lineedits`, `qtwidgets-menus`, `qtwidgets-scribble`, `qtwidgets-shapedclock`, `qtwidgets-shortcuteditor`, `qtwidgets-sliders`, `qtwidgets-spinboxes`, `qtwidgets-stardelegate`, `qtwidgets-systray` |
| QtNetwork | `qtnetwork-fortune` — the fortune server/client pair over loopback |
| QtSvg | `qtsvg-svgviewer` — the SVG Viewer's render/export path |
| QtSvgWidgets | `qtsvgwidgets-svgwidget` — QSvgWidget loading and off-screen rendering |
| QtPrintSupport | `qtprintsupport-notepad` — the notepad tutorial's QPrinter path |
| QtMultimedia | `qtmultimedia-player` — Qt's player example core: a generated WAV loaded through QMediaPlayer, plus the format/device classes and QVideoWidget |
| Qt Charts | `qtcharts-*` — 38 official Qt Charts examples (`demos/Charts/`) |
| Qt 3D | `qt3d-simple`, `qt3d-basicshapes`, `qt3d-text` (`demos/Qt3D/`) |

## Binding tests

`tests/` holds the tests that are about the *binding* rather than about an upstream example. They
run under `test-*` names in one executable, `tests/testapp`, and stay in the gate:

| test | why it exists |
|---|---|
| `test-bclinterop` | the BCL conversions: QPoint/QSize/QRect/QColor/vectors/quaternion/URL/GUID/QVariant |
| `test-connections` | signal connections as disposable handles, context objects and queued thread affinity |
| `test-core` | the QtCore value/utility classes (files, JSON, ids, timers, threads, IPC, XML stream) |
| `test-custommodel` | managed item models/delegates and Qt's calls back into them |
| `test-docformats` | the document modules together: SVG rendering/export and PDF output (XML is System.Xml's job; `test-xmlseam` covers the hand-off) |
| `test-events` | the input-event family and managed virtual event handlers |
| `test-exceptions` | the native exception boundary (`qFatal`, C++ unwinding across managed frames) |
| `test-graphics` | the graphics-view family (scene, items, off-screen render) |
| `test-iodevice` | a .NET Stream behind QIODevice: QImage/QXmlStreamReader reading it, write path, leak checks |
| `test-json` | JSON as C# containers: QJsonObject as a writable dictionary, QJsonArray as a list, unwrapped snapshots, LINQ |
| `test-metaobject` | C#-declared signals/properties (`[QtSignal]`/`[QtProperty]`) |
| `test-metatype` | the C# → Qt type-name map: value classes, namespaced bound classes, unsupported types |
| `test-network` | QtNetwork transport beyond the fortune example: host addresses, local-socket handoff, TCP |
| `test-ownership` | who may delete a native object; liveness and generation counters |
| `test-positioning` | QtPositioning: coordinate distance/azimuth, shape containment, position info, source enumeration |
| `test-qt3d-valuetypes` | Qt3D's trivially copyable QNodeId/QKeyFrame, including Qt's equality rules |
| `test-regexinterop` | the explicit System.Text.RegularExpressions ↔ QRegularExpression conversion |
| `test-rolesugar` | the role-taking API with the named `Qt.ItemDataRole` enum over the `int` primary overloads |
| `test-sqlmodel` | the SQL substitute: an ADO.NET result behind QSqlQueryModel/QSqlTableModel, edits written by SubmitAll |
| `test-subclass` | C# subclasses driven by Qt's own machinery (abstract widget, layout, model, runnable) |
| `test-valuetypes` | the POD value structs and their conversions |
| `test-variantlifetime` | QVariant's managed-payload table: every copy releases its entry |
| `test-variantobject` | QVariant's object-shaped API: Qt values, `QObject*`, and opaque managed payloads |
| `test-xmlseam` | System.Xml and QXmlStreamReader/Writer handing documents to each other |

`tests/WindowFlagsApi.cs` is parked: every assertion passes, but the process dies inside
`qwindows.dll` during teardown roughly three runs in ten, so it is not in the catalog.

## Remaining gaps

| module | official example coverage | notes |
|---|---|---|
| QtCore | `qtcore-savegame`, `qtcore-regexp` | |
| QtGui | the six painting examples | |
| QtWidgets | 19 widget examples | |
| QtNetwork | `qtnetwork-fortune` | |
| QtSvg | `qtsvg-svgviewer` | |
| QtSvgWidgets | `qtsvgwidgets-svgwidget` | |
| QtPrintSupport | `qtprintsupport-notepad` | |
| QtMultimedia | `qtmultimedia-player` | playback is asserted through a loaded WAV; camera capture needs a device |
| QtOpenGL | no demo | `QOpenGLFunctions` is not bound, so `hellogl2` cannot be ported as-is |
| QtOpenGLWidgets | no demo | `QOpenGLWidget` is bound; needs a GPU/software-GL runner |
| Qt Charts | 38 official examples | |
| Qt 3D | 3 official examples | Smoke runs windowless; View needs a real display for OpenGL |

A demo is registered by putting `[Demo("name")]` on its static class, which must expose
`public static int Run(DemoContext demo)`; each module's `Program` hands its assembly to
`DemoHost.Run`, and the test executable hands the test assembly to `TestHost.Run`.
`DemoDiscovery` finds the registrations in that assembly. There is no separate list to maintain,
and a duplicate or malformed registration fails at startup.
