# Module and package architecture

This document is the layout the binding follows as more Qt modules are added. It mirrors the
upstream projects rather than inventing a new grouping:

| project | package shape |
|---|---|
| **Qt (C++)** | one library, one header directory and one CMake target per Qt module (`Qt6::3DCore`, `Qt6::3DRender`, …); the installer ships a Qt addon (QtCharts, Qt3D) as one download |
| **PyQt6** | `PyQt6` (the binding modules) + `PyQt6-Qt6` (the Qt runtime) + `PyQt6-sip` (the binding runtime); each addon is its own package (`PyQt6-Charts`, `PyQt6-3D`) with a companion `PyQt6-<addon>-Qt6` runtime package |
| **PySide6** | `PySide6` meta-package → `PySide6-Essentials` + `PySide6-Addons`, plus `shiboken6` (runtime); each Qt module is a separate importable extension module inside those wheels |

QtSharp follows the **PyQt6 shape**: one package for the base binding, one package per addon, one
companion package for the addon's Qt runtime, and **one assembly / C# namespace per Qt module**.

## Packages

| package | contents | depends on |
|---|---|---|
| `QtSharp.Runtime` | `QtSharp.Runtime.dll` (handles, signals, marshalling; no Qt types) and the shared core native glue `QtSharp.Api.dll` | — |
| `QtSharp.Qt6` | the Qt 6 base runtime DLLs (`Qt6Core`, `Qt6Gui`, `Qt6Widgets`, …, `qwindows.dll`) | — |
| `QtSharp.Api` | ten assemblies: `QtSharp.QtCore`, `QtSharp.QtGui`, `QtSharp.QtWidgets`, `QtSharp.QtNetwork`, `QtSharp.QtSvg`, `QtSharp.QtSvgWidgets`, `QtSharp.QtPrintSupport`, `QtSharp.QtXml`, `QtSharp.QtOpenGL`, `QtSharp.QtOpenGLWidgets` | `QtSharp.Runtime`, `QtSharp.Qt6` |
| `QtSharp.Charts` | `QtSharp.Charts` assembly + `QtSharp.Charts.Native.dll` | `QtSharp.Api`, `QtSharp.Runtime`, `QtSharp.Charts.Qt6` |
| `QtSharp.Charts.Qt6` | `Qt6Charts.dll` | — |
| `QtSharp.Qt3D` | six assemblies: `QtSharp.Qt3DCore`, `QtSharp.Qt3DRender`, `QtSharp.Qt3DInput`, `QtSharp.Qt3DLogic`, `QtSharp.Qt3DAnimation`, `QtSharp.Qt3DExtras`; six native glues | `QtSharp.Api`, `QtSharp.Runtime`, `QtSharp.Qt3D.Qt6` |
| `QtSharp.Qt3D.Qt6` | `Qt63D*.dll`, `Qt6Concurrent`, `Qt6ShaderTools`, renderer/geometry/scene plugins | — |
| `QtSharp.Tools` | Qt command-line tools and MSBuild `.qrc` support | — |

Future addons follow the same pair: `QtSharp.Sql` + `QtSharp.Sql.Qt6`, `QtSharp.Multimedia` +
`QtSharp.Multimedia.Qt6`, `QtSharp.Pdf` + `QtSharp.Pdf.Qt6`, `QtSharp.WebEngine` +
`QtSharp.WebEngine.Qt6`, and so on. A module that lives in the base Qt runtime
(`QtSql`, `QtQml`, `QtQuick`, `QtTest`, …) is an assembly inside `QtSharp.Api` and needs no
companion runtime package beyond `QtSharp.Qt6`.

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
| `QtSharp.QtXml` | QtXml | QtCore |
| `QtSharp.QtOpenGL` | QtOpenGL | QtGui, QtCore |
| `QtSharp.QtOpenGLWidgets` | QtOpenGLWidgets | QtOpenGL, QtWidgets, QtGui, QtCore |

`Qt::` enums (`Qt.Alignment`, `Qt.Key`, …) belong to `QtSharp.QtCore`; the static `Qt` class lives
there. A type from another module is always written fully qualified in generated C# (for example
`QtSharp.QtGui.QColor` inside `QtSharp.QtWidgets`), so no `using` collisions remain possible.

The value structs follow their Qt header: `QPoint`, `QPointF`, `QSize`, `QRect`, `QDate`, `QTime`
are in `QtSharp.QtCore`; `QColor`, `QVector2D/3D/4D`, `QQuaternion` are in `QtSharp.QtGui`.

## Module registry

Every module — base or addon — is one row in `gen/ApiModel.Modules.cs`:

```csharp
new BindingModule {
    Name        = "qt3drender",
    Assembly    = "QtSharp.Qt3DRender",
    Namespace   = "QtSharp.Qt3DRender",
    Package     = "QtSharp.Qt3D",
    QtModules   = new[] { "Qt3DRender" },
    DependsOn   = new[] { "qt3dcore" },
    ExtraHeaders= new[] { "qrenderapi.h" },   // public enums no root includes
    Roots       = new[] { R("Qt3DRender::QCamera", "qcamera.h", "Qt3DRender"), … },
}
```

