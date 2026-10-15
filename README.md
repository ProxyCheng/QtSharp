# QtSharp

Qt for .NET, **generated from Qt's own headers** — no hand-written bindings, no runtime reflection
over Qt.

One libclang parse of the Qt headers produces a normalized API model (checked in, byte for byte
reproducible). A pure-function code generator turns that model into C# plus a C++ wrapper, and a
small runtime makes signals, virtual overrides and object ownership behave the way a C# program
expects. On top of that, 34 runnable demos — Qt's official examples plus tours of QtCore, QtNetwork
and the document modules — have been rewritten in C# and run by the repository's own gate.

```
Qt 6.11.0 headers (MSVC kit)
        │  libclang via ClangSharp — one translation unit, ~3 s, deterministic
        ▼
model/qtsharp.api.json                 ← checked in; the gate compares the fresh extraction to it
        │  qtgen codegen (a pure function of the model)
        ▼
out/api/QtSharp.Api.cs                 ← the binding: 496 classes, C# properties, signals as events
out/api/QtSharp.Api.native.cpp         ← one C++ subclass per bound class: virtual hooks, C ABI
        │  build_api.bat (cl.exe)
        ▼
out/api/QtSharp.Api.dll                ← the native glue
        +
runtime/ApiRuntime.cs                  ← QSignal/meta-object bookkeeping, ownership, event probes
        │
        ▼
demos/QtSharp.Demos.csproj             ← the binding + runtime + 34 demos, one assembly
        ▲                    ▲
   testbind/testapp     samples/hello  ← two thin entry points, one name → demo table
```

Current coverage: **496 classes / 10506 methods / 885 virtuals / 511 enums / 816 Q_PROPERTY
properties / 463 signals / 70 namespace functions** (QtCore, QtGui, QtWidgets, QtNetwork, QtSvg,
QtSvgWidgets, QtPrintSupport, QtXml, QtOpenGL, QtOpenGLWidgets). The gate runs all 34 demos
(**1165 assertions**) plus `samples/hello`.

---

## Prerequisites

| | needed for | notes |
|---|---|---|
| **Windows x64** | everything | the generated wrapper is MSVC C++; there is no Linux/macOS build |
| **.NET SDK 10** | everything | the projects target `net10.0` |
| **Visual Studio with the C++ workload** | `build_api.bat` | `cl.exe` and `vcvarsall.bat` |
| **Qt 6.11.0 MSVC 2022 64-bit** | extract + native build + running | dev files (headers, `.lib`) **and** the runtime DLLs |
| **clang builtin headers** | extract only | any clang version; the checked-in model used clang 19 |
| **NuGet reachable** | first build | restores `ClangSharp` + `libClangSharp.runtime.win-x64` |

Nothing Qt- or clang-shaped is committed (it is all in `.gitignore`), so a clone has to put those
three pieces somewhere. The defaults are the layout this repository was developed in; every one of
them can be pointed elsewhere instead:

| piece | default | override |
|---|---|---|
| Qt dev files (headers + lib) | `<repo>\Qt\6.11.0\msvc2022_64` | `QT_DIR` (build_api.bat), `QTSHARP_QT_INCLUDE` (generator) |
| Qt runtime DLLs + `platforms\qwindows.dll` | `<repo>\Qt\6.11.0\runtime` | `QtRuntimeDir` in `QtSharp.Api.props` |
| clang builtin headers | `<repo>\lib\lib\clang\<version>\include` | `QTSHARP_CLANG_RESOURCE_DIR` |
| Visual Studio | found among the 2022 / 2019 / 18.x installs | `VCVARS` (a `vcvarsall.bat`), `VCVARS_VER` (pin a toolset) |

### Getting the pieces

* **Qt**: take the `msvc2022_64` kit from the official installer (`C:\Qt\6.11.0\msvc2022_64`) and
  copy it to `<repo>\Qt\6.11.0\msvc2022_64`, or set `QT_DIR`/`QTSHARP_QT_INCLUDE` to it. The kit
  must contain the **private** headers (`include\QtCore\6.11.0\QtCore\private\...`) — the official
  binaries ship them.

  `aqt install-qt` cannot fetch 6.11.0 at the moment: Qt's repository no longer serves the top-level
  `desktop/Updates.xml` that aqt reads the archive folder name out of, so it looks for
  `qt6_6110/qt6_6110/` instead of the real `qt6_6110/qt6_6110_msvc2022_64/`. Downloading
  `qtbase-…-X86_64.7z` (plus `d3dcompiler_47-x64.7z`) from
  `…/desktop/qt6_6110/qt6_6110_msvc2022_64/qt.qt6.6110.win64_msvc2022_64/` and unpacking it at the
  kit root gives the same result. `qtbase` covers QtCore/QtGui/QtWidgets/QtNetwork/QtPrintSupport/
  QtXml/QtOpenGL; QtSvg and QtSvgWidgets come from the separate `qtsvg` module (add its
  `include`/`lib` the same way).
