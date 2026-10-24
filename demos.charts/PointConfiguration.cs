using System.Collections.Generic;
using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts PointConfigurationWidget: one point of a line series given a size, a label
    /// visibility and a label format of its own, through QXYSeries' per-point configuration.
    ///
    /// The example's colour control is left out: a colour reaches the configuration as
    /// QVariant(QColor), and that constructor is QVariant's *template* one, which the extractor
    /// cannot materialise per type - the same reason QComboBox::addItem(QString, QVariant) takes only
    /// the types QVariant has real constructors for. Size (double), label visibility (bool) and the
    /// label format (QString) all have real ones and are exercised here.
    /// </summary>
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
            check.Check(configuration is IDictionary<QXYSeries.PointConfiguration, QVariant>,
                "pointConfiguration() comes back as a dictionary");
            check.Check(configuration.Count == 0,
                $"an untouched point has no configuration of its own (got {configuration.Count} entries)");

            // The example's three controls.
            configuration[QXYSeries.PointConfiguration.Size] = new QVariant(12.0);
            configuration[QXYSeries.PointConfiguration.LabelVisibility] = new QVariant(true);
            configuration[QXYSeries.PointConfiguration.LabelFormat] = new QVariant("%.2f");
            series.SetPointConfiguration(4, configuration);

            var readBack = series.pointConfiguration(4);
            check.Check(readBack.Count == 3, $"the configuration sticks (got {readBack.Count} entries)");
            check.Check(readBack.ContainsKey(QXYSeries.PointConfiguration.Size),
                "the size is in there");
            check.Check(readBack[QXYSeries.PointConfiguration.Size].ToDouble() == 12.0,
                $"and it reads back as 12 (got {readBack[QXYSeries.PointConfiguration.Size].ToDouble()})");
            check.Check(readBack[QXYSeries.PointConfiguration.LabelVisibility].ToBool(),
                "the label visibility reads back true");
            check.Check(readBack[QXYSeries.PointConfiguration.LabelFormat].ToString() == "%.2f",
                $"the label format round-trips (got '{readBack[QXYSeries.PointConfiguration.LabelFormat]}')");

            // Changing one entry keeps the others.
            var update = series.pointConfiguration(4);
            update[QXYSeries.PointConfiguration.Size] = new QVariant(20.0);
            series.SetPointConfiguration(4, update);
            check.Check(series.pointConfiguration(4)[QXYSeries.PointConfiguration.Size].ToDouble() == 20.0,
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
