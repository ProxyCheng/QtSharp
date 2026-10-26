using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts BarStackedWidget: the bar chart's data again, but in a
    /// QStackedBarSeries, and with only the value axis given a range.
    /// </summary>
    [Demo("qtcharts-stackedbarchart")]
public static class StackedBarChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var set0 = new QBarSet("Jane");
            var set1 = new QBarSet("John");
            var set2 = new QBarSet("Axel");
            var set3 = new QBarSet("Mary");
            var set4 = new QBarSet("Samantha");

            set0.Append(new double[] { 1, 2, 3, 4, 5, 6 });
            set1.Append(new double[] { 5, 0, 0, 4, 0, 7 });
            set2.Append(new double[] { 3, 5, 8, 13, 8, 5 });
            set3.Append(new double[] { 5, 6, 7, 3, 4, 5 });
            set4.Append(new double[] { 9, 7, 5, 3, 1, 2 });

            var series = new QStackedBarSeries();
            series.Append(set0);
            series.Append(set1);
            series.Append(set2);
            series.Append(set3);
            series.Append(set4);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Simple Stacked Bar Chart");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);

            string[] categories = { "Jan", "Feb", "Mar", "Apr", "May", "Jun" };
            var axisX = new QBarCategoryAxis();
            axisX.Append(categories);
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            var axisY = new QValueAxis();
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(chart);

            check.Check(series.type() == QAbstractSeries.SeriesType.SeriesTypeStackedBar,
                $"the series reports itself as a stacked bar series (got {series.type()})");
            check.Check(series.Count == 5, $"the series reports its five sets (got {series.Count})");
            check.Check(series.BarSets().Length == 5, $"the series holds five sets (got {series.BarSets().Length})");

            // A stacked bar chart totals the sets per category, which is what tells it apart from the
            // plain bar chart at the axis level: the axis' own range is set below that total by the
            // example (the gallery gives no range here, so Qt picks one from the data).
            double janTotal = 1 + 5 + 3 + 5 + 9;
            check.Check(janTotal == 23, $"January stacks to {janTotal} across the five sets");

            check.Check(chart.Title == "Simple Stacked Bar Chart", $"the title round-trips (got '{chart.Title}')");
            check.Check(chart.animationOptions() == QChart.AnimationOptions.SeriesAnimations,
                "the animation option round-trips");
            check.Check(chart.Legend().Alignment == Qt.Alignment.AlignBottom, "the legend sits at the bottom");
            check.Check(series.AttachedAxes().Length == 2,
                $"the series is attached to both axes (got {series.AttachedAxes().Length})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            int ink = ChartsAssertions.InkSamples(rendered);
            check.Check(ink > 200, $"the stacked bars are actually drawn ({ink} ink samples)");

            return check.Finish();
        }
    }
}
