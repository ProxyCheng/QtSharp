# Conformance coverage

The gate treats Qt's own examples as the test suite, the same way a language binding claims
compatibility by running upstream programs. Three executables are registered, all driven by
`tools/run-window-tests.ps1` (each lists its own names with `--list`, so the gate and the tables
cannot drift):

| runner | source | windows | assertions |
|---|---|---|---|
| `testbind` | `demos/` — the official Qt examples, plus `tests/` — binding-specific tests | 34 | 1254 |
| `ChartsDemos` | `demos.charts/` — Qt Charts | 38 | 570 |
| `qt3ddemos` | `demos.qt3d/` — Qt 3D | 3 | 37 |

## Official demos, by module

The demo catalog in `demos/` contains only ports of official Qt examples. The file layout and the
runnable names both name the module the example exercises:

| module | demos |
|---|---|
| QtGui | `qtgui-basicdrawing`, `qtgui-gradients`, `qtgui-painterpaths`, `qtgui-composition`, `qtgui-transformations`, `qtgui-pathstroke` |
| QtWidgets | `qtwidgets-analogclock`, `qtwidgets-calculator`, `qtwidgets-calendarwidget`, `qtwidgets-completer`, `qtwidgets-lineedits`, `qtwidgets-menus`, `qtwidgets-scribble`, `qtwidgets-shapedclock`, `qtwidgets-shortcuteditor`, `qtwidgets-sliders`, `qtwidgets-spinboxes`, `qtwidgets-stardelegate`, `qtwidgets-systray`, `qtwidgets-documentviewer`, `qtwidgets-basiclayouts`, `qtwidgets-gallery`, `qtwidgets-customsortfilter` |
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
| `test-ownership` | who may delete a native object; liveness and generation counters |
| `test-valuetypes` | the POD value structs and their conversions |
| `test-network` | QtNetwork transport: host addresses, local-socket handoff, TCP |
| `test-docformats` | QtXml DOM, QtSvg rendering/export, QtPrintSupport PDF output |
| `test-custommodel` | managed item models/delegates and Qt's calls back into them |
| `test-graphics` | the graphics-view family (scene, items, off-screen render) |
| `test-bclinterop` | the BCL conversions: QPoint/QSize/QRect/QColor/vectors/quaternion/URL/GUID/QVariant |

`tests/WindowFlagsApi.cs` is parked: every assertion passes, but the process dies inside
`qwindows.dll` during teardown roughly three runs in ten, so it is not in the catalog.

## Remaining gaps

| module | official example coverage | notes |
|---|---|---|
| QtCore | none of the official QtCore examples is ported as a standalone demo; `test-core` covers the API surface | candidate: the official `corelib` examples |
| QtGui | the six painting examples | |
| QtWidgets | 17 widget examples | |
| QtNetwork | no official port; `test-network` covers the transport | candidate: HTTP client or fortune client/server |
| QtSvg | no official port; `test-docformats` exercises `QSvgRenderer`/`QSvgGenerator` | candidate: `svgviewer` |
| QtSvgWidgets | no demo | `QSvgWidget` is bound; add it to the `svgviewer` port |
| QtPrintSupport | no official port; `test-docformats` writes a PDF | candidate: notepad/order-form printing |
| QtXml | no official port; `test-docformats` covers `QDomDocument` | candidate: `dombookmarks` |
| QtOpenGL | no demo | `QOpenGLFunctions` is not bound, so `hellogl2` cannot be ported as-is |
| QtOpenGLWidgets | no demo | `QOpenGLWidget` is bound; needs a GPU/software-GL runner |
| Qt Charts | 38 official examples | |
| Qt 3D | 3 official examples | runs windowless |

A demo is registered by putting `[Demo("name")]` on its static class (which must expose
`public static int Run(QApplication app)`); `DemoCatalog`, `ChartsDemos`, the Qt3D runner and
`BindingTests` discover their own assembly with `DemoDiscovery`. There is no separate list to
maintain, and a duplicate or malformed registration fails at startup.
