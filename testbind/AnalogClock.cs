using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;

public unsafe partial class AnalogClock : QWidget
{
    public AnalogClock(QWidget parent = null) : base(parent, 0)
    {
        var timer = new QTimer(this);
        timer.TimeoutSignal.Connect(Update);
        timer.Start(1000);

        WindowTitle = "Analog Clock";
        Resize(200, 200);
    }

    protected override void PaintEvent(QPaintEvent e)
    {
        int[] hourHand = { 5, 14, -5, 14, -4, -71, 4, -71 };
        int[] minuteHand = { 4, 14, -4, 14, -3, -89, 3, -89 };
        int[] secondsHand = { 1, 14, -1, 14, -1, -89, 1, -89 };

        var hourColor = Palette.Color(QPalette.ColorRole.Text);
        var minuteColor = Palette.Color(QPalette.ColorRole.Text);
        var secondsColor = Palette.Color(QPalette.ColorRole.Accent);

        int side = Math.Min(Width, Height);

        using (var painter = new QPainter(this))
        {
            painter.SetRenderHint(QPainter.RenderHint.Antialiasing, true);

            painter.Translate(Width / 2, Height / 2);
            painter.Scale(side / 200.0, side / 200.0);

            painter.SetPen(global::QtSharp.QtCore.Qt.PenStyle.NoPen);
            painter.SetBrush(hourColor);

            using (var time = QTime.CurrentTime)
            {
                painter.Save();
                painter.Rotate(30.0 * (time.Hour + time.Minute / 60.0));
                using (var polygon = new QPolygon(4, ref hourHand[0]))
                    painter.DrawConvexPolygon(polygon);
                painter.Restore();

                for (int i = 0; i < 12; ++i)
                {
                    painter.DrawRect(73, -3, 16, 6);
                    painter.Rotate(30.0);
                }

                painter.SetBrush(minuteColor);

                painter.Save();
                painter.Rotate(6.0 * time.Minute);
                using (var polygon = new QPolygon(4, ref minuteHand[0]))
                    painter.DrawConvexPolygon(polygon);
                painter.Restore();

                painter.SetBrush(secondsColor);

                painter.Save();
                painter.Rotate(6.0 * time.Second);
                using (var polygon = new QPolygon(4, ref secondsHand[0]))
                    painter.DrawConvexPolygon(polygon);
                painter.DrawEllipse(-3, -3, 6, 6);
                painter.DrawEllipse(-5, -68, 10, 10);
                painter.Restore();
            }

            painter.SetPen(minuteColor);

            for (int j = 0; j < 60; ++j)
            {
                painter.DrawLine(92, 0, 96, 0);
                painter.Rotate(6.0);
            }
        }
    }
}
