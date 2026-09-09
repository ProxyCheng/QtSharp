using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

public class ScribbleArea : QWidget
{
    bool modified = false;
    bool scribbling = false;
    int myPenWidth = 1;
    QColor myPenColor = new QColor(Qt.GlobalColor.Blue);
    QImage image;
    QPoint lastPoint;

    public ScribbleArea(QWidget parent = null) : base(parent, 0)
    {
        SetAttribute(Qt.WidgetAttribute.WA_StaticContents, true);
    }

    public bool IsModified => modified;
    public QColor PenColor => myPenColor;
    public int PenWidth => myPenWidth;

    public void SetPenColor(QColor newColor) => myPenColor = newColor;
    public void SetPenWidth(int newWidth) => myPenWidth = newWidth;

    public void ClearImage()
    {
        image.Fill(new QColor(Qt.GlobalColor.White));
        modified = true;
        Update();
    }

    protected override void MousePressEvent(QMouseEvent e)
    {
        if (e.Button == Qt.MouseButton.LeftButton)
        {
            lastPoint = e.Position.ToPoint();
            scribbling = true;
        }
    }

    protected override void MouseMoveEvent(QMouseEvent e)
    {
        if (scribbling)
            DrawLineTo(e.Position.ToPoint());
    }

    protected override void MouseReleaseEvent(QMouseEvent e)
    {
        if (e.Button == Qt.MouseButton.LeftButton && scribbling)
        {
            DrawLineTo(e.Position.ToPoint());
            scribbling = false;
        }
    }

    protected override void PaintEvent(QPaintEvent e)
    {
        using (var painter = new QPainter(this))
            painter.DrawImage(new QRect(0, 0, Width, Height), image);
    }

    protected override void ResizeEvent(QResizeEvent e)
    {
        if (image == null || Width > image.Width || Height > image.Height)
        {
            int newWidth = Math.Max(Width + 128, image?.Width ?? 0);
            int newHeight = Math.Max(Height + 128, image?.Height ?? 0);
            ResizeImage(new QSize(newWidth, newHeight));
            Update();
        }
        base.ResizeEvent(e);
    }

    void DrawLineTo(QPoint endPoint)
    {
        if (image == null)
            ResizeImage(new QSize(Width + 128, Height + 128));

        using (var painter = new QPainter(image))
        {
            painter.SetPen(myPenColor);
            painter.DrawLine(lastPoint, endPoint);
        }
        modified = true;

        int rad = myPenWidth / 2 + 2;
        Update();
        lastPoint = endPoint;
    }

    void ResizeImage(QSize newSize)
    {
        if (image != null && image.Size == newSize)
            return;

        var newImage = new QImage(newSize, QImage.Format.FormatRGB32);
        newImage.Fill(new QColor(Qt.GlobalColor.White));
        if (image != null)
        {
            using (var painter = new QPainter(newImage))
                painter.DrawImage(new QPoint(0, 0), image);
            image.Dispose();
        }
        image = newImage;
    }
}

public class ScribbleWindow : QMainWindow
{
    ScribbleArea scribbleArea;

    public ScribbleWindow() : base(null, 0)
    {
        scribbleArea = new ScribbleArea(this);
        CentralWidget = scribbleArea;

        fileMenu = new QMenu("&File", this);
        optionMenu = new QMenu("&Options", this);
        MenuBar.AddMenu(fileMenu);
        MenuBar.AddMenu(optionMenu);

        CreateActions();

        WindowTitle = "Scribble";
        Resize(500, 500);
    }

    void CreateActions()
    {
        var clearAct = new QAction("&Clear Screen", this);
        clearAct.TriggeredSignal.Connect(_ => scribbleArea.ClearImage());

        var penColorAct = new QAction("&Pen Color...", this);
        penColorAct.TriggeredSignal.Connect(_ => PenColor());

        var penWidthAct = new QAction("Pen &Width...", this);
        penWidthAct.TriggeredSignal.Connect(_ => PenWidth());

        var exitAct = new QAction("E&xit", this);
        exitAct.TriggeredSignal.Connect(_ => Close());

        optionMenu.AddAction(clearAct);
        optionMenu.AddAction(penColorAct);
        optionMenu.AddAction(penWidthAct);
        optionMenu.AddSeparator();
        optionMenu.AddAction(exitAct);
    }

    QMenu fileMenu;
    QMenu optionMenu;

    void PenColor()
    {
        var color = QColorDialog.GetColor(scribbleArea.PenColor, this);
        if (color != null && color.IsValid)
            scribbleArea.SetPenColor(color);
    }

    void PenWidth()
    {
        bool ok = false;
        int width = QInputDialog.GetInt(this, "Scribble", "Select pen width:",
            scribbleArea.PenWidth, 1, 50, 1, ref ok);
        if (ok)
            scribbleArea.SetPenWidth(width);
    }
}
