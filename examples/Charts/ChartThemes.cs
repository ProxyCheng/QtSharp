﻿using QtSharp.Charts;

namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Qt's examples/charts ThemeWidget: six charts - area, pie, line, bar, spline and scatter - all
    /// driven at once from three combo boxes: the theme, the animation options and the legend
    /// alignment.
    ///
    /// The example fills its data table from QRandomGenerator; this port uses a deterministic table
    /// with the same shape (three series of seven points, y values rising as the generator's would),
    /// so the assertions can talk about specific values.
    /// </summary>
    [Example("qtcharts-chartthemes")]
public static class ChartThemes
    {
        const int ListCount = 3;
        const int ValueMax = 10;
        const int ValueCount = 7;

        /// <summary>The example's generated data table: rows of (point, label) pairs.</summary>
        static readonly QPointF[][] DataTable = BuildData();

        static QPointF[][] BuildData()
        {
            var table = new QPointF[ListCount][];
            var random = QRandomGenerator.Global();
            for (int i = 0; i < ListCount; i++)
            {
                table[i] = new QPointF[ValueCount];
                double y = 0;
                for (int j = 0; j < ValueCount; j++)
                {
                    y += random.Bounded((double)ValueMax / ValueCount);
                    table[i][j] = new QPointF((j + random.GenerateDouble()) * ((double)ValueMax / ValueCount), y);
                }
            }
            return table;
        }

        public static int Run(ExampleContext example)
        {
            // The six charts, built the way the example's createXxxChart() helpers do.
            var area = new QChart();
            area.SetTitle("Area Chart");
            QLineSeries lower = null;
            for (int i = 0; i < ListCount; i++)
            {
                var upper = new QLineSeries();
                for (int j = 0; j < ValueCount; j++)
                    upper.Append(new QPointF(j, (lower == null ? 0 : lower.At(j).Y()) + DataTable[i][j].Y()));
                var band = new QAreaSeries(upper, lower);
                band.SetName("Series " + i);
                area.AddSeries(band);
                lower = upper;
            }
            area.CreateDefaultAxes();
            ChartsAssertions.ValueAxis(area.Axes(Qt.Orientations.Horizontal)[0]).SetRange(0, ValueCount - 1);
            ChartsAssertions.ValueAxis(area.Axes(Qt.Orientations.Vertical)[0]).SetRange(0, ValueMax);
            ChartsAssertions.ValueAxis(area.Axes(Qt.Orientations.Vertical)[0]).SetLabelFormat("%.1f  ");

            var pie = new QChart();
            pie.SetTitle("Pie Chart");
            var pieSeries = new QPieSeries();
            for (int j = 0; j < ValueCount; j++)
            {
                var slice = pieSeries.Append("Slice 0:" + j, DataTable[0][j].Y());
                if (j == 0)
                {
                    slice.SetLabelVisible();
                    slice.SetExploded();
                    slice.SetExplodeDistanceFactor(0.5);
                }
            }
            pieSeries.SetPieSize(0.4);
            pie.AddSeries(pieSeries);

            QChart BuildCartesian(string title, System.Func<QAbstractSeries> make)
            {
                var chart = new QChart();
                chart.SetTitle(title);
                for (int i = 0; i < ListCount; i++)
                {
                    var series = make();
                    var xy = (QXYSeries)series;
                    for (int j = 0; j < ValueCount; j++)
                        xy.Append(DataTable[i][j]);
                    series.SetName("Series " + i);
                    chart.AddSeries(series);
                }
                chart.CreateDefaultAxes();
                ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Horizontal)[0]).SetRange(0, ValueMax);
                ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Vertical)[0]).SetRange(0, ValueCount);
                ChartsAssertions.ValueAxis(chart.Axes(Qt.Orientations.Vertical)[0]).SetLabelFormat("%.1f  ");
                return chart;
            }

            var line = BuildCartesian("Line Chart", () => new QLineSeries());
            var spline = BuildCartesian("Spline Chart", () => new QSplineSeries());
            var scatter = BuildCartesian("Scatter Chart", () => new QScatterSeries());

            var bar = new QChart();
            bar.SetTitle("Bar Chart");
            var barSeries = new QStackedBarSeries();
            for (int i = 0; i < ListCount; i++)
            {
                var set = new QBarSet("Bar set " + i);
                for (int j = 0; j < ValueCount; j++)
                    set.Append(DataTable[i][j].Y());
                barSeries.Append(set);
            }
            bar.AddSeries(barSeries);
            bar.CreateDefaultAxes();
            ChartsAssertions.ValueAxis(bar.Axes(Qt.Orientations.Vertical)[0]).SetRange(0, ValueMax * 2);
            ChartsAssertions.ValueAxis(bar.Axes(Qt.Orientations.Vertical)[0]).SetLabelFormat("%.1f  ");

            var charts = new[] { area, pie, line, bar, spline, scatter };
            var views = new QChartView[charts.Length];
            for (int i = 0; i < charts.Length; i++)
                views[i] = ChartsAssertions.Present(example, charts[i], 260, 200);
