# Changelog

All notable changes to QtSharp will be documented here.

The project is pre-1.0, so APIs and package layouts may change between releases.

## 0.15.0 - 2026-10-26

- **`QtSharp.Sql` restores the two Qt model classes that went with the QtSql binding**, implemented
  over ADO.NET instead: `QSqlQueryModel` (a read-only `QAbstractTableModel` over a query) and
  `QSqlTableModel` (staged edits, written by `SubmitAll` in one transaction, `RevertAll` to
  discard). They keep the `QtSharp.QtSql` namespace and class names, so code written against the
  removed binding still compiles, but there is no Qt SQL module behind them: pass any
  `DbConnection` (Microsoft.Data.Sqlite, SqlClient, Npgsql, ...).
- **`test-sqlmodel`** covers the seam rather than SQLite itself: a .NET result set behind a
  `QTableView`, cell reads through `QVariant`, and a staged edit reaching the database.
- Why this one gets a substitute and QtXml/QtWebSockets do not: `QSqlQueryModel` is an
  `QAbstractItemModel`, the interface Qt views consume; `QDomDocument` and `QWebSocket` are leaf
  services whose .NET counterparts can be used directly.

## 0.14.0 - 2026-10-26

- **QtPositioning is bound**, as the separate `QtSharp.Positioning` package: `QGeoCoordinate` and
  the geo value types (`QGeoRectangle`, `QGeoCircle`, `QGeoPath`, `QGeoPolygon`, `QGeoShape`,
  `QGeoAddress`, `QGeoLocation`), `QGeoPositionInfo`, `QGeoSatelliteInfo` and the position/
  satellite sources. `QtSharp.Positioning.Qt6` carries `Qt6Positioning.dll`.
- **`test-positioning`** covers the value math (Berlin-Paris distance and azimuth, altitude,
  rectangle/circle containment, path length), a position info carrying a coordinate, and the
  source enumeration (which tolerates a machine with no positioning plugin).

## 0.13.0 - 2026-10-26

- **QtXml, QtSql and QtWebSockets are gone.** .NET covers the same ground (System.Xml,
  EF Core/SQLite, ClientWebSocket), none of the three leaked types into the remaining bound
  modules, and `QtSharp.Api` is back to nine assemblies. `QXmlStreamReader`/`QXmlStreamWriter`
  stay in QtCore, so Qt-style XML streaming is still available.
- **The seams are tested, not the .NET libraries.** `test-xmlseam` hands documents between
  System.Xml and QXmlStreamReader/Writer; `test-docformats` keeps SVG/PDF and drops the DOM part.
  The QtSql and QtWebSockets demos and their packages went with their modules.
- The Qt6 runtime payload no longer ships Qt6Sql.dll, Qt6Xml.dll or the SQL driver plugins.

## 0.12.0 - 2026-10-26

- **A .NET `Stream` can sit behind Qt's `QIODevice`.** `StreamIODevice` implements the device
  virtuals (`readData`/`writeData`/`seek`/`pos`/`size`/`atEnd`/`isSequential`/`reset`), so
  `QImage.Load`, `QXmlStreamReader`, `QDataStream` and every other `QIODevice*` consumer read from
  or write to managed IO. `test-iodevice` covers both directions plus 1000 create/dispose cycles
  and 500 abandoned instances as a leak check.
- **The generator has a bridge registry.** `gen/Bridges.cs` is the single place where a .NET
  capability wraps itself in a Qt interface: a bridge says which classes it owns, which virtuals
  it publishes and how it maps special parameters (QIODevice's raw byte buffers become `IntPtr`).
  Adding the next bridge is a class plus a row, not another branch in the extractor.

## 0.11.0 - 2026-10-26

- **QJsonArray is a C# list.** It implements `IList<QJsonValue>` (indexer, Add, Insert, RemoveAt,
  Remove, IndexOf, Contains, Clear), and `ToObjectList()` gives the unwrapped snapshot for LINQ.
  Same ownership rule as the object: the wrappers the list hands out are the caller's, the snapshot
  is plain BCL values.

## 0.10.0 - 2026-10-26

- **QJsonObject is a writable C# dictionary.** It implements `IDictionary<string, QJsonValue>`
  (indexer, Add, Remove, ContainsKey, TryGetValue, Clear, the `ICollection` shape), and
  `ToObjectDictionary()` keeps the LINQ-friendly snapshot of unwrapped values. A second
  `IEnumerable<T>` on the same type would make LINQ inference ambiguous, so the unwrapped view is
  an extension method rather than an interface.
