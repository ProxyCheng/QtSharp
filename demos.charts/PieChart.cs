using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts PieWidget: five slices, the second one exploded with a visible label and
    /// a dark green pen, in a chart whose legend is hidden.
    ///
    /// QPieSeries::append(name, value) returns the slice, which is how the example reaches the one it
    /// wants to explode - the same call the binding publishes.
    /// </summary>
    [Demo("qtcharts-piechart")]
public static class PieChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series = new QPieSeries();
            series.Append("Jane", 1);
            series.Append("Joe", 2);
            series.Append("Andy", 3);
            series.Append("Barbara", 4);
            var axel = series.Append("Axel", 5);

            var slice = series.Slices()[1];
            slice.SetExploded();
            slice.SetLabelVisible();
            slice.SetPen(new QPen(new QBrush(new QColor(Qt.GlobalColor.darkGreen)), 2));
            slice.SetBrush(new QBrush(new QColor(Qt.GlobalColor.green)));

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Simple Pie Chart");
            ChartsAssertions.HideLegend(chart);

            var view = ChartsAssertions.Present(chart);

            check.Check(series.Count == 5, $"the series carries the example's five slices (got {series.Count})");
            check.Check(series.Sum == 15, $"the slices add up to 15 (got {series.Sum})");

            var slices = series.Slices();
            check.Check(slices.Length == 5, $"QList<QPieSlice*> came back with five slices (got {slices.Length})");
            check.Check(slices[1].Label == "Joe", $"the second slice is Joe (got '{slices[1].Label}')");
            check.Check(slices[3].Label == "Barbara", $"the fourth slice is Barbara (got '{slices[3].Label}')");

            // The slice the example explodes is the one append() returned, and it is the same object
            // the series hands back - that is the identity guarantee the wrapper cache provides.
            check.Check(ReferenceEquals(axel, slices[4]) || axel.Handle == slices[4].Handle,
                "the slice append() returned is the one the series holds");

            check.Check(slice.IsExploded(), "the second slice is exploded");
            check.Check(slice.IsLabelVisible(), "the second slice's label is visible");
            check.Check(!slices[0].IsExploded(), "the other slices are not exploded");
            // QPieSlice::percentage() is the fraction of the pie, not a 0..100 number.
            check.Check(Math4.Nearly(slice.Percentage, 2.0 / 15.0, 0.0001),
                $"Joe is 2/15 of the pie ({slice.Percentage:F4})");
            check.Check(Math4.Nearly(slice.AngleSpan, 48.0, 0.0001),
                $"2/15 of 360 is a 48-degree wedge (got {slice.AngleSpan})");
            check.Check(Math4.Nearly(slice.StartAngle, 24.0, 0.0001),
                $"Jane's 24 degrees come first, so Joe starts at 24 (got {slice.StartAngle})");
            check.Check(slice.Series().Handle == series.Handle, "a slice knows its series");
            check.Check(slice.Brush.Color().Green() == 255 && slice.Brush.Color().Red() == 0,
                $"the exploded slice was filled green (got {slice.Brush.Color().Green()})");

            check.Check(chart.Title == "Simple Pie Chart", $"the title round-trips (got '{chart.Title}')");
            check.Check(!ChartsAssertions.IsLegendVisible(chart), "the legend is hidden, as the example asks");
            check.Check(series.Type() == QAbstractSeries.SeriesType.SeriesTypePie,
                $"the series reports itself as a pie series (got {series.Type()})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            int ink = ChartsAssertions.InkSamples(rendered);
            check.Check(ink > 100, $"the pie is actually drawn ({ink} ink samples)");
            check.Check(ChartsAssertions.ColourSamples(rendered) > 10,
                $"the slices get distinct colours ({ChartsAssertions.ColourSamples(rendered)} colours)");

            return check.Finish();
        }
    }

    /// <summary>Small float comparisons, so an assertion can say "about this" without a helper library.</summary>
    internal static class Math4
    {
        public static bool Nearly(int a, int b, int tolerance) => System.Math.Abs(a - b) <= tolerance;
        public static bool Nearly(double a, double b, double tolerance) => System.Math.Abs(a - b) <= tolerance;
    }
}
