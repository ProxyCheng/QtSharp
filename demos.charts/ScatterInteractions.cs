using System;
using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts ScatterInteractionsWidget: a 7x7 grid of scatter points, and a click that
    /// moves the nearest one of them into a second series - the simplest of the gallery's
    /// interaction examples, and the one that shows remove() and append() moving a point between two
    /// series.
    /// </summary>
    public static class ScatterInteractions
    {
        /// <summary>The example's handleClickedPoint: move the closest point to the click.</summary>
        internal static void MoveClosest(QScatterSeries from, QScatterSeries to, QPointF clicked)
        {
            var closest = new QPointF(int.MaxValue, int.MaxValue);
            double distance = int.MaxValue;
            foreach (var candidate in from.Points())
            {
                double current = Math.Sqrt(Math.Pow(candidate.X() - clicked.X(), 2)
                                           + Math.Pow(candidate.Y() - clicked.Y(), 2));
                if (current < distance)
                {
                    distance = current;
                    closest = candidate;
                }
            }
            from.Remove(closest);
            to.Append(closest);
        }

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var scatter = new QScatterSeries();
            scatter.SetName("scatter1");
            for (double x = 0.5; x <= 4.0; x += 0.5)
                for (double y = 0.5; y <= 4.0; y += 0.5)
                    scatter.Append(x, y);

            var scatter2 = new QScatterSeries();
            scatter2.SetName("scatter2");

            var chart = new QChart();
            chart.SetTitle("Click to interact with scatter points");
            // The example adds the empty series first, so the grid is drawn on top of it.
            chart.AddSeries(scatter2);
            chart.AddSeries(scatter);
            chart.CreateDefaultAxes();
            ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Horizontal)[0]).SetRange(0, 4.5);
            ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Vertical)[0]).SetRange(0, 4.5);

            int clicks = 0;
            scatter.Clicked += point =>
            {
                clicks++;
                MoveClosest(scatter, scatter2, point);
            };

            var view = ChartsAssertions.Present(chart);

            check.Check(scatter.Count() == 64, $"the grid is 8x8 points (got {scatter.Count()})");
            check.Check(scatter2.Count() == 0, $"the receiving series starts empty (got {scatter2.Count()})");
            check.Check(chart.Series().Length == 2, $"both series are on the chart (got {chart.Series().Length})");
            check.Check(scatter2.Name == "scatter2", "the second series keeps its name");

            // A click near (3.1, 2.9) has to take (3.0, 3.0) - the nearest grid point - and move it.
            var target = new QPointF(3.1, 2.9);
            scatter.ClickedSignal.Emit(target);
            check.Check(clicks == 1, $"the click reached the handler (got {clicks})");
            check.Check(scatter.Count() == 63, $"the grid lost one point (got {scatter.Count()})");
            check.Check(scatter2.Count() == 1, $"and the other series gained it (got {scatter2.Count()})");

            var moved = scatter2.Points()[0];
            check.Check(Math4.Nearly(moved.X(), 3.0, 0.0001) && Math4.Nearly(moved.Y(), 3.0, 0.0001),
                $"the nearest grid point moved, not another one ({moved.X()},{moved.Y()})");

            // It is gone from the first series.
            bool stillThere = false;
            foreach (var p in scatter.Points())
                if (Math4.Nearly(p.X(), 3.0, 0.0001) && Math4.Nearly(p.Y(), 3.0, 0.0001))
                    stillThere = true;
            check.Check(!stillThere, "the moved point is no longer in the original series");

            // Two more clicks move two more points, always taking the nearest one.
            scatter.ClickedSignal.Emit(new QPointF(0.6, 0.6));
            scatter.ClickedSignal.Emit(new QPointF(4.4, 0.6));
            check.Check(scatter2.Count() == 3, $"three points have moved (got {scatter2.Count()})");
            check.Check(scatter.Count() == 61, $"the grid is down to 61 (got {scatter.Count()})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"both series are drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
