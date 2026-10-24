using System;
using System.Collections.Generic;

namespace QtSharp.Charts.Tests
{
    /// <summary>
    /// The ported Qt examples, and the one place that maps a name to one of them. QtSharp's own
    /// conformance suite works the same way: the list comes from the library, so a new example is
    /// covered as soon as it is registered here.
    ///
    /// The names follow the Qt examples they came from. In Qt 6.11 most of the stand-alone widgets
    /// examples were folded into examples/charts/chartsgallery, one widget per chart type; those are
    /// the names without a "chart" suffix. zoomlinechart is still its own example upstream.
    /// </summary>
    public static class ChartsDemos
    {
        static readonly (string Name, Func<QApplication, int> Run)[] Runners =
        {
            ("splinechart", SplineChart.Run),
            ("linechart", LineChart.Run),
            ("barchart", BarChart.Run),
            ("stackedbarchart", StackedBarChart.Run),
            ("percentbarchart", PercentBarChart.Run),
            ("horizontalbarchart", HorizontalBarChart.Run),
            ("horizontalstackedbarchart", HorizontalStackedBarChart.Run),
            ("horizontalpercentbarchart", HorizontalPercentBarChart.Run),
            ("lineandbar", LineAndBarChart.Run),
            ("piechart", PieChart.Run),
            ("donutchart", DonutChart.Run),
            ("nesteddonuts", NestedDonuts.Run),
            ("piedrilldown", PieDrilldown.Run),
            ("percentcalloutcustom", PercentCalloutCustom.Run),
            ("scatterchart", ScatterChart.Run),
            ("scatterinteractions", ScatterInteractions.Run),
            ("areachart", AreaChart.Run),
            ("boxplotchart", BoxPlotChart.Run),
            ("candlestickchart", CandlestickChart.Run),
            ("datetimeaxis", DateTimeAxisChart.Run),
            ("logvalueaxis", LogValueAxisChart.Run),
            ("multiaxis", MultiAxisChart.Run),
            ("polarchart", PolarChart.Run),
            ("legend", LegendChart.Run),
            ("legendmarkers", LegendMarkersChart.Run),
            ("selectedbar", SelectedBarChart.Run),
            ("lightmarkers", LightMarkers.Run),
            ("pointconfiguration", PointConfiguration.Run),
            ("interactions", Interactions.Run),
            ("stackeddrilldown", StackedDrilldown.Run),
            ("temperaturerecords", TemperatureRecords.Run),
            ("barmodelmapper", BarModelMapperChart.Run),
            ("modeldata", ModelDataChart.Run),
            ("dynamicspline", DynamicSplineChart.Run),
            ("chartthemes", ChartThemes.Run),
            ("customchart", CustomChart.Run),
            ("axislabelformat", AxisLabelFormat.Run),
            ("zoomlinechart", ZoomLineChart.Run),
        };

        public static IReadOnlyList<string> Names
        {
            get
            {
                var names = new List<string>();
                foreach (var runner in Runners)
                    names.Add(runner.Name);
                return names;
            }
        }

        public static string Usage
        {
            get
            {
                var text = new System.Text.StringBuilder();
                text.AppendLine("Qt Charts examples ported from Qt's own examples/charts:");
                foreach (var runner in Runners)
                    text.AppendLine("    " + runner.Name);
                return text.ToString();
            }
        }

        /// <summary>Runs one of them, or reports that the name is unknown.</summary>
        public static bool TryRun(string name, QApplication app, out int exitCode)
        {
            foreach (var runner in Runners)
            {
                if (runner.Name != name)
                    continue;
                exitCode = runner.Run(app);
                return true;
            }
            exitCode = 0;
            return false;
        }
    }
}
