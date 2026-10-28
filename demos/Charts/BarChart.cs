using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts BarWidget: five QBarSets of six values, a QBarSeries, a QBarCategoryAxis
    /// along the bottom and a QValueAxis on the left, with the legend at the bottom.
    ///
    /// The assertions are on what the binding produced - the values that went in come back out, the
    /// category axis holds the months, the value axis holds its range, and the chart renders - so
    /// they hold on a machine with no display.
    /// </summary>
    [Demo("qtcharts-barchart")]
public static class BarChart
    {
        public static int Run(DemoContext demo)
        {
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

            var series = new QBarSeries();
            series.Append(set0);
            series.Append(set1);
            series.Append(set2);
            series.Append(set3);
            series.Append(set4);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Simple Bar Chart");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);

            string[] categories = { "Jan", "Feb", "Mar", "Apr", "May", "Jun" };
            var axisX = new QBarCategoryAxis();
            axisX.Append(categories);
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            var axisY = new QValueAxis();
            axisY.SetRange(0, 15);
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(demo, chart);
#if QTSHARP_DEMO_SMOKE

            demo.Check(set0.Count() == 6, $"Jane's set carries six values (got {set0.Count()})");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(set0.At(3) == 4, $"a single bar reads back by index (got {set0.At(3)})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(set0.Sum() == 21, $"QBarSet::sum adds the set up (got {set0.Sum()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(set2.At(3) == 13, $"the 13 in Axel's set survives the QList<double> round trip (got {set2.At(3)})");
            #endif

            var sets = series.BarSets();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(sets.Length == 5, $"the series holds five bar sets (got {sets.Length})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(sets[4].Label == "Samantha",
                $"a bar set keeps its label (got '{sets[4].Label}')");
            #endif
#if QTSHARP_DEMO_SMOKE

            // QAbstractBarSeries::count() counts the bar *sets*, not the categories.
            demo.Check(series.Count == 5, $"the series reports its five sets (got {series.Count})");
#endif

            var readCategories = axisX.Categories;
            #if QTSHARP_DEMO_SMOKE
            demo.Check(readCategories.Length == 6 && readCategories[5] == "Jun",
                $"the category axis holds the months (got [{string.Join(",", readCategories)}])");
            #endif
#if QTSHARP_DEMO_SMOKE

            demo.Check(chart.Title == "Simple Bar Chart", $"the title round-trips (got '{chart.Title}')");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(chart.animationOptions() == QChart.AnimationOptions.SeriesAnimations,
                $"the animation option round-trips (got {chart.animationOptions()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(ChartsAssertions.IsLegendVisible(chart), "the legend is visible, as the example asks");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(chart.Legend().Alignment == Qt.Alignment.AlignBottom, "the legend sits at the bottom");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(chart.Series().Length == 1, $"the chart holds one series (got {chart.Series().Length})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series.AttachedAxes().Length == 2,
                $"the series is attached to both axes (got {series.AttachedAxes().Length})");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            int ink = ChartsAssertions.InkSamples(rendered);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(ink > 200, $"the bars are actually drawn ({ink} ink samples)");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(ChartsAssertions.ColourSamples(rendered) > 20,
                $"the five sets get five colours ({ChartsAssertions.ColourSamples(rendered)} colours)");
            #endif

            #if QTSHARP_DEMO_SMOKE
            return demo.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
