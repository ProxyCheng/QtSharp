using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts MultiAxisWidget: two spline series sharing one x axis but each getting a
    /// vertical axis of its own, the second one a QCategoryAxis with named bands, and both vertical
    /// axes coloured from their series' pen.
    /// </summary>
    [Demo("qtcharts-multiaxis")]
public static class MultiAxisChart
    {
        public static int Run(DemoContext demo)
        {
            var chart = new QChart();
            ChartsAssertions.HideLegend(chart);
            chart.SetTitle("Multiple Vertical Axes in Chart");

            var axisX = new QValueAxis();
            axisX.SetTickCount(10);
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);

            var series1 = new QSplineSeries();
            foreach (var p in new[] { new QPointF(1, 5), new QPointF(3.5, 18), new QPointF(4.8, 7.5),
                                      new QPointF(10, 2.5) })
                series1.Append(p);
            chart.AddSeries(series1);

            var axisY = new QValueAxis();
            axisY.SetLinePenColor(series1.Pen().Color());
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series1.AttachAxis(axisX);
            series1.AttachAxis(axisY);

            var series2 = new QSplineSeries();
            foreach (var p in new[] { new QPointF(1, 0.5), new QPointF(1.5, 4.5), new QPointF(2.4, 2.5),
                                      new QPointF(4.3, 12.5), new QPointF(5.2, 3.5), new QPointF(7.4, 16.5),
                                      new QPointF(8.3, 7.5), new QPointF(10, 17) })
                series2.Append(p);
            chart.AddSeries(series2);

            var axisY3 = new QCategoryAxis();
            axisY3.Append("Low", 5);
            axisY3.Append("Medium", 12);
            axisY3.Append("High", 17);
            axisY3.SetLinePenColor(series2.Pen().Color());
            axisY3.SetGridLinePen(series2.Pen());
            chart.AddAxis(axisY3, Qt.Alignment.AlignRight);
            series2.AttachAxis(axisX);
            series2.AttachAxis(axisY3);

            var view = ChartsAssertions.Present(demo, chart);
#if QTSHARP_DEMO_SMOKE

            demo.Check(chart.Series().Length == 2, $"the chart holds two series (got {chart.Series().Length})");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series1.Count() == 4, $"the first spline has four points (got {series1.Count()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series2.Count() == 8, $"the second has eight (got {series2.Count()})");
            #endif
#if QTSHARP_DEMO_SMOKE

            // One x axis for both, but three axes on the chart in total: the shared x, the left value
            // axis and the right category axis.
            demo.Check(series1.AttachedAxes().Length == 2, "the first series is on the x axis and the left y axis");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series2.AttachedAxes().Length == 2, "the second is on the x axis and the right y axis");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(chart.Axes(Qt.Orientations.Vertical, null).Length == 2,
                $"the chart has two vertical axes (got {chart.Axes(Qt.Orientations.Vertical, null).Length})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(chart.Axes(Qt.Orientations.Horizontal, null).Length == 1,
                $"and one horizontal one (got {chart.Axes(Qt.Orientations.Horizontal, null).Length})");
            #endif
#if QTSHARP_DEMO_SMOKE

            // The category axis' named bands.
            demo.Check(axisY3.Count == 3, $"the category axis has three bands (got {axisY3.Count})");
#endif
            var labels = axisY3.CategoriesLabels;
            #if QTSHARP_DEMO_SMOKE
            demo.Check(labels.Length == 3 && labels[0] == "Low" && labels[2] == "High",
                $"the bands are named Low/Medium/High (got [{string.Join(",", labels)}])");
            #endif
            #if QTSHARP_DEMO_SMOKE
            // QCategoryAxis::append(label, endValue) closes a band at that value, so "High" starts
            // where "Medium" ended.
            demo.Check(axisY3.EndValue("High") == 17,
                $"High ends at 17, where the example put it (got {axisY3.EndValue("High")})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(axisY3.StartValue("High") == 12,
                $"and starts where Medium ended (got {axisY3.StartValue("High")})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(axisY3.Type() == QAbstractAxis.AxisType.AxisTypeCategory,
                $"the right axis is a category axis (got {axisY3.Type()})");
            #endif
#if QTSHARP_DEMO_SMOKE

            // Both vertical axes take their colour from their series' pen, which is the example's way
            // of telling the reader which series belongs to which axis.
            demo.Check(axisY.LinePenColor() == series1.Pen().Color()
                        || axisY.LinePenColor().Red() == series1.Pen().Color().Red(),
                "the left axis took the first series' colour");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(axisY3.LinePenColor().Red() == series2.Pen().Color().Red(),
                "the right axis took the second series' colour");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(axisY3.GridLinePen.Width() == series2.Pen().Width(),
                "the right axis' grid lines use the series' pen");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                #if QTSHARP_DEMO_SMOKE
                demo.Check(ink > 100, $"both splines are actually drawn ({ink} ink samples)");
                #endif
            }

            #if QTSHARP_DEMO_SMOKE
            return demo.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
