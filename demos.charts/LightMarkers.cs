using System;
using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts LightMarkersWidget: a spline whose points are drawn as images rather than
    /// as the series' own marker, with a second image for the points that are selected, and a click
    /// selecting the point under the cursor.
    ///
    /// Two differences from the C++: the blue and green triangles come from ":blue_triangle" and
    /// ":green_triangle", which are not part of this repository, so they are painted here instead;
    /// and the three combo boxes of the gallery are represented by calling the same three setters.
    /// </summary>
    public static class LightMarkers
    {
        const double MarkerSize = 20.0;

        static QImage Rectangle(double size, QColor colour)
        {
            var image = new QImage((int)size, (int)size, QImage.Format.Format_RGB32);
            image.Fill(colour);
            return image;
        }

        static QImage Circle(double size, QColor colour)
        {
            var image = new QImage((int)size, (int)size, QImage.Format.Format_ARGB32);
            image.Fill(new QColor(0, 0, 0, 0));
            using var painter = new QPainter(image);
            painter.SetBrush(new QBrush(colour));
            var pen = painter.Pen();
            pen.SetWidth(0);
            painter.SetPen(pen);
            painter.DrawEllipse(new QRectF(0, 0, size * 0.9, size * 0.9));
            return image;
        }

        /// <summary>The example's green triangle, drawn rather than loaded from the qrc.</summary>
        static QImage Triangle(double size, QColor colour)
        {
            var image = new QImage((int)size, (int)size, QImage.Format.Format_ARGB32);
            image.Fill(new QColor(0, 0, 0, 0));
            using var painter = new QPainter(image);
            painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
            painter.SetBrush(new QBrush(colour));
            painter.SetPen(new QPen(new QColor(0, 0, 0, 0)));
            var path = new QPainterPath();
            path.MoveTo(new QPointF(size / 2, 0));
            path.LineTo(new QPointF(size, size));
            path.LineTo(new QPointF(0, size));
            path.CloseSubpath();
            painter.DrawPath(path);
            return image;
        }

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series = new QSplineSeries();
            foreach (var p in new[] { new QPointF(0.0, 0.0), new QPointF(0.5, 2.27), new QPointF(1.5, 2.2),
                                      new QPointF(3.3, 1.7), new QPointF(4.23, 3.1), new QPointF(5.3, 2.3),
                                      new QPointF(6.47, 4.1) })
                series.Append(p);
            series.SetMarkerSize(MarkerSize);
            series.SetLightMarker(Rectangle(MarkerSize, new QColor(Qt.GlobalColor.red)));
            series.SetSelectedLightMarker(Triangle(MarkerSize, new QColor(65, 105, 225)));

            var chart = new QChart();
            chart.AddSeries(series);
            chart.CreateDefaultAxes();
            ChartsAssertions.HideLegend(chart);
            chart.SetTitle("Select points with mouse click");

            var view = ChartsAssertions.Present(chart);

            check.Check(series.Count() == 7, $"the spline has seven points (got {series.Count()})");
            check.Check(Math4.Nearly(series.MarkerSize(), MarkerSize, 0.001),
                $"the marker size round-trips (got {series.MarkerSize()})");

            var marker = series.LightMarker();
            check.Check(!marker.IsNull(), "the unselected light marker is an image, not null");
            check.Check(marker.Width() == (int)MarkerSize && marker.Height() == (int)MarkerSize,
                $"the marker image is {MarkerSize}x{MarkerSize} (got {marker.Width()}x{marker.Height()})");
            check.Check(new QColor(marker.Pixel(0, 0)).Red() > 200,
                $"the unselected marker is the red rectangle ({new QColor(marker.Pixel(0, 0)).Red()})");

            check.Check(!series.SelectedLightMarker().IsNull(), "the selected marker is an image too");

            // The example's clicked handler: find the point and toggle its selection.
            //
            // The C++ is points().indexOf(point); here the points come back from Qt as fresh
            // wrappers each time, so the comparison is on the coordinates rather than on the
            // wrapper - a QList<QPointF> element is a value, and two wrappers for one value are not
            // the same C# object.
            int clicks = 0;
            series.Clicked += point =>
            {
                clicks++;
                var current = series.Points();
                for (int i = 0; i < current.Length; i++)
                {
                    if (Math4.Nearly(current[i].X(), point.X(), 0.0001)
                        && Math4.Nearly(current[i].Y(), point.Y(), 0.0001))
                    {
                        series.ToggleSelection(new[] { i });
                        break;
                    }
                }
            };

            var points = series.Points();
            check.Check(points.Length == 7, $"points() came back with seven (got {points.Length})");
            check.Check(Math4.Nearly(points[4].X(), 4.23, 0.0001),
                $"the fifth point is the one the example appended (got {points[4].X()})");
            check.Check(series.SelectedPoints().Length == 0, "nothing is selected to start with");

            series.ClickedSignal.Emit(points[2]);
            check.Check(clicks == 1, $"the click reached the handler (got {clicks})");
            check.Check(series.SelectedPoints().Length == 1, "the clicked point became selected");
            check.Check(series.SelectedPoints()[0] == 2, "and it is the one that was clicked");
            check.Check(series.IsPointSelected(2), "which isPointSelected agrees with");

            series.ClickedSignal.Emit(points[2]);
            check.Check(series.SelectedPoints().Length == 0, "clicking it again deselects it");

            // The line colour combo box in the gallery goes through setColor().
            series.SetColor(new QColor(70, 203, 155));
            check.Check(series.Color.Green() == 203, $"the line colour round-trips (got {series.Color.Green()})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"the markers are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
