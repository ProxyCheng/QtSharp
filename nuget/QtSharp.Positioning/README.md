# QtSharp.Positioning

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

Qt Positioning for .NET, generated from the Qt 6.12 headers the same way `QtSharp.Api` is:
`QGeoCoordinate` and the geo value types (`QGeoRectangle`, `QGeoCircle`, `QGeoPath`,
`QGeoPolygon`, `QGeoShape`, `QGeoAddress`, `QGeoLocation`), plus `QGeoPositionInfo`,
`QGeoSatelliteInfo` and the position/satellite sources.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.24.0" />
  <PackageReference Include="QtSharp.Positioning.win-x64" Version="0.24.0" />
</ItemGroup>
```

```csharp
using QtSharp.QtPositioning;

using var berlin = new QGeoCoordinate(52.52, 13.405);
using var paris = new QGeoCoordinate(48.8566, 2.3522);
double km = berlin.DistanceTo(paris) / 1000.0;
double bearing = berlin.AzimuthTo(paris);
```

The package depends on `QtSharp.Api` (and through it `QtSharp.Runtime`/`QtSharp.Qt6`) and on
`QtSharp.Positioning.Qt6`, the companion package that carries `Qt6Positioning.dll`. Position
sources need a platform positioning plugin; without one, `QGeoPositionInfoSource.AvailableSources`
is empty and the value types still work.
