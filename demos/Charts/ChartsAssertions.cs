using System;

namespace QtSharp.Charts.Demos
{
    /// <summary>What the ported examples assert on, so each one does not invent its own.</summary>
    public static class ChartsAssertions
    {
        /// <summary>
        /// How much ink the plot carries, sampled every other pixel: the series colour is dark on a
        /// chart's white background, so this is "the chart drew something" without depending on the
        /// exact pixels a font or a renderer would produce.
        /// </summary>
        public static int InkSamples(QPixmap rendered)
        {
            if (rendered == null)
                return 0;
            using var image = rendered.ToImage();
            int ink = 0;
            for (int y = 0; y < image.Height(); y += 2)
                for (int x = 0; x < image.Width(); x += 2)
                {
                    uint pixel = image.Pixel(x, y);
                    if ((pixel & 0xFF) < 200)
                        ink++;
                }
            return ink;
        }

        /// <summary>Number of distinct colours in the rendered chart, sampled every other pixel.</summary>
        public static int ColourSamples(QPixmap rendered)
        {
            if (rendered == null)
                return 0;
            using var image = rendered.ToImage();
            var seen = new System.Collections.Generic.HashSet<uint>();
            for (int y = 0; y < image.Height(); y += 2)
                for (int x = 0; x < image.Width(); x += 2)
                    seen.Add(image.Pixel(x, y));
            return seen.Count;
        }

        /// <summary>
        /// Puts a chart in a view and hands it back. In the Smoke build the host has already chosen
        /// the offscreen platform, so showing the view lets the chart lay out and paint without
        /// mapping a window; the View build registers it with the host instead.
        /// </summary>
        public static QChartView Present(DemoContext demo, QChart chart, int width = 420, int height = 320,
                                         QPainter.RenderHint renderHint = QPainter.RenderHint.Antialiasing)
        {
            var view = new QChartView(chart);
            view.SetRenderHint(renderHint);
            view.Resize(width, height);
#if QTSHARP_DEMO_SMOKE
            view.Show();
            Pump();
#else
            demo.Show(view);
#endif
            return view;
        }

        /// <summary>
        /// Shows or hides a chart's legend.
        ///
        /// QLegend derives from QGraphicsWidget, which in C++ is a QGraphicsItem as well, and the
        /// binding publishes that second base's members on QGraphicsObject - so setVisible() and the
        /// visible property come in by inheritance, the way PyQt's QLegend has them. The Qt examples
        /// call legend()->hide() and legend()->setVisible(); this is where that ends up.
        /// </summary>
        public static void SetLegendVisible(QChart chart, bool visible)
            => chart.Legend().SetVisible(visible);

        /// <summary>Hides the legend, the way the Qt examples' legend()->hide() does.</summary>
        public static void HideLegend(QChart chart)
            => chart.Legend().Hide();

        /// <summary>Whether the legend is showing, matching legend()->isVisible().</summary>
        public static bool IsLegendVisible(QChart chart)
            => chart.Legend().IsVisible();

        /// <summary>
        /// The QValueAxis view of an axis a chart created. QAbstractAxis::setRange takes QVariants
        /// (the axis may be any kind), so the qreal overload the Qt examples call is QValueAxis's -
        /// this is where chart->axes(...).first()->setRange(0, 20) ends up.
        /// </summary>
        public static QValueAxis ValueAxis(QAbstractAxis axis)
            => QValueAxis.FromHandle(axis.Handle);

        /// <summary>Lets Qt run: the animations and the layout need a few event-loop turns.</summary>
        public static void Pump(int rounds = 25)
        {
            for (int i = 0; i < rounds; i++)
            {
                QCoreApplication.ProcessEvents();
                System.Threading.Thread.Sleep(5);
            }
        }
    }
}
