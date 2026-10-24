using System;
using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts LegendMarkersWidget: four sine waves, every legend marker connected to a
    /// click handler that toggles its series' visibility and dims the marker while the series is
    /// hidden.
    ///
    /// This is the example that shows QLegendMarker is a real C# object: the handler gets the marker
    /// back, walks to its series, and writes the label brush, brush and pen through the binding.
    /// </summary>
    [Demo("qtcharts-legendmarkers")]
public static class LegendMarkersChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var chart = new QChart();
            var series = new System.Collections.Generic.List<QLineSeries>();

            void AddSeries()
            {
                var line = new QLineSeries();
                series.Add(line);
                line.SetName("line " + series.Count);

                int offset = chart.Series().Length;
                for (int i = 0; i < 360; i++)
                {
                    double x = offset * 20 + i;
                    line.Append(i, Math.Sin(x * Math.PI / 180.0));
                }

                chart.AddSeries(line);
                if (series.Count == 1)
                    chart.CreateDefaultAxes();
            }

            AddSeries();
            AddSeries();
            AddSeries();
            AddSeries();

            // The example's connectMarkers(): every marker of the legend, so a click reaches the
            // series behind it.
            int clicks = 0;
            int markersSeen = 0;
            foreach (var marker in chart.Legend().Markers())
            {
                markersSeen++;
                var captured = marker;
                captured.Clicked += () =>
                {
                    clicks++;
                    if (captured.Type() != QLegendMarker.LegendMarkerType.LegendMarkerTypeXY)
                        return;

                    // Toggle the series, then keep the marker visible: hiding a series would
                    // otherwise hide its marker too.
                    captured.Series().SetVisible(!captured.Series().IsVisible());
                    captured.SetVisible(true);

                    double alpha = captured.Series().IsVisible() ? 1.0 : 0.5;

                    var labelBrush = captured.LabelBrush;
                    var labelColour = labelBrush.Color();
                    labelColour.SetAlphaF((float)alpha);
                    labelBrush.SetColor(labelColour);
                    captured.SetLabelBrush(labelBrush);

                    var brush = captured.Brush;
                    var brushColour = brush.Color();
                    brushColour.SetAlphaF((float)alpha);
                    brush.SetColor(brushColour);
                    captured.SetBrush(brush);

                    var pen = captured.Pen;
                    var penColour = pen.Color();
                    penColour.SetAlphaF((float)alpha);
                    pen.SetColor(penColour);
                    captured.SetPen(pen);
                };
            }

            chart.SetTitle("Legend Markers (Click on legend to interact)");
            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(chart);

            check.Check(series.Count == 4, $"four sine waves were added (got {series.Count})");
            check.Check(chart.Series().Length == 4, $"the chart holds all four (got {chart.Series().Length})");
            check.Check(series[0].Count() == 360, $"a wave has a point per degree (got {series[0].Count()})");
            check.Check(markersSeen == 4, $"the legend has one marker per series (got {markersSeen})");
            check.Check(chart.Axes(Qt.Orientations.Vertical).Length == 1,
                "the first series' createDefaultAxes() built the axes");

            var markers = chart.Legend().Markers();
            check.Check(markers[0].Type() == QLegendMarker.LegendMarkerType.LegendMarkerTypeXY,
                $"a sine wave's marker is an XY marker (got {markers[0].Type()})");
            check.Check(markers[0].Series().Handle == series[0].Handle,
                "a marker knows the series it belongs to");
            check.Check(markers[1].Label == "line 2", $"the marker label follows the series' name (got '{markers[1].Label}')");

            // Clicking a marker hides the series and dims the marker to half alpha.
            check.Check(clicks == 0, "no marker has been clicked yet");
            check.Check(series[0].IsVisible(), "the first series starts visible");
            markers[0].ClickedSignal.Emit();
            check.Check(clicks == 1, $"the click reached the handler (got {clicks})");
            check.Check(!series[0].IsVisible(), "and the series behind the marker is now hidden");
            check.Check(markers[0].IsVisible(), "the marker itself is kept visible");
            check.Check(Math4.Nearly(markers[0].LabelBrush.Color().AlphaF(), 0.5, 0.01),
                $"the marker's label is dimmed to 50% (got {markers[0].LabelBrush.Color().AlphaF():F2})");
            check.Check(Math4.Nearly(markers[0].Brush.Color().AlphaF(), 0.5, 0.01),
                "the marker's brush is dimmed too");
            check.Check(Math4.Nearly(markers[0].Pen.Color().AlphaF(), 0.5, 0.01),
                "and so is its pen");

            // Clicking again brings it back.
            markers[0].ClickedSignal.Emit();
            check.Check(series[0].IsVisible(), "a second click shows the series again");
            check.Check(Math4.Nearly(markers[0].LabelBrush.Color().AlphaF(), 1.0, 0.01),
                $"and the marker goes back to full alpha (got {markers[0].LabelBrush.Color().AlphaF():F2})");
            check.Check(!series[1].IsVisible() == false, "the other series were left alone");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 100, $"the waves are actually drawn ({ink} ink samples)");
            }

            return check.Finish();
        }
    }
}