Base modules omit `Roots`: their classes come from the checked-in combined model, grouped by the
model's `header` (`QtCore/qobject.h` → `qtcore`). Their `Package` is `QtSharp.Api`, their native
library is the shared `QtSharp.Api` glue, and their `DependsOn` follows Qt's module dependency
graph.

Adding a module later means:

1. one registry row;
2. a model (`model/qtsharp.<name>.json`), extracted from its headers or produced by the core
   splitter;
3. a module project generated from the registry (`qtgen projects` / `tools/gen-projects.ps1`);
4. its runtime DLLs/plugins added to the companion `<package>.Qt6` project;
5. a row in the CI/release module list (or a list derived from `qtgen modules`).

No generator or build-script code is written per module.

## Models

* **Base binding.** `model/qtsharp.api.json` remains the checked-in extraction and the
  reproducibility gate. `qtgen split-core` writes one derived model per base module into
  `out/models/`: classes by their header's module directory, class-scoped enums to the owning
  class's module, `Qt::` enums to QtCore, free functions by header. Dependency models are loaded
  transitively by codegen, so a module model contains only what that module declares.
* **Addons.** Each module has its own checked-in model (`qtsharp.charts.json`,
  `qtsharp.qt3dcore.json`, …), extracted directly from its headers. An addon model keeps its
  dependencies' enums for type resolution, but only its own classes/enums/functions are emitted by
  its assembly.
* Every public enum of a module is published, not only the ones its own signatures mention: a
  dependent module may name any of them. `ExtensionModule.ExtraHeaders` covers namespace-level
  enums in headers no root includes (Qt3DRender's `qrenderapi.h` declares `API`).

## Code generation

* One codegen run per module, with its own output tree `out/<module>/generated/` and native glue
  `out/<module>/<Assembly>.native.cpp`.
* `ClassModules` / `EnumModules` remember which assembly owns every type; `CsTypeName` emits a
  local name for the current module, `QtSharp.Runtime`/`QtSharp.Api` qualified names where the
  base package is involved, and `QtSharp.<Owner>.<Name>` for a dependency module.
* Native glue is per module for addons. The base modules share the one core glue
  `QtSharp.Api.dll`, which ships in `QtSharp.Runtime`; their managed assemblies all P/Invoke it.
* Runtime sources:
  * `runtime/ApiRuntime.cs` → `QtSharp.Runtime` (IntPtr-based; must not reference Qt types);
  * `runtime/QtCore/*` → `QtSharp.QtCore` (QObject signal extensions, meta-object attachment,
    `BclInterop`, the `QCoreApplication` command-line constructor);
  * `runtime/QtGui/*`, `runtime/QtWidgets/*` → the `QGuiApplication` / `QApplication`
    command-line constructors.

## Projects

```
nuget/
  QtSharp.Runtime/                 managed runtime + shared native glue
  QtSharp.Qt6/                     Qt base runtime payload
  QtSharp.Api/                     pack project for the ten base assemblies
    Modules/QtSharp.QtCore/ …      one csproj per Qt module
  QtSharp.Charts/                  Charts assembly + native glue + pack
  QtSharp.Charts.Qt6/              Qt6Charts.dll
  QtSharp.Qt3D/                    pack project for six assemblies
    Modules/QtSharp.Qt3DCore/ …    one csproj per Qt3D module
  QtSharp.Qt3D.Qt6/                Qt3D runtime + plugins
  QtSharp.Tools/
```

The per-module csproj files are generated from the registry so adding a module stays a data
change. The pack projects only collect `lib/net10.0` assemblies and `tools/native` payloads.

## Migration stages

| stage | work | acceptance |
|---|---|---|
| **M0 (done)** | Qt3D split into six assemblies inside one `QtSharp.Qt3D` package | six assemblies build; official demos pass; package consumer runs and renders |
| **M1** | binding-module registry + `qtgen split-core` + ten base models | `qtsharp.api.json` extraction byte-identical; split deterministic; existing core demos unchanged |
| **M2** | runtime split (`QtSharp.Runtime`) + ten base assemblies + `QtSharp.Api` pack project | all 35 core demos pass; package consumer references `QtSharp.Api` and runs |
| **M3** | `QtSharp.Qt6`, `QtSharp.Charts.Qt6`, `QtSharp.Qt3D.Qt6` companion packages | package consumers get the right Qt DLLs/plugins; no duplicate payloads |
| **M4** | demos/samples/CI/release move to the new namespaces/packages; docs; publish | full gate + CI + release produce all packages |

Each stage keeps the combined core model byte-identical and the codegen deterministic. No stage
changes the C++ extraction input; only the managed grouping and packaging change.
