using System;
using System.Collections.Generic;
using System.Linq;
using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts BoxPlotWidget: two QBoxPlotSeries of twelve box-and-whisker sets, each
    /// built from a month's worth of raw values by the example's own BoxPlotDataReader (sort, take
    /// the extremes, and take the median of the whole set and of each half).
    ///
    /// The reader's five numbers go into QBoxSet's six-argument constructor -
    /// (lowerExtreme, lowerQuartile, median, upperQuartile, upperExtreme, label) - which is the same
    /// thing its box->setValue(QBoxSet::ValuePos, ...) calls do, and the one the binding publishes.
    /// </summary>
    [Demo("qtcharts-boxplotchart")]
public static class BoxPlotChart
    {
        /// <summary>The example's BoxPlotDataReader, on a line of "label values...".</summary>
        internal static QBoxSet ReadBox(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = new List<double>();
            for (int i = 1; i < parts.Length; i++)
                values.Add(double.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture));
            values.Sort();

            int count = values.Count;
            return new QBoxSet(
                values[0],                                       // LowerExtreme
                Median(values, 0, count / 2),                    // LowerQuartile
                Median(values, 0, count),                        // Median
                Median(values, count / 2 + (count % 2), count),  // UpperQuartile
                values[count - 1],                               // UpperExtreme
                parts[0]);
        }

        static double Median(List<double> sorted, int begin, int end)
        {
            int count = end - begin;
            if (count % 2 != 0)
                return sorted[count / 2 + begin];
            return (sorted[count / 2 + begin] + sorted[count / 2 - 1 + begin]) / 2.0;
        }

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var acmeSeries = new QBoxPlotSeries();
            acmeSeries.SetName("Acme Ltd");
            var boxWhiskSeries = new QBoxPlotSeries();
            boxWhiskSeries.SetName("BoxWhisk Inc");

            foreach (var line in GalleryData.Acme)
                acmeSeries.Append(ReadBox(line));
            foreach (var line in GalleryData.BoxWhisk)
                boxWhiskSeries.Append(ReadBox(line));

            var chart = new QChart();
            chart.AddSeries(acmeSeries);
            chart.AddSeries(boxWhiskSeries);
            chart.SetTitle("Acme Ltd. and BoxWhisk Inc. share deviation in 2012");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);

            chart.CreateDefaultAxes();
            // A box plot chart's default x axis is a category axis (one category per box), so only
            // the vertical one is a value axis; the example's setMax(34.0) call lands on the
            // categories' own API here.
            ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Vertical)[0]).SetMin(15.0);

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(chart);

            check.Check(acmeSeries.Count == 12, $"Acme has twelve months (got {acmeSeries.Count})");
            check.Check(boxWhiskSeries.Count == 12, $"BoxWhisk has twelve (got {boxWhiskSeries.Count})");
            check.Check(acmeSeries.Name == "Acme Ltd", $"the series keeps its name (got '{acmeSeries.Name}')");
            check.Check(acmeSeries.type() == QAbstractSeries.SeriesType.SeriesTypeBoxPlot,
                $"the series reports itself as a box plot series (got {acmeSeries.type()})");

            var sets = acmeSeries.BoxSets();
            check.Check(sets.Length == 12, $"QList<QBoxSet*> came back with twelve sets (got {sets.Length})");
            check.Check(sets[0].Label() == "Jan", $"the first set is January (got '{sets[0].Label()}')");
            check.Check(sets[11].Label() == "Dec", $"the last is December (got '{sets[11].Label()}')");

            // January's raw values run from 23.85 to 28.72, and its median is the eleventh of the
            // twenty-one sorted values - the reader's arithmetic, read back through the binding.
            var january = sets[0];
            check.Check(Math4.Nearly(january.At(0), 23.85, 0.0001),
                $"January's lower extreme (got {january.At(0)})");
            check.Check(Math4.Nearly(january.At(4), 28.72, 0.0001),
                $"January's upper extreme (got {january.At(4)})");
            check.Check(january.At(0) < january.At(1) && january.At(1) < january.At(2)
                        && january.At(2) < january.At(3) && january.At(3) < january.At(4),
                "the five numbers are ordered lower extreme < quartiles < median ... < upper extreme");
            check.Check(january.Count() == 5, $"a box carries five numbers (got {january.Count()})");

            var axisY = ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Vertical)[0]);
            var axisX = QBarCategoryAxis.FromHandle(chart.Axes(Qt.Orientations.Horizontal)[0].Handle);
            check.Check(Math4.Nearly(axisY.Min, 15.0, 0.001), $"the y axis starts at 15 (got {axisY.Min})");
            check.Check(axisX.Count == 12, $"the x axis has a category per month (got {axisX.Count})");
            var categories = axisX.Categories;
            check.Check(categories.Length == 12 && categories[0] == "Jan" && categories[11] == "Dec",
                $"and they are the months ({categories[0]}..{categories[11]})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 100,
                    $"the boxes are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
