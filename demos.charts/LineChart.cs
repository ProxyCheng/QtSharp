using System;
using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts/linechart: the same idea as splinechart with a QLineSeries of ten points
    /// and no explicit range - createDefaultAxes() picks one from the data.
    /// </summary>
    public static class LineChart
    {
        public static int Run(QApplication app)
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
                if (!ok) failures++;
            }

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

            var view = new QChartView(chart);
            view.SetRenderHint(QPainter.RenderHint.Antialiasing);
            view.Resize(400, 300);
            view.Show();
            for (int i = 0; i < 20; i++) { QCoreApplication.ProcessEvents(); System.Threading.Thread.Sleep(10); }

            Check(series.Count() == 10, $"the series carries the example's 10 points (got {series.Count()})");
            Check(chart.Title == "Simple line chart example", $"the title round-trips (got '{chart.Title}')");
            Check(series.Type() == QAbstractSeries.SeriesType.SeriesTypeLine,
                $"the series reports itself as a line series (got {series.Type()})");

            using var rendered = view.Grab();
            Check(rendered != null && rendered.Width() > 0, $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"the line is actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
            return failures == 0 ? 0 : 1;
        }
    }
}