- **An insert that returns a C++ iterator now returns the value it points at.** The extractor maps
  `QJsonObject::insert`/`QJsonArray::insert` to `QJsonValue` (the glue calls `insert(...).value()`)
  instead of dropping the return; other iterator-returning members stay unpublished, and a
  non-iterator unsupported return is still a void call.

## 0.9.0 - 2026-10-26

- **QJsonObject is a C# dictionary now.** It implements `IReadOnlyDictionary<string, object>`, so
  `foreach`, `TryGetValue` and LINQ work over a document; values go through `QVariant.ToObject()`
  (nested objects as `Dictionary<string, object>`, arrays as `object[]`), and keys enumerate in
  Qt's sorted order. `test-json` covers it. The interface is read-only: `insert`/`operator[]`
  return an iterator the model does not publish, so `IDictionary` waits for that fix.

## 0.8.0 - 2026-10-26

- **QtCore has official examples in the gate now.** `qtcore-savegame` ports the JSON Save Game
  example's document model (parse, nested reads, a `toJson` round trip) and `qtcore-regexp` ports
  the regexp example's global match/capture walk, so the module no longer relies on `test-core`
  alone for conformance.
- `QJsonObject` is read-only in the binding for now: `insert` and `operator[]` return an iterator,
  which the extractor does not publish, so a document is built from text (or replaced wholesale)
  rather than field by field.

## 0.7.0 - 2026-10-26

- **QtMultimedia is bound**, as the separate `QtSharp.Multimedia` package: playback
  (`QMediaPlayer`, `QAudioOutput`, `QAudioSink`), capture (`QCamera`, `QMediaCaptureSession`,
  `QImageCapture`, `QMediaRecorder`), the audio device/format and media format/metadata classes,
  and the widget half (`QVideoWidget`, `QGraphicsVideoItem`). `QtSharp.Multimedia.Qt6` carries the
  two libraries and the `multimedia` backend plugin.
- **`qtmultimedia-player`** ports the core of Qt's player example without needing a sound card: it
  writes a one-second PCM WAV, loads it through `QMediaPlayer` (ffmpeg backend), and asserts the
  media status, duration, audio track and the video widget's sink.
- Anonymous enums are no longer published: `QGraphicsVideoItem`'s `enum { Type = 14 }` came back
  from clang as `(unnamed enum at ...)`, which is not a C# type name.

## 0.6.0 - 2026-10-26

- **QtSql rows are C# sequences now.** `QSqlQuery.Rows()` yields one `Dictionary<string, object>`
  per row (values unwrapped through `QVariant.ToObject()`), `SelectRows` projects typed rows, and
  `QSqlRecord.Get<T>` does the conversion SQLite's `LongLong` needs. `QSqlQueryModel.Rows()` gives
  the same shape for a model. This is LINQ to Objects over the rows Qt returned: there is no
  `IQueryable` provider translating expressions to SQL and no ADO.NET bridge. `test-qtsql` covers
  the whole path in the gate.

## 0.5.0 - 2026-10-26

- **Invoking an `[QtSignal]` event is now an emit.** `ValueChanged?.Invoke(value)` in the declaring
  class activates the Qt signal, so `connect()`, QML and C++ see it, instead of only running the
  C# handlers. The runtime seeds an emit trampoline into the event's handler field and keeps its
  own connection out of that emission, so every handler still runs exactly once.
  `EmitSignal(ValueChanged, value)` remains as the explicit spelling. The trampoline makes the
  event non-null, so `?.Invoke` emits even with no subscriber, and `== null` no longer means
  "no handlers".
- **QtWebSockets is bound**, as a separate `QtSharp.WebSockets` package against `QtSharp.Api`:
  `QWebSocket`, `QWebSocketServer`, the CORS authenticator, the handshake options and the
  protocol enums. `QtSharp.WebSockets.Qt6` carries `Qt6WebSockets.dll`, the way the Charts and Qt3D
  packages carry their runtimes.
- **`qtwebsockets-echo`** runs Qt's WebSockets "echoserver" example against its own client over
  loopback: connect, text echo, a binary frame and a clean close, all asserted in one process.

## 0.4.0 - 2026-10-26

