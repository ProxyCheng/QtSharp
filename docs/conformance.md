# Conformance coverage

The gate treats Qt's own examples as the test suite, the same way a language binding claims
compatibility by running upstream programs. Three executables are registered, all driven by
`tools/run-window-tests.ps1` (each lists its own names with `--list`, so the gate and the tables
cannot drift):

| runner | source | windows | assertions |
|---|---|---|---|
| `testbind` | `demos/` — the official Qt examples, plus `tests/` — binding-specific tests | 56 | 1715 |
| `QtSharp.Charts.Demos` | `demos.charts/` — Qt Charts | 38 | 571 |
| `QtSharp.Qt3D.Demos` | `demos.qt3d/` — Qt 3D | 3 | 37 |

## Official demos, by module

The demo catalog in `demos/` contains only ports of official Qt examples. The file layout and the
runnable names both name the module the example exercises:

| module | demos |
|---|---|
| QtCore | `qtcore-savegame` — the JSON Save Game example's document model; `qtcore-regexp` — the regexp example's match/capture walk |
| QtGui | `qtgui-basicdrawing`, `qtgui-gradients`, `qtgui-painterpaths`, `qtgui-composition`, `qtgui-transformations`, `qtgui-pathstroke` |
| QtWidgets | `qtwidgets-analogclock`, `qtwidgets-calculator`, `qtwidgets-calendarwidget`, `qtwidgets-completer`, `qtwidgets-lineedits`, `qtwidgets-menus`, `qtwidgets-scribble`, `qtwidgets-shapedclock`, `qtwidgets-shortcuteditor`, `qtwidgets-sliders`, `qtwidgets-spinboxes`, `qtwidgets-stardelegate`, `qtwidgets-systray`, `qtwidgets-documentviewer`, `qtwidgets-basiclayouts`, `qtwidgets-gallery`, `qtwidgets-customsortfilter` |
| QtNetwork | `qtnetwork-fortune` — the fortune server/client pair over loopback |
| QtSvg | `qtsvg-svgviewer` — the SVG Viewer's render/export path |
| QtSvgWidgets | `qtsvgwidgets-svgwidget` — QSvgWidget loading and off-screen rendering |
| QtPrintSupport | `qtprintsupport-notepad` — the notepad tutorial's QPrinter path |
| QtMultimedia | `qtmultimedia-player` — Qt's player example core: a generated WAV loaded through QMediaPlayer, plus the format/device classes and QVideoWidget |
| Qt Charts | `qtcharts-*` — 38 official Qt Charts examples (`demos.charts/`) |
| Qt 3D | `qt3d-simple`, `qt3d-basicshapes`, `qt3d-text` (`demos.qt3d/`) |

## Binding tests

`tests/` holds the tests that are about the *binding* rather than about an upstream example. They
run under `test-*` names and stay in the gate:

| test | why it exists |
|---|---|
| `test-core` | the QtCore value/utility classes (files, JSON, ids, timers, threads, IPC, XML stream) |
| `test-events` | the input-event family and managed virtual event handlers |
| `test-exceptions` | the native exception boundary (`qFatal`, C++ unwinding across managed frames) |
| `test-metaobject` | C#-declared signals/properties (`[QtSignal]`/`[QtProperty]`) |
| `test-metatype` | the canonical C# -> Qt type-name map and `QMetaType.FromType<T>` |
| `test-connections` | signal connections: disposable handles, connection type and context lifetime |
| `test-ownership` | who may delete a native object; liveness and generation counters |
| `test-valuetypes` | the POD value structs and their conversions |
| `test-network` | QtNetwork transport beyond the fortune example: host addresses, local-socket handoff, TCP |
| `test-docformats` | the document modules together: SVG rendering/export and PDF output (XML is System.Xml's job; `test-xmlseam` covers the hand-off) |
| `test-custommodel` | managed item models/delegates and Qt's calls back into them |
| `test-graphics` | the graphics-view family (scene, items, off-screen render) |
| `test-bclinterop` | the BCL conversions: QPoint/QSize/QRect/QColor/vectors/quaternion/URL/GUID/QVariant |
| `test-regexinterop` | `Regex` <-> `QRegularExpression`: the mappable options, the rejecting ones and both dialect failures |
| `test-json` | JSON as C# containers: QJsonObject as a writable dictionary, QJsonArray as a list, unwrapped snapshots, LINQ |
| `test-iodevice` | a .NET Stream behind QIODevice: QImage/QXmlStreamReader reading it, write path, leak checks |
| `test-xmlseam` | System.Xml and QXmlStreamReader/Writer handing documents to each other |
| `test-positioning` | QtPositioning: coordinate distance/azimuth, shape containment, position info, source enumeration |
| `test-sqlmodel` | the SQL substitute: an ADO.NET result behind QSqlQueryModel/QSqlTableModel, edits written by SubmitAll |

`tests/WindowFlagsApi.cs` is parked: every assertion passes, but the process dies inside
`qwindows.dll` during teardown roughly three runs in ten, so it is not in the catalog.

## Remaining gaps

| module | official example coverage | notes |
|---|---|---|
| QtCore | `qtcore-savegame`, `qtcore-regexp` | |
| QtGui | the six painting examples | |
| QtWidgets | 17 widget examples | |
| QtNetwork | `qtnetwork-fortune` | |
| QtSvg | `qtsvg-svgviewer` | |
| QtSvgWidgets | `qtsvgwidgets-svgwidget` | |
| QtPrintSupport | `qtprintsupport-notepad` | |
| QtMultimedia | `qtmultimedia-player` | playback is asserted through a loaded WAV; camera capture needs a device |
| QtOpenGL | no demo | `QOpenGLFunctions` is not bound, so `hellogl2` cannot be ported as-is |
| QtOpenGLWidgets | no demo | `QOpenGLWidget` is bound; needs a GPU/software-GL runner |
| Qt Charts | 38 official examples | |
| Qt 3D | 3 official examples | runs windowless |

A demo is registered by putting `[Demo("name")]` on its static class (which must expose
`public static int Run(QApplication app)`); `DemoCatalog`, `QtSharp.Charts.Demos`, the Qt3D runner and
`BindingTests` discover their own assembly with `DemoDiscovery`. There is no separate list to
maintain, and a duplicate or malformed registration fails at startup.
