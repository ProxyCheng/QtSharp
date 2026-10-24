using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts ModelDataWidget: one table of four columns, with columns 0/1 feeding the
    /// first line series and 2/3 the second - two QVXYModelMapper objects over the same model.
    ///
    /// The sibling of the bar model mapper example: same model, but an X/Y mapper, which takes a
    /// separate column for each coordinate.
    /// </summary>
    [Demo("qtcharts-modeldata")]
public static class ModelDataChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var model = new MapperTableModel(12, 4) { ColumnCountValue = 4 };

            var chart = new QChart();
            chart.SetAnimationOptions(QChart.AnimationOptions.AllAnimations);

            var series1 = new QLineSeries();
            series1.SetName("Line 1");
            var mapper1 = new QVXYModelMapper();
            mapper1.SetXColumn(0);
            mapper1.SetYColumn(1);
            mapper1.SetSeries(series1);
            mapper1.SetModel(model);
            chart.AddSeries(series1);

            var series2 = new QLineSeries();
            series2.SetName("Line 2");
            var mapper2 = new QVXYModelMapper();
            mapper2.SetXColumn(2);
            mapper2.SetYColumn(3);
            mapper2.SetSeries(series2);
            mapper2.SetModel(model);
            chart.AddSeries(series2);

            chart.CreateDefaultAxes();

            var view = ChartsAssertions.Present(chart);

            check.Check(mapper1.XColumn == 0 && mapper1.YColumn == 1,
                $"the first mapper reads columns 0 and 1 ({mapper1.XColumn}/{mapper1.YColumn})");
            check.Check(mapper2.XColumn == 2 && mapper2.YColumn == 3,
                $"the second reads 2 and 3 ({mapper2.XColumn}/{mapper2.YColumn})");
            check.Check(mapper1.Model.Handle == model.Handle && mapper2.Model.Handle == model.Handle,
                "both mappers are on the same model");

            // One point per model row, with the value coming from the mapped column.
            check.Check(series1.Count() == 12, $"the first line has a point per row (got {series1.Count()})");
            check.Check(series2.Count() == 12, $"the second one too (got {series2.Count()})");
            check.Check(series1.Name == "Line 1" && series2.Name == "Line 2", "both keep their names");

            var points = series1.Points();
            check.Check(Math4.Nearly(points[0].X(), model.Cell(0, 0), 0.0001),
                $"the first point's x is the model's cell (0,0) ({points[0].X()} vs {model.Cell(0, 0)})");
            check.Check(Math4.Nearly(points[0].Y(), model.Cell(0, 1), 0.0001),
                $"and its y is the cell (0,1) ({points[0].Y()} vs {model.Cell(0, 1)})");
            check.Check(Math4.Nearly(points[5].X(), model.Cell(5, 0), 0.0001),
                "a later row reads its own row, not the first");
            check.Check(Math4.Nearly(series2.Points()[3].Y(), model.Cell(3, 3), 0.0001),
                "the second series reads the third column for y");

            // Editing a cell moves both the model and the series.
            model.SetData(model.Index(2, 1), new QVariant(777.0), (int)Qt.ItemDataRole.EditRole);
            check.Check(Math4.Nearly(series1.Points()[2].Y(), 777.0, 0.0001),
                $"editing the model moved the point (got {series1.Points()[2].Y()})");

            check.Check(chart.Series().Length == 2, $"both series are on the chart (got {chart.Series().Length})");
            check.Check(chart.Axes(Qt.Orientations.Horizontal, null).Length == 1
                        && chart.Axes(Qt.Orientations.Vertical, null).Length == 1,
                "createDefaultAxes built both axes");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 50,
                    $"both lines are drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
