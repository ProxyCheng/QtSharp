# Changelog

All notable changes to QtSharp will be documented here.

The project is pre-1.0, so APIs and package layouts may change between releases.

## Unreleased

- **The standalone `QtSharp.Charts` repository was merged in.** Its 38-example conformance suite
  (the official Qt Charts examples, 570 assertions) now lives in the main repository as
  `demos.charts/` and runs in the gate, CI and release against the separate `QtSharp.Charts`
  assembly. Charts' model, native glue and package had already been built here; the second
  repository is now redundant.
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
