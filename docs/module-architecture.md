# Module and package architecture

QtSharp binds selected Qt modules. Bound modules follow Qt's assembly and namespace grouping;
features already well served by .NET can use managed implementations and adapters instead.

| project | package shape |
|---|---|
| **Qt (C++)** | one library, one header directory and one CMake target per Qt module (`Qt6::3DCore`, `Qt6::3DRender`, …); the installer ships a Qt addon (QtCharts, Qt3D) as one download |
| **PyQt6** | `PyQt6` (the binding modules) + `PyQt6-Qt6` (the Qt runtime) + `PyQt6-sip` (the binding runtime); each addon is its own package (`PyQt6-Charts`, `PyQt6-3D`) with a companion `PyQt6-<addon>-Qt6` runtime package |
| **PySide6** | `PySide6` meta-package → `PySide6-Essentials` + `PySide6-Addons`, plus `shiboken6` (runtime); each Qt module is a separate importable extension module inside those wheels |

QtSharp follows the **PyQt6 shape**: one package for the base binding, one package per addon, one
companion package for each addon's Qt runtime, a binding-runtime package, and **one assembly / C#
namespace per Qt module**.

## Packages

| package | contents | depends on |
|---|---|---|
| `QtSharp.Runtime` | `QtSharp.Runtime.dll` (handles, signals, attributes, marshalling; no Qt types) and the shared native glue `QtSharp.Api.dll` | — |
| `QtSharp.Qt6` | the Qt 6 base runtime DLLs (`Qt6Core`, `Qt6Gui`, `Qt6Widgets`, …, `qwindows.dll`) | — |
| `QtSharp.Api` | nine assemblies: `QtSharp.QtCore`, `QtSharp.QtGui`, `QtSharp.QtWidgets`, `QtSharp.QtNetwork`, `QtSharp.QtSvg`, `QtSharp.QtSvgWidgets`, `QtSharp.QtPrintSupport`, `QtSharp.QtOpenGL`, `QtSharp.QtOpenGLWidgets` | `QtSharp.Runtime`, `QtSharp.Qt6` |
| `QtSharp.Charts` | `QtSharp.Charts` assembly + `QtSharp.Charts.Native.dll` | `QtSharp.Api`, `QtSharp.Charts.Qt6` |
| `QtSharp.Charts.Qt6` | `Qt6Charts.dll` | — |
| `QtSharp.Qt3D` | six assemblies: `QtSharp.Qt3DCore`, `QtSharp.Qt3DRender`, `QtSharp.Qt3DInput`, `QtSharp.Qt3DLogic`, `QtSharp.Qt3DAnimation`, `QtSharp.Qt3DExtras`; six native glues | `QtSharp.Api`, `QtSharp.Qt3D.Qt6` |
| `QtSharp.Qt3D.Qt6` | `Qt63D*.dll`, `Qt6Concurrent`, `Qt6ShaderTools`, renderer/geometry/scene plugins | — |
| `QtSharp.Tools` | Qt command-line tools and MSBuild `.qrc` support | — |

QtMultimedia and QtPositioning also have binding/runtime package pairs. `QtSharp.Sql` is
an ADO.NET implementation of the Qt SQL model APIs; it does not bind QtSql or require a Qt SQL
runtime package. QtQml, QtQuick, QtPdf and QtWebEngine are not currently bound.

Platform packages have a `.win-x64` or `.linux-x64` suffix; the table uses their base names.
On Linux the base `QtSharp.Qt6` package deploys the Qt runtime tree, including addon libraries
and plugins. `QtSharp.Tools` is Windows x64 only and has no platform suffix.

## Module selection and managed adapters

QtXml, QtSql and QtWebSockets are intentionally omitted. XML uses `System.Xml`, database
access uses ADO.NET providers, and WebSocket clients use `ClientWebSocket`. They are not
missing bindings waiting to be generated.

When another QtSharp component needs those facilities, a managed adapter can implement the
Qt-facing interface while using a .NET library internally. Existing examples are:

- [`QtSharp.Sql`](../nuget/QtSharp.Sql/README.md): ADO.NET query results and edits exposed
  through `QAbstractTableModel`-based query/table models for Qt views.
- [`StreamIODevice`](../runtime/QtCore/IoDeviceInterop.cs): a .NET `Stream` exposed through
  `QIODevice`, so Qt image loaders and other IO consumers can use it.
