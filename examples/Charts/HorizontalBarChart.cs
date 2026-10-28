﻿using QtSharp.Charts;

namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Qt's examples/charts HorizontalBarWidget: the bar chart again, laid on its side - the category
    /// axis moves to the left and the value axis to the bottom - with applyNiceNumbers() rounding the
    /// value axis off.
    /// </summary>
    [Example("qtcharts-horizontalbarchart")]
public static class HorizontalBarChart
    {
        public static int Run(ExampleContext example)
        {
            var series = new QHorizontalBarSeries();
            BarChartData.AddSets(series, out _, out _, out _, out _, out _);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Simple Horizontal Bar Chart");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);

            var axisY = new QBarCategoryAxis();
            axisY.Append(BarChartData.Months);
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            var axisX = new QValueAxis();
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);
            axisX.ApplyNiceNumbers();

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(example, chart);
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(series.type() == QAbstractSeries.SeriesType.SeriesTypeHorizontalBar,
                $"the series reports itself as a horizontal bar series (got {series.type()})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series.Count == 5, $"five sets (got {series.Count})");
            #endif
#if QTSHARP_EXAMPLE_SMOKE

            // The axes have swapped sides: the categories are on the left, the values along the
            // bottom. The chart's own axis lists are what say so.
            example.Check(axisY.Orientation == Qt.Orientation.Vertical,
                $"the category axis became vertical (got {axisY.Orientation})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(axisX.Orientation == Qt.Orientation.Horizontal,
                $"the value axis became horizontal (got {axisX.Orientation})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series.AttachedAxes().Length == 2, "the series is on both axes");
            #endif
#if QTSHARP_EXAMPLE_SMOKE

            // applyNiceNumbers() rounds the range to pleasant values; the largest bar is 9, so the
            // axis has to reach at least that far.
            example.Check(axisX.Max >= 9, $"the value axis holds the tallest bar (max {axisX.Max})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math4.Nearly(axisX.Max % 5, 0, 0.0001) || axisX.Max % 1 == 0,
                $"applyNiceNumbers left a round maximum ({axisX.Max})");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            if (rendered != null)
                example.Check(ChartsAssertions.InkSamples(rendered) > 200,
                    $"the bars are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");
            #endif

            #if QTSHARP_EXAMPLE_SMOKE
            return example.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
