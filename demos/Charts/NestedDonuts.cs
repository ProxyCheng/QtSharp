using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// Qt's examples/charts NestedDonutsWidget: five donuts of growing radius, each with three to
    /// five random slices, spinning on a QTimer and exploding the hovered slice.
    ///
    /// Two deliberate differences from the C++:
    /// - the hovered slice arrives through the QPieSlice::hovered signal's own argument list in
    ///   Qt 6, so the slice is captured in the handler instead of recovered with sender();
    /// - QRandomGenerator is seeded the same way, but the values are of course not the same ones, so
    ///   the assertions are about structure (how many donuts, how the radii nest, that a phase shift
    ///   moves the ring) rather than about specific values.
    /// </summary>
    [Demo("qtcharts-nesteddonuts")]
public static class NestedDonuts
    {
        public static int Run(DemoContext demo)
        {
            var view = new QChartView();
            view.SetRenderHint(QPainter.RenderHint.Antialiasing);
            var chart = view.Chart();
            ChartsAssertions.HideLegend(chart);
            chart.SetTitle("Nested Donuts (Hover over segments to explode them)");
            chart.SetAnimationOptions(QChart.AnimationOptions.AllAnimations);

            const double minSize = 0.1;
            const double maxSize = 0.9;
            const int donutCount = 5;

            var donuts = new QPieSeries[donutCount];
            var slices = new System.Collections.Generic.List<QPieSlice>();
            var random = QRandomGenerator.Global();

            for (int i = 0; i < donutCount; i++)
            {
                var donut = new QPieSeries();
                int sliceCount = 3 + random.Bounded(3);
                for (int j = 0; j < sliceCount; j++)
                {
                    double value = 100 + random.Bounded(100);
                    var slice = new QPieSlice($"{value}", value);
                    slice.SetLabelVisible(true);
                    slice.SetLabelColor(new QColor(Qt.GlobalColor.white));
                    slice.SetLabelPosition(QPieSlice.LabelPosition.LabelInsideTangential);

                    var captured = slice;
                    slice.Hovered += exploded =>
                    {
                        captured.SetExploded(exploded);
                        if (exploded)
                            chart.SetAnimationOptions(QChart.AnimationOptions.NoAnimation);
                    };

                    donut.Append(slice);
                    slices.Add(slice);
                }
                donut.SetHoleSize(minSize + i * (maxSize - minSize) / donutCount);
                donut.SetPieSize(minSize + (i + 1) * (maxSize - minSize) / donutCount);
                donuts[i] = donut;
                chart.AddSeries(donut);
            }

            view.Resize(420, 320);
            #if QTSHARP_DEMO_SMOKE
                view.Show();
            #else
                demo.Show(view);
            #endif

            ChartsAssertions.Pump();
#if QTSHARP_DEMO_SMOKE

            demo.Check(chart.Series().Length == donutCount,
                $"the chart holds {donutCount} donuts (got {chart.Series().Length})");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(slices.Count >= 15 && slices.Count <= 25,
                $"the random slice count lands in 3..5 per donut (got {slices.Count} in total)");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(!ChartsAssertions.IsLegendVisible(chart), "the legend is hidden");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(chart.animationOptions() == QChart.AnimationOptions.AllAnimations,
                "the animations start as AllAnimations");
            #endif
#if QTSHARP_DEMO_SMOKE

            // The rings nest: each one's hole is inside its own pie, and the pie of one ring starts
            // where the next ring's hole begins.
            demo.Check(Math4.Nearly(donuts[0].HoleSize, 0.1, 0.0001),
                $"the innermost hole is 0.1 (got {donuts[0].HoleSize})");
#endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(Math4.Nearly(donuts[4].PieSize(), 0.9, 0.0001),
                $"the outermost ring reaches 0.9 (got {donuts[4].PieSize()})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            for (int i = 1; i < donutCount; i++)
                demo.Check(donuts[i].HoleSize > donuts[i - 1].HoleSize,
                    $"ring {i}'s hole is wider than ring {i - 1}'s");
            #endif

            // The example's rotation: advance each ring's start and end angle by a phase shift and
            // check the window moved without changing its width.
            double before = donuts[0].PieStartAngle();
            double spanBefore = donuts[0].PieEndAngle() - before;
            foreach (var donut in donuts)
            {
                double phaseShift = -50 + random.Bounded(100);
                donut.SetPieStartAngle(donut.PieStartAngle() + phaseShift);
                donut.SetPieEndAngle(donut.PieEndAngle() + phaseShift);
            }
            double after = donuts[0].PieStartAngle();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(!Math4.Nearly(before, after, 0.0001),
                $"the rotation moved the start angle ({before} -> {after})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(Math4.Nearly(spanBefore, donuts[0].PieEndAngle() - after, 0.0001),
                "the rotation preserves each ring's angular window");
            #endif

            // Hovering is what explains the title: the signal reaches the C# handler and explodes the
            // very slice that emitted it.
            var target = slices[0];
            #if QTSHARP_DEMO_SMOKE
            demo.Check(!target.IsExploded(), "the first slice starts unexploded");
            #endif
            target.HoveredSignal.Emit(true);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(target.IsExploded(), "hovering it (through the signal) explodes it");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(chart.animationOptions() == QChart.AnimationOptions.NoAnimation,
                "hovering turns the animations off, as the example does");
            #endif
            target.HoveredSignal.Emit(false);
            #if QTSHARP_DEMO_SMOKE
            demo.Check(!target.IsExploded(), "un-hovering it puts it back");
            #endif

            foreach (var donut in donuts)
            {
                donut.SetPieStartAngle(0);
                donut.SetPieEndAngle(360);
            }
            #if QTSHARP_DEMO_SMOKE
            demo.Check(donuts[0].PieStartAngle() == 0 && donuts[0].PieEndAngle() == 360,
                "the rings can be reset to a full circle");
            #endif

            using var rendered = view.Grab();
            #if QTSHARP_DEMO_SMOKE
            demo.Check(rendered != null && rendered.Width() > 0,
                $"the chart renders off-screen ({rendered?.Width()}x{rendered?.Height()})");
            #endif
            if (rendered != null)
            {
                int ink = ChartsAssertions.InkSamples(rendered);
                #if QTSHARP_DEMO_SMOKE
                demo.Check(ink > 100, $"the rings are actually drawn ({ink} ink samples)");
                #endif
                #if QTSHARP_DEMO_SMOKE
                demo.Check(ChartsAssertions.ColourSamples(rendered) > 10,
                    $"the slices get distinct colours ({ChartsAssertions.ColourSamples(rendered)} colours)");
                #endif
            }

            #if QTSHARP_DEMO_SMOKE
            return demo.ExitCode;
            #else
            return 0;
            #endif
        }
    }
}
