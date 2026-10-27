# Building and testing QtSharp

The binding is generated from checked-in API models, compiled into C# assemblies and native
libraries, then checked with Qt example ports and binding tests. To build an application from
release packages, use the [quick start](../README.md#run-your-first-application).

## Prerequisites

| Requirement | Windows x64 | Linux x64/glibc |
|---|---|---|
| Managed toolchain | .NET 10 SDK | .NET 10 SDK |
| Native toolchain | MSVC with the Visual Studio C++ workload | g++ or clang++ with C++17 support |
| Qt development kit | Qt 6.12.0, MSVC 2022 64-bit kit | Qt 6.12.0, gcc_64 kit |
| Kit staging tools | 7-Zip; the fetcher uses curl or PowerShell downloads | curl, 7z and patchelf |
| Build orchestration | Windows PowerShell or PowerShell 7 | PowerShell 7 |
| Header extraction | clang resource headers; Windows only | Uses the checked-in models |

Qt kits, native libraries, generated code and build outputs are not committed. The first
generator build also restores ClangSharp and its host-specific runtime package from NuGet.

## Windows

From the repository root, fetch and stage the Qt kit:

```powershell
powershell -File tools/build-qt-kit.ps1 -QtVersion 6.12.0
```

The default destination is `out/qt-kit`: `kit` contains headers and libraries, and `runtime`
contains the DLLs and platform plugin. The script prints `QT_DIR` and `QT_RUNTIME_DIR` paths.
It can also use an existing kit with `-KitDir C:/Qt/6.12.0/msvc2022_64`.

Configure the paths in the shell running the build. For the default staging layout:

```powershell
$env:QT_DIR = (Resolve-Path out/qt-kit/kit).Path
$env:QTSHARP_QT_INCLUDE = Join-Path $env:QT_DIR 'include'
$env:QtRuntimeDir = (Resolve-Path out/qt-kit/runtime).Path
powershell -File tools/regen-api.ps1 -SkipExtract
```

`-SkipExtract` builds from the checked-in models. To run header extraction too, place clang's
resource headers under `lib/lib/clang/<version>/include`, including `stddef.h`, then omit
`-SkipExtract`. A different Qt kit or clang resource version may change the extracted model;
review those differences before adopting them.

The native build locates Visual Studio automatically. `VCVARS` overrides the path to
`vcvarsall.bat`; `VCVARS_VER` selects a toolset version. See [build_api.bat](../build_api.bat).

## Linux

From the repository root:

```bash
./tools/build-qt-kit.sh
export QT_DIR="$PWD/out/qt-kit/kit"
pwsh ./tools/regen-api.ps1
```

The fetcher stages headers and libraries under `out/qt-kit/kit`. Its runtime layout is:

```text
out/qt-kit/runtime/
  lib/
  plugins/
  qt.conf
```

It adjusts library and plugin rpaths with `patchelf` for this deployment layout. Use
`--kit-dir /path/to/gcc_64` to stage an existing kit instead of downloading one.
`QtSharp.Api.props` uses `out/qt-kit/runtime` by default on Linux; `QtRuntimeDir` overrides it.
`CXX` selects a different native compiler.

Applications need Qt's system runtime dependencies: X11/XCB libraries, xkbcommon, OpenGL/EGL,
DBus and fonts. On Ubuntu these include `libxcb-*`, `libxkbcommon-x11-0`, `libgl1`, `libegl1`
and `libdbus-1-3`. Install the libraries required by your Qt kit and display environment.
Multimedia playback also requires the matching FFmpeg runtime libraries; see
[deployment limitations](limitations.md).

Header extraction is skipped on Linux because the checked-in models describe the MSVC kit.
The generated C++ is compiled with the Linux toolchain. GUI tests run under `xvfb-run` in CI;
OpenGL tests use software rendering where needed.

## Generation and verification

The [regeneration script](../tools/regen-api.ps1) performs these steps:

1. Build the generator.
2. On Windows, extract the base and addon models and compare them with the checked-in files,
   unless `-SkipExtract` is set.
3. Generate the module sources and compare repeated generation for determinism. Extraction
   runs also verify the split base models.
4. Compile the native wrappers and managed assemblies.
5. Build and run the example and binding-test executables.

Use `-Adopt` only after reviewing intentional model changes. `-Jobs` controls parallel work;
`-Jobs 1` runs the tasks serially. Logs and generated files are written under `out/`.
Edit the generator, model configuration or handwritten runtime rather than generated files.

```text
Qt headers -> ClangSharp/libclang -> model/qtsharp.*.json
                                          | code generation
                                          +-> C# module sources
                                          +-> C++ wrappers
```

The shared base-module native glue is `out/api/QtSharp.Api.native.cpp`. Each module has a
generated C# tree under `out/<module>/generated`; addons have their own native glue libraries.
See [module architecture](module-architecture.md) for assembly boundaries and model ownership.

## Running examples and tests

After generation and native compilation, the projects can be built normally:

```text
dotnet build demos/QtSharp.Demos.csproj -c Release
dotnet build testbind/testbind.csproj -c Release
```

Keep the path configuration from the setup steps when running these builds. On Windows:

```powershell
./testbind/bin/Release/net10.0/testapp.exe --list
./testbind/bin/Release/net10.0/testapp.exe qtwidgets-calculator
./testbind/bin/Release/net10.0/testapp.exe test-ownership
```

On Linux, omit `.exe`. Use `xvfb-run -a` when a test needs a display on a headless machine.
`QT_QPA_PLATFORM=offscreen` is suitable for some non-rendering tests, but not the full suite:
some scenarios check rendered pixels or require the Windows platform plugin.

`QTSHARP_DEMO_MS` changes how long a demonstration remains open. The test runner lists each
scenario's result and retries a failed process once. See [conformance coverage](conformance.md)
for the example and test groups.

## Packaging and releases

[pack-qt-binding.ps1](../tools/pack-qt-binding.ps1) creates and checks platform-specific
packages. [test-packages.ps1](../tools/test-packages.ps1) restores them into a consumer project
to check dependency resolution and native deployment.

The [release workflow](../.github/workflows/release.yml) builds packages for `win-x64` and
`linux-x64`. Each release attaches a package archive for each platform, `QtSharp.Tools` for
Windows and `SHA256SUMS.txt`. See [CONTRIBUTING.md](../CONTRIBUTING.md#releases) for release
versioning and workflow commands.

## Repository layout

| Path | Purpose |
|---|---|
| `gen/`, `model/` | Generator and checked-in API models |
| `native/`, `runtime/` | Handwritten native and managed runtime code |
| `nuget/` | Binding, runtime and optional module package projects |
| `demos/`, `demos.charts/`, `demos.qt3d/` | Qt example ports |
| `tests/`, `testbind/` | Binding tests and the main test dispatcher |
| `samples/hello/`, `samples/compatibility/` | Release-package consumer examples |
| `tools/` | Build, staging, packaging and test scripts |
