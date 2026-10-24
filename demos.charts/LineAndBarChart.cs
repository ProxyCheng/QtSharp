using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts LineAndBarWidget: a QBarSeries and a QLineSeries sharing one category axis
    /// and one value axis, with the line series drawn as a trend over the bars.
    /// </summary>
    public static class LineAndBarChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var set0 = new QBarSet("Jane");
            var set1 = new QBarSet("John");
            var set2 = new QBarSet("Axel");
            var set3 = new QBarSet("Mary");
            var set4 = new QBarSet("Sam");

            set0.Append(new double[] { 1, 2, 3, 4, 5, 6 });
            set1.Append(new double[] { 5, 0, 0, 4, 0, 7 });
            set2.Append(new double[] { 3, 5, 8, 13, 8, 5 });
            set3.Append(new double[] { 5, 6, 7, 3, 4, 5 });
            set4.Append(new double[] { 9, 7, 5, 3, 1, 2 });

            var barSeries = new QBarSeries();
            barSeries.Append(set0);
            barSeries.Append(set1);
            barSeries.Append(set2);
            barSeries.Append(set3);
            barSeries.Append(set4);

            var lineSeries = new QLineSeries();
            lineSeries.SetName("trend");
            foreach (var p in new[] { new QPointF(0, 4), new QPointF(1, 15), new QPointF(2, 20),
                                      new QPointF(3, 4), new QPointF(4, 12), new QPointF(5, 17) })
                lineSeries.Append(p);

            var chart = new QChart();
            chart.AddSeries(barSeries);
            chart.AddSeries(lineSeries);
            chart.SetTitle("Line and Bar Chart");

            var axisX = new QBarCategoryAxis();
            axisX.Append(new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" });
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            lineSeries.AttachAxis(axisX);
            barSeries.AttachAxis(axisX);
            axisX.SetRange("Jan", "Jun");

            var axisY = new QValueAxis();
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            lineSeries.AttachAxis(axisY);
            barSeries.AttachAxis(axisY);
            axisY.SetRange(0, 20);

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(chart);

            check.Check(chart.Series().Length == 2, $"the chart holds both series (got {chart.Series().Length})");
            check.Check(lineSeries.Count() == 6, $"the trend has six points (got {lineSeries.Count()})");
            check.Check(barSeries.BarSets().Length == 5, $"the bars hold five sets (got {barSeries.BarSets().Length})");
            check.Check(lineSeries.Name == "trend", $"the trend keeps its name (got '{lineSeries.Name}')");

            check.Check(barSeries.AttachedAxes().Length == 2,
                $"the bars are on both axes (got {barSeries.AttachedAxes().Length})");
            check.Check(lineSeries.AttachedAxes().Length == 2,
                $"the line is on both axes (got {lineSeries.AttachedAxes().Length})");

            check.Check(axisX.Min == "Jan" && axisX.Max == "Jun",
                $"the category axis holds Jan..Jun (got {axisX.Min}..{axisX.Max})");
            check.Check(axisY.Min == 0 && axisY.Max == 20,
                $"the value axis holds 0..20 (got {axisY.Min}..{axisY.Max})");

            // The trend's first sample is 4 and its peak is 20 - inside the bar axis' range, which is
            // why the example can share one axis between the two series.
            check.Check(lineSeries.At(2).Y() == 20, $"the trend peaks at 20 (got {lineSeries.At(2).Y()})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 200, $"bars and trend are both drawn ({ink} ink samples)");
            }

            return check.Finish();
        }
    }
}
