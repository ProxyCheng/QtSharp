﻿namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Qt's examples/charts/linechart: the same idea as splinechart with a QLineSeries of ten points
    /// and no explicit range - createDefaultAxes() picks one from the data.
    /// </summary>
    [Example("qtcharts-linechart")]
    public static class LineChart
    {
        public static int Run(ExampleContext example)
        {
            var series = new QLineSeries();
            series.Append(0.0, 6.0);
            series.Append(2.0, 4.0);
            series.Append(3.0, 8.0);
            series.Append(7.0, 4.0);
            series.Append(10.0, 5.0);
            series.Append(11.0, 1.0);
            series.Append(13.0, 3.0);
            series.Append(17.0, 6.0);
            series.Append(18.0, 3.0);
            series.Append(20.0, 2.0);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.CreateDefaultAxes();
            chart.SetTitle("Simple line chart example");

#if QTSHARP_EXAMPLE_SMOKE
            var view = ChartsAssertions.Present(example, chart);

            example.Check(series.Count() == 10, $"the series carries the example's 10 points (got {series.Count()})");
            example.Check(chart.Title == "Simple line chart example", $"the title round-trips (got '{chart.Title}')");
            example.Check(series.type() == QAbstractSeries.SeriesType.SeriesTypeLine,
                $"the series reports itself as a line series (got {series.type()})");

            using var rendered = view.Grab();
            example.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                example.Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"the line is actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return example.ExitCode;
#else
            ChartsAssertions.Present(example, chart);
            return 0;
#endif
        }
    }
}
