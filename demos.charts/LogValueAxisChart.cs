using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts LogAxisWidget: six points spanning four orders of magnitude against a
    /// QLogValueAxis with base 8, and a QValueAxis whose tick count follows the series' point count.
    /// </summary>
    public static class LogValueAxisChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series = new QLineSeries();
            foreach (var p in new[] { new QPointF(1.0, 1.0), new QPointF(2.0, 73.0), new QPointF(3.0, 268.0),
                                      new QPointF(4.0, 17.0), new QPointF(5.0, 4325.0), new QPointF(6.0, 723.0) })
                series.Append(p);

            var chart = new QChart();
            chart.AddSeries(series);
            ChartsAssertions.HideLegend(chart);
            chart.SetTitle("Logarithmic Axis");

            var axisX = new QValueAxis();
            axisX.SetTitleText("Data point");
            axisX.SetLabelFormat("%i");
            axisX.SetTickCount(series.Count());
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            var axisY = new QLogValueAxis();
            axisY.SetTitleText("Values");
            axisY.SetLabelFormat("%g");
            axisY.SetBase(8.0);
            axisY.SetMinorTickCount(-1);
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            var view = ChartsAssertions.Present(chart);

            check.Check(axisY.Base == 8.0, $"the log axis uses base 8 (got {axisY.Base})");
            check.Check(axisY.MinorTickCount == -1, $"the minor tick count is -1 (got {axisY.MinorTickCount})");
            check.Check(axisY.LabelFormat == "%g", $"the label format round-trips (got '{axisY.LabelFormat}')");
            check.Check(axisY.TitleText == "Values", "the log axis title round-trips");
            check.Check(axisY.Type() == QAbstractAxis.AxisType.AxisTypeLogValue,
                $"the axis reports itself as a log value axis (got {axisY.Type()})");
            check.Check(axisX.TickCount == 6, $"the value axis has one tick per point (got {axisX.TickCount})");
            check.Check(axisX.Type() == QAbstractAxis.AxisType.AxisTypeValue,
                $"the other axis is a plain value axis (got {axisX.Type()})");

            // The default range of a log axis is 1..10 in the base's own terms: the data reaches
            // 4325, which is 8^4 = 4096 and then some, so Qt must have widened it beyond 8^1..8^2.
            check.Check(axisY.Max >= 4325.0,
                $"the log axis grew to hold the largest sample (max {axisY.Max})");
            check.Check(axisY.Min <= 1.0,
                $"the log axis starts at or below the smallest sample (min {axisY.Min})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 50, $"the curve is actually drawn ({ink} ink samples)");
            }

            return check.Finish();
        }
    }
}
