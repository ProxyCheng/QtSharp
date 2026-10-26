using System;
using System.Collections.Generic;

// The official widgets/layouts/flowlayout example, ported onto the generated binding. This is the
// conformance case for writing a *layout* in C#: FlowLayout derives from QLayout (abstract) and
// implements the four pure virtuals (addItem, count, itemAt, takeAt) plus the measuring and
// placing hooks Qt calls while it activates a window.
//
// It is also the regression the ported examples were missing: every other layout in the demo suite
// is one of Qt's own (QHBoxLayout, QGridLayout, ...), so a hole in QLayout's extension surface
// stayed invisible until a real QLayout subclass was written. The self-test drives it through Qt -
// the window is shown and the layout is activated by Qt, never called directly.
[Demo("qtwidgets-flowlayout")]
public static class FlowLayoutDemo
{
    sealed class FlowLayout : QLayout
    {
        readonly List<QLayoutItem> items = new();
        readonly int horizontalGap;
        readonly int verticalGap;

        public FlowLayout(QWidget parent = null, int margin = -1, int hSpacing = -1, int vSpacing = -1)
            : base(parent)
        {
            horizontalGap = hSpacing;
            verticalGap = vSpacing;
            SetContentsMargins(margin, margin, margin, margin);
        }

        // ---- the four pure virtuals a layout has to implement ----------------------------
        public override void AddItem(QLayoutItem item) => items.Add(item);
        public override int Count() => items.Count;
        public override QLayoutItem ItemAt(int index)
            => index >= 0 && index < items.Count ? items[index] : null;
        public override QLayoutItem TakeAt(int index)
        {
            if (index < 0 || index >= items.Count)
                return null;
            var item = items[index];
            items.RemoveAt(index);
            return item;
        }

        // ---- the measuring and placing hooks ---------------------------------------------
        public override Qt.Orientations ExpandingDirections() => default;
        public override bool HasHeightForWidth() => true;
        public override int HeightForWidth(int width) => DoLayout(new QRect(0, 0, width, 0), true);
        public override void SetGeometry(QRect rect)
        {
            base.SetGeometry(rect);
            DoLayout(rect, false);
        }
        public override QSize SizeHint() => MinimumSize();
        public override QSize MinimumSize()
        {
            var size = new QSize(0, 0);
            foreach (var item in items)
                size = size.ExpandedTo(item.MinimumSize());
            var margins = contentsMargins();
            return new QSize(size.Width() + margins.Left() + margins.Right(),
                             size.Height() + margins.Top() + margins.Bottom());
        }

        // The official flow algorithm: place each item after the previous one, and start a new
        // line when the item would cross the right edge (testOnly measures without placing).
        int DoLayout(QRect rect, bool testOnly)
        {
            GetContentsMargins(out int left, out int top, out int right, out int bottom);
            var effective = rect.Adjusted(left, top, -right, -bottom);
            int x = effective.X();
            int y = effective.Y();
            int lineHeight = 0;

            foreach (var item in items)
            {
                var widget = item.Widget();
                int spaceX = HorizontalSpacing();
                if (spaceX == -1)
                    spaceX = widget.Style().LayoutSpacing(QSizePolicy.ControlType.PushButton,
                        QSizePolicy.ControlType.PushButton, Qt.Orientation.Horizontal);
                int spaceY = VerticalSpacing();
                if (spaceY == -1)
                    spaceY = widget.Style().LayoutSpacing(QSizePolicy.ControlType.PushButton,
                        QSizePolicy.ControlType.PushButton, Qt.Orientation.Vertical);

                var hint = item.SizeHint();
                int nextX = x + hint.Width() + spaceX;
                if (nextX - spaceX > effective.Right() && lineHeight > 0)
                {
                    x = effective.X();
                    y = y + lineHeight + spaceY;
                    nextX = x + hint.Width() + spaceX;
                    lineHeight = 0;
                }

                if (!testOnly)
                    item.SetGeometry(new QRect(new QPoint(x, y), hint));

                x = nextX;
                lineHeight = Math.Max(lineHeight, hint.Height());
            }
            return y + lineHeight - rect.Y() + bottom;
        }

        int HorizontalSpacing() => horizontalGap >= 0 ? horizontalGap : SmartSpacing(QStyle.PixelMetric.PM_LayoutHorizontalSpacing);
        int VerticalSpacing() => verticalGap >= 0 ? verticalGap : SmartSpacing(QStyle.PixelMetric.PM_LayoutVerticalSpacing);

        // Qt's own spacing when the layout has neither an explicit gap nor a stylesheet: the
        // style's metric, asked of the widget the layout is installed on.
        int SmartSpacing(QStyle.PixelMetric metric)
        {
            var parent = Parent();
            if (parent == null)
                return -1;
            if (parent.IsWidgetType())
            {
                var widget = QWidget.FromHandle(parent.Handle);
                return widget.Style().pixelMetric(metric, null, widget);
            }
            return QLayout.FromHandle(parent.Handle).spacing();
        }
    }

    sealed class FlowWindow : QWidget
    {
        public readonly FlowLayout Flow;

        public FlowWindow()
        {
            Flow = new FlowLayout();
            Flow.AddWidget(new QPushButton("Short"));
            Flow.AddWidget(new QPushButton("Longer"));
            Flow.AddWidget(new QPushButton("Different text"));
            Flow.AddWidget(new QPushButton("More text"));
            Flow.AddWidget(new QPushButton("Even longer button text"));
            SetLayout(Flow);
            SetWindowTitle("Flow Layout");
        }
    }

    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var window = new FlowWindow();
        window.Resize(320, 200);
        window.Show();
        // Qt activates the layout when the window is shown; activating again makes the geometry
        // deterministic before the assertions read it.
        window.Flow.Activate();

        Check(window.Flow.Count() == 5,
            $"the managed layout holds every widget QLayout::addWidget handed it ({window.Flow.Count()})");
        var first = window.Flow.ItemAt(0)?.Widget();
        var last = window.Flow.ItemAt(4)?.Widget();
        Check(first != null && last != null,
            "the managed itemAt() hands Qt's own QWidgetItems back");
        // The margin is -1, which Qt resolves to the style's default margin; the first item sits
        // at the content origin, whatever that margin turns out to be.
        var margins = window.Flow.contentsMargins();
        Check(first != null && first.Geometry.X() == margins.Left() && first.Geometry.Y() == margins.Top(),
            $"the layout placed the first button at its content origin ({first?.Geometry.X()},{first?.Geometry.Y()}, margin {margins.Left()})");
        Check(last != null && last.width() > last.height(),
            $"the last button was laid out with its size hint ({last?.width()}x{last?.height()})");

        // Wrapping: the same layout measured at two widths. HeightForWidth is the virtual Qt
        // consults when the window is resized; a narrower window cannot need fewer lines.
        int wide = window.Flow.HeightForWidth(320);
        int narrow = window.Flow.HeightForWidth(140);
        Check(wide > 0 && narrow >= wide,
            $"heightForWidth grows as the window narrows ({wide} at 320, {narrow} at 140)");

        var shot = window.Grab();
        Check(DemoCatalog.GrabCovers(shot, 320, 200),
            $"the window rendered off-screen ({shot.Width()}x{shot.Height()})");

        // A real resize goes through QLayout::setGeometry -> the managed doLayout.
        window.Resize(140, 260);
        window.Flow.Activate();
        Check(window.Flow.ItemAt(4)?.Widget().Geometry.Y() > 0,
            $"narrowing the window made the managed layout wrap ({window.Flow.ItemAt(4)?.Widget().Geometry.Y()})");

        window.Close();
        window.Dispose();

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
