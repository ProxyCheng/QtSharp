using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts PolarChartWidget: a QPolarChart with an angular and a radial value axis,
    /// five series (scatter, spline, two star outlines and the area between them), and a QChartView
    /// subclass that zooms and scrolls on the keyboard.
    ///
    /// The subclass is the interesting half: it is a managed type deriving from this module's
    /// QChartView, and Qt calls its keyPressEvent override.
    /// </summary>
    [Demo("qtcharts-polarchart")]
public static class PolarChart
    {
        /// <summary>The example's PolarChartView: arrow keys scroll, +/- zoom.</summary>
        sealed class PolarChartView : QChartView
        {
            public PolarChartView(QWidget parent = null) : base(parent) { }

            public override void KeyPressEvent(QKeyEvent e)
            {
                switch (e.Key())
                {
                    case (int)Qt.Key.Key_Plus: Chart().ZoomIn(); break;
                    case (int)Qt.Key.Key_Minus: Chart().ZoomOut(); break;
                    case (int)Qt.Key.Key_Left: Chart().Scroll(-10, 0); break;
                    case (int)Qt.Key.Key_Right: Chart().Scroll(10, 0); break;
                    case (int)Qt.Key.Key_Up: Chart().Scroll(0, 10); break;
                    case (int)Qt.Key.Key_Down: Chart().Scroll(0, -10); break;
                    default: base.KeyPressEvent(e); break;
                }
                KeyPresses++;
            }

            public int KeyPresses;
        }

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            const double angularMin = -100;
            const double angularMax = 100;
            const double radialMin = -100;
            const double radialMax = 100;

            var series1 = new QScatterSeries();
            series1.SetName("scatter");
            for (double i = angularMin; i <= angularMax; i += 10)
                series1.Append(i, (i / radialMax) * radialMax + 8.0);

            var series2 = new QSplineSeries();
            series2.SetName("spline");
            for (double i = angularMin; i <= angularMax; i += 10)
                series2.Append(i, (i / radialMax) * radialMax);

            var series3 = new QLineSeries();
            series3.SetName("star outer");
            double ad = (angularMax - angularMin) / 8;
            double rd = (radialMax - radialMin) / 3 * 1.3;
            for (int i = 0; i <= 8; i++)
                series3.Append(angularMin + ad * i, i % 2 == 0 ? radialMax : radialMin + rd);

            var series4 = new QLineSeries();
            series4.SetName("star inner");
            ad = (angularMax - angularMin) / 8;
            rd = (radialMax - radialMin) / 3;
            for (int i = 0; i <= 8; i++)
                series4.Append(angularMin + ad * i, i % 2 == 0 ? radialMax : radialMin + rd);

            var series5 = new QAreaSeries();
            series5.SetName("star area");
            series5.SetUpperSeries(series3);
            series5.SetLowerSeries(series4);
            series5.SetOpacity(0.5);

            var chart = new QPolarChart();
            chart.AddSeries(series1);
            chart.AddSeries(series2);
            chart.AddSeries(series3);
            chart.AddSeries(series4);
            chart.AddSeries(series5);
            chart.SetTitle("Use arrow keys to scroll, +/- to zoom, and space to switch chart type.");

            var angularAxis = new QValueAxis();
            angularAxis.SetTickCount(9);
            angularAxis.SetLabelFormat("%.1f");
            angularAxis.SetShadesVisible(true);
            angularAxis.SetShadesBrush(new QBrush(new QColor(249, 249, 255)));
            chart.AddAxis(angularAxis, QPolarChart.PolarOrientation.PolarOrientationAngular);

            var radialAxis = new QValueAxis();
            radialAxis.SetTickCount(9);
            radialAxis.SetLabelFormat("%d");
            chart.AddAxis(radialAxis, QPolarChart.PolarOrientation.PolarOrientationRadial);

            foreach (var s in new QAbstractSeries[] { series1, series2, series3, series4, series5 })
            {
                s.AttachAxis(radialAxis);
                s.AttachAxis(angularAxis);
            }

            radialAxis.SetRange(radialMin, radialMax);
            angularAxis.SetRange(angularMin, angularMax);

            var view = new PolarChartView();
            view.SetChart(chart);
            view.SetRenderHint(QPainter.RenderHint.Antialiasing);
            view.Resize(420, 320);
            view.Show();
            ChartsAssertions.Pump();

            check.Check(chart.Series().Length == 5, $"the polar chart holds five series (got {chart.Series().Length})");
            check.Check(series1.Count() == 21, $"the scatter series walks -100..100 in tens (got {series1.Count()})");
            check.Check(series3.Count() == 9, $"a star outline has nine points (got {series3.Count()})");
            check.Check(series5.UpperSeries.Handle == series3.Handle && series5.LowerSeries.Handle == series4.Handle,
                "the star area is bounded by the two star outlines");
            check.Check(series5.Opacity == 0.5, $"the area is half transparent (got {series5.Opacity})");

            check.Check(radialAxis.Min == radialMin && radialAxis.Max == radialMax,
                $"the radial axis holds -100..100 (got {radialAxis.Min}..{radialAxis.Max})");
            check.Check(angularAxis.Min == angularMin && angularAxis.Max == angularMax,
                $"the angular axis holds -100..100 (got {angularAxis.Min}..{angularAxis.Max})");
            check.Check(angularAxis.TickCount == 9, $"the angular axis has nine ticks (got {angularAxis.TickCount})");
            check.Check(angularAxis.ShadesVisible, "the angular axis draws shades, as the example asks");
            check.Check(angularAxis.ShadesBrush.Color().Red() == 249,
                $"the shade colour round-trips (got {angularAxis.ShadesBrush.Color().Red()})");

            // The polar chart is a QChart: the two polar orientations are extra axes on top of the
            // ordinary ones, and Qt reports them through the chart's own axis list.
            check.Check(chart.Axes(Qt.Orientations.Vertical, null).Length >= 1,
                "the polar chart still reports vertical axes");

            // The managed subclass: Qt hands the key event to the override, which zooms.
            check.Check(view.KeyPresses == 0, "the view starts with no key presses");
            check.Check(!chart.IsZoomed(), "the chart starts unzoomed");
            ApiRuntime.SendKeyPress(view.Handle, (int)Qt.Key.Key_Plus);
            ChartsAssertions.Pump(5);
            check.Check(view.KeyPresses == 1,
                $"Qt routed a key press into the managed override (reached {view.KeyPresses} time(s))");

            ApiRuntime.SendKeyPress(view.Handle, (int)Qt.Key.Key_Left);
            ChartsAssertions.Pump(5);
            check.Check(view.KeyPresses == 2, $"the override saw the second key too (got {view.KeyPresses})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the polar chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 100, $"the series are actually drawn ({ink} ink samples)");
            }

            return check.Finish();
        }
    }
}
