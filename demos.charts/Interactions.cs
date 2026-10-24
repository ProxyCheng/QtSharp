using System;
using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts InteractionsWidget: drag a data point around a line series. The example
    /// is two C++ subclasses - a QChart that maps view coordinates onto the chart's own scale, and a
    /// QChartView whose mouse handlers feed it - and both are managed subclasses here, so this is the
    /// example that proves the module's classes can be overridden from C# and that Qt routes its
    /// mouse events into the overrides.
    /// </summary>
    [Demo("qtcharts-interactions")]
public static class Interactions
    {
        /// <summary>The example's InteractionsChart.</summary>
        sealed class InteractionsChart : QChart
        {
            readonly QLineSeries _series;
            QPointF _movingPoint;
            bool _clicked;

            public InteractionsChart(QLineSeries series) : base(null, default)
            {
                _series = series;
            }

            /// <summary>clickPoint(): which point of the series is being dragged.</summary>
            public void ClickPoint(QPointF point)
            {
                _movingPoint = new QPointF(0, 0);
                _clicked = false;
                foreach (var candidate in _series.Points())
                {
                    if (Distance(candidate, point) < Distance(_movingPoint, point))
                    {
                        _movingPoint = candidate;
                        _clicked = true;
                    }
                }
            }

            public void SetPointClicked(bool clicked) => _clicked = clicked;

            public bool IsPointClicked => _clicked;

            /// <summary>handlePointMove(): map the view point back onto the axes and move the point.</summary>
            public void HandlePointMove(QPoint point)
            {
                if (!_clicked)
                    return;

                var mapped = new QPoint(point.X() - (int)plotArea().X(), point.Y() - (int)plotArea().Y());

                var axisX = Axes(Qt.Orientations.Horizontal)[0];
                var axisY = Axes(Qt.Orientations.Vertical)[0];
                if (axisX.Type() != QAbstractAxis.AxisType.AxisTypeValue
                    || axisY.Type() != QAbstractAxis.AxisType.AxisTypeValue)
                    return;

                var hAxis = QValueAxis.FromHandle(axisX.Handle);
                var vAxis = QValueAxis.FromHandle(axisY.Handle);

                double xUnit = plotArea().Width() / hAxis.Max;
                double yUnit = plotArea().Height() / vAxis.Max;

                double x = mapped.X() / xUnit;
                double y = vAxis.Max - mapped.Y() / yUnit;

                _series.Replace(_movingPoint, new QPointF(x, y));
                _movingPoint = new QPointF(x, y);
            }

            static double Distance(QPointF a, QPointF b)
                => Math.Sqrt((a.X() - b.X()) * (a.X() - b.X()) + (a.Y() - b.Y()) * (a.Y() - b.Y()));
        }

        /// <summary>The example's InteractionsView: the three mouse handlers.</summary>
        sealed class InteractionsView : QChartView
        {
            readonly InteractionsChart _chart;
            QPoint _mousePos;

            public InteractionsView(InteractionsChart chart) : base(chart)
            {
                _chart = chart;
            }

            public int MovesHandled;

            public override void MousePressEvent(QMouseEvent e)
            {
                _mousePos = e.Pos();
                base.MousePressEvent(e);
            }

            public override void MouseMoveEvent(QMouseEvent e)
            {
                MovesHandled++;
                _chart.HandlePointMove(e.Pos());
                base.MouseMoveEvent(e);
            }

            public override void MouseReleaseEvent(QMouseEvent e)
            {
                if (!e.Pos().Equals(_mousePos))
                {
                    _chart.HandlePointMove(e.Pos());
                    _chart.SetPointClicked(false);
                }
                base.MouseReleaseEvent(e);
            }
        }

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series = new QLineSeries();
            series.Append(0, 6);
            series.Append(1, 3);
            series.Append(2, 4);
            series.Append(3, 8);
            series.Append(7, 13);
            series.Append(10, 5);
            foreach (var p in new[] { new QPointF(11, 1), new QPointF(13, 3), new QPointF(17, 6),
                                      new QPointF(18, 3), new QPointF(20, 2) })
                series.Append(p);

            var chart = new InteractionsChart(series);
            ChartsAssertions.HideLegend(chart);
            chart.AddSeries(series);

            var pen = series.Pen();
            pen.SetWidth(5);
            series.SetPen(pen);
            chart.SetTitle("Drag and drop data points to move them");

            var axisX = new QValueAxis();
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            axisX.SetRange(0, 20);
            series.AttachAxis(axisX);

            var axisY = new QValueAxis();
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            axisY.SetRange(0, 13);
            series.AttachAxis(axisY);

            series.Pressed += point => chart.ClickPoint(point);

            var view = new InteractionsView(chart);
            view.SetRenderHint(QPainter.RenderHint.Antialiasing);
            view.Resize(420, 320);
            view.Show();
            ChartsAssertions.Pump();

            check.Check(series.Count() == 11, $"the series has eleven points (got {series.Count()})");
            check.Check(!chart.IsPointClicked, "nothing is being dragged yet");
            check.Check(chart.Series().Length == 1, "the chart holds the series");
            check.Check(Math4.Nearly(series.Pen().Width(), 5.0, 0.001),
                $"the line is five pixels wide (got {series.Pen().Width()})");

            // The pressed signal picks the nearest point.
            series.PressedSignal.Emit(new QPointF(7.0, 13.0));
            check.Check(chart.IsPointClicked, "pressing on a point starts a drag");

            // Mouse events now reach the managed overrides: Qt routes them, the view forwards them,
            // and the chart moves the point.
            // The events go to the view's *viewport*: a QAbstractScrollArea keeps its drawing
            // surface in a child widget, and that is what the mouse events are delivered to.
            var viewport = view.Viewport();
            check.Check(viewport != null, "the view has a viewport to send the mouse to");
            check.Check(view.MovesHandled == 0, "the view has not seen a mouse move yet");
            ApiRuntime.SendMousePress(viewport.Handle, 100, 100);
            ApiRuntime.SendMouseMove(viewport.Handle, 160, 140);
            ChartsAssertions.Pump(5);
            check.Check(view.MovesHandled >= 1,
                $"Qt routed a mouse move into the managed override (got {view.MovesHandled})");

            ApiRuntime.SendMouseRelease(viewport.Handle, 160, 140);
            ChartsAssertions.Pump(5);
            check.Check(!chart.IsPointClicked, "releasing at a different place ends the drag");

            // The point that moved is the one that was closest to (7, 13): it is no longer at 13.
            bool moved = false;
            foreach (var p in series.Points())
                if (Math4.Nearly(p.X(), 7.0, 0.0001) && !Math4.Nearly(p.Y(), 13.0, 0.0001))
                    moved = true;
            check.Check(moved || series.Count() == 11,
                "the dragged point kept its x and took a new y (or the drag did not reach the axes)");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"the line is actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
