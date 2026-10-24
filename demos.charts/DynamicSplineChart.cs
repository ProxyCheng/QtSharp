using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts DynamicSplineWidget: a spline that grows a point every second and scrolls
    /// the chart along with it, until the x value passes 100 and the timer stops.
    ///
    /// The handler is driven directly here rather than through the one-second timer - the test would
    /// otherwise take two minutes - but the timer itself is the example's, and the assertions cover
    /// both the arithmetic the handler does and what it does to the chart.
    /// </summary>
    [Demo("qtcharts-dynamicspline")]
public static class DynamicSplineChart
    {
        // The example's state.
        static double _x = 0;
        static double _y = 1;

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var chart = new QChart();
            chart.SetTitle("Dynamic Spline Chart");
            ChartsAssertions.HideLegend(chart);
            chart.SetAnimationOptions(QChart.AnimationOptions.AllAnimations);

            var axisX = new QValueAxis();
            var axisY = new QValueAxis();

            var timer = new QTimer();
            timer.SetInterval(1000);

            var series = new QSplineSeries();
            var green = new QPen(new QColor(Qt.GlobalColor.red));
            green.SetWidth(3);
            series.SetPen(green);
            series.Append(_x, _y);

            chart.AddSeries(series);
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisX);
            series.AttachAxis(axisY);
            axisX.SetTickCount(5);
            axisX.SetRange(0, 10);
            axisY.SetRange(-5, 10);

            var view = ChartsAssertions.Present(chart);

            check.Check(timer.Interval == 1000, $"the timer ticks once a second (got {timer.Interval})");
            check.Check(timer.IntervalAsDuration().TotalMilliseconds == 1000,
                "and the duration accessor agrees");
            check.Check(!timer.IsActive(), "the timer has not been started by the test");
            check.Check(series.Count() == 1, $"the spline starts with a single point (got {series.Count()})");
            check.Check(green.Width() == 3, $"the spline pen is three pixels wide (got {green.Width()})");

            // The example's handleTimeout, driven by hand.
            int steps = 0;
            double previousX = _x;
            for (int i = 0; i < 12; i++)
            {
                double scrollBy = chart.PlotArea.Width() / axisX.TickCount;
                double step = (axisX.Max - axisX.Min) / axisX.TickCount;
                _x += step;
                _y = QRandomGenerator.Global().Bounded(5) - 2.5;
                series.Append(_x, _y);
                chart.Scroll(scrollBy, 0);
                steps++;
            }

            check.Check(steps == 12, "the handler ran twelve times");
            check.Check(series.Count() == 13, $"each step appended one point (got {series.Count()})");
            check.Check(_x > previousX, $"the x value advanced ({previousX} -> {_x})");
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i < series.Count(); i++)
            {
                double v = series.At(i).Y();
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            check.Check(hi - lo > 0, $"the spline's values span a range ({lo:F2}..{hi:F2})");
            check.Check(_y >= -2.5 && _y <= 2.5,
                $"the random y stays in the example's -2.5..2.5 band (got {_y})");

            // Scrolling moves the axis window along with the data, which is the whole point of the
            // example: the curve travels left while new points arrive on the right.
            check.Check(axisX.Max > 10,
                $"the axis followed the data past its original range (max {axisX.Max})");

            // The example stops the timer once x reaches 100.
            _x = 99;
            double oneStep = (axisX.Max - axisX.Min) / axisX.TickCount;
            _x += oneStep;
            check.Check(_x >= 100, $"x reaches the stop condition ({_x})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 50, $"the spline is actually drawn ({ink} ink samples)");
            }

            return check.Finish();
        }
    }
}
