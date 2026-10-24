using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts DonutWidget: a pie with a hole, the Fat slice exploded, the legend at the
    /// bottom, the Blue Cerulean theme and a small legend font.
    /// </summary>
    [Demo("qtcharts-donutchart")]
public static class DonutChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series = new QPieSeries();
            series.SetHoleSize(0.35);
            series.Append("Protein 4.2%", 4.2);
            var slice = series.Append("Fat 15.6%", 15.6);
            slice.SetExploded();
            slice.SetLabelVisible();
            series.Append("Other 23.8%", 23.8);
            series.Append("Carbs 56.4%", 56.4);

            var chart = new QChart();
            chart.SetTitle("Donut with a lemon glaze (100g)");
            chart.AddSeries(series);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);
            chart.SetTheme(QChart.ChartTheme.ChartThemeBlueCerulean);
            chart.Legend().SetFont(new QFont("Arial", 7));

            var view = ChartsAssertions.Present(chart);

            check.Check(series.HoleSize == 0.35, $"the hole is a third of the radius (got {series.HoleSize})");
            check.Check(series.Count == 4, $"four slices (got {series.Count})");
            check.Check(slice.IsExploded(), "the Fat slice is exploded");
            check.Check(slice.IsLabelVisible(), "the Fat slice's label is visible");
            check.Check(Math4.Nearly(series.Sum, 100.0, 0.001),
                $"the four slices are the whole 100g ({series.Sum:F2})");
            // percentage() is the fraction; 15.6 of 100 is 0.156.
            check.Check(Math4.Nearly(slice.Percentage, 0.156, 0.0001),
                $"Fat is 15.6% of the pie ({slice.Percentage:F4})");
            check.Check(slice.Label == "Fat 15.6%", $"the slice label round-trips (got '{slice.Label}')");

            check.Check(chart.Theme == QChart.ChartTheme.ChartThemeBlueCerulean,
                $"the theme round-trips (got {chart.Theme})");
            check.Check(chart.Legend().Font.PointSize() == 7,
                $"the legend font is 7pt (got {chart.Legend().Font.PointSize()})");
            check.Check(chart.Title == "Donut with a lemon glaze (100g)", "the title round-trips");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                // A donut has a hole: the centre of the plot area is the chart's background, not a
                // slice. That is the difference between this example and the plain pie.
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 100, $"the ring is actually drawn ({ink} ink samples)");
            }

            return check.Finish();
        }
    }
}
