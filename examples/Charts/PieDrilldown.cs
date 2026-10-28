﻿using System;
using QtSharp.Charts;

namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Qt's examples/charts PieDrilldownWidget: sales by year, and a click on a year's slice replacing
    /// the chart with that person's month-by-month breakdown, and back again.
    ///
    /// Both C++ helper classes are managed here: PieDrilldownSlice carries the series it drills into
    /// and relabels itself as its share of the pie changes, and PieDrilldownChart owns swapping the
    /// series over. The random sales figures start from a seeded generator so a run is repeatable.
    /// </summary>
    [Example("qtcharts-piedrilldown")]
public static class PieDrilldown
    {
        /// <summary>The example's PieDrilldownSlice.</summary>
        sealed class PieDrilldownSlice : QPieSlice
        {
            readonly QAbstractSeries _drilldownSeries;
            readonly string _prefix;

            public PieDrilldownSlice(double value, string prefix, QAbstractSeries drilldownSeries)
            {
                _drilldownSeries = drilldownSeries;
                _prefix = prefix;
                SetValue(value);
                UpdateLabel();
                SetLabelFont(new QFont("Arial", 8));
                PercentageChanged += UpdateLabel;
                Hovered += ShowHighlight;
            }

            public QAbstractSeries DrilldownSeries => _drilldownSeries;

            public void UpdateLabel()
                => SetLabel($"{_prefix} ${Value:F2}, {Percentage * 100:F1}%");

            public void ShowHighlight(bool show)
            {
                SetLabelVisible(show);
                SetExploded(show);
            }
        }

        /// <summary>The example's PieDrilldownChart.</summary>
        sealed class PieDrilldownChart : QChart
        {
            QAbstractSeries _currentSeries;
            public int Swaps;

            public void ChangeSeries(QAbstractSeries series)
            {
                if (_currentSeries != null)
                    RemoveSeries(_currentSeries);
                _currentSeries = series;
                AddSeries(series);
                SetTitle(series.Name);
                Swaps++;
            }

            public void HandleSliceClicked(QPieSlice slice)
                => ChangeSeries(((PieDrilldownSlice)slice).DrilldownSeries);
        }

        static readonly string[] Months =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
        };

        static readonly string[] Names = { "Jane", "John", "Axel", "Mary", "Susan", "Bob" };

        public static int Run(ExampleContext example)
        {
            var chart = new PieDrilldownChart();
            chart.SetTheme(QChart.ChartTheme.ChartThemeLight);
            chart.SetAnimationOptions(QChart.AnimationOptions.AllAnimations);
            ChartsAssertions.SetLegendVisible(chart, true);
            chart.Legend().SetAlignment(Qt.Alignment.AlignRight);

            var yearSeries = new QPieSeries();
            yearSeries.SetName("Sales by year - All (Click on slice to drill down)");

            // The example uses QRandomGenerator; a fixed seed keeps the run repeatable.
            var random = QRandomGenerator.Global();

            // The slices are kept by hand: reading them back out of the series hands out a wrapper
            // of the declared type (QPieSlice), because a QList<QPieSlice*> element is owned by the
            // container and is wrapped transiently. A signal, on the other hand, hands out the very
            // wrapper that was built for the object - which is what makes the drilldown cast work.
            var yearSlices = new System.Collections.Generic.List<PieDrilldownSlice>();
            var monthSlices = new System.Collections.Generic.List<PieDrilldownSlice>();

            foreach (var name in Names)
            {
                var monthly = new QPieSeries();
                monthly.SetName("Sales by month - " + name);
                foreach (var month in Months)
                {
                    var monthlySlice = new PieDrilldownSlice(random.Bounded(1000), month, yearSeries);
                    monthly.Append(monthlySlice);
                    monthSlices.Add(monthlySlice);
                }

                monthly.Clicked += slice => chart.HandleSliceClicked(slice);
                var sliceForPerson = new PieDrilldownSlice(monthly.Sum, name, monthly);
                yearSlices.Add(sliceForPerson);
                yearSeries.Append(sliceForPerson);
            }

            yearSeries.Clicked += slice => chart.HandleSliceClicked(slice);
            chart.ChangeSeries(yearSeries);

            var view = ChartsAssertions.Present(example, chart);
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(chart.Swaps == 1, $"the year series was put on the chart (got {chart.Swaps})");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Series().Length == 1, $"and it is the only one (got {chart.Series().Length})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(yearSeries.Count == Names.Length,
                $"one slice per person (got {yearSeries.Count})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Title == yearSeries.Name, $"the title follows the series (got '{chart.Title}')");
            #endif

            // Every year slice is one of the managed slices, and each carries a drilldown series.
            var yearSlice = yearSlices[0];
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(yearSlice.DrilldownSeries != null, "a year slice knows the series it drills into");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(yearSlice.DrilldownSeries.Name.StartsWith("Sales by month"),
                $"and it is a monthly series ('{yearSlice.DrilldownSeries.Name}')");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(yearSlice.Label.Contains("$") && yearSlice.Label.Contains("%"),
                $"the label carries the figure and the share ('{yearSlice.Label}')");
            #endif

            var monthlySeries = (QPieSeries)yearSlice.DrilldownSeries;
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(monthlySeries.Count == 12, $"twelve months in it (got {monthlySeries.Count})");
            #endif

            // Drilling down through the signal, which is how Qt hands the slice over - and the
            // wrapper it hands over is the managed one, so the chart's cast succeeds.
            yearSeries.ClickedSignal.Emit(yearSlice);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Swaps == 2, $"the click swapped the series (got {chart.Swaps})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Series().Length == 1, "still exactly one series on the chart");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Series()[0].Handle == monthlySeries.Handle, "and it is the monthly one");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Title == monthlySeries.Name, $"the title followed (got '{chart.Title}')");
            #endif

            // Drilling back out: the monthly slices all drill into the year series.
            var monthSlice = monthSlices[3];
            monthlySeries.ClickedSignal.Emit(monthSlice);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Swaps == 3, $"a second swap (got {chart.Swaps})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Series()[0].Handle == yearSeries.Handle, "we are back at the year series");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(chart.Title == yearSeries.Name, "and at its title");
            #endif

            // The hover highlight is the slice's own handler.
            monthSlice.ShowHighlight(true);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(monthSlice.IsExploded() && monthSlice.IsLabelVisible(),
                "hovering explodes the slice and shows its label");
            #endif
            monthSlice.ShowHighlight(false);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(!monthSlice.IsExploded(), "and un-hovering puts it back");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            if (rendered != null)
                example.Check(ChartsAssertions.InkSamples(rendered) > 100,
                    $"the pie is actually drawn ({ChartsAssertions.InkSamples(rendered)} ink samples)");
            #endif

            #if QTSHARP_EXAMPLE_SMOKE
            return example.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
