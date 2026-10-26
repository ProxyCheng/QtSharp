# QtSharp

[![CI](https://github.com/ProxyCheng/QtSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/ProxyCheng/QtSharp/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/ProxyCheng/QtSharp?display_name=tag)](https://github.com/ProxyCheng/QtSharp/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Target: Windows x64](https://img.shields.io/badge/target-Windows%20x64-0078D4.svg)](#prerequisites)

Qt for .NET, **generated from Qt's own headers** — no hand-written bindings, no runtime reflection
over Qt.

One libclang parse of the Qt headers produces a normalized API model (checked in, byte for byte
reproducible). A pure-function code generator turns that model into C# plus a C++ wrapper, and a
small runtime makes signals, virtual overrides and object ownership behave the way a C# program
expects. On top of that, 35 runnable demos — Qt's official examples plus tours of QtCore, QtNetwork
and the document modules — have been rewritten in C# and run by the repository's own gate.

```
Qt 6.12.0 headers (MSVC kit)
        │  libclang via ClangSharp — one translation unit, ~3 s, deterministic
        ▼
model/qtsharp.api.json                 ← checked in; the gate compares the fresh extraction to it
        │  qtgen split-core (a pure function of the model)
        ▼
model/qtsharp.qtcore.json …            ← eleven checked-in base models, one per Qt module
        │  qtgen codegen (one run per module)
        ▼
out/<module>/generated/QtCore/qobject.cs …  ← the binding, one C# file per Qt header: 508 classes,
                                               C# properties, signals as events, eleven assemblies
out/api/QtSharp.Api.native.cpp              ← one C++ subclass per bound class: virtual hooks, C ABI
        │  build_api.bat (cl.exe)
        ▼
out/api/QtSharp.Api.dll                     ← the shared native glue
        +
runtime/ApiRuntime.cs (QtSharp.Runtime)     ← QSignal, attributes, ownership, marshalling
runtime/QtCore, QtGui, QtWidgets            ← the QObject/app halves that belong to those modules
        │
        ▼
demos/QtSharp.Demos.csproj                  ← the official demos, referencing the eleven base assemblies
tests/QtSharp.BindingTests.csproj           ← binding-specific tests (test-*)
        ▲
   testbind/testapp                         ← the one entry point: demos + tests, one name table
```

Current coverage: **508 classes / 10997 methods / 913 virtuals / 522 enums / 849 Q_PROPERTY
properties / 480 signals / 68 namespace functions** (QtCore, QtGui, QtWidgets, QtNetwork, QtSql,
QtSvg, QtSvgWidgets, QtPrintSupport, QtXml, QtOpenGL, QtOpenGLWidgets). The gate runs 29 official Qt
examples (**920 assertions**), 11 binding-specific tests (**387 assertions**), the 38 official Qt
Charts examples (**570 assertions**) and the three official Qt3D examples (**37 assertions**).

Extension modules are separate assemblies and packages against the same runtime:

| module | package | contents |
|---|---|---|
| QtCharts | `QtSharp.Charts` | `QChart`, `QChartView`, the series, bars, pie and axis families |
| Qt3D | `QtSharp.Qt3D` | **230 classes** from Qt3DCore, Qt3DRender, Qt3DInput, Qt3DLogic, Qt3DAnimation and Qt3DExtras, as six assemblies/namespaces in one package |

## Install the binding

If you just want to write Qt applications in C#, install the release package — there is nothing
else to set up. No Qt SDK, no Visual Studio C++ workload, no clone:

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api" Version="0.4.0" />
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

The package carries the eleven module assemblies and depends on `QtSharp.Runtime` (the managed
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
  <PackageReference Include="QtSharp.Api" Version="0.4.0" />
  <PackageReference Include="QtSharp.Qt3D" Version="0.4.0" />
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

See `samples/qt3d` for a self-test that runs without a display.

## Project status

QtSharp is an active pre-1.0 project. The supported target is Windows x64 with Qt 6.12.x and
.NET 10. The generated model, native wrapper, and runtime are under active development, so binary
and source compatibility can change between releases. Contributions, reproducible bug reports, and
new conformance demos are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

The repository's own generated binding is the primary validation surface. It is not yet a stable
drop-in replacement for every Qt module or every desktop deployment scenario.

## Releases

Pushing a semantic-version tag such as `v0.4.0` starts the release workflow. It builds eight
Windows x64 packages and attaches them, with a `SHA256SUMS.txt`, to a GitHub Release:

| package | what it is |
|---|---|
| `QtSharp.Api` | the binding as eleven module assemblies — add it and write Qt applications in C# |
| `QtSharp.Runtime` | the managed runtime + native glue (brought in by `QtSharp.Api`) |
| `QtSharp.Qt6` | the Qt 6 runtime DLLs + platform plugin (brought in by `QtSharp.Api`) |
| `QtSharp.Tools` | Qt command-line tools (`rcc`, `uic`, `lrelease`, `lupdate`) + MSBuild `.qrc` support |
| `QtSharp.Charts` | the optional QtCharts extension, against `QtSharp.Api` |
| `QtSharp.Charts.Qt6` | Qt6Charts.dll (brought in by `QtSharp.Charts`) |
| `QtSharp.Qt3D` | the optional Qt3D extension (six modules), against `QtSharp.Api` |
| `QtSharp.Qt3D.Qt6` | the Qt3D runtime and plugins (brought in by `QtSharp.Qt3D`) |

Everything is built on the runner from the checked-in models: the generator runs without clang, the
Qt dev files (including the QtCharts, Qt3D and Qt Shader Tools addons) are fetched from Qt's own
repository, and the native wrappers are compiled with the MSVC tools that ship on the runner. CI
runs the same steps on every push, including the official Qt3D examples as the extension's
conformance suite, so the consumer path is verified continuously.

The packages are distributed through GitHub Releases. Publishing to nuget.org is kept as a separate
future step so a public NuGet API key is never required for ordinary repository builds.

---

## Prerequisites

| | needed for | notes |
|---|---|---|
| **Windows x64** | everything | the generated wrapper is MSVC C++; there is no Linux/macOS build |
| **.NET SDK 10** | everything | the projects target `net10.0` |
| **Visual Studio with the C++ workload** | `build_api.bat` | `cl.exe` and `vcvarsall.bat` |
| **Qt 6.12.0 MSVC 2022 64-bit** | extract + native build + running | dev files (headers, `.lib`) **and** the runtime DLLs; the QtCharts and Qt3D addons and Qt Shader Tools too (`tools\build-qt-kit.ps1` fetches them) |
| **clang builtin headers** | extract only | any clang version; the checked-in model used clang 19 |
| **NuGet reachable** | first build | restores `ClangSharp` + `libClangSharp.runtime.win-x64` |

Nothing Qt- or clang-shaped is committed (it is all in `.gitignore`), so a clone has to put those
three pieces somewhere. The defaults are the layout this repository was developed in; every one of
them can be pointed elsewhere instead:

| piece | default | override |
|---|---|---|
| Qt dev files (headers + lib) | `<repo>\Qt\6.12.0\msvc2022_64` | `QT_DIR` (build_api.bat), `QTSHARP_QT_INCLUDE` (generator) |
| Qt runtime DLLs + `platforms\qwindows.dll` | `<repo>\Qt\6.12.0\runtime` | `QtRuntimeDir` in `QtSharp.Api.props` |
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
  QtXml/QtOpenGL; QtSvg and QtSvgWidgets come from the separate `qtsvg` module (add its
  `include`/`lib` the same way).
* **Qt runtime DLLs**: `<repo>\Qt\6.12.0\runtime` needs the modules the binding links —
  `Qt6Core.dll`, `Qt6Gui.dll`, `Qt6Widgets.dll`, `Qt6Network.dll`, `Qt6Svg.dll`,
  `Qt6SvgWidgets.dll`, `Qt6PrintSupport.dll`, `Qt6Xml.dll`, `Qt6OpenGL.dll`,
  `Qt6OpenGLWidgets.dll` — plus the MSVC runtime DLLs (`msvcp140*.dll`, `vcruntime140*.dll`) and
  `platforms\qwindows.dll`. The easy way is to build any Qt widget app and run `windeployqt` on it.
  All of them must be the *same* Qt version (6.12.x) — they share `Qt6Core`.
* **clang builtin headers**: `<repo>\lib\lib\clang\<version>\include` (with `stddef.h` in it) — copy
  them out of an LLVM installation (`C:\Program Files\LLVM\lib\clang\19\include`) or from the
  `libclang.runtime.win-x64` NuGet package. Any version works; a different version from clang 19 may
  change a fact or two in the extracted model, which the model diff gate below will show you.

---

## Build and verify

```
powershell -File tools\regen-api.ps1
```

That is the whole thing (a clone's first run takes a few minutes — the native glue is a big
translation unit, and every module's demos and tests are built and run at the end). It runs, in order:

| step | what it does |
|---|---|
| `[1]` | builds the generator (`gen`) |
| `[2]` | extracts the model from the Qt headers into `out\qtsharp.api.json.new` (**~3 s**) and compares it to the checked-in `model\qtsharp.api.json` — byte for byte |
| `[2b]` | extracts the seven addon models (Charts, Qt3D) from their own headers and compares them to the checked-in ones — the same gate |
| `[3]` | splits the combined model into the eleven base models and runs the code generator **twice** for every module, comparing the trees |
| `[4]` | compiles the generated C++ wrapper and builds the eleven base assemblies |
| `[5]` | builds the official demos and the binding tests, then runs all of them with `testapp`, several windows at a time |

Two gates live in the middle of that: **the models must come out identical** (extraction is
deterministic by construction, so a difference means the Qt version, the clang headers or the
`Roots` table changed) and **the generator must be reproducible**. If a model legitimately changes,
review the diff and re-run with `-Adopt` to accept it; `-SkipExtract` skips steps 2, 2b and 3.

Step 5 starts several GUI processes at once (up to `min(6, cores)`; pass `-Jobs 1` for the old
one-at-a-time run, or `-Jobs 8` on a big machine). Each demo still leaves its window up for about a
second so it can be looked at. On a machine that is being used at the same time, one of them can
very occasionally die while Qt tears the process down — a `0xC000041D` exit *after* its own
`[SELFTEST OK]` line. That is a known flake, not a failed assertion; the gate prints each window's
result line with its name, retries a nonzero exit once, and a window that fails twice has its own
output printed, so the failure stays attributable even though the windows overlapped.

The same suite runs in CI on every push — the `Smoke tests` job in `.github/workflows/ci.yml`. It
takes the binding the `binding` job built from the checked-in model, fetches Qt, builds the examples
and the binding tests, then runs all of them through the same `tools/run-window-tests.ps1` pool, so it gets
the same parallelism and the same one retry for the teardown flake above. Note that the examples need a real Qt platform: `QT_QPA_PLATFORM=offscreen`
is *not* an option, because several of them assert on rendered pixels and one crashes without
`qwindows`.

Once that has succeeded, the ordinary `dotnet build` of any project works too:

```
dotnet build demos\QtSharp.Demos.csproj -c Release
dotnet build testbind\testbind.csproj -c Release
dotnet build samples\hello\Hello.csproj -c Release
```

---

## Run the examples

```
testapp                          # the list (official demos + binding tests)
testapp qtwidgets-calculator     # an official example
testapp qtgui-pathstroke         # QTSHARP_DEMO_MS=8000 keeps it on screen longer
testapp test-metaobject          # a binding test
```

The official demos are named after the module they exercise (`qtgui-*`, `qtwidgets-*`, plus
`qtsql-*`, `qtsvg-*`, `qtsvgwidgets-*`, `qtxml-*`, `qtprintsupport-*`, `qtnetwork-*`, `qtcharts-*`
and `qt3d-*`); the binding tests use `test-*` and cover
ownership, exceptions, C#-declared signals/properties, value types, the BCL conversions and the
modules whose upstream examples are not ported yet. `docs/conformance.md` has the full matrix.
Registration is discovery, not a list: a class carries `[Demo("qtwidgets-analogclock")]` and each
catalog finds it in its own assembly.

Every example is also a self-test: it drives the generated API, checks what it sees and prints
`[PASS]`/`[FAIL]` lines, ending in `[SELFTEST OK]`. That is what the gate reads, so the examples do
not rot.

---

## Write your own UI

A consumer project is a package reference and a program. The in-repository demos reference the
module projects directly (the same assemblies the package contains).

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api" Version="0.4.0" />
</ItemGroup>
```

A project that does not use the package can compile the binding from source by importing
`QtSharp.Api.props`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Only when you reference QtSharp.Demos: the binding is compiled in there, and compiling it
         twice defines every binding type in two assemblies (CS0433). Drop this line (and the
         ProjectReference) to compile the binding into your own project instead. -->
    <QtSharpCompileBinding>false</QtSharpCompileBinding>
  </PropertyGroup>

  <Import Project="..\..\QtSharp.Api.props" />   <!-- binding + runtime + the native/Qt DLL copies -->

  <ItemGroup>
    <ProjectReference Include="..\..\demos\QtSharp.Demos.csproj" />
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
  Guid id = (Guid)quuid;                    // QUuid <-> System.Guid
  object value = qvariant.ToObject();       // PyQt's QVariant.value(): int, string, DateTime, ...
  var variant = QVariant.From(value);       // and back
  ```
  There are no implicit operators on purpose: `==` on a handle class compares wrapper identity, so
  an implicit conversion would let `qdt == dateTime` compile and silently be false. Invalid or
  out-of-range Qt values throw `InvalidOperationException` instead of wrapping.
* **The application object** is created and run like PyQt does it: `new QApplication(args)`,
  `app.Exec()`, `app.Quit()`, `app.Exit(code)`. From code that has no application reference (a menu
  action), use `QCoreApplication.Instance().Quit()`.
* **Subclassing**: `override` the virtuals the model publishes — `SizeHint`, `PaintEvent`,
  `MousePressEvent`, `KeyPressEvent`, model/delegate virtuals (`RowCount`, `Data`, `Paint`,
  `CreateEditor`), `Runnable.Run`, `FilterAcceptsRow`, … The native side forwards to your override.
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
      EmitSignal(ValueChanged, value);          // the pyqtSignal emit
  }
  ```
  The Qt name is the member's name with a lower-case first letter. The C# event, a QML/C++ handler
  and a Qt `connect()` all reach the same handlers, and Qt reaches a property through
  `QObject::setProperty`/`property`, just like a moc-generated one.
  `[QtSignal]` needs a field-like event (the compiler's handler list is what the runtime reads).
  The rest of `Q_PROPERTY` maps to `QtProperty`'s properties: `Notify` (the signal named by its
  C# member or Qt name; `<Property>Changed` is used when omitted), `Reset` (a parameterless
  method; `Reset<Property>` by convention), `Constant`, `Final`, `Required`, `Designable`,
  `Scriptable`, `Stored`, `User` and `Revision`. `BINDABLE` is not exposed: it needs binding
  support in the native runtime, not just a flag.
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

The result is `out/packages/QtSharp.Tools.0.4.0.nupkg`. `-QtVersion` and `-PackageVersion`
override the defaults. Put the upstream license texts/notices in a `QtSharp-ThirdPartyNotices`
subdirectory beside the source tools to include them in the payload. Tools are staged under
`out/qt-tools`; the script checks QtCore's version and runs captured, time-limited startup checks.

In a consumer project:

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Tools" Version="0.4.0" PrivateAssets="all" />
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
| `model/qtsharp.<module>.json` | the eleven checked-in base models (`split-core`) and the addon models (`charts`, six Qt3D modules) the assemblies are generated from |
| `gen/` | the generator: `ApiModel.cs` (model shape, vocabulary), `ApiModel.Modules.cs` (the module registry), `CoreSplit.cs` (combined → per-module models), `ClangExtractor.cs` (the libclang front end), `ApiCodeGen.cs` (model → C# + C++) |
| `native/` | the hand-written runtime (`QtSharpApiRuntime.cpp`: moc signal forwarder, probes) and the crash tracer |
| `runtime/` | the managed runtime: `ApiRuntime.cs` (`QtSharp.Runtime`) and the per-module halves under `QtCore/`, `QtGui/`, `QtWidgets/` |
| `demos/` | `QtSharp.Demos.csproj` — the binding, the runtime and the official Qt examples, grouped as `QtGui/` and `QtWidgets/`; each class is registered with `[Demo("qtwidgets-...")]` and `DemoCatalog` discovers it |
| `tests/` | `QtSharp.BindingTests.csproj` — the binding-specific tests (`test-*`), registered the same way and discovered by `BindingTests` |
| `testbind/` | `testapp`, the one entry point: it dispatches both catalogs |
| `samples/qt3d/` | a Qt3D sample: windowless self-test by default, `window` opens the scene |
| `demos.qt3d/` | the official Qt3D C++ examples (`simple-cpp`, `basicshapes-cpp`, `3d-text`) as the extension's conformance tests |
| `demos.charts/` | the official Qt Charts examples (38 windows) as the Charts module's conformance tests |
| `nuget/QtSharp.Api/`, `nuget/QtSharp.Runtime/`, `nuget/QtSharp.Qt6/` | the base binding and its runtime packages (eleven module assemblies inside `QtSharp.Api`) |
| `nuget/QtSharp.Charts*`, `nuget/QtSharp.Qt3D*` | the two addon packages and their `*.Qt6` runtime companions |
| `docs/conformance.md` | which Qt examples run against which module, and the remaining coverage gaps |
| `tools/regen-api.ps1` | the build and the gate |
| `tools/clangprobe/` | the probe used to validate the libclang front end (not part of the build) |
| `tools/disasm.py` | capstone-based RVA disassembly helper |
| `QtSharp.Api.props` | the one import a consumer project needs |

---

## Known limitations

* **Windows x64 and Qt 6.12.0 only** — the model is extracted from that kit, and the wrapper is
  MSVC-compiled.
* **The bound modules** are QtCore, QtGui, QtWidgets, QtNetwork (host addressing, local sockets
  `QLocalServer`/`QLocalSocket`, TCP/UDP, the HTTP client), QtSql (`QSqlDatabase`/`QSqlQuery`, the
  record/field classes, `QSqlQueryModel`/`QSqlTableModel`; the SQLite driver ships in
  `QtSharp.Qt6`), QtSvg/QtSvgWidgets, QtPrintSupport
  (`QPrinter`, print dialogs/preview), QtXml (the `QDom*` DOM; `QXmlStreamReader/Writer` are in
  QtCore) and QtOpenGL/QtOpenGLWidgets (`QOpenGLWidget`, `QOpenGLContext`, shader/buffer classes).
    **Not bound**: QtMultimedia, QtQml/QtQuick, QtPdf, QtWebEngine.
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
* **Qt3D-specific virtuals are not override points yet.** The override whitelist is the core's
  widget/event/model set, so a managed subclass of a Qt3D class can use its API but cannot replace
  `QAbstractAspect::onEngineStartup`-style hooks (or `executeCommand`/`jobsToExecute`). Signals,
  properties, enums and flags are bound normally (including class-scoped enums in signal
  signatures).
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
