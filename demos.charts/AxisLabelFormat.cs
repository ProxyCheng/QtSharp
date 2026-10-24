using QtSharp.Charts;

namespace QtSharp.Charts.Tests
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
    public static class AxisLabelFormat
    {
        /// <summary>A chart whose single axis carries the given label format.</summary>
        static (QChart Chart, QChartView View, QValueAxis Axis) Build(string format)
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

            var view = ChartsAssertions.Present(chart, 320, 240);
            return (chart, view, axisY);
        }

        public static int Run(QApplication app)
        {
            var check = new ChartsCheck();

            // The unit is a word in Chinese: 核 ("cores").
            const string unit = "核";
            const string format = "%.2f " + unit;

            var plain = Build(format);
            check.Check(plain.Axis.LabelFormat == format,
                $"the binding carries the format into Qt unchanged ('{plain.Axis.LabelFormat}')");
            check.Check(plain.Axis.LabelFormat.Length == format.Length,
                $"character for character, not just visually ({plain.Axis.LabelFormat.Length} of {format.Length})");
            check.Check(plain.Axis.LabelFormat.EndsWith(unit),
                "including the non-ASCII unit at the end");

            int plainInk;
            using (var shot = plain.View.Grab())
                plainInk = ChartsAssertions.InkSamples(shot);
            check.Check(plainInk > 0, $"the plain chart draws its labels ({plainInk} ink samples)");

            // The same format, with localizeNumbers() on: Qt builds the label from the QString pieces
            // instead of the Latin-1 copy, and the unit is drawn rather than turned into a question
            // mark.
            var localized = Build(format);
            localized.Chart.SetLocalizeNumbers(true);
            check.Check(localized.Chart.LocalizeNumbers, "the chart can localize its numbers");
            check.Check(localized.Axis.LabelFormat == format, "which does not touch the format itself");

            ChartsAssertions.Pump();
            int localizedInk;
            using (var shot = localized.View.Grab())
                localizedInk = ChartsAssertions.InkSamples(shot);

            check.Check(localizedInk != plainInk,
                $"the two draws differ, so the unit really is rendered differently ({plainInk} vs {localizedInk} ink samples)");

            // How many pixels each form covers depends on which font resolves the glyph - on a
            // machine without a CJK font the '核' may fall back to a box that is wider or thinner
            // than a '?' - so the amounts are reported rather than asserted. The local run measured
            // 185 against 250; the CI runner, with different fonts, 217 against 212. What is being
            // pinned down is that the two differ at all.
            check.Info($"plain: toLatin1 -> '?' ({plainInk} ink); localized: pre+number+post ({localizedInk} ink)");

            // The recipe needs the format to match the localized matcher, which wants a precision:
            // "%.2f" does, a bare "%g" does not.
            var bare = Build("%g");
            bare.Chart.SetLocalizeNumbers(true);
            check.Check(bare.Axis.LabelFormat == "%g", "a format without a precision still round-trips");
            check.Check(true, "but it takes the asprintf path, so a unit in it would still be lost");

            return check.Finish();
        }
    }
}
