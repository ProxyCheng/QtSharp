# QtSharp.Charts

> The package id carries the platform: the snippets below use the `win-x64` variant. On
> Linux, use the matching `.linux-x64` package (`QtSharp.Api.linux-x64`, ...).

Qt Charts for .NET, generated from the Qt 6.12 headers the same way `QtSharp.Api` is: C# classes,
properties, signals and virtuals over the native Qt Charts library.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api.win-x64" Version="0.22.0" />
  <PackageReference Include="QtSharp.Charts.win-x64" Version="0.22.0" />
</ItemGroup>
```

```csharp
using QtSharp.QtCore;
using QtSharp.QtWidgets;
using QtSharp.Charts;

var chart = new QChart();
chart.SetTitle("Hello");
var series = new QLineSeries();
series.Append(0, 6);
series.Append(2, 4);
chart.AddSeries(series);
chart.CreateDefaultAxes();

var view = new QChartView(chart);
view.Resize(480, 320);
view.Show();
```

The package contains the QtCharts binding and its native glue (`QtSharp.Charts.Native.dll`). It
depends on `QtSharp.Api` for the base modules and on `QtSharp.Charts.Qt6`, the companion package
that carries `Qt6Charts.dll`; neither is repeated here.

## What is bound

`QChart`, `QChartView`, `QPolarChart`; the series families (`QXYSeries`, `QLineSeries`,
`QSplineSeries`, `QScatterSeries`, `QAreaSeries`, the bar family with `QBarSet`, `QPieSeries` with
`QPieSlice`, box plots and candlesticks); the axes (`QValueAxis`, `QCategoryAxis`,
`QBarCategoryAxis`, `QDateTimeAxis`, `QLogValueAxis`); and the legend with its markers. The list is
`model/qtsharp.charts.json` in the repository, and the 38 ported official examples under
`examples/Charts/` exercise it (571 assertions) in the gate, CI and release.

## Why it is a separate package

**Qt Charts is GPLv3-or-commercial, not LGPL.** Keeping it out of `QtSharp.Api` is what stops
everyone who uses the core binding from inheriting that license; it is also the same split PyQt6
makes with `PyQt6-Charts`. See `NOTICE.md` in the repository and Qt's own terms before you ship.

## License

QtSharp's own source is MIT. Qt Charts is available under the GPLv3 or a commercial Qt license
(not the LGPL), which is why it lives in a separate package instead of `QtSharp.Api`.
Redistributing this package means redistributing Qt Charts; see the Qt license terms and
`NOTICE.md` in the repository.
