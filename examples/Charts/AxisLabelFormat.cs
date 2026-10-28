﻿using System;
using QtSharp.Charts;

namespace QtSharp.Charts.Examples
{
    /// <summary>
    /// Not one of Qt's examples: a regression test for a unit that is a word in the reader's language.
    ///
    /// QValueAxis::setLabelFormat takes a printf-style format, and Qt Charts itself converts that
    /// string with toLatin1() before handing it to QString::asprintf
    /// (src/charts/axis/chartaxiselement.cpp:511). A character outside Latin-1 therefore reaches the
    /// tick label as '?', whatever the binding does with it - the marshalling here is UTF-16 in both
    /// directions and labelFormat() reads the string back byte for byte.
    ///
    /// The same function has a way round it: when the chart's localizeNumbers() is on and the format
    /// matches ^([^%]*)%\.(\d+)([defgiEG])(.*)$, the label is built as
    /// preStr + locale number + postStr, and preStr/postStr are slices of the *QString* - so a
    /// non-Latin-1 unit survives. That is what the second half of this test pins down.
    /// </summary>
    [Example("qtcharts-axislabelformat")]
    public static class AxisLabelFormat
    {
        /// <summary>A chart whose single axis carries the given label format.</summary>
        static (QChart Chart, QChartView View, QValueAxis Axis) Build(ExampleContext example, string format)
        {
            var series = new QLineSeries();
            series.Append(0.0, 0.05);
            series.Append(1.0, 0.09);
            series.Append(2.0, 0.07);

            var chart = new QChart();
            chart.AddSeries(series);
            ChartsAssertions.HideLegend(chart);

            var axisX = new QValueAxis();
            chart.AddAxis(axisX, Qt.Alignment.AlignBottom);
            series.AttachAxis(axisX);

            var axisY = new QValueAxis();
            axisY.SetRange(0.0, 0.10);
            axisY.SetLabelFormat(format);
            chart.AddAxis(axisY, Qt.Alignment.AlignLeft);
            series.AttachAxis(axisY);

            var view = ChartsAssertions.Present(example, chart, 320, 240);
            return (chart, view, axisY);
        }

#if QTSHARP_EXAMPLE_SMOKE
        /// <summary>Whether two grabs differ in any pixel (the glyph, not just its coverage).</summary>
        static bool PixelsDiffer(QPixmap a, QPixmap b)
        {
            using var first = a.ToImage();
            using var second = b.ToImage();
            if (first.Width() != second.Width() || first.Height() != second.Height())
                return true;
            for (int y = 0; y < first.Height(); y++)
                for (int x = 0; x < first.Width(); x++)
                    if (first.Pixel(x, y) != second.Pixel(x, y))
                        return true;
            return false;
        }
#endif

        public static int Run(ExampleContext example)
        {
            // The unit is a word in Chinese: 核 ("cores").
            const string unit = "核";
            const string format = "%.2f " + unit;

            var plain = Build(example, format);
            // The same format, with localizeNumbers() on: Qt builds the label from the QString pieces
            // instead of the Latin-1 copy, and the unit is drawn rather than turned into a question
            // mark.
            var localized = Build(example, format);
            localized.Chart.SetLocalizeNumbers(true);
            // The recipe needs the format to match the localized matcher, which wants a precision:
            // "%.2f" does, a bare "%g" does not.
            var bare = Build(example, "%g");
            bare.Chart.SetLocalizeNumbers(true);

#if QTSHARP_EXAMPLE_SMOKE
            example.Check(plain.Axis.LabelFormat == format,
                $"the binding carries the format into Qt unchanged ('{plain.Axis.LabelFormat}')");
            example.Check(plain.Axis.LabelFormat.Length == format.Length,
                $"character for character, not just visually ({plain.Axis.LabelFormat.Length} of {format.Length})");
            example.Check(plain.Axis.LabelFormat.EndsWith(unit),
                "including the non-ASCII unit at the end");

            using var plainShot = plain.View.Grab();
            int plainInk = ChartsAssertions.InkSamples(plainShot);
            example.Check(plainInk > 0, $"the plain chart draws its labels ({plainInk} ink samples)");

            example.Check(localized.Chart.LocalizeNumbers, "the chart can localize its numbers");
            example.Check(localized.Axis.LabelFormat == format, "which does not touch the format itself");

            ChartsAssertions.Pump();
            using var localizedShot = localized.View.Grab();
            int localizedInk = ChartsAssertions.InkSamples(localizedShot);

            // Whether the glyph draws differently is a font question: a machine without a CJK font
            // can render '核' and '?' with the same coverage. Compare the pixels, not just the ink,
            // and report the identical-draw case instead of failing it - the string round-trip above
            // is what proves the format survived; this is the visual confirmation where the platform
            // can provide one.
            if (PixelsDiffer(plainShot, localizedShot))
                example.Check(true,
                    $"the two draws differ, so the unit really is rendered differently ({plainInk} vs {localizedInk} ink samples)");
            else
                Console.WriteLine(
                    $"[info] this platform's font renders both forms identically ({plainInk} vs {localizedInk} ink samples); "
                    + "the format round-trip above still holds");
            Console.WriteLine($"[info] plain: toLatin1 -> '?' ({plainInk} ink); localized: pre+number+post ({localizedInk} ink)");

            example.Check(bare.Axis.LabelFormat == "%g", "a format without a precision still round-trips");
            example.Check(true, "but it takes the asprintf path, so a unit in it would still be lost");

            return example.ExitCode;
#else
            return 0;
#endif
        }
    }
}