- **QtSql is bound**, as the eleventh base module of `QtSharp.Api`: `QSqlDatabase`/`QSqlDriver`,
  `QSqlQuery`/`QSqlResult`, the `QSqlRecord`/`QSqlField`/`QSqlIndex`/`QSqlError` value classes and
  the three table models. The Qt runtime payload now ships `Qt6Sql.dll` and the SQL driver plugins
  under `sqldrivers\`, so a consumer can open a SQLite database without deploying Qt itself.
- **`qtsql-querymodel`** ports Qt's SQL "Query Model" example: the `person` table built through
  prepared statements, a `QSqlQueryModel` shown in a `QTableView`, and a `QSqlTableModel` edit
  committed through `submitAll`.
- The generator now emits the container helper types used by a class's *inherited* virtuals:
  `QSqlQueryModel` dispatches `QAbstractItemModel::itemData`/`mimeData`, and those signatures use
  helpers that so far only existed inside QtCore's assembly.

## 0.3.0 - 2026-10-26

- **The binding is generated from Qt 6.12.0.** The checked-in model was re-extracted from the
  6.12.0 `msvc2022_64` kit, so the assemblies, the native glue and the packages now carry 6.12's
  API: `QSortFilterProxyModel::DataChangeRelevanceFlags`, `QStyleHints::toolTipWakeUpDelay`,
  `QImageReader::effectiveSize`, `QTreeView::itemDecorationRect`, `QMetaType::rawId`,
  `QEasingCurve::fromCubicBezier`, `QHostInfo::clearCache`, `QDialogButtonBox`'s
  `standardButtonText`/`standardButtonShortcut`, and the `QScatterSeries` signals that Qt now
  declares for the property move to `QXYSeries`.
- **The seven addon models are extracted by the gate now.** `regen-api.ps1` step `[2b]` extracts
  the Charts and Qt3D models from their own headers and compares them to the checked-in files
  byte for byte, exactly like the combined base model; previously they were checked in but never
  re-extracted, so a Qt upgrade could leave them behind.
- **The header scanners unwrap `QT6_ONLY(...)`/`QT7_ONLY(...)`.** Qt 6.12 wraps declarations that
  differ between Qt 6 and Qt 7 in those macros (`QScatterSeries::colorChanged` is one), and the
  Q_PROPERTY/signal scanners read the raw header text, so the wrapper made them invisible.
- **The model files are pinned to LF** in `.gitattributes`. The model gate compares bytes, and
  `core.autocrlf=true` was checking the checked-in models out as CRLF, which made the gate report
  a model change on every Windows checkout.
## 0.2.0 - 2026-10-24

- **The standalone `QtSharp.Charts` repository was merged in.** Its 38-example conformance suite
  (the official Qt Charts examples, 570 assertions) now lives in the main repository as
  `demos.charts/` and runs in the gate, CI and release against the separate `QtSharp.Charts`
  assembly. Charts' model, native glue and package had already been built here; the second
  repository is now redundant.
- **The demos are now only the official Qt examples, grouped and named by module.** `demos/`
  contains the 28 ported examples under `QtGui/`, `QtWidgets/`, `QtSvg/`, `QtSvgWidgets/`,
  `QtXml/`, `QtPrintSupport/` and `QtNetwork/`, and both the files and the `--list` names say which
  module they exercise (`qtgui-basicdrawing`, `qtwidgets-analogclock`, …).
  The Qt Charts examples are `qtcharts-*`; Qt3D keeps `qt3d-*`.
- **Binding-specific tests moved to `tests/`.** Ownership, the exception boundary, C#-declared
  signals/properties, value types, the QtCore tour, network transports, document formats, managed
  models and the graphics-view tour are still run by the gate, but under `test-*` names instead of
  pretending to be official demos. The redundant API smoke test and the widget tour (covered by the
  official gallery) were removed.
- Generated Q_PROPERTY wrappers now carry `[QtNativeProperty(name, type, writable)]` and generated
  signals (the `QSignal` property and the C# event) carry `[QtNativeSignal(mocSignature)]`. They
  are the read-side markers for members whose Qt metadata already exists, as opposed to the
  declaration-side `[QtProperty]`/`[QtSignal]` a C# subclass uses; `test-metaobject` asserts both.
- **BCL conversions for the Qt value types with a natural C# counterpart**, following PyQt's
  explicit `toPy...`/constructor model rather than replacing Qt types in signatures: QPoint/
  QPointF/QSize/QSizeF/QRect/QRectF <-> System.Drawing, QColor <-> System.Drawing.Color,
  QVector2D/3D/4D <-> System.Numerics.Vector2/3/4, QQuaternion <-> System.Numerics.Quaternion,
  QUrl <-> System.Uri, QUuid <-> System.Guid, QByteArray <-> byte[], plus `QVariant.ToObject()`
  and `QVariant.From(object)` (the QVariant.value() equivalent). The existing QDateTime/QDate/QTime
  conversions are unchanged. A new `test-bclinterop` exercises them in the gate.
- The IDE view is clean now that the module projects link their generated and runtime sources under
  `Generated/` and `Runtime/` virtual folders instead of listing every out-of-project path, and the
  package projects hide their payload DLLs (`Visible=false`, still packed). Generated files that
  would contain no declarations (a module with no namespace-level enums, for example) are not
  written at all.
- The demo/test projects now reference the ten base assemblies instead of compiling the generated
  binding into QtSharp.Demos. The public API is exercised across the same assembly boundaries the
  package ships, and the IDE no longer lists the generated `out/**/*.cs` as files of the demo
  project.
- Added the remaining feasible official example ports as module-named demos:
  `qtsvg-svgviewer`, `qtsvgwidgets-svgwidget`, `qtxml-dombookmarks`,
  `qtprintsupport-notepad` and `qtnetwork-fortune`. OpenGL remains deferred because
  `QOpenGLFunctions` is not bound and CI has no guaranteed GL context.
- The repository builds with `TreatWarningsAsErrors` (`Directory.Build.props`). The generated
  `QPrinter.PdfVersion()` now says `new` where it hides `QPagedPaintDevice.PdfVersion` (the
  generator adds nested enum names to the inherited-member table), and the GitHub Actions were
  upgraded past the Node 20 deprecation line.
- The demo projects are named consistently - `QtSharp.Demos`, `QtSharp.Charts.Demos`,
  `QtSharp.Qt3D.Demos` - and `QtSharp.slnx` groups the generator, runtime packages, base modules,
  addons, demos, tests and samples into solution folders instead of one flat list.
- Demo registration is attribute-based: `[Demo("qtwidgets-analogclock")]` on a static class with
  `Run(QApplication)`, discovered by each catalog. The hand-maintained runner/name tables are
  gone; duplicate or malformed registrations fail at startup.
- `samples/hello` was removed. `testbind` is the one entry point; consumers start from the package
  reference in the README, and the Qt3D/resource samples remain.
- **The whole binding is split the way PyQt6 is.** `QtSharp.Api` now contains ten assemblies, one
  per Qt module and one C# namespace each: `QtSharp.QtCore`, `QtSharp.QtGui`, `QtSharp.QtWidgets`,
  `QtSharp.QtNetwork`, `QtSharp.QtSvg`, `QtSharp.QtSvgWidgets`, `QtSharp.QtPrintSupport`,
  `QtSharp.QtXml`, `QtSharp.QtOpenGL`, `QtSharp.QtOpenGLWidgets`. A type owned by another module is
  emitted fully qualified, so the namespaces cannot collide.
- **New runtime and companion packages**, matching PyQt6's dependency graph:
  `QtSharp.Runtime` (managed handles/signals/marshalling + the shared native `QtSharp.Api.dll`
  glue), `QtSharp.Qt6` (the Qt base runtime DLLs, MSVC runtime and platform plugin),
  `QtSharp.Charts.Qt6` and `QtSharp.Qt3D.Qt6` (each addon's Qt runtime and plugins). Consumers
  reference only `QtSharp.Api`, `QtSharp.Charts` or `QtSharp.Qt3D`; the companions flow as NuGet
  dependencies and copy their payload through `buildTransitive`.
- `qtgen split-core` derives one model per base Qt module from the combined `qtsharp.api.json`.
  The ten split models are checked in and are what the assemblies are generated from; the combined
  model remains the extraction gate and produces only the shared native glue
  (`qtgen codegen --native-only`). The splitter assigns classes by header, enums by the module
  that declares them, and fails on a dependency cycle instead of silently emitting one.
- The runtime is split along the same assembly lines: `runtime/ApiRuntime.cs` (Qt-type-free) builds
  `QtSharp.Runtime`, while the `QObject` signal half, `QtSignals`, the BCL conversions and the
  command-line constructors live in `runtime/QtCore`, `runtime/QtGui` and `runtime/QtWidgets`,
  beside the generated classes they extend. A partial class cannot span assemblies.
- The generator no longer has any addon-specific branch. All module rows (the ten base modules,
  Charts and Qt3D's six) live in `gen/ApiModel.Modules.cs`; the libclang front end moved to
  `ClangExtractor.cs`.
- Demos, samples, CI and release moved to the module namespaces and now pack, upload and verify all
  seven packages; `tools/regen-api.ps1` gates the split models and codegen determinism per module,
  and `tools/pack-qt-binding.ps1` inspects every package's payload.

- Added the `QtSharp.Qt3D` extension package: 230 classes from Qt3DCore, Qt3DRender, Qt3DInput,
  Qt3DLogic, Qt3DAnimation and Qt3DExtras, as **six assemblies with six namespaces**
  (`QtSharp.Qt3DCore` … `QtSharp.Qt3DExtras`) in one package, mirroring Qt's module split and the
  `PyQt6-3D` / `PySide6-Addons` package shape. The payload carries the six native glues, the Qt3D
  runtime DLLs and the renderer/geometry/scene plugins.
- Extension modules can now span several Qt include directories and defines, and can bind classes
  that live in a C++ namespace (Qt3DCore::QEntity). The extractor identifies every class, base and
  signature type by its fully qualified C++ name; the class table carries the C++ spelling for the
  native glue. Namespace-scope declarations of the module's *own* headers are flattened into the
  extension's C# namespace (Qt3DRender::API becomes `API`); core declarations such as
  `Qt::Alignment` keep their namespace because they resolve in the core assembly. Nothing in the
  generator branches on a module's name.
- `tools/build-qt-kit.ps1` describes the addons as a table (marker header, include/lib/bin
  patterns, runtime modules, plugin directories) and merges them generically; adding an extension
  module is a row, not another copy of the merge code.
- Ported Qt's official C++ Qt3D examples as the extension's conformance suite:
  `qt3d-simple` (simple-cpp), `qt3d-basicshapes` (basicshapes-cpp) and `qt3d-text` (3d-text),
  run by `tools/run-window-tests.ps1` exactly like the core demos.
- `QtSharp.Charts` is now packable with the same `buildTransitive` native-payload handling as the
  core and Qt3D packages; CI/release build and upload the addon packages alongside the core.
- Fixed the Q_PROPERTY scanner for a type whose template arguments contain spaces
  (`Q_PROPERTY(QList<QSpriteSheetItem *> sprites …)`), which it read as a property named `>`, and
  for declarations with a leading space after the parenthesis (`Q_PROPERTY( bool hasTopEndcap …)`),
  which it read as properties named `Bool`/`Float`/...
- `tools/build-qt-kit.ps1` now fetches the Qt3D addon along with qtbase/qtsvg/QtCharts, and copies
  its runtime DLLs and plugins.
- The generated C# binding is now split one file per Qt header
  (`out/<module>/generated/<Module>/<header>.cs`), mirroring the C++ include layout instead of one
  9 MB / 194k-line source file. The determinism gate hashes the generated tree. The native C++
  glue stays a single translation unit.
- Generated wrapper types now carry `[GeneratedCode("qtgen", …)]`, and their XML docs spell out
  each Q_PROPERTY's Qt name/type/access and each signal's Qt signature. `[QtProperty]`/`[QtSignal]`
  remain only for members a C# subclass declares, which is where they have runtime meaning. A
  generated type is `partial` only when `runtime/*.cs` extends it (read from those sources, not a
  second list), and `unsafe` is confined to the members that use pointers or function pointers.
- The demo windows now run several at a time: `tools/run-window-tests.ps1` starts them in a pool
  (default `min(6, cores)`, `-Jobs` to change), keeps each window's output attributable, and
  retries the known teardown flake once. The gate, CI and the release job all use it.
- Added `[QtProperty]` and `[QtSignal]` declarations: an attributed C# property or field-like event
  becomes a Qt property or signal. They are the declaration mechanism (the old static
  `QMetaProperty<>`/`QMetaSignal<>` fields are gone). `[QtProperty]` carries the rest of
  `Q_PROPERTY`: `Notify` (inferred as `<Property>Changed` when omitted), `Reset` (inferred as
  `Reset<Property>`), `Constant`, `Final`, `Required`, `Designable`, `Scriptable`, `Stored`,
  `User` and `Revision`.
- Added the `QtSharp.Api` binding package: reference it and write Qt applications in C# with no Qt
  SDK or C++ toolchain installed. It ships the managed binding, the native wrapper and the Qt
  runtime, and copies them to the build and publish output.
- Added `tools/build-qt-kit.ps1`, which fetches and discovers the Qt dev files and runtime DLLs
  from Qt's own repository on a machine with nothing Qt installed.
- CI now builds the binding package from the checked-in model on every push.
- Added a tag-driven GitHub Release workflow for `QtSharp.Tools`, including a SHA-256 checksum.
- Added the `QtSharp.Tools` development package with Qt command-line tools.
- Added MSBuild `QtResource` support for compiling `.qrc` files to `.rcc` and copying resources
  during build and publish.
- Added the resources integration sample.
- Added repository governance files, contribution guidance, and CI scaffolding.

## 0.1.0

- Initial development package metadata for `QtSharp.Tools`.
