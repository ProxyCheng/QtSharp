# QtSharp.Charts

Qt Charts for .NET, generated from the Qt 6.11 headers the same way `QtSharp.Api` is: C# classes,
properties, signals and virtuals over the native Qt Charts library.

```xml
<ItemGroup>
  <PackageReference Include="QtSharp.Api" Version="0.1.0" />
  <PackageReference Include="QtSharp.Charts" Version="0.1.0" />
</ItemGroup>
```

```csharp
using QtSharp.Api;
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

The package contains the QtCharts binding, its native glue (`QtSharp.Charts.Native.dll`) and
`Qt6Charts.dll`. It references `QtSharp.Api` and does not repeat any of its types.

## License

QtSharp's own source is MIT. Qt Charts is available under the GPLv3 or a commercial Qt license
(not the LGPL), which is why it lives in a separate package instead of `QtSharp.Api`.
Redistributing this package means redistributing Qt Charts; see the Qt license terms and
`NOTICE.md` in the repository.
