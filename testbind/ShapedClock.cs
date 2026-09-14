using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

public class ShapedClock : QWidget
{
    QPoint dragPosition;
    bool dragging;

    public ShapedClock() : base(null, (int)Qt.WindowType.FramelessWindowHint | (int)Qt.WindowType.WindowSystemMenuHint)
    {
        SetAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, true);

        var timer = new QTimer(this);
        timer.TimeoutSignal.Connect(Update);
        timer.Start(1000);

        var quitAction = new QAction("E&xit", this);
        quitAction.TriggeredSignal.Connect(_ => QApplication.Quit());
        quitAction.Shortcut = new QKeySequence(QKeySequence.StandardKey.Quit);
        AddAction(quitAction);

        ContextMenuPolicy = Qt.ContextMenuPolicy.ActionsContextMenu;
        ToolTip = "Drag the clock with the left mouse button.\nUse the right mouse button to open a context menu.";
        WindowTitle = "Shaped Analog Clock";
    }

    public override QSize SizeHint => new QSize(200, 200);

    protected override void MousePressEvent(QMouseEvent @event)
    {
        if (@event.Button == Qt.MouseButton.LeftButton)
        {
            dragPosition = Sub(@event.GlobalPos, FrameGeometry.TopLeft);
            dragging = true;
            @event.Accept();
        }
    }

    protected override void MouseMoveEvent(QMouseEvent @event)
    {
        // The original tests (event->buttons() & Qt::LeftButton); that accessor is not
        // part of the current bindings, so the pressed state is tracked instead.
        if (dragging)
        {
            Move(Sub(@event.GlobalPos, dragPosition));
            @event.Accept();
        }
    }

    protected override void MouseReleaseEvent(QMouseEvent @event)
    {
        if (@event.Button == Qt.MouseButton.LeftButton)
            dragging = false;
    }

    static QPoint Sub(QPoint a, QPoint b) => new QPoint(a.X - b.X, a.Y - b.Y);

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
            painter.SetBrush(Palette.Color(QPalette.ColorRole.Window));
            painter.Opacity = 0.9;
            painter.DrawEllipse(-98, -98, 196, 196);
            painter.Opacity = 1.0;

            using (var time = QTime.CurrentTime)
            {
                painter.SetBrush(hourColor);

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
