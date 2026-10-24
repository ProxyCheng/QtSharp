using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts PccWidget: a pie chart with every slice's appearance editable - label,
    /// value, pen, brush, label brush and font, label position, arm length, exploded distance, plus
    /// the series' own position, size and angles and the chart's theme and animations.
    ///
    /// Ported here: the custom slice subclass (PccCustomSlice, which remembers the brush a slice was
    /// made with) and every settings function the example's controls call, driven directly rather
    /// than through the form. Left out: the two popup editors (PccPenTool/PccBrushTool are QWidget
    /// windows of their own, and the font dialog is modal), and the responsive layout juggling in
    /// resizeEvent - all presentation, none of it about the chart.
    /// </summary>
    [Demo("qtcharts-percentcalloutcustom")]
public static class PercentCalloutCustom
    {
        /// <summary>The example's PccCustomSlice: a slice that remembers its original brush.</summary>
        sealed class PccCustomSlice : QPieSlice
        {
            readonly QBrush _originalBrush;

            public PccCustomSlice(string label, double value, QBrush brush = null)
            {
                SetLabel(label);
                SetValue(value);
                _originalBrush = brush ?? new QBrush(new QColor(0x77, 0x77, 0x77));
                SetBrush(_originalBrush);
            }

            public QBrush OriginalBrush() => _originalBrush;
        }

        /// <summary>The example's appendSlice/insertSlice/removeSlice.</summary>
        static QPieSlice AppendSlice(QPieSeries series)
        {
            var slice = new PccCustomSlice("Slice " + (series.Count + 1), 10.0);
            series.Append(slice);
            return slice;
        }

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var chart = new QChart();
            chart.SetTitle("Pie Chart Customization");
            chart.SetAnimationOptions(QChart.AnimationOptions.AllAnimations);

            var series = new QPieSeries();
            var slice1 = new PccCustomSlice("Slice 1", 10.0, new QBrush(new QColor(0xff, 0x00, 0x00)));
            var slice2 = new PccCustomSlice("Slice 2", 20.0, new QBrush(new QColor(0x00, 0xff, 0x00)));
            var slice3 = new PccCustomSlice("Slice 3", 30.0, new QBrush(new QColor(0x00, 0x00, 0xff)));
            var slice4 = new PccCustomSlice("Slice 4", 40.0);
            var slice5 = new PccCustomSlice("Slice 5", 50.0);
            series.Append(slice1);
            series.Append(slice2);
            series.Append(slice3);
            series.Append(slice4);
            series.Append(slice5);
            series.SetLabelsVisible();
            chart.AddSeries(series);

            // handleSliceClicked(): the settings panel reads the clicked slice back out.
            QPieSlice selected = null;
            int clicks = 0;
            series.Clicked += slice =>
            {
                clicks++;
                selected = slice;
            };

            var view = ChartsAssertions.Present(chart);

            // --- the series settings (updateSerieSettings)
            series.SetHorizontalPosition(0.5);
            series.SetVerticalPosition(0.5);
            series.SetPieSize(0.7);
            series.SetPieStartAngle(0);
            series.SetPieEndAngle(360);
            series.SetHoleSize(0.2);
            check.Check(series.HorizontalPosition == 0.5 && series.VerticalPosition == 0.5,
                "the series position round-trips");
            check.Check(series.PieSize() == 0.7, $"the pie size (got {series.PieSize()})");
            check.Check(series.HoleSize == 0.2, $"the hole size (got {series.HoleSize})");
            check.Check(series.PieStartAngle() == 0 && series.PieEndAngle() == 360,
                "the angles round-trip");

            // --- the slice settings (updateSliceSettings), on the slice a click selected
            series.ClickedSignal.Emit(slice3);
            check.Check(clicks == 1 && selected == slice3,
                $"clicking a slice selects it, as the settings panel does (got {clicks})");

            var custom = (PccCustomSlice)selected;
            custom.SetLabel("Renamed");
            custom.SetValue(35.0);
            custom.SetPen(new QPen(new QBrush(new QColor(0, 0, 0)), 2, Qt.PenStyle.DashLine));
            custom.SetBrush(new QBrush(new QColor(0x12, 0x34, 0x56)));
            custom.SetLabelBrush(new QBrush(new QColor(0xff, 0xff, 0xff)));
            custom.SetLabelVisible(true);
            custom.SetLabelArmLengthFactor(0.25);
            custom.SetLabelPosition(QPieSlice.LabelPosition.LabelInsideTangential);
            custom.SetExploded(true);
            custom.SetExplodeDistanceFactor(0.25);

            check.Check(custom.Label == "Renamed", $"the label changed (got '{custom.Label}')");
            check.Check(custom.Value == 35.0, $"the value changed (got {custom.Value})");
            check.Check(custom.Pen.Style() == Qt.PenStyle.DashLine, $"the pen changed (got {custom.Pen.Style()})");
            check.Check(custom.Pen.Width() == 2, "and its width");
            check.Check(custom.Brush.Color().Red() == 0x12, $"the brush changed (got {custom.Brush.Color().Red():X2})");
            check.Check(custom.IsLabelVisible(), "the label is visible");
            check.Check(custom.LabelArmLengthFactor == 0.25,
                $"the arm length (got {custom.LabelArmLengthFactor})");
            check.Check(custom.labelPosition() == QPieSlice.LabelPosition.LabelInsideTangential,
                $"the label position (got {custom.labelPosition()})");
            check.Check(custom.IsExploded(), "the slice is exploded");
            check.Check(custom.ExplodeDistanceFactor == 0.25,
                $"the exploded distance (got {custom.ExplodeDistanceFactor})");

            // The slice still remembers the brush it was built with, which is what the panel shows.
            check.Check(custom.OriginalBrush().Color().Blue() == 0xff,
                $"the original brush is still the one it was made with (got {custom.OriginalBrush().Color().Blue():X2})");

            // --- appendSlice / insertSlice / removeSlice
            var appended = AppendSlice(series);
            check.Check(series.Count == 6, $"appendSlice grew the series (got {series.Count})");
            check.Check(appended.Label == "Slice 6", $"and numbered the label (got '{appended.Label}')");

            int index = System.Array.FindIndex(series.Slices(), s => s.Handle == custom.Handle);
            check.Check(index == 2, $"the selected slice is the third (got {index})");
            series.Insert(index, new PccCustomSlice("Inserted", 10.0));
            check.Check(series.Count == 7, $"insertSlice grew it again (got {series.Count})");
            check.Check(series.Slices()[index].Label == "Inserted",
                $"and put it where it was asked to (got '{series.Slices()[index].Label}')");

            series.Remove(appended);
            check.Check(series.Count == 6, $"removeSlice took one back out (got {series.Count})");

            // --- the chart settings (updateChartSettings)
            foreach (var theme in new[] { QChart.ChartTheme.ChartThemeLight, QChart.ChartTheme.ChartThemeDark,
                                          QChart.ChartTheme.ChartThemeBlueCerulean, QChart.ChartTheme.ChartThemeQt })
            {
                chart.SetTheme(theme);
                check.Check(chart.Theme == theme, $"the {theme} theme applies to the whole chart");
            }

            chart.SetAnimationOptions(QChart.AnimationOptions.NoAnimation);
            check.Check(chart.animationOptions() == QChart.AnimationOptions.NoAnimation,
                "the animations can be switched off");
            chart.SetAnimationOptions(QChart.AnimationOptions.AllAnimations);
            check.Check(chart.animationOptions() == QChart.AnimationOptions.AllAnimations, "and back on");

            ChartsAssertions.SetLegendVisible(chart, true);
            check.Check(ChartsAssertions.IsLegendVisible(chart), "the legend can be shown");
            ChartsAssertions.HideLegend(chart);
            check.Check(!ChartsAssertions.IsLegendVisible(chart), "and hidden");

            view.SetRenderHint(QPainter.RenderHint.Antialiasing, true);

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                check.Check(ChartsAssertions.InkSamples(rendered) > 100,
                    $"the pie is actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");
                check.Check(ChartsAssertions.ColourSamples(rendered) > 8,
                    $"the slices have their own colours ({ChartsAssertions.ColourSamples(rendered)} colours)");
            }

            return check.Finish();
        }
    }
}
