using System.Collections.Generic;
using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts PointConfigurationWidget: one point of a line series given a size, a label
    /// visibility and a label format of its own, through QXYSeries' per-point configuration.
    ///
    /// The example's colour control is represented by setting the configuration's Color entry
    /// directly: QColor converts to QVariant through the generated implicit operator, so the value
    /// is a real QColor variant rather than an opaque managed object. Size (double), label
    /// visibility (bool) and the label format (QString) cross the same way.
    /// </summary>
    [Demo("qtcharts-pointconfiguration")]
public static class PointConfiguration
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series = new QLineSeries();
            series.SetPointsVisible(true);
            foreach (var p in new[] { new QPointF(0, 7), new QPointF(2, 4), new QPointF(3, 5),
                                      new QPointF(7, 4), new QPointF(10, 5), new QPointF(11, 1),
                                      new QPointF(13, 3), new QPointF(17, 6), new QPointF(18, 3),
                                      new QPointF(20, 2) })
                series.Append(p);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.CreateDefaultAxes();
            chart.SetTitle("Select points with mouse click");
            ChartsAssertions.HideLegend(chart);

            var view = ChartsAssertions.Present(chart);

            check.Check(series.Count() == 10, $"ten points (got {series.Count()})");
            check.Check(series.PointsVisible, "the points are visible, as the example asks");

            // Selecting a point the way the example's clicked handler does.
            series.SelectPoint(4);
            var selected = series.SelectedPoints();
            check.Check(selected.Length == 1 && selected[0] == 4,
                $"point 4 is selected (got [{string.Join(",", selected)}])");
            check.Check(series.IsPointSelected(4), "and isPointSelected agrees");

            // Its configuration starts empty - Qt falls back to the series' own settings.
            var configuration = series.pointConfiguration(4);
            check.Check(configuration is IDictionary<QXYSeries.PointConfiguration, object>,
                "pointConfiguration() comes back as a dictionary");
            check.Check(configuration.Count == 0,
                $"an untouched point has no configuration of its own (got {configuration.Count} entries)");

            // The example's four controls: colour, size, label visibility and format.
            configuration[QXYSeries.PointConfiguration.Color] = new QColor(190, 30, 45);
            configuration[QXYSeries.PointConfiguration.Size] = 12.0;
            configuration[QXYSeries.PointConfiguration.LabelVisibility] = true;
            configuration[QXYSeries.PointConfiguration.LabelFormat] = "%.2f";
            series.SetPointConfiguration(4, configuration);

            var readBack = series.pointConfiguration(4);
            check.Check(readBack.Count == 4, $"the configuration sticks (got {readBack.Count} entries)");
            check.Check(readBack.ContainsKey(QXYSeries.PointConfiguration.Size),
                "the size is in there");
            var color = readBack[QXYSeries.PointConfiguration.Color];
            check.Check(color is QColor c && c.Red() == 190 && c.Green() == 30
                        && c.Blue() == 45,
                "the colour survives as a real QColor variant");
            check.Check((double)readBack[QXYSeries.PointConfiguration.Size] == 12.0,
                $"and it reads back as 12 (got {readBack[QXYSeries.PointConfiguration.Size]})");
            check.Check((bool)readBack[QXYSeries.PointConfiguration.LabelVisibility],
                "the label visibility reads back true");
            check.Check(readBack[QXYSeries.PointConfiguration.LabelFormat] as string == "%.2f",
                $"the label format round-trips (got '{readBack[QXYSeries.PointConfiguration.LabelFormat]}')");

            // Changing one entry keeps the others.
            var update = series.pointConfiguration(4);
            update[QXYSeries.PointConfiguration.Size] = 20.0;
            series.SetPointConfiguration(4, update);
            check.Check((double)series.pointConfiguration(4)[QXYSeries.PointConfiguration.Size] == 20.0,
                "the size can be changed on its own");

            // Clearing it puts the point back to the series' defaults.
            series.ClearPointConfiguration(4);
            check.Check(series.pointConfiguration(4).Count == 0, "clearPointConfiguration empties it");

            check.Check(series.pointConfiguration(1).Count == 0, "the other points were never touched");

            series.DeselectAllPoints();
            check.Check(series.SelectedPoints().Length == 0, "deselectAllPoints clears the selection");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"the line and its points are drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
