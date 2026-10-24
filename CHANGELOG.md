# Changelog

All notable changes to QtSharp will be documented here.

The project is pre-1.0, so APIs and package layouts may change between releases.

## Unreleased

- The generated C# binding is now split one file per Qt header
  (`out/api/generated/<Module>/<header>.cs`), mirroring the C++ include layout instead of one
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
