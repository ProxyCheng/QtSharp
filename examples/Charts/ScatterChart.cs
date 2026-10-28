﻿using QtSharp.Charts;

namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Qt's examples/charts ScatterWidget: three scatter series with different marker shapes and
    /// sizes, the third one drawn with a star image as its brush, and the legend marker shape taken
    /// from the series.
    ///
    /// The star is painted into a QImage through a QPainter, which is also the first thing in this
    /// suite that exercises the painting classes from the extension's side.
    /// </summary>
    [Example("qtcharts-scatterchart")]
public static class ScatterChart
    {
        public static int Run(ExampleContext example)
        {
            var series0 = new QScatterSeries();
            series0.SetName("scatter1");
            series0.SetMarkerShape(QScatterSeries.MarkerShape.MarkerShapeCircle);
            series0.SetMarkerSize(15.0);

            var series1 = new QScatterSeries();
            series1.SetName("scatter2");
            series1.SetMarkerShape(QScatterSeries.MarkerShape.MarkerShapeRectangle);
            series1.SetMarkerSize(20.0);

            var series2 = new QScatterSeries();
            series2.SetName("scatter3");
            series2.SetMarkerShape(QScatterSeries.MarkerShape.MarkerShapeRectangle);
            series2.SetMarkerSize(30.0);

            series0.Append(0, 6);
            series0.Append(2, 4);
            series0.Append(3, 8);
            series0.Append(7, 4);
            series0.Append(10, 5);

            foreach (var p in new[] { new QPointF(1, 1), new QPointF(3, 3), new QPointF(7, 6),
                                      new QPointF(8, 3), new QPointF(10, 2) })
                series1.Append(p);
            foreach (var p in new[] { new QPointF(1, 5), new QPointF(4, 6),
                                      new QPointF(6, 3), new QPointF(9, 5) })
                series2.Append(p);

            // The star brush, exactly as the example builds it: a five-pointed path, then a
            // transparent image painted with it.
            var starPath = new QPainterPath();
            starPath.MoveTo(new QPointF(28, 15));
            for (int i = 1; i < 5; ++i)
                starPath.LineTo(new QPointF(14 + 14 * System.Math.Cos(0.8 * i * System.Math.PI),
                                            15 + 14 * System.Math.Sin(0.8 * i * System.Math.PI)));
            starPath.CloseSubpath();

            using var star = new QImage(30, 30, QImage.Format.Format_ARGB32);
            star.Fill(new QColor(0, 0, 0, 0));
            using (var painter = new QPainter(star))
            {
                painter.SetRenderHint(QPainter.RenderHint.Antialiasing);
                var pen = new QPen(new QColor(0xf6a625));
                painter.SetPen(pen);
                painter.SetBrush(new QBrush(pen.Color()));
                painter.DrawPath(starPath);
            }
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(star.Width() == 30 && star.Height() == 30, "the star image is 30x30");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(CountOpaque(star) > 50, $"the star path actually painted ({CountOpaque(star)} opaque pixels)");
            #endif

            series2.SetBrush(new QBrush(star));
            series2.SetPen(new QPen(new QColor(0, 0, 0, 0)));

            var chart = new QChart();
            chart.AddSeries(series0);
            chart.AddSeries(series1);
            chart.AddSeries(series2);
            chart.SetTitle("Simple Scatter Chart");
            chart.CreateDefaultAxes();
            chart.SetDropShadowEnabled(false);
            chart.Legend().SetMarkerShape(QLegend.MarkerShape.MarkerShapeFromSeries);

            var view = ChartsAssertions.Present(example, chart);
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(series0.Count() == 5, $"the first series carries five points (got {series0.Count()})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series1.Count() == 5, $"the second series carries five points (got {series1.Count()})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series2.Count() == 4, $"the third series carries four points (got {series2.Count()})");
            #endif
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(series0.markerShape() == QScatterSeries.MarkerShape.MarkerShapeCircle,
                $"the first series uses circles (got {series0.markerShape()})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series1.markerShape() == QScatterSeries.MarkerShape.MarkerShapeRectangle,
                $"the second uses rectangles (got {series1.markerShape()})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(series0.MarkerSize() == 15.0 && series1.MarkerSize() == 20.0 && series2.MarkerSize() == 30.0,
                $"the three marker sizes round-trip ({series0.MarkerSize()}/{series1.MarkerSize()}/{series2.MarkerSize()})");
            #endif
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(series0.type() == QAbstractSeries.SeriesType.SeriesTypeScatter,
                $"a scatter series reports itself as one (got {series0.type()})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(!chart.IsDropShadowEnabled(), "the drop shadow is off, as the example asks");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Legend().markerShape() == QLegend.MarkerShape.MarkerShapeFromSeries,
                $"the legend takes its marker shape from the series (got {chart.Legend().markerShape()})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Series().Length == 3, $"the chart holds three series (got {chart.Series().Length})");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                #if QTSHARP_EXAMPLE_SMOKE
                example.Check(ink > 50, $"the markers are actually drawn ({ink} ink samples)");
                #endif
            }

            #if QTSHARP_EXAMPLE_SMOKE
            return example.ExitCode;
            #else
            return 0;
            #endif
        }

        /// <summary>How many pixels of the star image the path actually covered.</summary>
        static int CountOpaque(QImage image)
        {
            int opaque = 0;
            for (int y = 0; y < image.Height(); y++)
                for (int x = 0; x < image.Width(); x++)
                    if (((image.Pixel(x, y) >> 24) & 0xFF) != 0)
                        opaque++;
            return opaque;
        }
    }
}
