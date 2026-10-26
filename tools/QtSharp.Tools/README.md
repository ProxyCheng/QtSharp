# QtSharp.Tools

Windows x64 Qt development tools, with their native dependencies. No Qt SDK or Python
installation is needed to run the packaged tools. The initial payload contains Qt 6.12.0
`rcc`, `uic`, `lrelease` and `lupdate`; `moc` is included when present in the source distribution.

```xml
<PackageReference Include="QtSharp.Tools" Version="0.3.0" PrivateAssets="all" />
<QtResource Include="assets\sample.qrc" />
```

Builds compile `QtResource` items to binary `.rcc` files and copy them to the output and publish
directories, retaining their project-relative directory (for example `assets/sample.rcc`).
Register the file using `QResource.RegisterResource(path)` before accessing `:/` paths.
Resource payload changes are picked up on every build, even if the `.qrc` file is unchanged.

Run a tool through MSBuild:

```text
dotnet msbuild MyApp.csproj -t:QtSharpRunTool -p:QtSharpTool=rcc -p:QtSharpToolArguments=--help
```

`QtSharpToolsDir` exposes the packaged executables; `QtSharpRcc` can override the resource compiler.
`QtSharpToolArguments` is a command-line string: use ordinary command-line quoting for paths.
MSBuild captures tool output, avoiding Qt's Windows fallback message boxes.

`uic` is the upstream native tool and generates C++, not C#. `moc` likewise serves native C++
code; C# signals/properties use QtSharp's managed meta-object declarations. The package does
not currently provide Designer, Linguist GUI, deployment tools or a C# `.ui` compiler.

Qt tools remain subject to their upstream Qt licenses. Distributors must include the license
texts and notices applicable to their chosen Qt distribution in the staged payload.
