using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts BarModelMapperWidget: a twelve-by-six table, and the five rows from the
    /// fourth on mapped into four bar sets - so editing the table moves the bars.
    ///
    /// This is what a model mapper is for, and it is the only place in the gallery where the series'
    /// data comes from a model rather than from append() calls.
    /// </summary>
    [Demo("qtcharts-barmodelmapper")]
public static class BarModelMapperChart
    {
        const int FirstRow = 3;
        const int RowCount = 5;

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var model = new MapperTableModel(12, 6) { ColumnCountValue = 6 };

            var series = new QBarSeries();
            var mapper = new QVBarModelMapper();
            mapper.SetFirstBarSetColumn(1);
            mapper.SetLastBarSetColumn(4);
            mapper.SetFirstRow(FirstRow);
            mapper.SetRowCount(RowCount);
            mapper.SetSeries(series);
            mapper.SetModel(model);

            var chart = new QChart();
            chart.SetAnimationOptions(QChart.AnimationOptions.AllAnimations);
            chart.AddSeries(series);

            var axisX = new QBarCategoryAxis();
            axisX.Append(new[] { "April", "May", "June", "July", "August" });
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            var axisY = new QValueAxis();
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            var view = ChartsAssertions.Present(chart);

            // The mapper's own settings.
            check.Check(mapper.FirstBarSetColumn == 1, $"the first column (got {mapper.FirstBarSetColumn})");
            check.Check(mapper.LastBarSetColumn == 4, $"the last column (got {mapper.LastBarSetColumn})");
            check.Check(mapper.FirstRow == FirstRow, $"the first row (got {mapper.FirstRow})");
            check.Check(mapper.RowCount == RowCount, $"the row count (got {mapper.RowCount})");
            check.Check(mapper.Model.Handle == model.Handle, "the mapper knows the model");
            check.Check(mapper.Series.Handle == series.Handle, "and the series");

            // Columns 1..4 become four bar sets, each with one bar per mapped row of the model.
            check.Check(series.Count == 4,
                $"the mapper built four bar sets - one per column (got {series.Count})");
            check.Check(series.BarSets().Length == 4, "and the series agrees");

            var sets = series.BarSets();
            check.Check(sets[0].Count() == RowCount,
                $"each set has one bar per mapped row (got {sets[0].Count()})");
            check.Check(Math4.Nearly(sets[0].At(0), model.Cell(FirstRow, 1), 0.0001),
                $"the first bar is the model's cell at ({FirstRow},1) ({sets[0].At(0)} vs {model.Cell(FirstRow, 1)})");
            check.Check(Math4.Nearly(sets[3].At(2), model.Cell(FirstRow + 2, 4), 0.0001),
                $"and the last set reads column 4 ({sets[3].At(2)} vs {model.Cell(FirstRow + 2, 4)})");

            // The mapper is a live link: writing a cell through the model pushes the value out to the
            // series, which is the whole point of the example.
            double before = sets[1].At(1);
            var index = model.Index(FirstRow + 1, 2);
            model.SetData(index, 1234.0, (int)Qt.ItemDataRole.EditRole);
            check.Check(Math4.Nearly(sets[1].At(1), 1234.0, 0.0001),
                $"editing the model moved the bar ({before} -> {sets[1].At(1)})");

            check.Check(axisX.Count == 5, $"five month categories (got {axisX.Count})");

            // The axis is not re-scaled by a model edit, and neither is it by createDefaultAxes()
            // called again afterwards - it was built when the series was first mapped and keeps
            // that range. Worth knowing before you point a mapper at an editable table and expect
            // the chart to grow with it: set the range yourself when the data can get bigger.
            check.Check(axisY.Max < 1234.0,
                $"the axis keeps the range it was built with (max {axisY.Max})");
            check.Check(axisY.Max >= model.Cell(FirstRow, 1),
                "which still covers the values the model had when it was mapped");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
                check.Check(ChartsAssertions.InkSamples(rendered) > 200,
                    $"the bars are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");

            return check.Finish();
        }
    }
}