* **Qt runtime DLLs**: `<repo>\Qt\6.11.0\runtime` needs the modules the binding links —
  `Qt6Core.dll`, `Qt6Gui.dll`, `Qt6Widgets.dll`, `Qt6Network.dll`, `Qt6Svg.dll`,
  `Qt6SvgWidgets.dll`, `Qt6PrintSupport.dll`, `Qt6Xml.dll`, `Qt6OpenGL.dll`,
  `Qt6OpenGLWidgets.dll` — plus the MSVC runtime DLLs (`msvcp140*.dll`, `vcruntime140*.dll`) and
  `platforms\qwindows.dll`. The easy way is to build any Qt widget app and run `windeployqt` on it.
  All of them must be the *same* Qt version (6.11.x) — they share `Qt6Core`.
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
translation unit, and 34 demos are built and run at the end). It runs, in order:

| step | what it does |
|---|---|
| `[1]` | builds the generator (`gen`) |
| `[2]` | extracts the model from the Qt headers into `out\qtsharp.api.json.new` (**~3 s**) and compares it to the checked-in `model\qtsharp.api.json` — byte for byte |
| `[3]` | runs the code generator **twice** and compares the two outputs |
| `[4]` | compiles the generated C++ wrapper + the runtime into `out\api\QtSharp.Api.dll` |
| `[5]` | builds `demos` (binding + runtime + 34 demos) and runs every demo with `testapp` |
| `[6]` | builds and runs `samples\hello` (its window, `hello selftest`, `hello api-transform`, `hello help`) |

Two gates live in the middle of that: **the model must come out identical** (it is deterministic by
construction, so a difference means the Qt version, the clang headers or the `Roots` table changed)
and **the generator must be reproducible**. If the model legitimately changes, review the diff and
re-run with `-Adopt` to accept it; `-SkipExtract` skips step 2 and 3.

Step 5 is 34 GUI processes back to back, and each demo leaves its window up for about a second so it
can be looked at. On a machine that is being used at the same time, one of them can very occasionally
die while Qt tears the process down — a `0xC000041D` exit *after* its own `[SELFTEST OK]` line. That
is a known flake (it is in `CONTEXT.md`), not a failed assertion; the gate echoes each demo name
before running it so it is obvious where it happened, and simply running it again is the answer.

Once that has succeeded, the ordinary `dotnet build` of any project works too:

```
dotnet build demos\QtSharp.Demos.csproj -c Release
dotnet build testbind\testbind.csproj -c Release
dotnet build samples\hello\Hello.csproj -c Release
```

---

## Run the examples

```
testapp                       # the list of demos (34 names)
testapp api-calculator        # run one
testapp api-analogclock       # QTSHARP_DEMO_MS=8000 keeps it on screen longer

hello                         # samples/hello's own window
hello help                    # the same list
hello api-pathstroke          # the same examples, from the sample
hello selftest                # the sample's own signal/property checks, no window
```

Every example is also a self-test: it drives the generated API, checks what it sees and prints
`[PASS]`/`[FAIL]` lines, ending in `[SELFTEST OK]`. That is what the gate reads, so the examples do
not rot.

---

## Write your own UI

