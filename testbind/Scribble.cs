using System;
using System.Collections.Generic;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// Port of examples/widgets/widgets/scribble/scribblearea.h|cpp
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

    public bool OpenImage(string fileName)
    {
        var loadedImage = new QImage();
        if (!loadedImage.Load(fileName))
            return false;

        var newSize = loadedImage.Size.ExpandedTo(Size);
        image = ResizeImage(loadedImage, newSize);
        modified = false;
        Update();
        return true;
    }

    public bool SaveImage(string fileName, string fileFormat)
    {
        var visibleImage = ResizeImage(image, Size);
        if (visibleImage.Save(fileName, fileFormat))
        {
            modified = false;
            return true;
        }
        return false;
    }

    public void SetPenColor(QColor newColor) => myPenColor = newColor;
    public void SetPenWidth(int newWidth) => myPenWidth = newWidth;

    public void ClearImage()
    {
        if (image == null)
            image = ResizeImage(null, new QSize(Math.Max(Width + 128, 128), Math.Max(Height + 128, 128)));
        image.Fill(new QColor(Qt.GlobalColor.White));
        modified = true;
        Update();
    }

    public void Print()
    {
        // The original example prints through QtPrintSupport, which is not bound here.
    }

    protected override void MousePressEvent(QMouseEvent @event)
    {
        if (@event.Button == Qt.MouseButton.LeftButton)
        {
            lastPoint = @event.Position.ToPoint();
            scribbling = true;
        }
    }

    protected override void MouseMoveEvent(QMouseEvent @event)
    {
        // The original checks (event->buttons() & Qt::LeftButton) && scribbling.
        // QPointerEvent::buttons() is not part of the current bindings, but scribbling
        // is set on left press and cleared on left release, so it is equivalent here.
        if (scribbling)
            DrawLineTo(@event.Position.ToPoint());
    }

    protected override void MouseReleaseEvent(QMouseEvent @event)
    {
        if (@event.Button == Qt.MouseButton.LeftButton && scribbling)
        {
            DrawLineTo(@event.Position.ToPoint());
            scribbling = false;
        }
    }

    protected override void PaintEvent(QPaintEvent @event)
    {
        if (image == null)
            return;

        using var painter = new QPainter(this);
        var dirtyRect = @event.Rect;
        painter.DrawImage(dirtyRect, image, dirtyRect);
    }

    protected override void ResizeEvent(QResizeEvent @event)
    {
        if (image == null || Width > image.Width || Height > image.Height)
        {
            int newWidth = Math.Max(Width + 128, image?.Width ?? 0);
            int newHeight = Math.Max(Height + 128, image?.Height ?? 0);
            image = ResizeImage(image, new QSize(newWidth, newHeight));
            Update();
        }
        base.ResizeEvent(@event);
    }

    void DrawLineTo(QPoint endPoint)
    {
        if (image == null)
            image = ResizeImage(null, new QSize(Math.Max(Width + 128, 128), Math.Max(Height + 128, 128)));

        using (var painter = new QPainter(image))
        using (var pen = new QPen(myPenColor, myPenWidth, Qt.PenStyle.SolidLine,
                                  Qt.PenCapStyle.RoundCap, Qt.PenJoinStyle.RoundJoin))
        {
            painter.Pen = pen;
            painter.DrawLine(lastPoint, endPoint);
        }
        modified = true;

        int rad = (myPenWidth / 2) + 2;
        Update(new QRect(lastPoint, endPoint).Normalized.Adjusted(-rad, -rad, +rad, +rad));
        lastPoint = endPoint;
    }

    QImage ResizeImage(QImage img, QSize newSize)
    {
        if (img != null && img.Size == newSize)
            return img;

        var newImage = new QImage(newSize, QImage.Format.FormatRGB32);
        newImage.Fill(new QColor(Qt.GlobalColor.White));
        if (img != null)
        {
            using (var painter = new QPainter(newImage))
                painter.DrawImage(new QPoint(0, 0), img);
        }
        return newImage;
    }
}

// Port of examples/widgets/widgets/scribble/mainwindow.h|cpp
public class ScribbleWindow : QMainWindow
{
    // QImageWriter::supportedImageFormats() is not part of the current bindings, so the
    // common write formats are listed explicitly. Everything else mirrors the original.
    static readonly string[] ImageFormats =
    {
        "bmp", "jpg", "jpeg", "png", "ppm", "tif", "tiff", "webp", "xbm", "xpm"
    };

    readonly List<object> keepAlive = new();
    readonly List<QAction> saveAsActs = new();

    ScribbleArea scribbleArea;

    QAction openAct;
    QAction printAct;
    QAction exitAct;
    QAction penColorAct;
    QAction penWidthAct;
    QAction clearScreenAct;
    QAction aboutAct;
    QAction aboutQtAct;

    QMenu saveAsMenu;
    QMenu fileMenu;
    QMenu optionMenu;
    QMenu helpMenu;

    public ScribbleWindow() : base(null, 0)
    {
        scribbleArea = keep(new ScribbleArea(this));
        CentralWidget = scribbleArea;

        CreateActions();
        CreateMenus();

        WindowTitle = "Scribble";
        Resize(500, 500);
    }

    T keep<T>(T obj) where T : class
    {
        keepAlive.Add(obj);
        return obj;
    }

