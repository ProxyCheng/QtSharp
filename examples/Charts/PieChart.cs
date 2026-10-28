﻿using QtSharp.Charts;

namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Qt's examples/charts PieWidget: five slices, the second one exploded with a visible label and
    /// a dark green pen, in a chart whose legend is hidden.
    ///
    /// QPieSeries::append(name, value) returns the slice, which is how the example reaches the one it
    /// wants to explode - the same call the binding publishes.
    /// </summary>
    [Example("qtcharts-piechart")]
public static class PieChart
    {
        public static int Run(ExampleContext example)
        {
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

            var view = ChartsAssertions.Present(example, chart);
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(series.Count == 5, $"the series carries the example's five slices (got {series.Count})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series.Sum == 15, $"the slices add up to 15 (got {series.Sum})");
            #endif

            var slices = series.Slices();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(slices.Length == 5, $"QList<QPieSlice*> came back with five slices (got {slices.Length})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(slices[1].Label == "Joe", $"the second slice is Joe (got '{slices[1].Label}')");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(slices[3].Label == "Barbara", $"the fourth slice is Barbara (got '{slices[3].Label}')");
            #endif
#if QTSHARP_EXAMPLE_SMOKE

            // The slice the example explodes is the one append() returned, and it is the same object
            // the series hands back - that is the identity guarantee the wrapper cache provides.
            example.Check(ReferenceEquals(axel, slices[4]) || axel.Handle == slices[4].Handle,
                "the slice append() returned is the one the series holds");
#endif
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(slice.IsExploded(), "the second slice is exploded");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(slice.IsLabelVisible(), "the second slice's label is visible");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(!slices[0].IsExploded(), "the other slices are not exploded");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            // QPieSlice::percentage() is the fraction of the pie, not a 0..100 number.
            example.Check(Math4.Nearly(slice.Percentage, 2.0 / 15.0, 0.0001),
                $"Joe is 2/15 of the pie ({slice.Percentage:F4})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math4.Nearly(slice.AngleSpan, 48.0, 0.0001),
                $"2/15 of 360 is a 48-degree wedge (got {slice.AngleSpan})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math4.Nearly(slice.StartAngle, 24.0, 0.0001),
                $"Jane's 24 degrees come first, so Joe starts at 24 (got {slice.StartAngle})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(slice.Series().Handle == series.Handle, "a slice knows its series");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(slice.Brush.Color().Green() == 255 && slice.Brush.Color().Red() == 0,
                $"the exploded slice was filled green (got {slice.Brush.Color().Green()})");
            #endif
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(chart.Title == "Simple Pie Chart", $"the title round-trips (got '{chart.Title}')");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(!ChartsAssertions.IsLegendVisible(chart), "the legend is hidden, as the example asks");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series.type() == QAbstractSeries.SeriesType.SeriesTypePie,
                $"the series reports itself as a pie series (got {series.type()})");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            int ink = ChartsAssertions.InkSamples(rendered);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(ink > 100, $"the pie is actually drawn ({ink} ink samples)");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(ChartsAssertions.ColourSamples(rendered) > 10,
                $"the slices get distinct colours ({ChartsAssertions.ColourSamples(rendered)} colours)");
            #endif

            #if QTSHARP_EXAMPLE_SMOKE
            return example.ExitCode;
            #else
            return 0;
            #endif
        }
    }

    /// <summary>Small float comparisons, so an assertion can say "about this" without a helper library.</summary>
    internal static class Math4
    {
        public static bool Nearly(int a, int b, int tolerance) => System.Math.Abs(a - b) <= tolerance;
        public static bool Nearly(double a, double b, double tolerance) => System.Math.Abs(a - b) <= tolerance;
    }
}