`samples/hello` is the template: one `Program.cs`, one props import, no other plumbing.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Only when you reference QtSharp.Demos: the binding is compiled in there, and compiling it
         twice defines every QtSharp.Api type in two assemblies (CS0433). Drop this line (and the
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
using QtSharp.Api;

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
        window.SetWindowTitle("Hello");
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
* **The application object** is created and run like PyQt does it: `new QApplication(args)`,
  `app.Exec()`, `app.Quit()`, `app.Exit(code)`. From code that has no application reference (a menu
  action), use `QCoreApplication.Instance().Quit()`.
* **Subclassing**: `override` the virtuals the model publishes — `SizeHint`, `PaintEvent`,
  `MousePressEvent`, `KeyPressEvent`, model/delegate virtuals (`RowCount`, `Data`, `Paint`,
  `CreateEditor`), `Runnable.Run`, `FilterAcceptsRow`, … The native side forwards to your override.
* **Declaring your own signal or property** (PyQt's `pyqtSignal`/`pyqtProperty`) works, see
  `samples/hello/HelloCounter.cs`. The declarations must be **static** fields:
  ```csharp
  public static readonly QMetaSignal<int> ValueChangedSignal = new("valueChanged");
  public static readonly QMetaProperty<int> Value = new(
      "value", o => ((HelloCounter)o)._value, (o, v) => ((HelloCounter)o).SetValue(v));

  public event Action<int> ValueChanged            // optional: the same signal as a C# event
  {
      add => ValueChangedSignal.Connect(this, value);
      remove => ValueChangedSignal.Disconnect(this, value);
  }
  ```
  Qt reaches `Value` through `QObject::setProperty`/`property`, just like a moc-generated one.
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
DLL dependencies (Windows x64). The initial Qt 6.11.2 payload includes `rcc`, `uic`, `lrelease`
and `lupdate`; `moc` is also copied when supplied by the source distribution. Consumers do not
need a Qt SDK or Python installed. It is currently built locally, not published to nuget.org.

To produce the package, point the staging script at a directory containing these executables
and matching DLLs (normally a Qt kit's `bin`):

```powershell
powershell -File tools\pack-qt-tools.ps1 -QtToolsSource C:\Qt\6.11.2\msvc2022_64\bin
```

The result is `out/packages/QtSharp.Tools.0.1.0.nupkg`. `-QtVersion` and `-PackageVersion`
override the defaults. Put the upstream license texts/notices in a `QtSharp-ThirdPartyNotices`
subdirectory beside the source tools to include them in the payload. Tools are staged under
`out/qt-tools`; the script checks QtCore's version and runs captured, time-limited startup checks.

In a consumer project:

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Tools" Version="0.1.0" PrivateAssets="all" />
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
| `model/qtsharp.api.json` | the checked-in API model — the interface between the two halves |
| `gen/` | the generator: `ApiModel.cs` (model shape, `Roots` table, vocabulary), `ApiModel2.cs` (the libclang front end), `ApiCodeGen.cs` (model → C# + C++) |
| `native/` | the hand-written runtime (`QtSharpApiRuntime.cpp`: moc signal forwarder, probes) and the crash tracer |
| `runtime/` | the managed runtime published with the binding (`ApiRuntime.cs`, `ApiApplication.cs`) |
| `demos/` | `QtSharp.Demos.csproj` — the binding, the runtime and the 34 demos; `ApiDemos.cs` is the name → window table |
| `testbind/` | `testapp`, the test entry point (13 lines: hand the argument to `ApiDemos`) |
| `samples/hello/` | the sample / trial entry point |
| `tools/regen-api.ps1` | the build and the gate |
| `tools/clangprobe/` | the probe used to validate the libclang front end (not part of the build) |
| `tools/disasm.py` | capstone-based RVA disassembly helper |
| `QtSharp.Api.props` | the one import a consumer project needs |
| `CONTEXT.md` | the design log: every round of work, what was measured, what was learned — long, but that is where the reasoning is |

---

## Known limitations

* **Windows x64 and Qt 6.11.0 only** — the model is extracted from that kit, and the wrapper is
  MSVC-compiled.
* **The bound modules** are QtCore, QtGui, QtWidgets, QtNetwork (host addressing, local sockets
  `QLocalServer`/`QLocalSocket`, TCP/UDP, the HTTP client), QtSvg/QtSvgWidgets, QtPrintSupport
  (`QPrinter`, print dialogs/preview), QtXml (the `QDom*` DOM; `QXmlStreamReader/Writer` are in
  QtCore) and QtOpenGL/QtOpenGLWidgets (`QOpenGLWidget`, `QOpenGLContext`, shader/buffer classes).
  **Not bound**: QtSql (no dev files in the kit), QtMultimedia, QtQml/QtQuick, QtPdf, QtWebEngine.
* **Not ported**: the RHI examples (they need `QRhi` and prebuilt shaders) and Window Embedding.
  PDF *output* works through `QPrinter` (see `api-docformats`); PDF *reading* needs the QtPdf
  module, which is not bound.
* `api-windowflags` is written and passes all 115 assertions, but it dies about three runs in ten
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
