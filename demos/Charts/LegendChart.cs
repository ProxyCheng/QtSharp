using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts LegendWidget: the legend's own options - tool tips, a translucent brush
    /// and pen, interactivity, detaching it from the chart, bold and italic fonts, adding and
    /// removing bar sets, and cycling the alignment.
    ///
    /// The example's four geometry spin boxes are left out: they drive
    /// QLegend::setGeometry()/x()/y()/size(), which come from QGraphicsWidget and are not part of the
    /// bound surface (the same secondary-base gap that hides legend->hide() in the spline example).
    /// Everything else is the example's own sequence.
    /// </summary>
    [Demo("qtcharts-legend")]
public static class LegendChart
    {
        public static int Run(DemoContext demo)
        {
            var chart = new QChart();
            chart.SetTitle("Legend Options");

            var series = new QBarSeries();
            for (int i = 0; i < 4; i++)
            {
                var barSet = new QBarSet("set " + series.Count);
                double delta = series.Count * 0.1;
                barSet.Append(new[] { 1 + delta, 2 + delta, 3 + delta, 4 + delta });
                series.Append(barSet);
            }
            chart.AddSeries(series);
            chart.CreateDefaultAxes();

            var legend = chart.Legend();
            legend.SetShowToolTips(true);
            legend.SetBrush(new QBrush(new QColor(128, 128, 128, 128)));
            legend.SetPen(new QPen(new QColor(192, 192, 192, 192)));
            legend.SetInteractive(true);
            ChartsAssertions.SetLegendVisible(chart, true);
            legend.SetAlignment(Qt.Alignment.AlignBottom);

            var font = legend.Font;
            font.SetPointSizeF(12.0);
            legend.SetFont(font);

            // The example turns the legend's background on exactly while it is detached.
            #if QTSHARP_DEMO_SMOKE
            bool sawAttachedChange = false;
            #endif
            bool lastAttached = false;
            legend.AttachedToChartChanged += attachedToChart =>
            {
                #if QTSHARP_DEMO_SMOKE
                sawAttachedChange = true;
                #endif
                lastAttached = attachedToChart;
                legend.SetBackgroundVisible(!attachedToChart);
            };

            var view = ChartsAssertions.Present(demo, chart);
#if QTSHARP_DEMO_SMOKE

            demo.Check(legend.ShowToolTips, "the legend shows tool tips");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.IsInteractive(), "the legend is interactive");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(ChartsAssertions.IsLegendVisible(chart), "the legend is visible");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.Alignment == Qt.Alignment.AlignBottom, "the legend sits at the bottom");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.Brush().Color().Alpha() == 128,
                $"the translucent brush round-trips (alpha {legend.Brush().Color().Alpha()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.Pen().Color().Alpha() == 192,
                $"the translucent pen round-trips (alpha {legend.Pen().Color().Alpha()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.Font.PointSizeF() == 12.0,
                $"the legend font is 12pt (got {legend.Font.PointSizeF()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series.BarSets().Length == 4, $"four bar sets to start with (got {series.BarSets().Length})");
            #endif

            // Add Barset.
            var added = new QBarSet("set " + series.Count);
            added.Append(new[] { 1.4, 2.4, 3.4, 4.4 });
            series.Append(added);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series.BarSets().Length == 5, $"adding a bar set grew the series (got {series.BarSets().Length})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(chart.Legend().Markers(null).Length >= 5,
                $"the legend grew a marker with it (got {chart.Legend().Markers(null).Length})");
            #endif

            // Remove Barset: the example drops the last one.
            var sets = series.BarSets();
            series.Remove(sets[sets.Length - 1]);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(series.BarSets().Length == 4, $"removing one shrank it back (got {series.BarSets().Length})");
            #endif
#if QTSHARP_DEMO_SMOKE

            // Detach and attach again - the signal the example drives its background from.
            demo.Check(legend.IsAttachedToChart(), "the legend starts attached to the chart");
#endif
            legend.DetachFromChart();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(!legend.IsAttachedToChart(), "after detachFromChart it is not attached");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(sawAttachedChange && !lastAttached,
                "the attachedToChartChanged signal reached the handler with false");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.IsBackgroundVisible(),
                "the handler turned the legend's background on while it is detached");
            #endif
            legend.AttachToChart();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.IsAttachedToChart(), "attachToChart puts it back");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(lastAttached, "and the signal reports true again");
            #endif

            // Toggle Interactive, then Bold and Italic, as the example's buttons do.
            legend.SetInteractive(!legend.IsInteractive());
            #if QTSHARP_DEMO_SMOKE
            demo.Check(!legend.IsInteractive(), "interactivity toggles off");
            #endif
            legend.SetInteractive(!legend.IsInteractive());
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.IsInteractive(), "and back on");
            #endif

            var bold = legend.Font;
            bold.SetBold(!bold.Bold());
            legend.SetFont(bold);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.Font.Bold(), "the legend font can be made bold");
            #endif
            var italic = legend.Font;
            italic.SetItalic(!italic.Italic());
            legend.SetFont(italic);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.Font.Italic(), "and italic");
            #endif

            // Align (Bottom) -> the example cycles Top/Left/Bottom/Right.
            legend.SetAlignment(Qt.Alignment.AlignRight);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(legend.Alignment == Qt.Alignment.AlignRight,
                $"the alignment cycles (got {legend.Alignment})");
            #endif
            legend.SetAlignment(Qt.Alignment.AlignBottom);

            using var rendered = view.Grab();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                #if QTSHARP_DEMO_SMOKE
                demo.Check(ink > 100, $"the bars and the legend are drawn ({ink} ink samples)");
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
