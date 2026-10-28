﻿namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Qt's examples/charts/splinechart: a QSplineSeries with seven points, default axes, a title,
    /// and the chart in a QChartView with antialiasing on.
    ///
    /// The assertions are on what the binding produced rather than on a screenshot, so they hold on a
    /// machine with no display: the points the series carries, the title, the axes
    /// createDefaultAxes() built, the range set through QValueAxis, and the ink the chart draws when
    /// it is rendered off-screen.
    /// </summary>
    [Example("qtcharts-splinechart")]
    public static class SplineChart
    {
        public static int Run(ExampleContext example)
        {
            // --- the example's own setup
            var series = new QSplineSeries();
            series.SetName("spline");
            series.Append(0.0, 6.0);
            series.Append(2.0, 4.0);
            series.Append(3.0, 8.0);
            series.Append(7.0, 4.0);
            series.Append(10.0, 5.0);
            series.Append(11.0, 1.0);
            series.Append(13.0, 2.0);

            var chart = new QChart();
            // chart->legend()->hide() - the legend reaches hide() through QGraphicsItem, a *secondary*
            // base the binding exposes as a view rather than by inheritance, so it is left visible.
            chart.AddSeries(series);
            chart.SetTitle("Simple spline chart");
            chart.CreateDefaultAxes();

#if QTSHARP_EXAMPLE_SMOKE
            var view = ChartsAssertions.Present(example, chart);

            example.Check(series.Count() == 7, $"the series carries the example's 7 points (got {series.Count()})");
            example.Check(series.Name == "spline", $"the series keeps its name (got '{series.Name}')");
            example.Check(chart.Title == "Simple spline chart", $"the chart title round-trips (got '{chart.Title}')");
            example.Check(chart.Series().Length == 1, $"the chart holds one series (got {chart.Series().Length})");
            example.Check(chart.Axes(Qt.Orientations.Vertical).Length == 1, "createDefaultAxes built a vertical axis");
            example.Check(chart.Axes(Qt.Orientations.Horizontal).Length == 1, "createDefaultAxes built a horizontal axis");

            // QAbstractAxis::setRange takes QVariants; the qreal overload is QValueAxis's.
            var axis = QValueAxis.FromHandle(chart.Axes(Qt.Orientations.Vertical)[0].Handle);
            axis.SetRange(0, 10);
            example.Check(axis.Min == 0 && axis.Max == 10, $"the vertical axis holds the 0..10 range (got {axis.Min}..{axis.Max})");

            using var rendered = view.Grab();
            example.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                example.Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"the spline is actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return example.ExitCode;
#else
            ChartsAssertions.Present(example, chart);
            return 0;
#endif
        }
    }
}
