using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts HorizontalPercentBarWidget: the percent bar chart, laid on its side.
    /// </summary>
    [Demo("qtcharts-horizontalpercentbarchart")]
public static class HorizontalPercentBarChart
    {
        public static int Run(DemoContext demo)
        {
            var series = new QHorizontalPercentBarSeries();
            BarChartData.AddSets(series, out _, out _, out _, out _, out _);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Simple Horizontal Percent Bar Chart");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);

            var axisY = new QBarCategoryAxis();
            axisY.Append(BarChartData.Months);
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            var axisX = new QValueAxis();
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(demo, chart);
#if QTSHARP_DEMO_SMOKE

            demo.Check(series.type() == QAbstractSeries.SeriesType.SeriesTypeHorizontalPercentBar,
                $"the series reports itself as a horizontal percent bar series (got {series.type()})");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series.Count == 5, $"five sets (got {series.Count})");
            #endif

            ChartsAssertions.Pump();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(Math4.Nearly(axisX.Max, 100.0, 0.001),
                $"the value axis is scaled to 100% (got {axisX.Max})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(axisY.Orientation == Qt.Orientation.Vertical, "the categories are on the left");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            if (rendered != null)
                demo.Check(ChartsAssertions.InkSamples(rendered) > 200,
                    $"the bars are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");
            #endif

            #if QTSHARP_DEMO_SMOKE
            return demo.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
