# Conformance coverage

The gate treats Qt's own examples as the test suite, the same way a language binding claims
compatibility by running upstream programs. Three executables are registered, all driven by
`tools/run-window-tests.ps1` (each lists its own names with `--list`, so the gate and the demo
table cannot drift):

| runner | source | windows | assertions |
|---|---|---|---|
| `testbind` / `samples/hello` | `demos/` — the base binding | 35 | 1319 |
| `ChartsDemos` | `demos.charts/` — Qt Charts | 38 | 570 |
| `qt3ddemos` | `demos.qt3d/` — Qt 3D | 3 | 37 |

## Module by module

| module | dedicated upstream example ports | notes |
|---|---|---|
| QtCore | yes — the core tour (`api-core`: files, JSON, ids, timers, threads, IPC), `api-valuetypes`, `api-exceptions`, `api-metaobject` | |
| QtGui | yes — painting (`api-basicdrawing`, `api-gradients`, `api-painterpaths`, `api-composition`, `api-transform`, `api-pathstroke`), events (`api-events`), images/text | |
| QtWidgets | yes — `api-analogclock`, `api-calculator`, `api-lineedits`, `api-spinboxes`, `api-sliders`, `api-calendarwidget`, `api-basiclayouts`, `api-menus`, `api-scribble`, `api-gallery`, `api-systray`, `api-docviewer`, `api-completer`, `api-customsortfilter`, `api-stardelegate`, `api-shortcut`, `api-shapedclock`, … | |
| QtNetwork | partial — `api-network` (host addresses, local-socket handoff, TCP) | a direct port of Qt's HTTP or fortune examples is not in the gate; the transport path is covered |
| QtSvg | partial — `api-docformats` exercises `QSvgRenderer` and `QSvgGenerator` | upstream `svgviewer` not ported |
| QtSvgWidgets | **no dedicated demo** | `QSvgWidget` is bound; it is not exercised by the gate yet |
| QtPrintSupport | partial — `api-docformats` writes a PDF through `QPrinter` | upstream printing UI examples not ported |
| QtXml | partial — `api-docformats` builds/parses a `QDomDocument`; `api-core` covers `QXmlStreamReader` | upstream `dombookmarks` not ported |
| QtOpenGL | **no dedicated demo** | `QOpenGLShaderProgram`, `QOpenGLBuffer` and `QOpenGLVertexArrayObject` are bound, but `QOpenGLFunctions` is not, so Qt's `hellogl2` cannot be ported as-is |
| QtOpenGLWidgets | **no dedicated demo** | `QOpenGLWidget` is bound; a context-dependent demo needs a GPU/software GL runner |
| Qt Charts | yes — 38 official examples (`demos.charts/`) | includes the gallery widgets, model mappers, polar/zoom examples |
| Qt 3D | yes — `simple-cpp`, `basicshapes-cpp`, `3d-text` (`demos.qt3d/`) | runs windowless, builds and asserts the scene |

## Closing the remaining gaps

The natural next ports are:

1. **QtSvg + QtSvgWidgets** — `examples/svg/svgviewer`: `QSvgRenderer` rendering to an image plus a
   `QSvgWidget`, asserted headless (the official UI itself can stay a window).
2. **QtXml** — `examples/xml/dombookmarks`: load the official `jennifer.xbel`, walk the
   `QDomDocument`, edit and write it back through a `QTreeWidget`.
3. **QtPrintSupport** — Qt's notepad/order-form printing example: render a document to PDF through
   `QPrinter` and assert the output, with the print dialog kept as the interactive path.
4. **QtNetwork** — the fortune client/server pair over loopback, or an HTTP client against a
   `QTcpServer` that answers locally (no internet dependency).
5. **QtOpenGL/OpenGLWidgets** — `hellogl2` needs `QOpenGLFunctions` in the model first, and a CI
   runner with a usable OpenGL context; without both it would be a demo that cannot assert what it
   renders.

A new demo is registered in `demos/ApiDemos.cs` (or the addon's `ChartsDemos`/Qt3D dispatcher),
which is all the gate needs to start running it.
