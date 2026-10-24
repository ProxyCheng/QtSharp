# QtSharp.Qt3D

Qt 3D for .NET, generated from the Qt 6.11 headers the same way `QtSharp.Api` is: real C#
classes, properties, signals and virtuals over the native Qt3D libraries.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api" Version="0.1.0" />
  <PackageReference Include="QtSharp.Qt3D" Version="0.1.0" />
</ItemGroup>
```

```csharp
using QtSharp.Api;
using QtSharp.Qt3D;

var app = new QGuiApplication(args);
var view = new Qt3DWindow();
var root = new QEntity();
var cube = new QEntity(root);
cube.AddComponent(new QCuboidMesh());
cube.AddComponent(new QPhongMaterial());
view.SetRootEntity(root);
view.Show();
return app.Exec();
```

The package contains all six Qt3D modules in one assembly and one C# namespace: Qt3DCore,
Qt3DRender, Qt3DInput, Qt3DLogic, Qt3DAnimation and Qt3DExtras. It references `QtSharp.Api` and
does not repeat any of its types.

## A note on names

Qt3D's `QTransform`, `QBuffer`, `QAction`, `QKeyEvent`, `QMouseEvent` and `QWheelEvent` have the
same names as QtGui/QtCore/QtWidgets classes in `QtSharp.Api`. Inside the binding the Qt3D class
shadows the core one, so the two are unambiguous in a file that declares its namespace as
`QtSharp.Qt3D`; in a file that imports both namespaces, alias the Qt3D one:

```csharp
using QTransform3D = QtSharp.Qt3D.QTransform;
```

## License

QtSharp's own source is MIT. Qt 3D is available under the LGPLv3, the GPLv2, or a commercial Qt
license; it lives in its own package because it is an optional addon family, not because of the
license. Redistributing this package means redistributing the Qt 3D runtime; see the Qt license
terms and `NOTICE.md` in the repository.
