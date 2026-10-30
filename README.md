# QtSharp

[![CI](https://github.com/ProxyCheng/QtSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/ProxyCheng/QtSharp/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/ProxyCheng/QtSharp?display_name=tag)](https://github.com/ProxyCheng/QtSharp/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Target: Windows x64 · Linux x64](https://img.shields.io/badge/target-Windows%20x64%20%C2%B7%20Linux%20x64-0078D4.svg)](#prerequisites)

[English](README.md) | [简体中文](README.zh-CN.md)

**Build Qt desktop applications in C# on Windows and Linux.**

QtSharp aims to make Qt as convenient to use from C# as PyQt/PySide are from Python, adapting
the API and module design to C# and the .NET ecosystem. It provides bindings for Qt Widgets,
painting, networking and optional modules such as Charts, Qt3D and Multimedia.

**Pre-1.0:** the API may change substantially between releases. Source and binary compatibility
are not guaranteed. Pin package versions and read the [changelog](CHANGELOG.md) before upgrading.

Current targets: **.NET 10, Qt 6.12.0, Windows x64 and Linux x64 (glibc)**.

[Get started](#run-your-first-application) · [Examples](#examples) ·
[API guide](docs/api-guide.md) · [Releases](https://github.com/ProxyCheng/QtSharp/releases)

## Install the binding

Install the **.NET 10 SDK**, then download **QtSharp-0.25.0-win-x64.zip** or
**QtSharp-0.25.0-linux-x64.zip** from [v0.25.0](https://github.com/ProxyCheng/QtSharp/releases/tag/v0.25.0).
Extract the archive into a local NuGet source folder, keeping all its packages together.
The packages include the native binding and Qt runtime; application developers do not need
a Qt SDK or C++ compiler.

### Run your first application

Create a console application:

```text
dotnet new console -n HelloQt --framework net10.0
cd HelloQt
```

Add the package for your build platform, replacing the source path with your extracted folder.

Windows / PowerShell:

```powershell
dotnet add package QtSharp.Api.win-x64 --version 0.25.0 --source C:/Downloads/QtSharp-packages
```

Linux:

```bash
dotnet add package QtSharp.Api.linux-x64 --version 0.25.0 --source /home/you/Downloads/QtSharp-packages
```

Replace `Program.cs` with:

```csharp
using System;
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

Run `dotnet run` to open the window. Linux also needs the system
[X11/OpenGL dependencies](docs/development.md#linux) and a working graphical desktop.

If you have cloned this repository, the same program is in [samples/hello](samples/hello/Hello.csproj):

```powershell
dotnet restore samples/hello/Hello.csproj --source C:/Downloads/QtSharp-packages
dotnet run --project samples/hello/Hello.csproj --no-restore
```

On Linux, replace the local source path. The sample selects the package from the build host.

Packages are distributed through **GitHub Releases**, not nuget.org. The `QtSharp` package
on nuget.org belongs to a different project.

## Examples

For an application using QtSharp, see [PolyMount](https://github.com/ProxyCheng/PolyMount),
also maintained by ProxyCheng.

The repository includes C# ports of official Qt examples:

| Use case | Source |
|---|---|
| Desktop controls and item views | [Widgets Gallery](examples/QtWidgets/WidgetsGallery.cs) |
| Text editing and printing | [Notepad](examples/QtPrintSupport/Notepad.cs) |
| Custom drawing and mouse input | [Scribble](examples/QtWidgets/Scribble.cs) |
| Sorting and filtering data | [Custom Sort/Filter](examples/QtWidgets/CustomSortFilter.cs) |
| Charts | [Charts examples](examples/Charts) |
| 3D scenes | [Basic Shapes](examples/Qt3D/BasicShapesDemo.cs) |
| Media playback | [Player](examples/QtMultimedia/Player.cs) |

These examples use the repository's [development build](docs/development.md). The gate runs them
as its conformance suite: **73 examples (1544 assertions)** in ten executables — the base-module
ports, the Qt Charts ports and the three Qt 3D scenes — plus **48 binding tests (1164 assertions)**.
The WebView2 test asserts only under a real window; the offscreen gate reports it as skipped.
See [conformance coverage](docs/conformance.md) for the scenarios they check.

Current coverage: **483 classes / 9947 methods / 1901 virtuals / 517 enums / 834 Q_PROPERTY
properties / 475 signals / 72 namespace functions** (QtCore, QtGui, QtWidgets, QtNetwork, QtSvg,
QtSvgWidgets, QtPrintSupport, QtOpenGL, QtOpenGLWidgets).

## Module scope and .NET integration

The design priorities are:

- Make signals, properties, subclassing and object lifetime straightforward to use.
- Use C# properties, events, generics, collections and delegates where they improve the API.
- Reuse .NET libraries and infrastructure, with adapters for QtSharp components where needed.

QtSharp does not aim to bind every Qt module. Where .NET has mature libraries that fit C#
applications well, we prefer those libraries:

| Qt module intentionally omitted | .NET alternative | QtSharp integration |
|---|---|---|
| QtXml | `System.Xml` | QtCore's `QXmlStreamReader`/`QXmlStreamWriter` remain available. |
| QtSql | ADO.NET providers | `QtSharp.Sql` exposes query/table models backed by a `DbConnection`. |
| QtWebSockets | `ClientWebSocket` | Use the .NET API directly; no Qt-facing adapter is provided yet. |
| QtWebEngine | System web view (Edge WebView2 on Windows) | Not bound. `QtSharp.WebView` hosts WebView2 in a `QWidget` (Windows x64); Qt WebView is the cross-platform Qt module if needed. See [web view options](docs/webview.md). |

`QtSharp.Sql`, [`StreamIODevice`](runtime/QtCore/IoDeviceInterop.cs) (a .NET `Stream` as
`QIODevice`), and the System.IO/BCL bridges under [`runtime/QtCore`](runtime/QtCore)
(`QIODevice.AsStream`, `QDir`/`QFileInfo` ↔ `DirectoryInfo`/`FileInfo`, `QDateTime` ↔
`DateTime`, ...) already use this approach. Further adapters may make C# libraries
available through the model, IO or other interfaces used by QtSharp components.

## Comparison with similar projects

| Project | Focus | Relationship to QtSharp |
|---|---|---|
| [ddobrev/QtSharp](https://github.com/ddobrev/QtSharp) | Earlier C# Qt bindings generated with CppSharp. | This project is an independent implementation with a different generator and runtime. See the [migration notes](docs/legacy-issues.md). |
| [Qt Bridges](https://doc.qt.io/qtbridges/) | Connecting application logic in C# and other languages to QML/Qt Quick interfaces. | QtSharp focuses on general-purpose C# bindings for Qt Widgets (`QWidget`) and other selected Qt APIs. |

QtSharp is not affiliated with ddobrev/QtSharp and does not preserve its source or binary API.
QtSharp does not currently bind QtQml/QtQuick. For a QML/Qt Quick UI with application logic
written in C#, you can use the official [Qt Bridge for C#](https://github.com/qt/qtbridge-csharp).

## Packages

Platform package names end in `.win-x64` or `.linux-x64`. The table uses their base names.
The sample projects and package examples use v0.25.0.

| Package | Contents |
|---|---|
| `QtSharp.Api` | The nine base module assemblies: Core, Gui, Widgets, Network, Svg, SvgWidgets, PrintSupport, OpenGL and OpenGLWidgets. |
| `QtSharp.Runtime`, `QtSharp.Qt6` | Binding and Qt runtime dependencies, installed automatically. |
| `QtSharp.Charts` | Charts and data visualization. |
| `QtSharp.Qt3D` | Qt3DCore, Render, Input, Logic, Animation and Extras. |
| `QtSharp.Multimedia` | Media playback, capture and device APIs. |
| `QtSharp.Positioning` | Coordinates, shapes, positioning and satellite APIs. |
| `QtSharp.Sql` | ADO.NET-backed query and table models. |
| `QtSharp.WebView` | A `QWidget` host for Microsoft Edge WebView2 (Windows x64 only); web content without QtWebEngine. |

Optional binding packages pull in their runtime dependencies. `QtSharp.Tools` is a separate
Windows x64 package, without a platform suffix, containing Qt command-line tools and MSBuild
resource support. See the [tools guide](tools/QtSharp.Tools/README.md) and
[package architecture](docs/module-architecture.md) for details.

## Known limitations

- Supported platforms are Windows x64 and Linux x64/glibc. macOS, ARM64 and musl are not packaged.
- QtQml/QtQuick, QtPdf and QtWebEngine are not bound. PDF output through `QPrinter` is supported.
  `QtSharp.WebView` (Edge WebView2) is Windows x64 only.
- Single-file publishing is not supported on Linux.
- APIs using C++ `long` retain 32-bit values on Linux; use the corresponding 64-bit APIs where needed.
- Some native window teardown scenarios remain unstable.

See [detailed limitations](docs/limitations.md) before choosing a deployment approach.

## Prerequisites

Applications need the .NET 10 SDK and the runtime dependencies described above.
Building QtSharp itself also needs a Qt development kit and a native compiler. See the
[development guide](docs/development.md) for setup, generation, testing and example commands.

## Contributing

Bug reports, fixes and additional example ports are welcome. Please include a minimal
reproduction with [bug reports](https://github.com/ProxyCheng/QtSharp/issues), and read
[CONTRIBUTING.md](CONTRIBUTING.md) before working on the binding.

## License and third-party notices

QtSharp's original source code is available under the [MIT License](LICENSE). Qt and other
dependencies have their own licenses. See [NOTICE.md](NOTICE.md) before redistributing binaries.
