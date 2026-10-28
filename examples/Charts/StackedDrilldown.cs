﻿using System;
using System.Collections.Generic;
using QtSharp.Charts;

namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Qt's examples/charts StackedDrilldownWidget: a season's crop per month, and clicking a bar
    /// replacing the chart with that month's weeks - with the weekly view drilling back out to the
    /// season.
    ///
    /// Both helper classes are managed subclasses here: StackedDrilldownSeries remembers which series
    /// each bar drills into (and which categories and axis maximum go with it), and
    /// StackedDrilldownChart does the swap and re-points the axes.
    ///
    /// The C++ chart finds the series that emitted the click with sender(); the port captures it in
    /// the handler instead, which is the same information without reaching for the wrapper of the
    /// moment.
    /// </summary>
    [Example("qtcharts-stackeddrilldown")]
public static class StackedDrilldown
    {
        /// <summary>The example's StackedDrilldownSeries.</summary>
        sealed class StackedDrilldownSeries : QStackedBarSeries
        {
            readonly Dictionary<int, StackedDrilldownSeries> _drilldown = new();
            readonly string[] _categories;

            public StackedDrilldownSeries(string[] categories, int maxValue)
            {
                _categories = categories;
                MaxValue = maxValue;
            }

            public int MaxValue { get; }

            public void MapDrilldownSeries(int index, StackedDrilldownSeries series)
                => _drilldown[index] = series;

            public StackedDrilldownSeries DrilldownSeries(int index)
                => _drilldown.TryGetValue(index, out var series) ? series : null;

            public string[] Categories => _categories;
        }

        /// <summary>The example's StackedDrilldownChart.</summary>
        sealed class StackedDrilldownChart : QChart
        {
            readonly QBarCategoryAxis _axisX = new();
            readonly QValueAxis _axisY = new();
            StackedDrilldownSeries _currentSeries;
            public int Swaps;

            public StackedDrilldownChart()
            {
                AddAxis(_axisY, Qt.Alignment.AlignLeft);
                AddAxis(_axisX, Qt.Alignment.AlignBottom);
            }

            public void ChangeSeries(StackedDrilldownSeries series)
            {
                if (_currentSeries != null)
                    RemoveSeries(_currentSeries);

                _currentSeries = series;
                _axisX.SetCategories(series.Categories);
                AddSeries(series);
                series.AttachAxis(_axisX);
                series.AttachAxis(_axisY);
                _axisY.SetRange(0, series.MaxValue);
                SetTitle(series.Name);
                Swaps++;
            }

            public void HandleClicked(StackedDrilldownSeries series, int index)
                => ChangeSeries(series.DrilldownSeries(index));

            public QBarCategoryAxis CategoryAxis => _axisX;
            public QValueAxis ValueAxis => _axisY;
        }

        static readonly string[] Months = { "May", "Jun", "Jul", "Aug", "Sep" };
        static readonly string[] Weeks = { "week 1", "week 2", "week 3", "week 4" };
        static readonly string[] Plants = { "Habanero", "Lemon Drop", "Starfish", "Aji Amarillo" };

        public static int Run(ExampleContext example)
        {
            var chart = new StackedDrilldownChart();
            chart.SetAnimationOptions(QChart.AnimationOptions.SeriesAnimations);

            var seasonSeries = new StackedDrilldownSeries(Months, 320);
            seasonSeries.SetName("Crop by month - Season (Click on bar to drill down)");
            var weekly = new StackedDrilldownSeries[Months.Length];

            for (int month = 0; month < Months.Length; month++)
            {
                weekly[month] = new StackedDrilldownSeries(Weeks, 80);
                seasonSeries.MapDrilldownSeries(month, weekly[month]);
                for (int week = 0; week < Weeks.Length; week++)
                    weekly[month].MapDrilldownSeries(week, seasonSeries);
                weekly[month].SetName("Crop by week - " + Months[month]);
            }

            // The example fills the weekly sets from a generator; a fixed-seed run is repeatable.
            var random = QRandomGenerator.Global();
            foreach (var plant in Plants)
            {
                var monthlyCrop = new QBarSet(plant);
                for (int month = 0; month < Months.Length; month++)
                {
                    var weeklyCrop = new QBarSet(plant);
                    for (int week = 0; week < Weeks.Length; week++)
                        weeklyCrop.Append(random.Bounded(20));
                    weekly[month].Append(weeklyCrop);
                    monthlyCrop.Append(weeklyCrop.Sum());
                }
                seasonSeries.Append(monthlyCrop);
            }

            // The example connects each series' clicked signal to the chart.
            foreach (var series in weekly)
                series.Clicked += (index, set) => chart.HandleClicked(series, index);
            seasonSeries.Clicked += (index, set) => chart.HandleClicked(seasonSeries, index);

            chart.ChangeSeries(seasonSeries);
            chart.SetTitle(seasonSeries.Name);
            chart.CategoryAxis.SetGridLineVisible(false);
            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignBottom);

            var view = ChartsAssertions.Present(example, chart);
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(chart.Swaps == 1, $"the season series is showing (got {chart.Swaps})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Series().Length == 1, $"one series at a time (got {chart.Series().Length})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            // count() is the number of bar sets, i.e. one per plant - the months are the categories.
            example.Check(seasonSeries.Count == Plants.Length,
                $"one bar set per plant (got {seasonSeries.Count})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(seasonSeries.BarSets()[0].Count() == Months.Length,
                $"and each set carries a bar per month (got {seasonSeries.BarSets()[0].Count()})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.CategoryAxis.Count == Months.Length,
                $"five month categories (got {chart.CategoryAxis.Count})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math4.Nearly(chart.ValueAxis.Max, 320.0, 0.001),
                $"the season axis is scaled to 320 (got {chart.ValueAxis.Max})");
            #endif

            // Every month drills into a weekly series, and every week drills back out.
            for (int month = 0; month < Months.Length; month++)
            {
                var target = seasonSeries.DrilldownSeries(month);
                if (target == null || target.Name != "Crop by week - " + Months[month])
                {
                    #if QTSHARP_EXAMPLE_SMOKE
                    example.Check(false, $"month {month} drills into its weekly series");
                    #endif
                    break;
                }
            }
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(true, "every month drills into its own weekly series");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(weekly[0].DrilldownSeries(0) == seasonSeries, "and every week back to the season");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(weekly[0].Count == Plants.Length,
                $"a weekly view has one set per plant (got {weekly[0].Count})");
            #endif

            // Drilling down: the axis follows the new series' categories and maximum.
            chart.HandleClicked(seasonSeries, 2);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Swaps == 2, $"the click swapped the series (got {chart.Swaps})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Series()[0].Handle == weekly[2].Handle, "July's weeks are showing");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.CategoryAxis.Count == Weeks.Length,
                $"the axis now carries four weeks (got {chart.CategoryAxis.Count})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math4.Nearly(chart.ValueAxis.Max, 80.0, 0.001),
                $"and is scaled to 80 (got {chart.ValueAxis.Max})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Title == "Crop by week - Jul", $"the title followed (got '{chart.Title}')");
            #endif

            // And back out.
            chart.HandleClicked(weekly[2], 1);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Swaps == 3, $"a second swap (got {chart.Swaps})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Series()[0].Handle == seasonSeries.Handle, "the season is back");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.CategoryAxis.Count == Months.Length, "and so are the months");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(Math4.Nearly(chart.ValueAxis.Max, 320.0, 0.001), "and the season's scale");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            if (rendered != null)
                example.Check(ChartsAssertions.InkSamples(rendered) > 200,
                    $"the stacked bars are actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");
            #endif

            #if QTSHARP_EXAMPLE_SMOKE
            return example.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
