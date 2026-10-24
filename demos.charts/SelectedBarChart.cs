using QtSharp.Charts;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// Qt's examples/charts SelectedBarWidget: four bar sets of five values, each set given a darker
    /// selected colour, and a click on a bar toggling its selection while three labels track the
    /// selected, unselected and total sums.
    /// </summary>
    public static class SelectedBarChart
    {
        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            var chicken = new QBarSet("Chicken");
            var pork = new QBarSet("Pork");
            var turkey = new QBarSet("Turkey");
            var ham = new QBarSet("Ham");
            chicken.Append(new[] { 15.0, 12.0, 8.0, 11.5, 13.7 });
            pork.Append(new[] { 9.0, 11.0, 9.0, 7.0, 12.2 });
            turkey.Append(new[] { 5.0, 7.6, 9.3, 8.0, 8.1 });
            ham.Append(new[] { 5.4, 7.1, 9.3, 12.3, 11.3 });

            var setList = new[] { chicken, pork, turkey, ham };
            double totalSum = chicken.Sum() + pork.Sum() + turkey.Sum() + ham.Sum();

            var series = new QBarSeries();
            series.Append(chicken);
            series.Append(pork);
            series.Append(turkey);
            series.Append(ham);

            var chart = new QChart();
            chart.AddSeries(series);
            chart.SetTitle("Meat Consumption (Click on bars to select them)");
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);
            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            // Each set's selected colour is its own brush colour, darkened.
            foreach (var barSet in series.BarSets())
                barSet.SetSelectedColor(barSet.Brush.Color().Darker());

            var axisX = new QBarCategoryAxis();
            axisX.SetCategories(new[] { "2017", "2018", "2019", "2020", "2021" });
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            var axisY = new QValueAxis();
            axisY.SetRange(0, 20);
            axisY.SetTitleText("Tons");
            axisY.SetLabelsAngle(-90);
            axisY.SetTitleVisible(true);
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            // The example's handler: toggle the clicked bar, then recompute the two sums.
            double selectedSum = 0;
            double unselectedSum = totalSum;
            int clicks = 0;
            series.Clicked += (index, set) =>
            {
                clicks++;
                set.ToggleSelection(new[] { index });
                selectedSum = 0;
                foreach (var barSet in setList)
                {
                    foreach (int k in barSet.SelectedBars())
                        selectedSum += barSet.At(k);
                }
                unselectedSum = totalSum - selectedSum < 0 ? 0 : totalSum - selectedSum;
            };

            var view = ChartsAssertions.Present(chart);

            check.Check(Math4.Nearly(totalSum, 191.8, 0.001), $"the four sets total {totalSum} tons");
            check.Check(chicken.Sum() == 60.2, $"Chicken sums to 60.2 (got {chicken.Sum()})");
            check.Check(series.BarSets().Length == 4, $"four sets in the series (got {series.BarSets().Length})");
            check.Check(axisX.Count == 5, $"five year categories (got {axisX.Count})");
            check.Check(axisX.Min == "2017" && axisX.Max == "2021",
                $"the categories run 2017..2021 (got {axisX.Min}..{axisX.Max})");

            // The selected colour really is the brush colour, darkened.
            var brush = chicken.Brush.Color();
            var selected = chicken.SelectedColor();
            check.Check(selected.Lightness() <= brush.Lightness(),
                $"the selected colour is not lighter than the brush (got {selected.Lightness()} vs {brush.Lightness()})");

            check.Check(!chicken.IsBarSelected(0), "no bar starts selected");
            check.Check(chicken.SelectedBars().Length == 0, "and a set reports no selection");

            // Click the second bar of Chicken: 12 tons selected.
            series.ClickedSignal.Emit(1, chicken);
            check.Check(clicks == 1, $"the click reached the C# handler (got {clicks})");
            check.Check(chicken.IsBarSelected(1), "the clicked bar is now selected");
            check.Check(chicken.SelectedBars().Length == 1, "exactly one bar is selected");
            check.Check(Math4.Nearly(selectedSum, 12.0, 0.001), $"selected sum is 12 tons (got {selectedSum})");
            check.Check(Math4.Nearly(unselectedSum, totalSum - 12.0, 0.001),
                $"unselected sum is the rest (got {unselectedSum})");

            // A second click on the same bar toggles it off again.
            series.ClickedSignal.Emit(1, chicken);
            check.Check(!chicken.IsBarSelected(1), "the second click toggled it off");
            check.Check(chicken.SelectedBars().Length == 0, "and nothing is selected again");
            check.Check(Math4.Nearly(selectedSum, 0.0, 0.001), "so the selected sum is back to zero");

            // A third, on a different set, shows the selection is per set.
            series.ClickedSignal.Emit(0, ham);
            check.Check(ham.IsBarSelected(0) && !chicken.IsBarSelected(0),
                "selection is tracked per bar set");
            check.Check(Math4.Nearly(selectedSum, 5.4, 0.001), $"Ham's first bar is 5.4 tons (got {selectedSum})");

            using var rendered = view.Grab();
            check.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                check.Check(ink > 200, $"the bars are actually drawn ({ink} ink samples)");
            }

            return check.Finish();
        }
    }
}
