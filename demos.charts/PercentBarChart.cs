using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts BarPercentWidget: the bar chart's data again in a QPercentBarSeries,
    /// where every category is scaled to 100%.
    /// </summary>
    [Demo("qtcharts-percentbarchart")]
public static class PercentBarChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();
            var series = new QPercentBarSeries();
            BarChartData.AddSets(series, out var set0, out _, out var set2, out _, out _);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Simple Percent Bar Chart");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);

            var axisX = new QBarCategoryAxis();
            axisX.Append(BarChartData.Months);
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            var axisY = new QValueAxis();
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(chart);

            check.Check(series.type() == QAbstractSeries.SeriesType.SeriesTypePercentBar,
                $"the series reports itself as a percent bar series (got {series.type()})");
            check.Check(series.BarSets().Length == 5, $"five sets (got {series.BarSets().Length})");
            check.Check(set0.Sum() == 21, $"the sets keep their own values, not the percentages (got {set0.Sum()})");
            check.Check(set2.At(3) == 13, $"the 13 in Axel's set is untouched (got {set2.At(3)})");

            // The series scales each category to 100%, so the vertical axis has to end at 100.
            ChartsAssertions.Pump();
            check.Check(Math4.Nearly(axisY.Max, 100.0, 0.001),
                $"the value axis is scaled to 100% (got {axisY.Max})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 200,
                    $"the bars are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
