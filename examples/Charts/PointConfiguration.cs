﻿using System.Collections.Generic;
using QtSharp.Charts;

namespace QtSharp.Charts.Examples
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
    [Example("qtcharts-pointconfiguration")]
public static class PointConfiguration
    {
        public static int Run(ExampleContext example)
        {
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

            var view = ChartsAssertions.Present(example, chart);
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(series.Count() == 10, $"ten points (got {series.Count()})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series.PointsVisible, "the points are visible, as the example asks");
            #endif

            // Selecting a point the way the example's clicked handler does.
            series.SelectPoint(4);
            var selected = series.SelectedPoints();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(selected.Length == 1 && selected[0] == 4,
                $"point 4 is selected (got [{string.Join(",", selected)}])");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series.IsPointSelected(4), "and isPointSelected agrees");
            #endif

            // Its configuration starts empty - Qt falls back to the series' own settings.
            var configuration = series.pointConfiguration(4);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(configuration is IDictionary<QXYSeries.PointConfiguration, object>,
                "pointConfiguration() comes back as a dictionary");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(configuration.Count == 0,
                $"an untouched point has no configuration of its own (got {configuration.Count} entries)");
            #endif

            // The example's four controls: colour, size, label visibility and format.
            configuration[QXYSeries.PointConfiguration.Color] = new QColor(190, 30, 45);
            configuration[QXYSeries.PointConfiguration.Size] = 12.0;
            configuration[QXYSeries.PointConfiguration.LabelVisibility] = true;
            configuration[QXYSeries.PointConfiguration.LabelFormat] = "%.2f";
            series.SetPointConfiguration(4, configuration);

            var readBack = series.pointConfiguration(4);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(readBack.Count == 4, $"the configuration sticks (got {readBack.Count} entries)");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(readBack.ContainsKey(QXYSeries.PointConfiguration.Size),
                "the size is in there");
            #endif
            var color = readBack[QXYSeries.PointConfiguration.Color];
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(color is QColor c && c.Red() == 190 && c.Green() == 30
                        && c.Blue() == 45,
                "the colour survives as a real QColor variant");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check((double)readBack[QXYSeries.PointConfiguration.Size] == 12.0,
                $"and it reads back as 12 (got {readBack[QXYSeries.PointConfiguration.Size]})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check((bool)readBack[QXYSeries.PointConfiguration.LabelVisibility],
                "the label visibility reads back true");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(readBack[QXYSeries.PointConfiguration.LabelFormat] as string == "%.2f",
                $"the label format round-trips (got '{readBack[QXYSeries.PointConfiguration.LabelFormat]}')");
            #endif

            // Changing one entry keeps the others.
            var update = series.pointConfiguration(4);
            update[QXYSeries.PointConfiguration.Size] = 20.0;
            series.SetPointConfiguration(4, update);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check((double)series.pointConfiguration(4)[QXYSeries.PointConfiguration.Size] == 20.0,
                "the size can be changed on its own");
            #endif

            // Clearing it puts the point back to the series' defaults.
            series.ClearPointConfiguration(4);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series.pointConfiguration(4).Count == 0, "clearPointConfiguration empties it");
            #endif
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(series.pointConfiguration(1).Count == 0, "the other points were never touched");
#endif

            series.DeselectAllPoints();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series.SelectedPoints().Length == 0, "deselectAllPoints clears the selection");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            if (rendered != null)
                example.Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"the line and its points are drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");
            #endif

            #if QTSHARP_EXAMPLE_SMOKE
            return example.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
