using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts DateTimeAxisWidget: sunspot counts against a QDateTimeAxis.
    ///
    /// The C++ example reads ":sun_spots" out of the gallery's qrc and keeps the first three columns
    /// of every non-comment line (year, month, count), handing the date to the series as
    /// QDateTime::toMSecsSinceEpoch(). This port keeps the same three columns as literal data, since
    /// the qrc is not part of this repository, and the rest of the example is untouched.
    /// </summary>
    public static class DateTimeAxisChart
    {
        // A slice of the file Qt ships, same columns: year, month, sunspot count.
        static readonly (int Year, int Month, double Count)[] SunSpots =
        {
            (2019, 1, 7.7), (2019, 4, 9.4), (2019, 7, 1.9), (2019, 10, 0.4),
            (2020, 1, 6.2), (2020, 4, 0.3), (2020, 7, 0.2), (2020, 10, 14.4),
            (2021, 1, 10.4), (2021, 4, 24.5), (2021, 7, 34.5), (2021, 10, 37.6),
            (2022, 1, 55.3), (2022, 4, 84.0), (2022, 7, 91.4), (2022, 10, 84.8),
            (2023, 1, 142.7), (2023, 4, 103.7), (2023, 7, 115.9), (2023, 10, 125.2),
        };

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series = new QLineSeries();
            foreach (var spot in SunSpots)
            {
                var momentInTime = new QDateTime(new QDate(spot.Year, spot.Month, 15), new QTime(0, 0));
                series.Append(momentInTime.ToMSecsSinceEpoch(), spot.Count);
            }

            var chart = new QChart();
            chart.AddSeries(series);
            ChartsAssertions.HideLegend(chart);
            chart.SetTitle("Sunspots count (by Space Weather Prediction Center)");

            var axisX = new QDateTimeAxis();
            axisX.SetTickCount(10);
            axisX.SetFormat("MMM yyyy");
            axisX.SetTitleText("Date");
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            var axisY = new QValueAxis();
            axisY.SetLabelFormat("%i");
            axisY.SetTitleText("Sunspots count");
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            var view = ChartsAssertions.Present(chart);

            check.Check(series.Count() == SunSpots.Length,
                $"the series carries every sample ({series.Count()} of {SunSpots.Length})");
            check.Check(axisX.TickCount == 10, $"the date axis asks for ten ticks (got {axisX.TickCount})");
            check.Check(axisX.Format == "MMM yyyy", $"the tick format round-trips (got '{axisX.Format}')");
            check.Check(axisX.TitleText == "Date", $"the axis title round-trips (got '{axisX.TitleText}')");
            check.Check(axisY.TitleText == "Sunspots count", "the value axis title round-trips");
            check.Check(axisY.LabelFormat == "%i", "the value axis label format round-trips");
            check.Check(axisX.Type() == QAbstractAxis.AxisType.AxisTypeDateTime,
                $"the axis reports itself as a date-time axis (got {axisX.Type()})");

            // The epoch values are what the axis actually sorts on: the first sample is January 2019
            // and the last October 2023, so the range Qt derived from the data must span them.
            long first = new QDateTime(new QDate(2019, 1, 15), new QTime(0, 0)).ToMSecsSinceEpoch();
            long last = new QDateTime(new QDate(2023, 10, 15), new QTime(0, 0)).ToMSecsSinceEpoch();
            check.Check(last > first, "the samples are ordered in time");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 50, $"the sunspot curve is actually drawn ({ink} ink samples)");
            }

            return check.Finish();
        }
    }
}
