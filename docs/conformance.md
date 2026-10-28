# Conformance coverage

The gate treats Qt's own examples as the test suite, the same way a language binding claims
compatibility by running upstream programs. Each module with examples has its own executable, and
each holds exactly one `Main`, one `QApplication` and the module's reflected `[Example]` classes:

| executable | project | windows | assertions |
|---|---|---|---|
| `QtSharp.Examples.QtCore` | `examples/QtCore/` | 2 | 17 |
| `QtSharp.Examples.QtGui` | `examples/QtGui/` | 6 | 180 |
| `QtSharp.Examples.QtWidgets` | `examples/QtWidgets/` | 19 | 704 |
| `QtSharp.Examples.QtNetwork` | `examples/QtNetwork/` | 1 | 9 |
| `QtSharp.Examples.QtSvg` | `examples/QtSvg/` | 1 | 8 |
| `QtSharp.Examples.QtSvgWidgets` | `examples/QtSvgWidgets/` | 1 | 4 |
| `QtSharp.Examples.QtPrintSupport` | `examples/QtPrintSupport/` | 1 | 2 |
| `QtSharp.Examples.QtMultimedia` | `examples/QtMultimedia/` | 1 | 12 |
| `QtSharp.Charts.Examples` | `examples/Charts/` | 38 | 571 |
| `QtSharp.Qt3D.Examples` | `examples/Qt3D/` | 3 | 37 |
| `testapp` (the binding tests) | `tests/testapp/` | 36 | 945 |

`tools/run-examples.ps1` builds all ten example executables and hands each to
`tools/run-window-tests.ps1`; the gate, CI and the release build all use that one table. A runner
lists its own names with `--list`, so the gate and the tables cannot drift.

## Smoke and View

The example sources compile twice, selected by the build configuration:

| configuration | alias | product |
|---|---|---|
| `Smoke` | `Release` | the automated half: builds each scene, checks it, prints `[PASS]`/`[FAIL]` and returns an exit code. No event loop runs; the host picks the offscreen platform before the one `QApplication` is constructed |
| `View` | `Debug` | the interactive half: builds the scene, hands the window to the host and runs the event loop until the last window closes (`--seconds N` closes it early) |

`examples/ExampleMode.props` defines `QTSHARP_EXAMPLE_SMOKE`/`QTSHARP_EXAMPLE_VIEW`; those constants compile
the `ExampleContext` surface down to `Check`/`ExitCode` (Smoke) or `Show` (View), so an example that
mixes the two products does not compile. An example owns its shared construction and has one body per
mode; View contains no assertions and Smoke contains no window registration.

The Smoke host selects `offscreen` by default (with the Windows font directory set, which the
offscreen plugin does not discover itself), so the gate never maps a window. `QTSHARP_EXAMPLE_PLATFORM`
or an already-set `QT_QPA_PLATFORM` overrides that; the host prints one `[platform] ...` line
saying where the choice came from. The Qt3D View product is the one exception: it renders through
OpenGL, which the offscreen plugin on Windows does not provide, so its View runs need a real
display. Its Smoke product never shows the `Qt3DWindow`, so the gate is unaffected.

## Official examples, by module

The example catalog contains only ports of official Qt examples. The executable, the directory and
the runnable names all name the module the example exercises:

| module | examples |
|---|---|
| QtCore | `qtcore-savegame` — the JSON Save Game example's document model; `qtcore-regexp` — the regexp example's match/capture walk |
| QtGui | `qtgui-basicdrawing`, `qtgui-gradients`, `qtgui-painterpaths`, `qtgui-composition`, `qtgui-transformations`, `qtgui-pathstroke` |
| QtWidgets | `qtwidgets-analogclock`, `qtwidgets-basiclayouts`, `qtwidgets-calculator`, `qtwidgets-calendarwidget`, `qtwidgets-collidingmice`, `qtwidgets-completer`, `qtwidgets-customsortfilter`, `qtwidgets-documentviewer`, `qtwidgets-flowlayout`, `qtwidgets-gallery`, `qtwidgets-lineedits`, `qtwidgets-menus`, `qtwidgets-scribble`, `qtwidgets-shapedclock`, `qtwidgets-shortcuteditor`, `qtwidgets-sliders`, `qtwidgets-spinboxes`, `qtwidgets-stardelegate`, `qtwidgets-systray` |
| QtNetwork | `qtnetwork-fortune` — the fortune server/client pair over loopback |
| QtSvg | `qtsvg-svgviewer` — the SVG Viewer's render/export path |
| QtSvgWidgets | `qtsvgwidgets-svgwidget` — QSvgWidget loading and off-screen rendering |
| QtPrintSupport | `qtprintsupport-notepad` — the notepad tutorial's QPrinter path |
| QtMultimedia | `qtmultimedia-player` — Qt's player example core: a generated WAV loaded through QMediaPlayer, plus the format/device classes and QVideoWidget |
| Qt Charts | `qtcharts-*` — 38 official Qt Charts examples (`examples/Charts/`) |
| Qt 3D | `qt3d-simple`, `qt3d-basicshapes`, `qt3d-text` (`examples/Qt3D/`) |

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
| `test-metaobjectcache` | the native meta-object cache identity: base class, class name and member list |
| `test-metatype` | the C# → Qt type-name map: value classes, namespaced bound classes, unsupported types |
| `test-nativeslot` | the read-side `[QtNativeSlot]` marker on generated slots and `Q_INVOKABLE` methods |
| `test-network` | QtNetwork transport beyond the fortune example: host addresses, local-socket handoff, TCP |
| `test-ownership` | who may delete a native object; liveness and generation counters |
| `test-positioning` | QtPositioning: coordinate distance/azimuth, shape containment, position info, source enumeration |
| `test-qt3d-valuetypes` | Qt3D's trivially copyable QNodeId/QKeyFrame, including Qt's equality rules |
| `test-qtslot` | C# methods as real Qt slots: by-name invoke, overload signatures, `connectSlotsByName`, validation |
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
| QtOpenGL | no example | `QOpenGLFunctions` is not bound, so `hellogl2` cannot be ported as-is |
| QtOpenGLWidgets | no example | `QOpenGLWidget` is bound; needs a GPU/software-GL runner |
| Qt Charts | 38 official examples | |
| Qt 3D | 3 official examples | Smoke runs windowless; View needs a real display for OpenGL |

An example is registered by putting `[Example("name")]` on its static class, which must expose
`public static int Run(ExampleContext example)`; each module's `Program` hands its assembly to
`ExampleHost.Run`, and the test executable hands the test assembly to `TestHost.Run`.
`ExampleDiscovery` finds the registrations in that assembly. There is no separate list to maintain,
and a duplicate or malformed registration fails at startup.