- [XML interoperability tests](../tests/XmlSeamApi.cs): documents passed between `System.Xml`
  and QtCore's `QXmlStreamReader`/`QXmlStreamWriter`.

Further adapters can be added as integration needs arise. There is no Qt-facing WebSocket
adapter at present. QtQml/QtQuick, QtPdf and QtWebEngine are also unbound, but are separate
coverage gaps rather than the .NET substitutions described here.

## Assemblies in the base binding

| assembly / namespace | Qt module | builds on |
|---|---|---|
| `QtSharp.QtCore` | QtCore | — |
| `QtSharp.QtGui` | QtGui | QtCore |
| `QtSharp.QtWidgets` | QtWidgets | QtGui, QtCore |
| `QtSharp.QtNetwork` | QtNetwork | QtCore |
| `QtSharp.QtSvg` | QtSvg | QtGui, QtCore |
| `QtSharp.QtSvgWidgets` | QtSvgWidgets | QtWidgets, QtGui, QtSvg, QtCore |
| `QtSharp.QtPrintSupport` | QtPrintSupport | QtWidgets, QtGui, QtCore |
| `QtSharp.QtOpenGL` | QtOpenGL | QtGui, QtCore |
| `QtSharp.QtOpenGLWidgets` | QtOpenGLWidgets | QtOpenGL, QtWidgets, QtGui, QtCore |

`Qt::` enums (`Qt.Alignment`, `Qt.Key`, …) belong to `QtSharp.QtCore`; every module that declares
free functions publishes its own static `Qt` class for them (qHash of a `QFont` is QtGui's), and a
program importing several module namespaces can alias the one it means. The type from another
module is always written fully qualified in generated C# (for example `QtSharp.QtGui.QColor`
inside `QtSharp.QtWidgets`), so no `using` collisions remain possible.

The value structs follow their Qt header: `QPoint`, `QPointF`, `QSize`, `QRect`, `QDate`, `QTime`
are in `QtSharp.QtCore`; `QColor`, `QVector2D/3D/4D`, `QQuaternion` are in `QtSharp.QtGui`.

## Module registry

Every module — base or addon — is one row in `gen/ApiModel.Modules.cs`:

```csharp
static ExtensionModule Qt3DRenderModule() => new ExtensionModule {
    Name        = "qt3drender",
    Assembly    = "QtSharp.Qt3DRender",
    Namespace   = "QtSharp.Qt3DRender",
    Package     = "QtSharp.Qt3D",
    NativeLibrary = "QtSharp.Qt3DRender.Native",
    QtModules   = new[] { "Qt3DRender" },
    DependsOn   = new[] { "qtcore", "qtgui", "qtopengl", "qt3dcore" },
    ExtraHeaders= new[] { "qrenderapi.h" },   // public enums no root includes
    Roots       = new[] { R("Qt3DRender::QCamera", "qcamera.h", "Qt3DRender"), … },
};
```

Base modules omit `Roots`: their classes come from the checked-in combined model, grouped by the
model's `header` (`QtCore/qobject.h` → `qtcore`). Their `Package` is `QtSharp.Api`, their native
library is the shared `QtSharp.Api` glue, and their `DependsOn` follows Qt's module dependency
graph.

Adding a module later means:

1. one registry row (base) or a family of rows (an addon with several Qt modules);
2. a model (`model/qtsharp.<name>.json`), extracted from its headers or produced by the core
   splitter;
3. a module project under `nuget/<package>/Modules/`, with its `DependsOn` as project references;
4. its runtime DLLs/plugins added to the companion `<package>.Qt6` project;
5. a row in the CI/release module list (or a list derived from `qtgen modules`).

No generator or build-script code is written per module.

## Models

