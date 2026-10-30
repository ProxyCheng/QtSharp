# QtSharp.Api

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

Qt for .NET, organized the way PyQt6 is: one install, one assembly and one C# namespace per Qt
module.

**Pre-1.0:** the API may change substantially between releases. Source and binary compatibility
are not guaranteed. Pin package versions and test your application before upgrading.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.25.0" />
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

That is the whole setup: no Qt SDK, no Visual Studio C++ workload, and no extra MSBuild
properties. `QtSharp.Api` brings `QtSharp.Runtime` (the managed runtime and the native
`QtSharp.Api.dll` glue) and `QtSharp.Qt6` (the Qt DLLs and the platform plugin); both copy their
payload next to your executable on build and publish.

## What is inside

| assembly | namespace | Qt module |
|---|---|---|
| `QtSharp.QtCore` | `QtSharp.QtCore` | QtCore (and the `Qt` namespace enums, the `Qt` static class) |
| `QtSharp.QtGui` | `QtSharp.QtGui` | QtGui |
| `QtSharp.QtWidgets` | `QtSharp.QtWidgets` | QtWidgets |
| `QtSharp.QtNetwork` | `QtSharp.QtNetwork` | QtNetwork |
| `QtSharp.QtSvg` | `QtSharp.QtSvg` | QtSvg |
| `QtSharp.QtSvgWidgets` | `QtSharp.QtSvgWidgets` | QtSvgWidgets |
| `QtSharp.QtPrintSupport` | `QtSharp.QtPrintSupport` | QtPrintSupport |
| `QtSharp.QtXml` | `QtSharp.QtXml` | QtXml |
| `QtSharp.QtOpenGL` | `QtSharp.QtOpenGL` | QtOpenGL |
| `QtSharp.QtOpenGLWidgets` | `QtSharp.QtOpenGLWidgets` | QtOpenGLWidgets |

A type from another module is written fully qualified in generated code
(`QtSharp.QtGui.QColor` inside `QtSharp.QtWidgets`), so the namespaces cannot collide.
`using QtSharp.QtWidgets;` is enough for a typical widget application because the QtWidgets
assembly builds on QtGui and QtCore; add their usings when you name their types directly.

The value structs follow their Qt header: `QPoint`, `QPointF`, `QSize`, `QRect`, `QDate`, `QTime`
are in `QtSharp.QtCore`; `QColor`, `QVector2D/3D/4D`, `QQuaternion` are in `QtSharp.QtGui`.

Addons are separate packages on top of this one: `QtSharp.Charts` and `QtSharp.Qt3D`, each with a
companion `*.Qt6` package carrying its Qt runtime.
