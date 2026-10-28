using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts HorizontalStackedBarWidget: the stacked bar chart, laid on its side.
    /// </summary>
    [Demo("qtcharts-horizontalstackedbarchart")]
public static class HorizontalStackedBarChart
    {
        public static int Run(DemoContext demo)
        {
            var series = new QHorizontalStackedBarSeries();
            BarChartData.AddSets(series, out _, out _, out var set2, out _, out _);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Simple Horizontal Stacked Bar Chart");
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

            demo.Check(series.type() == QAbstractSeries.SeriesType.SeriesTypeHorizontalStackedBar,
                $"the series reports itself as a horizontal stacked bar series (got {series.type()})");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series.Count == 5, $"five sets (got {series.Count})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(set2.At(3) == 13, $"the 13 in Axel's set survives (got {set2.At(3)})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(axisY.Orientation == Qt.Orientation.Vertical,
                "the categories are on the left");
            #endif

            // Stacked: the axis has to cover the tallest column total (9+7+5+5+6 = 32 in June).
            #if QTSHARP_DEMO_SMOKE
            double june = 6 + 7 + 5 + 5 + 2;
            demo.Check(axisX.Max >= june, $"the axis covers June's {june} (max {axisX.Max})");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            if (rendered != null)
                demo.Check(ChartsAssertions.InkSamples(rendered) > 200,
                    $"the stacked bars are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");
            #endif

            #if QTSHARP_DEMO_SMOKE
            return demo.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
