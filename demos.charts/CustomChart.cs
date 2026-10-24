using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts CustomChartWidget: no custom C++ at all, just the chart's own appearance
    /// API pushed as far as it goes - a gradient background and plot area, an orange 5px line, a
    /// white title, and both axes as QCategoryAxis with named bands, white labels and no grid lines.
    ///
    /// It is the example to read when you want to know how far the styling API alone will take you,
    /// so the assertions are all about the styling having come back out.
    /// </summary>
    [Demo("qtcharts-customchart")]
public static class CustomChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var series = new QLineSeries();
            foreach (var p in new[] { new QPointF(0, 6), new QPointF(9, 4), new QPointF(15, 20),
                                      new QPointF(25, 12), new QPointF(29, 26) })
                series.Append(p);

            var chart = new QChart();
            ChartsAssertions.HideLegend(chart);
            chart.AddSeries(series);

            var pen = new QPen(new QColor(0xfdb157));
            pen.SetWidth(5);
            series.SetPen(pen);

            var font = new QFont();
            font.SetPixelSize(18);
            chart.SetTitleFont(font);
            chart.SetTitleBrush(new QBrush(new QColor(Qt.GlobalColor.white)));
            chart.SetTitle("Custom Chart");

            var backgroundGradient = new QLinearGradient();
            backgroundGradient.SetStart(new QPointF(0, 0));
            backgroundGradient.SetFinalStop(new QPointF(0, 1));
            backgroundGradient.SetColorAt(0.0, new QColor(0xd2d0d1));
            backgroundGradient.SetColorAt(1.0, new QColor(0x4c4547));
            backgroundGradient.SetCoordinateMode(QGradient.CoordinateMode.ObjectBoundingMode);
            chart.SetBackgroundBrush(new QBrush(backgroundGradient));

            var plotAreaGradient = new QLinearGradient();
            plotAreaGradient.SetStart(new QPointF(0, 1));
            plotAreaGradient.SetFinalStop(new QPointF(1, 0));
            plotAreaGradient.SetColorAt(0.0, new QColor(0x555555));
            plotAreaGradient.SetColorAt(1.0, new QColor(0x55aa55));
            plotAreaGradient.SetCoordinateMode(QGradient.CoordinateMode.ObjectBoundingMode);
            chart.SetPlotAreaBackgroundBrush(new QBrush(plotAreaGradient));
            chart.SetPlotAreaBackgroundVisible(true);

            var axisX = new QCategoryAxis();
            var axisY = new QCategoryAxis();

            var labelsFont = new QFont();
            labelsFont.SetPixelSize(12);
            axisX.SetLabelsFont(labelsFont);
            axisY.SetLabelsFont(labelsFont);

            var axisPen = new QPen(new QColor(0xd18952));
            axisPen.SetWidth(2);
            axisX.SetLinePen(axisPen);
            axisY.SetLinePen(axisPen);

            var axisBrush = new QBrush(new QColor(Qt.GlobalColor.white));
            axisX.SetLabelsBrush(axisBrush);
            axisY.SetLabelsBrush(axisBrush);

            axisX.SetGridLineVisible(false);
            axisY.SetGridLineVisible(false);
            axisY.SetShadesPen(new QPen(Qt.PenStyle.NoPen));
            axisY.SetShadesBrush(new QBrush(new QColor(0x99, 0xcc, 0xcc, 0x55)));
            axisY.SetShadesVisible(true);

            axisX.Append("low", 10);
            axisX.Append("optimal", 20);
            axisX.Append("high", 30);
            axisX.SetRange(0, 30);

            axisY.Append("slow", 10);
            axisY.Append("med", 20);
            axisY.Append("fast", 30);
            axisY.SetRange(0, 30);

            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisX);
            series.AttachAxis(axisY);

            var view = ChartsAssertions.Present(chart);

            check.Check(series.Count() == 5, $"the series has five points (got {series.Count()})");
            check.Check(Math4.Nearly(series.Pen().Width(), 5.0, 0.001),
                $"the line is five pixels wide (got {series.Pen().Width()})");
            check.Check(series.Pen().Color().Red() == 0xfd && series.Pen().Color().Blue() == 0x57,
                "the line is the example's orange");

            check.Check(chart.Title == "Custom Chart", $"the title round-trips (got '{chart.Title}')");
            check.Check(chart.TitleFont().PixelSize() == 18,
                $"the title font is 18px (got {chart.TitleFont().PixelSize()})");
            check.Check(chart.TitleBrush().Color().Red() == 255 && chart.TitleBrush().Color().Green() == 255,
                "the title is drawn in white");
            check.Check(chart.IsPlotAreaBackgroundVisible(), "the plot area has its own background");
            check.Check(chart.BackgroundBrush().Gradient() != null,
                "the chart background is a gradient, as the example sets it");

            // Both axes are category axes with named bands and no grid lines.
            check.Check(axisX.Count == 3 && axisY.Count == 3, $"three bands per axis ({axisX.Count}/{axisY.Count})");
            var xLabels = axisX.CategoriesLabels;
            check.Check(xLabels.Length == 3 && xLabels[1] == "optimal",
                $"the x bands are named ({string.Join(",", xLabels)})");
            check.Check(axisY.CategoriesLabels[1] == "med", "the y bands are named too");
            check.Check(!axisX.IsGridLineVisible() && !axisY.IsGridLineVisible(),
                "the grid lines are off, as the example asks");
            check.Check(Math4.Nearly(axisX.LinePen.Width(), 2.0, 0.001),
                $"the axis pen is two pixels wide (got {axisX.LinePen.Width()})");
            check.Check(axisY.ShadesVisible, "the y axis draws shaded bands");
            check.Check(Math4.Nearly(axisX.Max, 30.0, 0.001), $"the x axis ends at 30 (got {axisX.Max})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 100, $"the styled line is drawn ({ink} ink samples)");
                // A gradient background is what this example is for, so the render has to carry many
                // shades rather than the two or three a flat one would.
                check.Check(ChartsAssertions.ColourSamples(rendered) > 40,
                    $"the gradients produce many shades ({ChartsAssertions.ColourSamples(rendered)} colours)");
            }

            return check.Finish();
        }
    }
}
