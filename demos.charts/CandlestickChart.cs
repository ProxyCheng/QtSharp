using System;
using System.Collections.Generic;
using System.Globalization;
using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts CandlestickWidget: Acme Ltd's daily candles for July 2015, green when the
    /// day closed up and red when it closed down, with the category axis labelled by day of month.
    ///
    /// The example's CandlestickDataReader does the same splitting this does - timestamp, open, high,
    /// low, close - and hands each line to QCandlestickSet.
    /// </summary>
    [Demo("qtcharts-candlestickchart")]
public static class CandlestickChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var acmeSeries = new QCandlestickSeries();
            acmeSeries.SetName("Acme Ltd");
            acmeSeries.SetIncreasingColor(new QColor(Qt.GlobalColor.green));
            acmeSeries.SetDecreasingColor(new QColor(Qt.GlobalColor.red));

            var categories = new List<string>();
            var sets = new List<QCandlestickSet>();
            foreach (var line in GalleryData.Candles)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var set = new QCandlestickSet(
                    double.Parse(parts[1], CultureInfo.InvariantCulture),   // open
                    double.Parse(parts[2], CultureInfo.InvariantCulture),   // high
                    double.Parse(parts[3], CultureInfo.InvariantCulture),   // low
                    double.Parse(parts[4], CultureInfo.InvariantCulture),   // close
                    double.Parse(parts[0], CultureInfo.InvariantCulture));  // timestamp
                acmeSeries.Append(set);
                sets.Add(set);
                categories.Add(QDateTime.FromMSecsSinceEpoch((long)set.Timestamp).ToString("dd"));
            }

            var chart = new QChart();
            chart.AddSeries(acmeSeries);
            chart.SetTitle("Acme Ltd. Historical Data (July 2015)");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);
            chart.CreateDefaultAxes();

            var axisX = QBarCategoryAxis.FromHandle(chart.Axes(Qt.Orientations.Horizontal)[0].Handle);
            axisX.SetCategories(categories.ToArray());

            var axisY = ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Vertical)[0]);
            axisY.SetMax(axisY.Max * 1.01);
            axisY.SetMin(axisY.Min * 0.99);

            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(chart);

            check.Check(acmeSeries.Count == 22, $"the series holds July's 22 sessions (got {acmeSeries.Count})");
            check.Check(acmeSeries.Type() == QAbstractSeries.SeriesType.SeriesTypeCandlestick,
                $"the series reports itself as a candlestick series (got {acmeSeries.Type()})");
            check.Check(acmeSeries.IncreasingColor.Green() == 255 && acmeSeries.IncreasingColor.Red() == 0,
                "up days are green");
            check.Check(acmeSeries.DecreasingColor.Red() == 255 && acmeSeries.DecreasingColor.Green() == 0,
                "down days are red");

            // The first candle: 126.90 open, 126.94 high, 125.99 low, 126.60 close.
            var first = acmeSeries.Sets()[0];
            check.Check(Math4.Nearly(first.Open, 126.90, 0.0001), $"the first open (got {first.Open})");
            check.Check(Math4.Nearly(first.High, 126.94, 0.0001), $"the first high (got {first.High})");
            check.Check(Math4.Nearly(first.Low, 125.99, 0.0001), $"the first low (got {first.Low})");
            check.Check(Math4.Nearly(first.Close, 126.60, 0.0001), $"the first close (got {first.Close})");
            check.Check(Math4.Nearly(first.Timestamp, 1435708800000.0, 0.5),
                $"the first timestamp (got {first.Timestamp})");
            check.Check(first.High >= first.Open && first.Low <= first.Close,
                "the high is above both the open and the close, the low below them");

            // The day-of-month labels come out of the timestamps.
            var readCategories = axisX.Categories;
            check.Check(readCategories.Length == 22, $"one label per candle (got {readCategories.Length})");
            check.Check(readCategories[0] == "01", $"the first is the 1st (got '{readCategories[0]}')");
            check.Check(categories.Count == sets.Count, "every candle got a label");

            // The example pads the value axis by 1% either side.
            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 100,
                    $"the candles are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
