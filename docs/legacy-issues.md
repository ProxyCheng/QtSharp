# Migration notes for ddobrev/QtSharp users

This QtSharp is an independent implementation for Qt 6 and .NET 10. It is not affiliated
with [ddobrev/QtSharp](https://github.com/ddobrev/QtSharp) and does not preserve the older
project's API or build commands.

**The API may change substantially before 1.0. Source and binary compatibility are not
guaranteed between releases.** Pin package versions and test your application when upgrading.

## Common API differences

| Earlier question | This implementation |
|---|---|
| [#60](https://github.com/ddobrev/QtSharp/issues/60), [#79](https://github.com/ddobrev/QtSharp/issues/79): QStringList construction and append | Pass a C# `string[]` to list-taking APIs. |
| [#81](https://github.com/ddobrev/QtSharp/issues/81): objects in QVariant | Variant-taking APIs accept and return `object`. Existing QObject wrappers and ordinary managed objects retain their identity when stored and retrieved. General `qobject_cast<T>` support is not available. |
| [#80](https://github.com/ddobrev/QtSharp/issues/80): nested layout disposal | Qt parenting determines ownership; deleting a parent invalidates the child wrappers. The nested-layout reproduction passes with the Windows v0.19.0 package. |
| [#57](https://github.com/ddobrev/QtSharp/issues/57): QTreeWidget.ItemClicked | Generated signal handlers wrap items with `FromHandle`. The compatibility sample checks item identity, event delivery and disconnection. Its GUI scenario has not completed local runtime verification. |
| [#61](https://github.com/ddobrev/QtSharp/issues/61): QObject.Sender | No sender-context API is exposed. Capture the emitter in the event handler's closure. |
| [#50](https://github.com/ddobrev/QtSharp/issues/50): findChild/findChildren | Generic lookup methods are not exposed. Use `Children()` for manual traversal. C# type checks alone do not implement native downcasts. |

Multiple header labels:

```csharp
tree.SetColumnCount(2);
tree.SetHeaderLabels(new[] { "Key", "Value" });
```

Objects in Qt dynamic properties:

```csharp
using var target = new QTimer();
using var storage = new QTimer();
storage.SetProperty("target", target);
var sameTimer = storage.Property("target") as QTimer;
```

Item-data APIs use the same object conversion:

```csharp
combo.AddItem("button", button);
var sameButton = combo.ItemData(0, (int)Qt.ItemDataRole.UserRole) as QPushButton;
```

These examples use objects that already have managed wrappers. They do not cover discovery
of the most-derived managed type for an unknown native QObject.

To identify the emitter in a handler, capture it explicitly:

```csharp
var edit = new QLineEdit();
edit.TextChanged += text => HandleEdit(edit, text);
```

Here `HandleEdit` is application code. This is a workaround for the missing `Sender` API.

## Build and platform differences

| Earlier question | Current support |
|---|---|
| Windows x64 / MSVC ([#64](https://github.com/ddobrev/QtSharp/issues/64), [#42](https://github.com/ddobrev/QtSharp/issues/42)) | `QtSharp.Api.win-x64`, built with MSVC. Qt strings use C# `string`. |
| Linux ([#9](https://github.com/ddobrev/QtSharp/issues/9), [#45](https://github.com/ddobrev/QtSharp/issues/45)) | `QtSharp.Api.linux-x64`, built with g++ for x64/glibc. System X11/OpenGL libraries are still required. |
| CppSharp generation failures ([#82](https://github.com/ddobrev/QtSharp/issues/82), [#8](https://github.com/ddobrev/QtSharp/issues/8)) | This generator uses ClangSharp/libclang and checked-in models. Application developers can use release packages without generating or compiling native bindings. Old Qt 4/5 and MinGW configurations are unsupported. |
| Missing inline DLLs ([#31](https://github.com/ddobrev/QtSharp/issues/31), [#52](https://github.com/ddobrev/QtSharp/issues/52)) | Platform packages deploy this implementation's native glue and Qt libraries; they do not use the old inline DLLs. |
| Setup ([#19](https://github.com/ddobrev/QtSharp/issues/19), [#48](https://github.com/ddobrev/QtSharp/issues/48)) | Start with the [quick start](../README.md#run-your-first-application) or [Hello World](../samples/hello/Hello.csproj). Building the binding itself requires the [development prerequisites](../README.md#prerequisites). |

## Unsupported requests

Qt Quick/QML, QML invocation and type registration are not supported
([#40](https://github.com/ddobrev/QtSharp/issues/40),
[#55](https://github.com/ddobrev/QtSharp/issues/55),
[#56](https://github.com/ddobrev/QtSharp/issues/56)). Neither are macOS, Android, iOS,
Sailfish or Tizen ([#59](https://github.com/ddobrev/QtSharp/issues/59),
[#78](https://github.com/ddobrev/QtSharp/issues/78),
[#84](https://github.com/ddobrev/QtSharp/issues/84)); only the Linux x64/glibc part of #84
is covered by current platform packages.

There is no supported process-injection or external Qt Spy API
([#46](https://github.com/ddobrev/QtSharp/issues/46)). GitHub release packages do not imply
inclusion in Linux distribution repositories ([#69](https://github.com/ddobrev/QtSharp/issues/69)).
Models are checked in and CI artifacts include generated C#, but release archives contain
NuGet packages, not a generated-source bundle; Linux 32-bit is unsupported
([#66](https://github.com/ddobrev/QtSharp/issues/66)).

## Compatibility checks

[samples/compatibility](../samples/compatibility/Compatibility.csproj) checks the API
differences above using release packages. Restore from the extracted platform archive,
replacing the local path below:

```powershell
dotnet restore samples/compatibility/Compatibility.csproj --source C:/Downloads/QtSharp-packages
dotnet build samples/compatibility/Compatibility.csproj --no-restore
dotnet run --project samples/compatibility/Compatibility.csproj --no-build -- layouts
dotnet run --project samples/compatibility/Compatibility.csproj --no-build -- variants
dotnet run --project samples/compatibility/Compatibility.csproj --no-build -- api-gaps
dotnet run --project samples/compatibility/Compatibility.csproj --no-build -- strings
dotnet run --project samples/compatibility/Compatibility.csproj --no-build -- tree
```

On Linux, use your extracted package path. `strings` and `tree` initialize QApplication and
need a working Qt platform plugin. `tree` emits the native item signal; it does not simulate
a mouse click. The other checks do not initialize a graphical application.

Checked on **2026-10-28**, using the checksum-verified **Windows v0.19.0** release archive,
commit `11a9b5ae25ea026b2e34c2bf0d02de4780bf77d0`:

| Check | Result |
|---|---|
| `layouts` | Passed: parent and child layouts are invalidated; repeated disposal and finalization complete. |
| `variants` | Passed: QObject and ordinary managed values preserve reference identity through dynamic properties. |
| `api-gaps` | Confirmed `Sender`, `sender`, `FindChild` and `FindChildren` are absent from the QObject member surface. |
| `strings`, `tree` | Compiled, but the local run timed out during QApplication initialization, before the assertions. The cause was not established. |

See also the [Windows and Linux release checks](https://github.com/ProxyCheng/QtSharp/actions/runs/40489656599)
and the broader [example and test suite](conformance.md).
