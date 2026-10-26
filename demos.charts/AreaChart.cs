using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts AreaWidget: two line series bounding a filled QAreaSeries, a 3px pen and
    /// a green linear gradient in object-bounding coordinates, with both axes given explicit ranges.
    /// </summary>
    [Demo("qtcharts-areachart")]
public static class AreaChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series0 = new QLineSeries();
            var series1 = new QLineSeries();

            foreach (var p in new[] { new QPointF(1, 5), new QPointF(3, 7), new QPointF(7, 6),
                                      new QPointF(9, 7), new QPointF(12, 6), new QPointF(16, 7),
                                      new QPointF(18, 5) })
                series0.Append(p);
            foreach (var p in new[] { new QPointF(1, 3), new QPointF(3, 4), new QPointF(7, 3),
                                      new QPointF(8, 2), new QPointF(12, 3), new QPointF(16, 4),
                                      new QPointF(18, 3) })
                series1.Append(p);

            var series = new QAreaSeries(series0, series1);
            series.SetName("Batman");

            var pen = new QPen(new QColor(0x059605));
            pen.SetWidth(3);
            series.SetPen(pen);

            var gradient = new QLinearGradient(new QPointF(0, 0), new QPointF(0, 1));
            gradient.SetColorAt(0.0, new QColor(0x3cc63c));
            gradient.SetColorAt(1.0, new QColor(0x26f626));
            gradient.SetCoordinateMode(QGradient.CoordinateMode.ObjectBoundingMode);
            series.SetBrush(new QBrush(gradient));

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Simple Area Chart");
            chart.CreateDefaultAxes();
            ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Horizontal)[0]).SetRange(0, 20);
            ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Vertical)[0]).SetRange(0, 10);

            var view = ChartsAssertions.Present(chart);

            check.Check(series0.Count() == 7, $"the upper line has seven points (got {series0.Count()})");
            check.Check(series1.Count() == 7, $"the lower line has seven points (got {series1.Count()})");
            check.Check(series.UpperSeries.Handle == series0.Handle, "the area knows its upper series");
            check.Check(series.LowerSeries.Handle == series1.Handle, "the area knows its lower series");
            check.Check(series.Name == "Batman", $"the area keeps its name (got '{series.Name}')");
            check.Check(series.type() == QAbstractSeries.SeriesType.SeriesTypeArea,
                $"the series reports itself as an area (got {series.type()})");
            check.Check(Math4.Nearly(series.Pen().Width(), 3.0, 0.001),
                $"the outline is three pixels wide (got {series.Pen().Width()})");
            check.Check(series.Pen().Color().Green() == 0x96,
                $"the outline is the example's green (got {series.Pen().Color().Green():X2})");

            check.Check(chart.Axes(Qt.Orientations.Horizontal).Length == 1, "createDefaultAxes built one x axis");
            check.Check(chart.Axes(Qt.Orientations.Vertical).Length == 1, "createDefaultAxes built one y axis");

            var axisX = QValueAxis.FromHandle(chart.Axes(Qt.Orientations.Horizontal)[0].Handle);
            var axisY = QValueAxis.FromHandle(chart.Axes(Qt.Orientations.Vertical)[0].Handle);
            check.Check(Math4.Nearly(axisX.Min, 0, 0.001) && Math4.Nearly(axisX.Max, 20, 0.001),
                $"the x axis holds 0..20 (got {axisX.Min}..{axisX.Max})");
            check.Check(Math4.Nearly(axisY.Min, 0, 0.001) && Math4.Nearly(axisY.Max, 10, 0.001),
                $"the y axis holds 0..10 (got {axisY.Min}..{axisY.Max})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                // The point of the example is the *fill*, so the assertion counts the green the
                // gradient put between the two lines rather than just any ink.
                using var image = rendered.ToImage();
                int green = 0;
                for (int y = 0; y < image.Height(); y += 2)
                    for (int x = 0; x < image.Width(); x += 2)
                    {
                        var c = new QColor(image.Pixel(x, y));
                        if (c.Green() > 0x80 && c.Red() < 0xA0 && c.Blue() < 0xA0)
                            green++;
                    }
                check.Check(green > 500, $"the area between the lines is filled green ({green} samples)");
            }

            return check.Finish();
        }
    }
}