    protected override void CloseEvent(QCloseEvent @event)
    {
        if (MaybeSave())
            @event.Accept();
        else
            @event.Ignore();
    }

    void Open()
    {
        if (MaybeSave())
        {
            string fileName = QFileDialog.GetOpenFileName(this, "Open File", QDir.CurrentPath);
            if (!string.IsNullOrEmpty(fileName))
                scribbleArea.OpenImage(fileName);
        }
    }

    void PenColor()
    {
        var newColor = QColorDialog.GetColor(scribbleArea.PenColor);
        if (newColor.IsValid)
            scribbleArea.SetPenColor(newColor);
    }

    void PenWidth()
    {
        bool ok = false;
        int newWidth = QInputDialog.GetInt(this, "Scribble", "Select pen width:",
                                           scribbleArea.PenWidth, 1, 50, 1, ref ok);
        if (ok)
            scribbleArea.SetPenWidth(newWidth);
    }

    void About()
    {
        QMessageBox.About(this, "About Scribble",
            "<p>The <b>Scribble</b> example shows how to use QMainWindow as the "
            + "base widget for an application, and how to reimplement some of "
            + "QWidget's event handlers to receive the events generated for "
            + "the application's widgets:</p><p> We reimplement the mouse event "
            + "handlers to facilitate drawing, the paint event handler to "
            + "update the application and the resize event handler to optimize "
            + "the application's appearance. In addition we reimplement the "
            + "close event handler to intercept the close events before "
            + "terminating the application.</p><p> The example also demonstrates "
            + "how to use QPainter to draw an image in real time, as well as "
            + "to repaint widgets.</p>");
    }

    void CreateActions()
    {
        openAct = keep(new QAction("&Open...", this));
        openAct.SetShortcuts(QKeySequence.StandardKey.Open);
        openAct.TriggeredSignal.Connect(_ => Open());

        foreach (var format in ImageFormats)
        {
            string fmt = format;
            var action = keep(new QAction(fmt.ToUpperInvariant() + "...", this));
            action.TriggeredSignal.Connect(_ => SaveFile(fmt));
            saveAsActs.Add(action);
        }

        printAct = keep(new QAction("&Print...", this));
        printAct.TriggeredSignal.Connect(_ => scribbleArea.Print());

        exitAct = keep(new QAction("E&xit", this));
        exitAct.SetShortcuts(QKeySequence.StandardKey.Quit);
        exitAct.TriggeredSignal.Connect(_ => Close());

        penColorAct = keep(new QAction("&Pen Color...", this));
        penColorAct.TriggeredSignal.Connect(_ => PenColor());

        penWidthAct = keep(new QAction("Pen &Width...", this));
        penWidthAct.TriggeredSignal.Connect(_ => PenWidth());

        clearScreenAct = keep(new QAction("&Clear Screen", this));
        clearScreenAct.TriggeredSignal.Connect(_ => scribbleArea.ClearImage());

        aboutAct = keep(new QAction("&About", this));
        aboutAct.TriggeredSignal.Connect(_ => About());

        aboutQtAct = keep(new QAction("About &Qt", this));
        aboutQtAct.TriggeredSignal.Connect(_ => QMessageBox.AboutQt(this));
    }

    void CreateMenus()
    {
        saveAsMenu = keep(new QMenu("&Save As", this));
        foreach (var action in saveAsActs)
            saveAsMenu.AddAction(action);

        fileMenu = keep(new QMenu("&File", this));
        fileMenu.AddAction(openAct);
        fileMenu.AddMenu(saveAsMenu);
        fileMenu.AddAction(printAct);
        fileMenu.AddSeparator();
        fileMenu.AddAction(exitAct);

        optionMenu = keep(new QMenu("&Options", this));
        optionMenu.AddAction(penColorAct);
        optionMenu.AddAction(penWidthAct);
        optionMenu.AddSeparator();
        optionMenu.AddAction(clearScreenAct);

        helpMenu = keep(new QMenu("&Help", this));
        helpMenu.AddAction(aboutAct);
        helpMenu.AddAction(aboutQtAct);

        MenuBar.AddMenu(fileMenu);
        MenuBar.AddMenu(optionMenu);
        MenuBar.AddMenu(helpMenu);
    }

    bool MaybeSave()
    {
        if (scribbleArea.IsModified)
        {
            var ret = QMessageBox.Warning(this, "Scribble",
                "The image has been modified.\n"
                + "Do you want to save your changes?",
                (int)QMessageBox.StandardButton.Save
                | (int)QMessageBox.StandardButton.Discard
                | (int)QMessageBox.StandardButton.Cancel);
            if (ret == QMessageBox.StandardButton.Save)
                return SaveFile("png");
            else if (ret == QMessageBox.StandardButton.Cancel)
                return false;
        }
        return true;
    }

    bool SaveFile(string fileFormat)
    {
        string initialPath = QDir.CurrentPath + "/untitled." + fileFormat;

        string fileName = QFileDialog.GetSaveFileName(this, "Save As", initialPath,
            fileFormat.ToUpperInvariant() + " Files (*." + fileFormat + ");;All Files (*)");
        if (string.IsNullOrEmpty(fileName))
            return false;
        return scribbleArea.SaveImage(fileName, fileFormat);
    }
}
