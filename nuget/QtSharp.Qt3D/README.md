# QtSharp.Qt3D

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

Qt 3D for .NET, generated from the Qt 6.12 headers the same way `QtSharp.Api` is. The package
contains **six assemblies, one per Qt module**, each with its own namespace:

| assembly / namespace | Qt module | builds on |
|---|---|---|
| `QtSharp.Qt3DCore` | Qt3DCore | QtCore, QtGui |
| `QtSharp.Qt3DRender` | Qt3DRender | Qt3DCore, QtCore, QtGui, QtOpenGL |
| `QtSharp.Qt3DInput` | Qt3DInput | Qt3DCore, QtCore, QtGui |
| `QtSharp.Qt3DLogic` | Qt3DLogic | Qt3DCore, QtCore |
| `QtSharp.Qt3DAnimation` | Qt3DAnimation | Qt3DCore, Qt3DRender, QtCore, QtGui |
| `QtSharp.Qt3DExtras` | Qt3DExtras | all of the above, QtWidgets, QtOpenGL |

This mirrors `PyQt6-3D` / `PySide6-Addons`: one package to install, separate importable modules.
The package depends on `QtSharp.Api` and on `QtSharp.Qt3D.Qt6`, the companion package that carries
the Qt3D libraries and the renderer/geometry/scene plugins.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.23.0" />
  <PackageReference Include="QtSharp.Qt3D.win-x64" Version="0.23.0" />
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
return app.Exec();
```

## A note on names

Qt3D's `QTransform`, `QBuffer`, `QAction`, `QKeyEvent`, `QMouseEvent` and `QWheelEvent` have the
same names as QtGui/QtCore/QtWidgets classes in `QtSharp.Api`. Importing both namespaces makes
those names ambiguous, so alias the Qt3D one:

```csharp
using QTransform3D = QtSharp.Qt3DCore.QTransform;
```

## License

QtSharp's own source is MIT. Qt 3D is available under the LGPLv3, the GPLv2, or a commercial Qt
license; it lives in its own package because it is an optional addon family, not because of the
license. Redistributing this package means redistributing the Qt 3D runtime; see the Qt license
terms and `NOTICE.md` in the repository.