* **Base binding.** `model/qtsharp.api.json` remains the checked-in extraction and the
  reproducibility gate. `qtgen split-core` writes one derived model per base module into
  `out/split-models/`; the nine `model/qtsharp.<module>.json` files are checked in and are what the
  assemblies are generated from. Classes are grouped by their header's module directory, enums by
  the module that *declares* them (recorded by the extractor, which is what distinguishes
  `Qt::BrushStyle` in `QtCore/qnamespace.h` from a class-scoped QtGui enum), and free functions by
  header. If the dependency graph shows a class whose base would live in a module the class does
  not build on (Qt's forwarding headers can make the directory misleading), the splitter fails
  loudly instead of emitting an assembly cycle.
* **Addons.** Each module has its own checked-in model (`qtsharp.charts.json`,
  `qtsharp.qt3dcore.json`, …), extracted directly from its headers. An addon model keeps its
  dependencies' enums and classes for type resolution; only its own classes/enums/functions are
  emitted by its assembly.
* Every public enum of a module is published, not only the ones its own signatures mention: a
  dependent module may name any of them. `ExtensionModule.ExtraHeaders` covers namespace-level
  enums in headers no root includes (Qt3DRender's `qrenderapi.h` declares `API`).
* The combined model produces **only the shared native glue** (`codegen --native-only`); the
  combined C# tree is not a build input and is not written.

## Code generation

* One codegen run per module, with its own output tree `out/<module>/generated/` and, for an
  addon, its own native glue `out/<module>/<Assembly>.native.cpp`. The base modules share the one
  glue `out/api/QtSharp.Api.native.cpp` generated from the combined model.
* `ClassModules` / `EnumModules` remember which assembly owns every type (including the enums an
  addon model carries from its dependencies); `CsTypeName` emits a local name for the current
  module and `QtSharp.<Owner>.<Name>` for another module, and enum defaults are rewritten the same
  way (`Qt.MatchExactly` becomes `QtSharp.QtCore.Qt.MatchExactly` in QtWidgets).
* Runtime sources:
  * `runtime/ApiRuntime.cs` → `QtSharp.Runtime` (handles, `QSignal`, attributes, the wrapper
    cache, marshalling; must not reference Qt types);
  * `runtime/QtCore/*` → `QtSharp.QtCore` (the `QObject` signal half, `QtSignals`, the BCL
    conversions, the `QCoreApplication` command-line constructor);
  * `runtime/QtGui/*`, `runtime/QtWidgets/*` → the `QGuiApplication` / `QApplication` constructors.
  A partial class cannot span assemblies, so each half lives where the generated class it extends
  is compiled.

## Projects

```
nuget/
  QtSharp.Runtime/                 managed runtime + shared native glue
  QtSharp.Qt6/                     Qt base runtime payload
  QtSharp.Api/                     pack project for the nine base assemblies
    Modules/QtSharp.QtCore/ …      one csproj per Qt module
  QtSharp.Charts/                  Charts assembly + native glue + pack
  QtSharp.Charts.Qt6/              Qt6Charts.dll
  QtSharp.Qt3D/                    pack project for six assemblies
    Modules/QtSharp.Qt3DCore/ …    one csproj per Qt3D module
  QtSharp.Qt3D.Qt6/                Qt3D runtime + plugins
```

The pack projects only collect `lib/net10.0` assemblies and `tools/native` payloads; the companion
`*.Qt6` packages own the Qt DLLs and use `buildTransitive` to copy them next to the consumer's
executable. `QtSharp.Api` and the addon packages depend on their runtime companions with ordinary
NuGet dependencies, so a consumer never lists the runtime packages by hand.

## Verification

`tools/regen-api.ps1` is the full gate: it re-extracts the combined model, checks the split models
are byte-identical, generates every module twice (determinism), builds the shared and per-addon
native glues, builds the base and addon assemblies, runs the official examples and
binding tests (including the Qt Charts and Qt3D conformance suites). `tools/pack-qt-binding.ps1` packs and
inspects the platform packages; a local feed consumer test then proves the dependency graph and the
`buildTransitive` payload.

## Historical migration stages

| stage | work | acceptance |
|---|---|---|
| **M0 (done)** | Qt3D split into six assemblies inside one `QtSharp.Qt3D` package | six assemblies build; official examples pass; package consumer runs and renders |
| **M1 (done)** | binding-module registry + `qtgen split-core` + base models | `qtsharp.api.json` extraction gate; split deterministic and checked in |
| **M2 (done)** | runtime split (`QtSharp.Runtime`) + base assemblies + `QtSharp.Api` pack project | core examples pass; a consumer references `QtSharp.Api` and runs |
| **M3 (done)** | `QtSharp.Qt6`, `QtSharp.Charts.Qt6`, `QtSharp.Qt3D.Qt6` companion packages | consumers get the right Qt DLLs/plugins; no duplicate payloads |
| **M4 (done)** | examples/packaging/CI/release move to the new namespaces/packages; docs; publish | full gate + CI + release produce all seven packages |
