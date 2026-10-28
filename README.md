# QtSharp

[![CI](https://github.com/ProxyCheng/QtSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/ProxyCheng/QtSharp/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/ProxyCheng/QtSharp?display_name=tag)](https://github.com/ProxyCheng/QtSharp/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Target: Windows x64 · Linux x64](https://img.shields.io/badge/target-Windows%20x64%20%C2%B7%20Linux%20x64-0078D4.svg)](#prerequisites)

Qt for .NET, **generated from Qt's own headers** — no hand-written bindings, no runtime reflection
over Qt.

One libclang parse of the Qt headers produces a normalized API model (checked in, byte for byte
reproducible). A pure-function code generator turns that model into C# plus a C++ wrapper, and a
small runtime makes signals, virtual overrides and object ownership behave the way a C# program
expects. On top of that, 73 runnable demos — Qt's official examples plus tours of QtCore, QtNetwork
and the document modules — have been rewritten in C#, one executable per demo module, and run by
the repository's own gate.

```
Qt 6.12.0 headers (MSVC kit)
        │  libclang via ClangSharp — one translation unit, ~3 s, deterministic
        ▼
model/qtsharp.api.json                 ← checked in; the gate compares the fresh extraction to it
        │  qtgen split-core (a pure function of the model)
        ▼
model/qtsharp.qtcore.json …            ← nine checked-in base models, one per Qt module
        │  qtgen codegen (one run per module)
        ▼
out/<module>/generated/QtCore/qobject.cs …  ← the binding, one C# file per Qt header: 508 classes,
                                               C# properties, signals as events, nine assemblies
out/api/QtSharp.Api.native.cpp              ← one C++ subclass per bound class: virtual hooks, C ABI
        │  build_api.bat (cl.exe)
        ▼
out/api/QtSharp.Api.dll                     ← the shared native glue
        +
runtime/ApiRuntime.cs (QtSharp.Runtime)     ← QSignal, attributes, ownership, marshalling
runtime/QtCore, QtGui, QtWidgets            ← the QObject/app halves that belong to those modules
        │
        ▼
demos/<Module>/QtSharp.Demos.<Module>.csproj ← one executable per module: the official demos
demos/Charts/, demos/Qt3D/                    ← the extension modules' demo executables
demos/hosting/                                ← [Demo] discovery, DemoContext and DemoHost
tests/QtSharp.BindingTests.csproj             ← binding-specific tests (test-*)
tests/testapp/testapp.csproj                  ← the one test entry point
```

Current coverage: **483 classes / 9938 methods / 1901 virtuals / 516 enums / 834 Q_PROPERTY
properties / 475 signals / 68 namespace functions** (QtCore, QtGui, QtWidgets, QtNetwork, QtSvg,
QtSvgWidgets, QtPrintSupport, QtOpenGL, QtOpenGLWidgets). The gate runs 73 demos (**1544
assertions**) in ten executables — 32 official base-module examples plus the tours, 38 official Qt
Charts examples and the three official Qt3D examples — and 24 binding-specific tests (**779
assertions**).

Extension modules are separate assemblies and packages against the same runtime:

| module | package | contents |
|---|---|---|
| QtCharts | `QtSharp.Charts` | `QChart`, `QChartView`, the series, bars, pie and axis families |
| Qt3D | `QtSharp.Qt3D` | **230 classes** from Qt3DCore, Qt3DRender, Qt3DInput, Qt3DLogic, Qt3DAnimation and Qt3DExtras, as six assemblies/namespaces in one package |
| QtMultimedia | `QtSharp.Multimedia` | playback (`QMediaPlayer`, `QAudioOutput`, `QAudioSink`), capture (`QCamera`, `QMediaCaptureSession`, `QImageCapture`, `QMediaRecorder`), the device/format classes and `QVideoWidget`/`QGraphicsVideoItem` |
| QtPositioning | `QtSharp.Positioning` | `QGeoCoordinate` and the geo value types (rectangle/circle/path/polygon/shape, address, location), `QGeoPositionInfo`, `QGeoSatelliteInfo` and the position/satellite sources |
| QtSql (substitute) | `QtSharp.Sql` | `QSqlQueryModel`/`QSqlTableModel` re-implemented over ADO.NET; pass any `DbConnection` |

## Install the binding

If you just want to write Qt applications in C#, install the release package — there is nothing
else to set up. No Qt SDK, no Visual Studio C++ workload, no clone:

```xml
<ItemGroup>
  <!-- win-x64 or linux-x64: the package id carries the platform, and only that platform's
       Qt runtime is inside. -->
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.19.0" />
</ItemGroup>
```

```csharp
using QtSharp.QtWidgets;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var app = new QApplication(args);
        var window = new QWidget();
        window.WindowTitle = "Hello from C#";
        window.Resize(320, 120);
        window.Show();
        return app.Exec();
    }
}
```

The package carries the nine module assemblies and depends on `QtSharp.Runtime` (the managed
runtime and native wrapper) and `QtSharp.Qt6` (the Qt runtime); all three copy their payload next
to your executable on build and publish. One assembly and one C# namespace per Qt module, the way
PyQt6 is organized. Download the `.nupkg` from a release and add its folder as a
package source:

```powershell
dotnet add package QtSharp.Api --source .\downloads
```

The package is named `QtSharp.Api` deliberately: `QtSharp` on nuget.org is an unrelated,
long-abandoned project.

Qt 3D is a separate package, because it is an optional addon family of six Qt libraries rather
than part of qtbase. It references the core package and adds all six modules:

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.19.0" />
  <PackageReference Include="QtSharp.Qt3D.win-x64" Version="0.19.0" />
</ItemGroup>
```

```csharp
using QtSharp.QtGui;
using QtSharp.Qt3DCore;
using QtSharp.Qt3DRender;
using QtSharp.Qt3DExtras;

var view = new Qt3DWindow();
var root = new QEntity();
var cube = new QEntity(root);
cube.AddComponent(new QCuboidMesh());
cube.AddComponent(new QPhongMaterial());
view.SetRootEntity(root);
view.Show();
```

See `demos/Qt3D` for the official Qt3D examples, which run windowless in the gate.

## Project status

QtSharp is an active pre-1.0 project. The supported targets are Windows x64 and Linux x64 (glibc),
with Qt 6.12.x and .NET 10. The generated model, native wrapper, and runtime are under active
development, so binary and source compatibility can change between releases. Contributions,
reproducible bug reports, and new conformance demos are welcome; see
[CONTRIBUTING.md](CONTRIBUTING.md).

The repository's own generated binding is the primary validation surface. It is not yet a stable
drop-in replacement for every Qt module or every desktop deployment scenario.

## Releases

Pushing a semantic-version tag such as `v0.19.0` starts the release workflow. It builds every
package below once for `win-x64` and once for `linux-x64` - the id carries the RID
(`QtSharp.Api.win-x64`, `QtSharp.Api.linux-x64`, ...), and only that platform's Qt runtime and
native glue are inside. A GitHub Release has a flat asset list, so the release attaches one
archive per platform (`QtSharp-<version>-win-x64.zip`, `QtSharp-<version>-linux-x64.zip`), the
Windows-only `QtSharp.Tools` package and a `SHA256SUMS.txt`. Extract your platform's archive and
add the folder as a local NuGet source. The managed assemblies are identical in both variants.

Every row below is published as `<name>.win-x64` and `<name>.linux-x64`; reference the one that
matches the machine you build on.

| package | what it is |
|---|---|
| `QtSharp.Api` | the binding as nine module assemblies — add it and write Qt applications in C# |
| `QtSharp.Runtime` | the managed runtime + native glue (brought in by `QtSharp.Api`) |
| `QtSharp.Qt6` | the Qt 6 runtime for that platform — Windows DLLs + `qwindows.dll`, or the `lib/` + `plugins/` prefix and `qt.conf` on Linux (brought in by `QtSharp.Api`) |
| `QtSharp.Tools` | Qt command-line tools (`rcc`, `uic`, `lrelease`, `lupdate`) + MSBuild `.qrc` support — Windows x64 only |
| `QtSharp.Charts` | the optional QtCharts extension, against `QtSharp.Api` |
| `QtSharp.Charts.Qt6` | Qt6Charts.dll (brought in by `QtSharp.Charts`; on Linux the base `QtSharp.Qt6` already deploys the whole Qt tree) |
| `QtSharp.Qt3D` | the optional Qt3D extension (six modules), against `QtSharp.Api` |
| `QtSharp.Qt3D.Qt6` | the Qt3D runtime and plugins (brought in by `QtSharp.Qt3D`; Windows payload) |
| `QtSharp.Multimedia` | the optional QtMultimedia extension, against `QtSharp.Api` |
| `QtSharp.Multimedia.Qt6` | Qt6Multimedia.dll, Qt6MultimediaWidgets.dll and the backend plugin (brought in by `QtSharp.Multimedia`; Windows payload) |
| `QtSharp.Positioning` | the optional QtPositioning extension, against `QtSharp.Api` |
| `QtSharp.Positioning.Qt6` | Qt6Positioning.dll (brought in by `QtSharp.Positioning`; Windows payload) |
| `QtSharp.Sql` | the Qt model classes QtSql used to bind (`QSqlQueryModel`, `QSqlTableModel`) over ADO.NET — no Qt SQL module involved |

Everything is built on the runners from the checked-in models: the generator runs without clang,
the Qt dev files (including the QtCharts, Qt3D, QtMultimedia and Qt Shader Tools addons) are
fetched from Qt's own repository, and the native wrappers are compiled with the platform's
toolchain — MSVC on Windows, g++ on Linux. CI runs the same steps on every push: a Windows job and
a Linux job build and test their own payload, a pack job combines the two into the multi-RID
packages, and a consumer restores them on both platforms. The official Qt3D examples are part of
the Windows gate, so the extension's conformance suite runs continuously.

The packages are distributed through GitHub Releases. Publishing to nuget.org is kept as a separate
future step so a public NuGet API key is never required for ordinary repository builds.

---

## Prerequisites

| | needed for | notes |
|---|---|---|
| **Windows x64** | the Windows build | the generated wrapper is MSVC C++ |
| **Linux x64 (glibc)** | the Linux build | the same wrapper compiled with g++; X11/GL system libraries to run |
| **.NET SDK 10** | everything | the projects target `net10.0` |
| **Visual Studio with the C++ workload** | `build_api.bat` | `cl.exe` and `vcvarsall.bat` |
| **g++ (or clang++)** | `build_api.sh` | `$CXX` selects the compiler; C++17 |
| **Qt 6.12.0** | native build + running | Windows: MSVC 2022 64-bit dev files **and** runtime DLLs; Linux: the `gcc_64` kit. The repository's fetchers download the QtCharts, Qt3D, QtMultimedia, QtPositioning and Qt Shader Tools addons too |
| **clang builtin headers** | extract only (Windows) | any clang version; the checked-in model used clang 19 |
| **patchelf, 7z** | the Linux build | `p7zip-full` for the Qt archives, `patchelf` for the deployment rpaths |
| **NuGet reachable** | first build | restores `ClangSharp` + `libClangSharp.runtime.<host>` |

Nothing Qt- or clang-shaped is committed (it is all in `.gitignore`), so a clone has to put those
three pieces somewhere. The defaults are the layout this repository was developed in; every one of
them can be pointed elsewhere instead:

| piece | default | override |
|---|---|---|
| Qt dev files (headers + lib) | Windows `<repo>\Qt\6.12.0\msvc2022_64`; Linux `<repo>/out/qt-kit/kit` | `QT_DIR`, `QTSHARP_QT_INCLUDE` (generator) |
| Qt runtime | Windows `<repo>\Qt\6.12.0\runtime` (DLLs + `platforms\qwindows.dll`); Linux `<repo>/out/qt-kit/runtime` (`lib/` + `plugins/` + `qt.conf`) | `QtRuntimeDir` in `QtSharp.Api.props` (a Linux runtime dir must include `qt.conf`, or set `QT_PLUGIN_PATH`) |
| clang builtin headers | `<repo>\lib\lib\clang\<version>\include` | `QTSHARP_CLANG_RESOURCE_DIR` |
| Visual Studio | found among the 2022 / 2019 / 18.x installs | `VCVARS` (a `vcvarsall.bat`), `VCVARS_VER` (pin a toolset) |

### Getting the pieces

* **Qt**: the repository's own fetcher does this — `tools\build-qt-kit.ps1 -QtVersion 6.12.0 -Dest <dir>`
  downloads the `msvc2022_64` archives from Qt's online repository (qtbase, qtsvg and the addons),
  merges the addon pieces into one kit and stages the runtime DLLs. Put the kit at
  `<repo>\Qt\6.12.0\msvc2022_64` and the runtime DLLs at `<repo>\Qt\6.12.0\runtime`, or set
  `QT_DIR`/`QTSHARP_QT_INCLUDE`/`QtRuntimeDir` to wherever they are. A kit from the official
  installer (`C:\Qt\6.12.0\msvc2022_64`) works too. The kit must contain the **private** headers
  (`include\QtCore\6.12.0\QtCore\private\...`) — the official binaries ship them.

  `aqt install-qt` has been unreliable for this repository: Qt's repository no longer serves the
  top-level `desktop/Updates.xml` that aqt reads the archive folder name out of, so it looks for
  `qt6_6120/qt6_6120/` instead of the real `qt6_6120/qt6_6120_msvc2022_64/`. Downloading
  `qtbase-…-X86_64.7z` (plus `d3dcompiler_47-x64.7z`) from
  `…/desktop/qt6_6120/qt6_6120_msvc2022_64/qt.qt6.6120.win64_msvc2022_64/` and unpacking it at the
  kit root gives the same result. `qtbase` covers QtCore/QtGui/QtWidgets/QtNetwork/QtPrintSupport/
  QtOpenGL; QtSvg and QtSvgWidgets come from the separate `qtsvg` module (add its
  `include`/`lib` the same way).
* **Qt runtime DLLs**: `<repo>\Qt\6.12.0\runtime` needs the modules the binding links —
  `Qt6Core.dll`, `Qt6Gui.dll`, `Qt6Widgets.dll`, `Qt6Network.dll`, `Qt6Svg.dll`,
  `Qt6SvgWidgets.dll`, `Qt6PrintSupport.dll`, `Qt6OpenGL.dll`,
  `Qt6OpenGLWidgets.dll` — plus the MSVC runtime DLLs (`msvcp140*.dll`, `vcruntime140*.dll`) and
  `platforms\qwindows.dll`. The easy way is to build any Qt widget app and run `windeployqt` on it.
  All of them must be the *same* Qt version (6.12.x) — they share `Qt6Core`.
* **Qt on Linux**: `tools/build-qt-kit.sh` does the same job for the `gcc_64` kit — it downloads
  the qtbase, qtsvg, ICU and addon archives from Qt's own repository (the same HTML-listing and
  7z mechanism the Windows fetcher uses; `aqt` cannot be used for the reason above), merges the
  addons into `<repo>/out/qt-kit/kit` and stages the deployment tree:

  ```
  out/qt-kit/kit       include/ + lib/ + plugins/       (QT_DIR)
  out/qt-kit/runtime   lib/*.so.6 + plugins/ + qt.conf  (QT_RUNTIME_DIR)
  ```

  It pins the deployment rpaths with `patchelf` (`$ORIGIN` on the libraries,
  `$ORIGIN/../../lib` on the plugins), so an application directory needs no `LD_LIBRARY_PATH`.
  `--kit-dir` points at an existing kit instead of downloading one. The script needs `curl`, `7z`
  (`p7zip-full`) and `patchelf`; the built application additionally needs the system X11/GL
  libraries Qt itself needs (`libxcb-*`, `libxkbcommon-x11-0`, `libgl1`/`libegl1`, `libdbus-1-3`,
  fonts). The staged runtime includes QtDeclarative (Qt6Qml/Qt6Quick, which QtMultimedia's ffmpeg
  backend links); for playback the machine also needs the distribution's FFmpeg runtime libraries
  (`libavcodec`, `libavformat`, `libavutil`, `libswresample`, `libswscale`).
* **clang builtin headers**: `<repo>\lib\lib\clang\<version>\include` (with `stddef.h` in it) — copy
  them out of an LLVM installation (`C:\Program Files\LLVM\lib\clang\19\include`) or from the
  `libclang.runtime.win-x64` NuGet package. Any version works; a different version from clang 19 may
  change a fact or two in the extracted model, which the model diff gate below will show you.
  Extraction is Windows-only by design: the model is the MSVC kit's, and a Linux checkout rebuilds
  everything from the checked-in model (`regen-api.ps1` skips the extraction steps off Windows).

---

## Build and verify

```
# Windows
powershell -File tools\regen-api.ps1

# Linux (PowerShell 7)
pwsh ./tools/regen-api.ps1
```

That is the whole thing (a clone's first run takes a few minutes — the native glue is a big
translation unit, and every module's demos and tests are built and run at the end). On Linux the
extraction steps are skipped (the model is the Windows one); everything else runs, including the
determinism gate, the g++ build and the demo/test suite. For a bare native build without the gate,
`./build_api.sh` and `tools/build-native-module.sh <module>` compile the glue directly. It runs, in order:

| step | what it does |
|---|---|
| `[1]` | builds the generator (`gen`) |
| `[2]` | extracts the model from the Qt headers into `out\qtsharp.api.json.new` (**~3 s**) and compares it to the checked-in `model\qtsharp.api.json` — byte for byte |
| `[2b]` | extracts the addon models (Charts, Qt3D, Multimedia, Positioning) from their own headers and compares them to the checked-in ones — the same gate |
| `[3]` | splits the combined model into the nine base models and runs the code generator **twice** for every module, comparing the trees |
| `[4]` | compiles the generated C++ wrappers (the core one and one per extension module) and builds the nine base assemblies, all at once |
| `[5]` | builds the ten demo executables (Smoke) and the binding-test executable, then runs them several at a time |

Two gates live in the middle of that: **the models must come out identical** (extraction is
deterministic by construction, so a difference means the Qt version, the clang headers or the
`Roots` table changed) and **the generator must be reproducible**. If a model legitimately changes,
review the diff and re-run with `-Adopt` to accept it; `-SkipExtract` skips steps 2, 2b and 3.

The independent build steps run in a process pool (`tools/parallel.ps1`): the addon extractions,
the codegen runs (both the real tree and the determinism check tree) and the native compiles, up to
`min(8, cores)` at a time. Pass `-Jobs 1` for the old one-at-a-time run, or a larger number on a big
machine. Step 5 starts several processes at once (up to `min(6, cores)`; the same `-Jobs` value
caps it). Each demo leaves its window up for about a second so it can be looked at, but the Smoke
host selects the offscreen QPA platform before constructing the `QApplication`, so nothing is
mapped on the desktop. On a machine that is being used at the same time, one of them can very
occasionally die while Qt tears the process down — a `0xC000041D` exit *after* its own last
`[PASS]` line. That is a known flake, not a failed assertion; the gate prints each process's result
line with its name, retries a nonzero exit once, and a process that fails twice has its own output
printed, so the failure stays attributable even though the windows overlapped.

The same suite runs in CI on every push — the `Smoke tests` job in `.github/workflows/ci.yml` on
Windows and the `linux-bind` job on Ubuntu. Both build the ten demo executables and the test
executable with `tools/run-demos.ps1` and `tools/run-window-tests.ps1`, so they get the same
parallelism and the same one retry for the teardown flake above. No Xvfb and no real display are
needed: the Smoke and test hosts choose `offscreen` themselves (and point Qt at the system font
directory on Windows, which the offscreen plugin does not discover). The Qt3D examples never show
their window in Smoke, so they need no OpenGL there; an interactive View run of them does need a
real display. The package consumer check on Linux uses the offscreen platform
(`tools/test-packages.ps1` selects it when there is no display).

Once that has succeeded, the ordinary `dotnet build` of any project works too:

```
dotnet build demos\QtWidgets\QtSharp.Demos.QtWidgets.csproj -c Release
dotnet build demos\Charts\QtSharp.Charts.Demos.csproj -c Release
dotnet build tests\testapp\testapp.csproj -c Release
dotnet build samples\resources\Resources.csproj -c Release
```

---

## Run the examples

Release builds the Smoke product and Debug (or `-c View`) the interactive one:

```
# Release (Smoke): --list prints the module's demos; <name> runs one and returns its exit code.
demos\QtWidgets\bin\Release\net10.0\QtSharp.Demos.QtWidgets
demos\QtWidgets\bin\Release\net10.0\QtSharp.Demos.QtWidgets qtwidgets-calculator
demos\QtGui\bin\Release\net10.0\QtSharp.Demos.QtGui qtgui-pathstroke
tests\testapp\bin\Release\net10.0\testapp test-metaobject

# View: the same names, with the window on screen; --seconds N closes it after N seconds.
dotnet build demos\QtWidgets\QtSharp.Demos.QtWidgets.csproj -c Debug
demos\QtWidgets\bin\Debug\net10.0\QtSharp.Demos.QtWidgets --list
demos\QtWidgets\bin\Debug\net10.0\QtSharp.Demos.QtWidgets qtwidgets-calculator --seconds 8
```

The ten executables are `QtSharp.Demos.{QtCore,QtGui,QtWidgets,QtNetwork,QtSvg,QtSvgWidgets,QtPrintSupport,QtMultimedia}`
and `QtSharp.{Charts,Qt3D}.Demos`, each under `demos/<module>/bin/<Configuration>/net10.0/`. On
Linux the executable has no extension (`demos/QtWidgets/bin/Release/net10.0/QtSharp.Demos.QtWidgets`).
The Smoke host and the test host choose the offscreen platform automatically (and the system font
directory, which the offscreen plugin needs for real layout); set `QTSHARP_DEMO_PLATFORM=windows`
or `QT_QPA_PLATFORM=windows` for a real display. View runs need a real display for Qt3D, which
renders through OpenGL.

The official demos are named after the module they exercise (`qtcore-*`, `qtgui-*`, `qtwidgets-*`, plus
`qtsvg-*`, `qtsvgwidgets-*`, `qtprintsupport-*`, `qtnetwork-*`, `qtcharts-*`,
`qtmultimedia-*` and `qt3d-*`); the binding tests use `test-*` and cover
ownership, exceptions, C#-declared signals/properties, value types, the BCL conversions and the
modules whose upstream examples are not ported yet. `docs/conformance.md` has the full matrix.
Registration is discovery, not a list: a class carries `[Demo("qtwidgets-analogclock")]` and each
executable finds the names in its own assembly.

Every Smoke example is also a self-test: it drives the generated API, checks what it sees and
prints `[PASS]`/`[FAIL]` lines, ending with a nonzero exit code when a check failed. That is what
the gate reads, so the examples do not rot.

---

## Write your own UI

A consumer project is a package reference and a program. The in-repository demos reference the
module projects directly (the same assemblies the package contains).

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.19.0" />
</ItemGroup>
```

A project that does not use the package can compile the binding from source by importing
`QtSharp.Api.props`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Only when you reference a demo executable: the binding is compiled in there, and compiling
         it twice defines every binding type in two assemblies (CS0433). Drop this line (and the
         ProjectReference) to compile the binding into your own project instead. -->
    <QtSharpCompileBinding>false</QtSharpCompileBinding>
  </PropertyGroup>

  <Import Project="..\..\QtSharp.Api.props" />   <!-- binding + runtime + the native/Qt DLL copies -->

  <ItemGroup>
    <ProjectReference Include="..\..\demos\QtWidgets\QtSharp.Demos.QtWidgets.csproj" />
  </ItemGroup>
</Project>
```

```csharp
using QtSharp.QtWidgets;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var app = new QApplication(args);      // the one application object, the PyQt line

        var window = new QWidget();
        var layout = new QVBoxLayout(window);
        var label = new QLabel("Hello, QtSharp");
        var button = new QPushButton("Count");
        int clicks = 0;
        button.Clicked += _ => label.SetText($"clicked {++clicks} time(s)");   // a signal is an event

        layout.AddWidget(label);
        layout.AddWidget(button);
        window.WindowTitle = "Hello";
        window.Show();

        return app.Exec();                     // app.Quit() ends it
    }
}
```

### The API in one page

* **Properties are properties.** `Q_PROPERTY` becomes a C# property with the PascalCase name, and
  the accessors keep their C++ spelling: `edit.Text`, `edit.text()`, `edit.SetText("...")`;
  `timer.Interval`, `timer.interval()`. A property whose name would collide with a nested enum, a
  virtual or a field is skipped and the accessors stay.
* **Signals are events**, and also a `QSignal` — both spellings work:
  ```csharp
  button.Clicked += handler;          // C# event: -= really disconnects (QObject::disconnect)
  button.ClickedSignal.Connect(handler);
  button.ClickedSignal.Emit(false);   // manual emit; only QSignal has it
  ```
  `Q_SIGNAL`s with more than one overload get the 2,3,… suffix (`Activated2Signal`/`Activated2`).
* **Qt value types convert to the BCL explicitly.** PyQt's `toPyDateTime()`/`QDateTime(datetime)`
  idea in C#:
  ```csharp
  var qdt = new QDateTime(dateTime);        // Utc -> Qt::UTC, Local/Unspecified -> Qt::LocalTime
  DateTime back = qdt.ToDateTime();         // qdt.ToDateTimeOffset() keeps the offset
  var qd = new QDate(dateOnly);             // qdate.ToDateOnly()
  var qt = new QTime(timeOnly);             // qtime.ToTimeOnly()
  byte[] data = qba.ToArray();              // new QByteArray(data) is binary-safe

  Point pt = (Point)qpoint;                 // QPoint/QSize/QRect <-> System.Drawing
  Color c = (Color)qcolor;                  // QColor <-> System.Drawing.Color
  Vector3 v = (Vector3)qvector;             // QVector2D/3D/4D <-> System.Numerics
  Quaternion q = (Quaternion)qquaternion;   // QQuaternion <-> System.Numerics.Quaternion
  Uri uri = (Uri)qurl;                      // QUrl <-> System.Uri
  Guid id = Guid.CreateV5(ns, name);        // QUuid is a Guid; v3/v5 are extensions on it
  char upper = 'a'.ToUpper();               // QChar is a char; Unicode extras are extensions
  object value = model.Data(index);         // a QVariant signature is object: QColor, int, string, ...
  model.SetData(index, value);              // and back, without naming a variant type
  ```
  There are no implicit operators on purpose: `==` on a handle class compares wrapper identity, so
  an implicit conversion would let `qdt == dateTime` compile and silently be false. Invalid or
  out-of-range Qt values throw `InvalidOperationException` instead of wrapping.

  One type goes the other way: **QUuid is `System.Guid`** - there is no QUuid type, the same way
  there is no QString. It is 16 bytes of data, so every Qt signature that takes or returns one
  surfaces as a `Guid` (value equality, no disposal), and Qt stores the same 16 bytes in a
  QVariant's Uuid type. The Qt-only operations are C# 14 extensions on `Guid`
  (`Guid.CreateV3`/`Guid.CreateV5`, `Guid.FromRfc4122`, `id.Version()`, `id.Variant()`,
  `id.ToRfc4122()`), so a call site needs the .NET 10 compiler. A null QUuid is `Guid.Empty`;
  parsing is `Guid.Parse`/`Guid.TryParse`; v4 and v7 ids are `Guid.NewGuid`/`Guid.CreateVersion7`;
  Qt's string formats are Guid's "B"/"D"/"N".

  QChar gets the same treatment: it is one UTF-16 code unit, so it is `char` and there is no QChar
  type. The classification and case methods are extensions (`'A'.IsLetter()`, `'a'.ToUpper()`,
  `'́'.CombiningClass()`), `Category()` returns the BCL's `System.Globalization.UnicodeCategory`,
  and the properties the BCL has no API for (script, bidi direction, joining type, decomposition,
  mirroring, case folding, Unicode version) call Qt's own tables. The static code-point overloads
  are extensions too: `char.Category(0x41)`, `char.Script(0x41)`. A `QChar*` buffer is a `string`
  (`QCollator.Compare`, `QRawFont.GlyphIndexesForChars`).

  The same rule covers every other trivially copyable Qt value: **QModelIndex, QColor, QSizePolicy,
  QTransform, QElapsedTimer, QDeadlineTimer, QOperatingSystemVersion, QTextLength, QRgba64,
  QPixelFormat, QPointingDeviceUniqueId, QMetaMethod/QMetaEnum/QMetaClassInfo, QMetaType,
  QCalendar, QMetaProperty, QTableWidgetSelectionRange, QTextLine, QTextInlineObject, QAudioFormat
  and Qt3D's QNodeId/QKeyFrame are C# structs**, not handles - no `using`, no finalizer, value
  equality. A parameter Qt passes as `const T&` crosses as the value, a `T&` as `ref`/`out`, and a
  method that mutates in place writes back. `QCalendar()` (Gregorian) and `QKeyFrame()` (Bezier)
  keep their parameterless constructors because their zero values are not Qt's defaults.
  Equality and hashing are Qt's: the generated struct calls `operator==`/`qHash` when Qt declares
  them and falls back to the value's fields (or bytes) when it does not, so `-0.0` vs `0.0`,
  `QTextLength`'s fuzzy comparison and `QColor`'s spec rules are exactly Qt's. The comparison
  crosses the native boundary once per call.
  QMatrix4x4 is the one deliberate hold-out: its non-const `QMatrix4x4*` out parameters need a
  dedicated rule before it can stop being a handle. `QMetaObject` stays a handle too: Qt returns it
  as a `const QMetaObject*` to static data, and the struct-pointer convention would surface that as
  an `IntPtr` instead.
* **A `Regex` and a `QRegularExpression` convert explicitly, never implicitly.** They are two
  different engines (the BCL's and PCRE2), and Qt's match/iterator results cannot become
  `System.Text.RegularExpressions.Match`, so the types stay separate. The extension methods in
  `QtSharp.QtCore.QRegularExpressionInterop` carry the pattern and the five options both engines
  spell the same way:
  ```csharp
  Regex regex = qre.ToRegex();                    // PCRE2 pattern compiled by .NET
  using var qre2 = regex.ToQRegularExpression();  // .NET pattern compiled by Qt
  ```
  | `QRegularExpression.PatternOptions` | `RegexOptions` |
  |---|---|
  | `CaseInsensitiveOption` | `IgnoreCase` |
  | `MultilineOption` | `Multiline` |
  | `DotMatchesEverythingOption` | `Singleline` |
  | `ExtendedPatternSyntaxOption` | `IgnorePatternWhitespace` |
  | `DontCaptureOption` | `ExplicitCapture` |

  An option with no counterpart throws `NotSupportedException` instead of being silently dropped:
  Qt's `InvertedGreedinessOption`/`UseUnicodePropertiesOption` when reading a `QRegularExpression`,
  and .NET's `RightToLeft`, `ECMAScript` and `NonBacktracking` when building one (`ToRegex`'s
  `extraOptions` are .NET-only additions made after the mapping, so they are not constrained by
  it). `RegexOptions.Compiled` is accepted and ignored when building the Qt pattern (an execution
  strategy, not pattern semantics), and `CultureInvariant` is accepted as a no-op (Qt's case folding
  is locale-independent), though culture-sensitive .NET matching can still differ. Conversion
  transfers a pattern; it does not translate dialects. The pattern must compile in the target
  engine (`RegexParseException` from `new Regex`; `ArgumentException` carrying Qt's `ErrorString()`
  and `PatternErrorOffset()` when PCRE2 rejects it), but a pattern both accept can still match
  differently. A converted `Regex` is a pattern carrier: Qt APIs still evaluate with PCRE2,
  `QRegularExpression.Match()` still returns Qt's match objects, and `Regex.Match()` re-evaluates
  with the .NET engine.
* **JSON objects are dictionaries, arrays are lists.** `QJsonObject` implements
  `IDictionary<string, QJsonValue>` (a writable view over the Qt object; `Insert` returns the value
  the C++ iterator points at) and `QJsonArray` implements `IList<QJsonValue>`. The LINQ-friendly
  snapshots are `ToObjectDictionary()` and `ToObjectList()`, with values unwrapped the way a
  variant's `ToObject()` unwraps them (nested objects as `Dictionary<string, object>`, arrays as
  `object[]`). Object keys enumerate in Qt's sorted order.
* **A .NET Stream can be a Qt IO device.** `StreamIODevice` (in `QtSharp.QtCore`) implements
  `QIODevice`'s virtuals (`readData`/`writeData`/`seek`/`size`/...), so `QImage.Load`,
  `QXmlStreamReader`, `QDataStream` and anything else taking a `QIODevice*` reads from or writes to
  managed IO. The generator publishes QIODevice's virtuals through a small bridge registry
  (`gen/Bridges.cs`), which is also where future bridges belong.
* **The application object** is created and run like PyQt does it: `new QApplication(args)`,
  `app.Exec()`, `app.Quit()`, `app.Exit(code)`. From code that has no application reference (a menu
  action), use `QCoreApplication.Instance().Quit()`.
* **Subclassing**: `override` the virtuals the model publishes — `sizeHint` (the `SizeHint`
  property reads it, the same way Qt's Q_PROPERTY does), `PaintEvent`, `MousePressEvent`,
  `KeyPressEvent`, model/delegate virtuals (`RowCount`, `Data`, `Paint`, `CreateEditor`),
  `Runnable.Run`, `FilterAcceptsRow`, `QGraphicsItem::boundingRect`/`paint`/`advance`, … The
  native side forwards to your override. A virtual is an override point whenever its signature is
  expressible; private and `final` ones stay callable only.
* **Declaring your own signal or property** (PyQt's `pyqtSignal`/`pyqtProperty`): put the
  attribute on the member, and the member is the declaration:
  ```csharp
  [QtSignal] public event Action<int> ValueChanged;          // the Qt signal valueChanged(int)
  [QtProperty] public int Value { get; set; }                // readable and writable
  [QtProperty("caption")] public string Text { get; set; }   // an explicit Qt name
  [QtProperty] public string Label => "read-only";           // get-only: a read-only property

  void SetValue(int value)
  {
      Value = value;
      ValueChanged?.Invoke(value);              // the pyqtSignal emit: Qt sees it, then the handlers
  }
  ```
  Raising the signal is invoking the event, the way PyQt's `signal.emit()` is: the runtime keeps
  an emit trampoline in the event's handler field, so `?.Invoke` calls `QMetaObject::activate` and
  a QML/C++ `connect` sees it too. `EmitSignal(ValueChanged, value)` is the explicit spelling of
  the same emit. The trampoline is always there, so the event is never null and `?.Invoke` emits
  even with no C# subscriber; `ValueChanged == null` is not a "has subscribers" test. Only
  `+=`/`-=` the event: assigning it (`ValueChanged = null`) also drops the trampoline, and the
  Invoke spellings stop reaching Qt.
  The Qt name is the member's name with a lower-case first letter. The C# event, a QML/C++ handler
  and a Qt `connect()` all reach the same handlers, and Qt reaches a property through
  `QObject::setProperty`/`property`, just like a moc-generated one.
  `[QtSignal]` needs a field-like event (the compiler's handler list is what the runtime reads).
  The rest of `Q_PROPERTY` maps to `QtProperty`'s properties: `Notify` (the signal named by its
  C# member or Qt name; `<Property>Changed` is used when omitted), `Reset` (a parameterless
  method; `Reset<Property>` by convention), `Constant`, `Final`, `Required`, `Designable`,
  `Scriptable`, `Stored`, `User` and `Revision`. `BINDABLE` is not exposed: it needs binding
  support in the native runtime, not just a flag. For the same reason the generated classes omit
  Qt's `bindableFoo()` accessors and the `QBindable<T>` handle they return: that handle's `Value`
  only repeats the property's own getter and setter, while `setBinding`, `onValueChanged` and
  `hasBinding` are not bound. Use the normal property accessor and, where Qt declares one, its
  `...Changed` NOTIFY signal instead. `QTimer`'s `active`/`interval`/`singleShot`/`timerType`,
  `QMovie.cacheMode`/`speed` and `QGeoPositionInfoSource.updateInterval` have no NOTIFY signal,
  so changes to them are not observable through the binding until the binding feature (or a
  per-class signal) exists.
  `tests/MetaObjectApi.cs` is the same declaration under test.
  The generated Qt classes are deliberately different: their properties and signals already exist
  in moc's meta-object, so they do not carry these attributes. Their types are marked
  `[GeneratedCode("qtgen", …)]`, and the XML docs say what a member is — for example
  `The windowTitle property (Q_PROPERTY, QString, read/write)` and
  `The windowTitleChanged(QString) signal as a C# event`. `[QtProperty]`/`[QtSignal]` are only for
  members *you* declare on a C# subclass. A generated member instead carries the read-side
  `[QtNativeProperty(name, type, writable)]` or `[QtNativeSignal(mocSignature)]`, so reflection
  and tooling can tell the two apart without guessing from the type. Only the types the
  hand-written runtime extends (the application classes, `QObject`, and the date/time/byte-array
  types) are `partial`; every other
  wrapper is a whole declaration, and `unsafe` sits on the few constructors that pass a function
  pointer rather than on the class.
* **Ownership**: an object you `new` is yours — `Dispose()` (or the finalizer) deletes it, unless you
  handed it to Qt (a parent, a layout, a scene, a view), in which case the generated destructor asks
  `parent()` first and leaves it alone. Objects Qt owns are never deleted by the GC, and once Qt has
  destroyed one, touching its wrapper throws `ObjectDisposedException` instead of crashing.
* **A destroyed object reads as null.** A wrapper whose Qt object is gone compares equal to null the
  way a destroyed `UnityEngine.Object` does, so `if (w != null) w.Use();` is the guard to write, and
  `w.IsAlive` asks the same question by name (PyQt's `sip.isValid`). Only the operator is
  liveness-aware — `w?.Use()`, `w ?? fallback` and `w is null` test the reference itself, exactly as
  they do in Unity.
* **An exception in a handler is reported, not fatal.** A signal handler or a virtual override runs
  inside Qt's C++ frames, where an exception has nowhere to land — PyQt answers that by printing the
  traceback and calling `qFatal()`, PySide by printing and carrying on. The runtime catches it at the
  boundary, writes it to stderr and raises `ApiRuntime.UnhandledCallbackException`; subscribe to that
  to fail loudly instead of quietly.
* **Events** that Qt hands to a handler are read-only wrappers (`e.Position()`, `e.Key()`,
  `e.Buttons()`, `e.TimerId()`); they are transient — do not keep them.
* **The example classes are `sealed`** on purpose (they are meant to be read, not inherited); the one
  exception is `ApiArthurFrame`, the small base widget with a `virtual Paint(QPainter)` hook.

---

## Qt development tools

`QtSharp.Tools` is a separate development NuGet package containing native Qt tools and their
DLL dependencies (Windows x64). The initial Qt 6.12.0 payload includes `rcc`, `uic`, `lrelease`
and `lupdate`; `moc` is also copied when supplied by the source distribution. Consumers do not
need a Qt SDK or Python installed. It is currently built locally, not published to nuget.org.

To produce the package, point the staging script at a directory containing these executables
and matching DLLs (normally a Qt kit's `bin`):

```powershell
powershell -File tools\pack-qt-tools.ps1 -QtToolsSource C:\Qt\6.12.0\msvc2022_64\bin
```

The result is `out/packages/QtSharp.Tools.0.19.0.nupkg`. `-QtVersion` and `-PackageVersion`
override the defaults. Put the upstream license texts/notices in a `QtSharp-ThirdPartyNotices`
subdirectory beside the source tools to include them in the payload. Tools are staged under
`out/qt-tools`; the script checks QtCore's version and runs captured, time-limited startup checks.

In a consumer project:

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Tools" Version="0.19.0" PrivateAssets="all" />
  <QtResource Include="assets\sample.qrc" />
</ItemGroup>
```

Building automatically compiles to `assets/sample.rcc` in the output/publish directory.
Register it with `QResource.RegisterResource(path)` to use its `:/` paths. The compiler runs
on each build to pick up changes to referenced assets as well as the `.qrc` file itself.

The executable directory is exposed as `$(QtSharpToolsDir)`. A captured command-line entry:

```text
dotnet msbuild MyApp.csproj -t:QtSharpRunTool -p:QtSharpTool=rcc -p:QtSharpToolArguments=--help
```

`uic` generates native C++, not C#; a C# `.ui` compiler is not implemented yet. Designer,
Linguist GUI and deployment tools are not included in this first payload.

The resource integration sample checks the complete package-to-resource-read path:

```powershell
dotnet restore samples\resources\Resources.csproj --source D:\QtSharp\out\packages
dotnet run --project samples\resources\Resources.csproj -c Release --no-restore
```

Replace the local source path with your checkout's `out/packages` directory.

## Repository layout

| path | what |
|---|---|
| `model/qtsharp.api.json` | the checked-in combined API model — the extraction gate and the shared native glue's input |
| `model/qtsharp.<module>.json` | the nine checked-in base models (`split-core`) and the addon models (`charts`, six Qt3D modules) the assemblies are generated from |
| `gen/` | the generator: `ApiModel.cs` (model shape, vocabulary), `ApiModel.Modules.cs` (the module registry), `CoreSplit.cs` (combined → per-module models), `ClangExtractor.cs` (the libclang front end), `ApiCodeGen.cs` (model → C# + C++) |
| `native/` | the hand-written runtime (`QtSharpApiRuntime.cpp`: moc signal forwarder, probes) and the crash tracer |
| `runtime/` | the managed runtime: `ApiRuntime.cs` (`QtSharp.Runtime`) and the per-module halves under `QtCore/`, `QtGui/`, `QtWidgets/` |
| `demos/hosting/` | the shared demo host: `[Demo]` discovery, `DemoContext` (Smoke's `Check`/`ExitCode` or View's `Show`) and `DemoHost` (the one `QApplication`, `--list`, `--seconds`) |
| `demos/<module>/` | one executable per module — `QtCore`, `QtGui`, `QtWidgets`, `QtNetwork`, `QtSvg`, `QtSvgWidgets`, `QtPrintSupport`, `QtMultimedia`, `Charts`, `Qt3D`; the classes carry `[Demo("qtwidgets-...")]` and `DemoDiscovery` finds them in the executable's own assembly |
| `demos/DemoMode.props` | the Smoke/View configuration aliases (`Release`/`Debug`) and the `QTSHARP_DEMO_*` constants |
| `tests/` | `QtSharp.BindingTests.csproj` — the binding-specific tests (`test-*`), registered the same way; `tests/testapp/` is the one test executable and dispatches them |
| `nuget/QtSharp.Api/`, `nuget/QtSharp.Runtime/`, `nuget/QtSharp.Qt6/` | the base binding and its runtime packages (nine module assemblies inside `QtSharp.Api`) |
| `nuget/QtSharp.Charts*`, `nuget/QtSharp.Qt3D*`, `nuget/QtSharp.Multimedia*` | the addon packages and their `*.Qt6` runtime companions |
| `docs/conformance.md` | which Qt examples run against which module, and the remaining coverage gaps |
| `docs/model-vocabulary.md` | the Qt-to-C# type vocabulary, the rule for adding a missing spelling, and the spellings it still leaves out |
| `tools/regen-api.ps1` | the build and the gate |
| `tools/run-demos.ps1` | builds and runs the ten demo executables (the gate, CI and release share this table) |
| `tools/run-window-tests.ps1` | the process pool and result counter for one executable (`--list`, one run per name, one retry) |
| `tools/parallel.ps1` | the process pool the gate and CI use to run independent build steps several at a time |
| `tools/clangprobe/` | the probe used to validate the libclang front end (not part of the build) |
| `tools/disasm.py` | capstone-based RVA disassembly helper |
| `QtSharp.Api.props` | the one import a consumer project needs |

---

## Known limitations

* **Windows x64 and Linux x64 (glibc), Qt 6.12.0 only** — the model is extracted from the MSVC
  kit, and the Linux build compiles the same generated C++ with g++. ARM64 and musl are not
  packaged.
* **`long`-typed Qt APIs stay 32-bit on Linux.** The model comes from the MSVC headers, where
  `long` is 32 bits, so the C ABI pins it to `int`/`uint` on both platforms and the wrapper
  converts at the Qt call. That keeps one C# API across platforms, but `QByteArray.toLong`,
  `QLocale.toLong`, the `QEventPoint` timestamps and the `QThread`/`QWaitCondition` timeout
  parameters truncate to 32 bits on Linux (the timestamps wrap after ~49.7 days of uptime). Use the
  `QByteArray::toLongLong` / `QLocale::toLongLong` overloads when the full range matters.
* **Windows-only Qt members are not bound.** `QProcess.nativeArguments`/`setNativeArguments` are
  declared by Qt only on Windows; the generator filters them out so one API surface compiles on
  both platforms. Use `ProcessStartInfo.Arguments` instead.
* **The addon `.Qt6` packages have no separate Linux payload.** On Linux the base
  `QtSharp.Qt6` package deploys the whole Qt tree (`lib/` + `plugins/` + `qt.conf`, addon modules
  included), because every addon package depends on it anyway; the addon packages still carry the
  addon's native glue for both RIDs.
* **Single-file publish is not supported on Linux.** The deployment relies on `$ORIGIN` rpaths
  between the application directory, `lib/` and `plugins/`; a single-file bundle extracts the
  native libraries elsewhere and breaks that layout.
* **`QtSharp.Tools` is Windows x64 only** — its `rcc`/`uic`/`lrelease`/`lupdate` payload is the
  MSVC build. On Linux use the tools from the Qt kit directly.
* **QtMultimedia on Linux needs the distribution's FFmpeg runtime.** Qt's ffmpeg backend plugin
  links `libavcodec`/`libavformat`/`libavutil`/`libswresample`/`libswscale`; install the ffmpeg
  runtime packages that match the kit's build (on Ubuntu 24.04, the `libavcodec60` family). The
  binding and the plugin load without them, but `QMediaPlayer` reports no media.
* **`QtSharp.Sql` materializes a whole result set.** Qt's `QSqlTableModel` fetches in 256-row
  batches; the substitute loads everything up front, so page very large tables with `WHERE`/`LIMIT`.
  Its per-cell `Data()` also crosses the managed/native boundary (a constant cost per visible cell).
* **`QAudioFormat` has no constructor overloads in the binding.** Qt 6.12 declares none beyond the
  implicit default (the AST does not list it), so there is no `QAudioFormat(...)`; as a struct its
  default (`new QAudioFormat()` / `default`) is Qt's default format, and the setters fill it in.
  `QAudioDevice.PreferredFormat()` is still the useful starting point.
* **Bindable property handles are not exposed.** Qt's `bindableFoo()` accessors and their
  `QBindable<T>` return type are absent; use the property getter/setter and its NOTIFY signal
  instead. `QTimer.active`/`interval`/`singleShot`/`timerType`, `QMovie.cacheMode`/`speed` and
  `QGeoPositionInfoSource.updateInterval` have no NOTIFY signal, so changes to them cannot be
  observed through the binding. Re-adding `QBindable<T>` only makes sense together with the
  binding members the current half-API left out (`SetBinding`, `OnValueChanged`, `HasBinding`,
  `ClearBinding`), and a test proving the auto-update.
* **The bound modules** are QtCore, QtGui, QtWidgets, QtNetwork (host addressing, local sockets
  `QLocalServer`/`QLocalSocket`, TCP/UDP, the HTTP client), QtSvg/QtSvgWidgets, QtPrintSupport
  (`QPrinter`, print dialogs/preview) and QtOpenGL/QtOpenGLWidgets (`QOpenGLWidget`,
  `QOpenGLContext`, shader/buffer classes).
    **Not bound**: QtXml/QtSql/QtWebSockets (System.Xml, EF Core/Sqlite and ClientWebSocket cover
    them; QtXml callers keep `QXmlStreamReader/Writer` in QtCore), QtQml/QtQuick, QtPdf, QtWebEngine.
* **QtCharts and Qt3D are optional packages, not part of `QtSharp.Api`.** Both are addon modules
  outside `qtbase`/`qtsvg`, so they ship the way PySide6 and PyQt6 split their addons: separate
  assemblies and packages that reference the core (`PySide6-Essentials` ships only a
  `QtCharts.pyi` stub and leaves the binding and `Qt6Charts.dll` to `PySide6-Addons`; PyQt6 keeps
  QtCharts in a separate `PyQt6-Charts` wheel). QtCharts is additionally **GPLv3-or-commercial
  rather than LGPL**, so keeping it out of `QtSharp.Api` is also what stops every consumer from
  inheriting that license. Qt3D is **LGPLv3/GPLv2/commercial**; it is split for the packaging
  reason (an optional family of six libraries), not for licensing.
  Qt3D's `QTransform`, `QBuffer`, `QAction`, `QKeyEvent`, `QMouseEvent` and `QWheelEvent` share
  their names with core classes; each lives in its Qt module namespace (`QtSharp.Qt3DCore`,
  `QtSharp.Qt3DInput`), so a file that imports both aliases the one it means (`using
  QTransform3D = QtSharp.Qt3DCore.QTransform;`).
* **Qt3D-specific virtuals are override points too.** There is no name whitelist: a managed
  subclass of a Qt3D class can replace `QAbstractAspect::onEngineStartup`-style hooks and
  `executeCommand`/`jobsToExecute`. A private or `final` virtual, and a signature the glue cannot
  reproduce, stays callable only. Signals, properties, enums and flags are bound normally
  (including class-scoped enums in signal signatures).
* **Not ported**: the RHI examples (they need `QRhi` and prebuilt shaders) and Window Embedding.
  PDF *output* works through `QPrinter` (see `test-docformats`); PDF *reading* needs the QtPdf
  module, which is not bound.
* `tests/WindowFlagsApi.cs` is written and passes all 115 assertions, but it dies about three runs in ten
  while Qt tears the process down (a native message pump reaching a destroyed window), so it is
  deliberately not wired into the dispatcher.
* An optional parameter whose C++ default is a *bare enumerator that is not an enumerator of the
  parameter's type* still truncates the overload (the C# default would be 0, which Qt often
  reserves for "invalid"). A boxed-value default is fine now: the parameter defaults to `null` and
  the native glue substitutes the declared expression, which is what unlocked
  `QPixmap.fill(QColor)`, `QWidget.grab(QRect)`, `QComboBox.addItem(QString, QVariant)` and
  `QTcpServer.listen(address, port)`.
* A `const uchar *` *return* is dropped (there is no length to go with it), so `QResource.data()`
  and `QImage.bits()` are absent; `QResource.uncompressedData()`, `QByteArray` and the `byte[]`
  parameters cover the same ground. Nested classes (`QFont::Tag`, `QTextOption::Tab`) are not
  bound either — the model publishes top-level classes and enums only.
* Hand-written code inside a bound class is not preserved by a regeneration: the binding lives in
  `out/api` (gitignored), and everything hand-written lives in `runtime/`.

## License and third-party notices

QtSharp's original source code is available under the [MIT License](LICENSE). Qt, ClangSharp,
the Qt command-line tools, and other bundled or restored dependencies remain under their own
licenses. See [NOTICE.md](NOTICE.md) before redistributing binaries or the `QtSharp.Tools` package.
