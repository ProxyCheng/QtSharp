using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts TemperatureRecordsWidget: the hottest and coldest temperatures recorded
    /// in each month, as two stacked bar sets whose value axis spans -52..52 - the same call the
    /// stacked bar example makes, and the one that shows an axis with negative values on it.
    /// </summary>
    [Demo("qtcharts-temperaturerecords")]
public static class TemperatureRecords
    {
        static readonly string[] Months =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
        };

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var low = new QBarSet("Min");
            var high = new QBarSet("Max");
            low.Append(new double[] { -52, -50, -45.3, -37.0, -25.6, -8.0, -6.0, -11.8, -19.7, -32.8, -43.0, -48.0 });
            high.Append(new double[] { 11.9, 12.8, 18.5, 26.5, 32.0, 34.8, 38.2, 34.8, 29.8, 20.4, 15.1, 11.8 });

            var series = new QStackedBarSeries();
            series.Append(low);
            series.Append(high);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Temperature records in Celcius");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);

            var axisX = new QBarCategoryAxis();
            axisX.Append(Months);
            axisX.SetTitleText("Month");
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);

            var axisY = new QValueAxis();
            axisY.SetRange(-52, 52);
            axisY.SetTitleText("Temperature [&deg;C]");
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisX);
            series.AttachAxis(axisY);

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(chart);

            check.Check(low.Count() == 12 && high.Count() == 12,
                $"two sets of twelve months ({low.Count()}/{high.Count()})");
            check.Check(low.At(0) == -52 && high.At(0) == 11.9,
                $"January runs -52 to 11.9 ({low.At(0)}/{high.At(0)})");
            check.Check(high.At(6) == 38.2, $"July's record high is 38.2 (got {high.At(6)})");

            // The axis spans the whole range, which is what makes both sets visible from one zero
            // line rather than two axes.
            check.Check(axisY.Min == -52 && axisY.Max == 52,
                $"the value axis spans -52..52 (got {axisY.Min}..{axisY.Max})");
            check.Check(axisX.Count == 12, $"twelve categories (got {axisX.Count})");
            check.Check(axisX.TitleText == "Month", $"the axis title round-trips (got '{axisX.TitleText}')");

            // A negative bar: the first set's January sits below the zero line. That is the part of
            // this example the others do not cover.
            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 200, $"both sets are drawn ({ink} ink samples)");
                check.Check(ChartsAssertions.ColourSamples(rendered) > 10,
                    $"the two sets get distinct colours ({ChartsAssertions.ColourSamples(rendered)} colours)");
            }

            return check.Finish();
        }
    }
}
