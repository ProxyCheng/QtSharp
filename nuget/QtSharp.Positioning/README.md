# QtSharp.Positioning

Qt Positioning for .NET, generated from the Qt 6.12 headers the same way `QtSharp.Api` is:
`QGeoCoordinate` and the geo value types (`QGeoRectangle`, `QGeoCircle`, `QGeoPath`,
`QGeoPolygon`, `QGeoShape`, `QGeoAddress`, `QGeoLocation`), plus `QGeoPositionInfo`,
`QGeoSatelliteInfo` and the position/satellite sources.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api" Version="0.16.0" />
  <PackageReference Include="QtSharp.Positioning" Version="0.16.0" />
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
