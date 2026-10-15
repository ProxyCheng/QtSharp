# QtSharp.Api

Qt for .NET: the generated C# binding, the native glue, and the Qt runtime — in one package.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api" Version="0.1.0" />
</ItemGroup>
```

```csharp
using QtSharp.Api;

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
properties. The package brings `QtSharp.Api.dll` (the native wrapper), the Qt runtime DLLs and
`platforms\qwindows.dll`, and copies them next to your executable on build and publish.

## What is inside

| | |
|---|---|
| `lib/net10.0/QtSharp.dll` | the managed binding (`QtSharp.Api` namespace) and the runtime |
| the native payload | `QtSharp.Api.dll`, the Qt 6 modules and the Windows platform plugin |

The managed assembly is named `QtSharp.dll` because the native wrapper already owns
`QtSharp.Api.dll`; the namespace you program against is `QtSharp.Api`.

## Notes

* **Windows x64 only.** The native wrapper is MSVC-compiled and the process must be x64;
  the package sets `PlatformTarget` to `x64` when your project does not set one.
* Qt is used under its own license. See the repository's `NOTICE.md` before redistributing
  binaries that include these Qt libraries.
* API references, prerequisites, examples and troubleshooting live in the repository
  readme: https://github.com/ProxyCheng/QtSharp