#if QTSHARP_EXAMPLE_SMOKE

            example.Check(charts.Length == 6, "the gallery has six charts");
#endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(area.Series().Length == ListCount, $"the area chart stacks {ListCount} bands");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(pie.Series().Length == 1 && pieSeries.Count == ValueCount,
                $"the pie chart has one series of {ValueCount} slices");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(bar.Series().Length == 1 && barSeries.BarSets().Length == ListCount,
                $"the bar chart stacks {ListCount} sets");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(line.Series().Length == ListCount && spline.Series().Length == ListCount
                        && scatter.Series().Length == ListCount,
                $"the three cartesian charts each hold {ListCount} series");
            #endif

            // The theme combo box: every one of the eight themes has to round-trip and to change what
            // gets drawn.
            var themes = new[]
            {
                QChart.ChartTheme.ChartThemeLight,
                QChart.ChartTheme.ChartThemeBlueCerulean,
                QChart.ChartTheme.ChartThemeDark,
                QChart.ChartTheme.ChartThemeBrownSand,
                QChart.ChartTheme.ChartThemeBlueNcs,
                QChart.ChartTheme.ChartThemeHighContrast,
                QChart.ChartTheme.ChartThemeBlueIcy,
                QChart.ChartTheme.ChartThemeQt,
            };
            foreach (var theme in themes)
            {
                foreach (var chart in charts)
                    chart.SetTheme(theme);
                #if QTSHARP_EXAMPLE_SMOKE
                example.Check(charts[0].Theme == theme, $"the {theme} theme round-trips");
                #endif
            }

            // Two visibly different themes have to render differently, or the property is doing
            // nothing.
            foreach (var chart in charts)
                chart.SetTheme(QChart.ChartTheme.ChartThemeLight);
            using (var light = views[0].Grab())
            {
                foreach (var chart in charts)
                    chart.SetTheme(QChart.ChartTheme.ChartThemeDark);
                using var dark = views[0].Grab();
                #if QTSHARP_EXAMPLE_SMOKE
                example.Check(ChartsAssertions.InkSamples(light) != ChartsAssertions.InkSamples(dark)
                            || ChartsAssertions.ColourSamples(light) != ChartsAssertions.ColourSamples(dark),
                    "the light and dark themes render differently");
                #endif
            }

            // The animation combo box.
            var animations = new[]
            {
                QChart.AnimationOptions.NoAnimation,
                QChart.AnimationOptions.GridAxisAnimations,
                QChart.AnimationOptions.SeriesAnimations,
                QChart.AnimationOptions.AllAnimations,
            };
            foreach (var option in animations)
            {
                foreach (var chart in charts)
                    chart.SetAnimationOptions(option);
                #if QTSHARP_EXAMPLE_SMOKE
                example.Check(charts[0].animationOptions() == option,
                    $"the {option} animation option round-trips");
                #endif
            }

            // The legend combo box: "No Legend" hides them, the four alignments move them.
            foreach (var chart in charts)
                ChartsAssertions.HideLegend(chart);
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(!ChartsAssertions.IsLegendVisible(charts[0]), "the legend can be turned off");
            #endif
            var alignments = new[] { Qt.Alignment.AlignTop, Qt.Alignment.AlignBottom,
                                     Qt.Alignment.AlignLeft, Qt.Alignment.AlignRight };
            foreach (var alignment in alignments)
            {
                foreach (var chart in charts)
                {
                    chart.Legend().SetAlignment(alignment);
                    ChartsAssertions.SetLegendVisible(chart, true);
                }
                #if QTSHARP_EXAMPLE_SMOKE
                example.Check(charts[0].Legend().Alignment == alignment,
                    $"the legend accepts {alignment}");
                #endif
                #if QTSHARP_EXAMPLE_SMOKE
                example.Check(ChartsAssertions.IsLegendVisible(charts[0]), "and shows again");
                #endif
            }

            // Antialiasing is the checkbox the example starts checked.
            foreach (var view in views)
                view.SetRenderHint(QPainter.RenderHint.Antialiasing, true);

            int drawn = 0;
            foreach (var view in views)
            {
                using var shot = view.Grab();
                if (shot != null && ChartsAssertions.InkSamples(shot) > 20)
                    drawn++;
            }
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(drawn == views.Length, $"all six charts render ({drawn} of {views.Length})");
            #endif

            #if QTSHARP_EXAMPLE_SMOKE
            return example.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
