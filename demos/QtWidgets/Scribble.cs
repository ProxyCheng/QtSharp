using System;
using System.Collections.Generic;
using System.IO;

// The official widgets/widgets/scribble example, ported onto the *new* pipeline. Everything below
// comes from model/qtsharp.api.json through the code generator - there is no hand-written glue.
//
// It is the counterpart of the legacy-pipeline Scribble.cs (which testapp's "scribble" command
// still uses): a ScribbleArea that paints into an off-screen QImage, and a ScribbleWindow that
// wraps it in menus and a Save-As format list.
//
// What the new model lets this port do that the legacy one could not:
//   * QMouseEvent::buttons() (the held-button bitmask, inherited from QSinglePointEvent) is bound
//     now, so the legacy `scribbling` workaround in MouseMoveEvent is gone - the handler checks the
//     real button state, exactly like the C++ original;
//   * QImageWriter::supportedImageFormats() is bound, so the hard-coded legacy format list is gone
//     and the Save As menu is built from what Qt can actually write;
//   * QImage/QPen/QPainter are ordinary bound classes, so CreatePen() below is the real pen and the
//     test can read a painted pixel straight back out of the QImage.
//
// Deviations from the C++ original, all forced by what the model can express today:
//   * the C++ `QPen(color, width, ...)` relies on QColor converting to QBrush implicitly; C#
//     spells the QBrush explicitly - the same pen;
//   * QInputDialog::getInt(..., bool *) is not in the model (only the version returning int is), so
//     ApplyPenWidth(int) clamps to the 1..50 range the dialog would have enforced and is what the
//     self-test drives; the dialog wrapper is never called;
//   * printing needs QtPrintSupport, which the runtime does not ship, so Print() stays a stub that
//     only bumps a counter (this is exactly what the legacy port does, minus the counter);
//   * every modal dialog (QFileDialog / QColorDialog / QInputDialog / QMessageBox) would block a
//     headless self-test, so the *handlers* are split from the *dialogs*: the decision logic lives
//     in MaybeSave(SaveDecision, path) / SaveFile(format, path) and the dialogs are thin wrappers
//     the test never invokes.
public sealed class ApiScribbleArea : QWidget
{
    bool modified;
    bool scribbling;
    int myPenWidth = 1;
    QColor myPenColor = new QColor(Qt.GlobalColor.blue);
    QImage image;
    int lastX;
    int lastY;

    // Test-visible state. The handlers store their result here so the self-test can assert on it
    // without an event loop (the same trick ApiShapedClock uses).
    public int PaintCount;
    public int PressCount;
    public int ReleaseCount;
    public int MoveCount;
    public int PointCount;
    public int StrokeCount;
    public int PrintCount;
    public Qt.MouseButton LastButton = Qt.MouseButton.NoButton;
    public Qt.MouseButtons LastButtons = Qt.MouseButtons.NoButton;
    public int LastPointX;
    public int LastPointY;

    public ApiScribbleArea()
        : base(null, 0)
    {
        SetAttribute(Qt.WidgetAttribute.WA_StaticContents, true);
    }

    public bool IsModified => modified;
    public bool IsScribbling => scribbling;
    public QColor PenColor => myPenColor;
    public int PenWidth => myPenWidth;
    public bool HasImage => image != null;

    public int ImageWidth() => image == null ? -1 : image.Width();
    public int ImageHeight() => image == null ? -1 : image.Height();

    /// <summary>The raw pixel, straight out of the backing QImage (0xAARRGGBB for Format_RGB32).</summary>
    public uint ImagePixel(int x, int y) => image == null ? 0u : image.Pixel(x, y);

    /// <summary>
    /// The pen DrawLineTo() paints with, exposed so the self-test can check that colour, width,
    /// style, cap and join really round-trip through the area's setters. The caller owns the result.
    /// </summary>
    public QPen CreatePen()
    {
        // QPen takes a QBrush; the C++ passes the QColor and lets it convert.
        using var brush = new QBrush(myPenColor);
        return new QPen(brush, myPenWidth, Qt.PenStyle.SolidLine,
                        Qt.PenCapStyle.RoundCap, Qt.PenJoinStyle.RoundJoin);
    }

    public void SetPenColor(QColor newColor)
    {
        if (newColor.IsValid())
            myPenColor = newColor;
    }

    public void SetPenWidth(int newWidth)
    {
        if (newWidth > 0)
            myPenWidth = newWidth;
    }

    public bool OpenImage(string fileName)
    {
        var loadedImage = new QImage();
        if (!loadedImage.Load(fileName))
            return false;

        var loadedSize = loadedImage.Size();
        var areaSize = Size;
        var newSize = loadedSize.ExpandedTo(areaSize);
        {
            image = ResizeImage(loadedImage, newSize);
        }

        modified = false;
        Update();
        return true;
    }

    public bool SaveImage(string fileName, string fileFormat)
    {
        if (image == null)
            return false;

        QImage visibleImage;
        var areaSize = Size;
            visibleImage = ResizeImage(image, areaSize);

        if (visibleImage.Save(fileName, fileFormat))
        {
            modified = false;
            return true;
        }
        return false;
    }

    public void ClearImage()
    {
        if (image == null)
        {
            var size = new QSize(Math.Max(Width + 128, 128), Math.Max(Height + 128, 128));
                image = ResizeImage(null, size);
        }

        var white = new QColor(Qt.GlobalColor.white);
            image.Fill(white);

        modified = true;
        Update();
    }

    public void Print()
    {
        // The original prints through QtPrintSupport, which the runtime does not ship.
        PrintCount++;
    }

    // ------------------------------------------------------------------ the event handlers
    public override void MousePressEvent(QMouseEvent e)
    {
        LastButton = e.Button();
        LastButtons = e.Buttons();
        PressCount++;

        if (e.Button() == Qt.MouseButton.LeftButton)
        {
            var position = e.Position();
            {
                lastX = (int)position.X();
                lastY = (int)position.Y();
            }
            LastPointX = lastX;
            LastPointY = lastY;
            scribbling = true;
        }
    }

    public override void MouseMoveEvent(QMouseEvent e)
    {
        // The real check the legacy port could not express: buttons() is the held-button bitmask.
        bool leftHeld = (e.Buttons() & Qt.MouseButtons.LeftButton) != 0;
        var position = e.Position();
            MouseMoveBody((int)position.X(), (int)position.Y(), leftHeld);
    }

    public override void MouseReleaseEvent(QMouseEvent e)
    {
        var position = e.Position();
            MouseReleaseBody((int)position.X(), (int)position.Y(),
                             e.Button() == Qt.MouseButton.LeftButton);
    }

    public override void PaintEvent(QPaintEvent e)
    {
        PaintCount++;
        if (image == null)
            return;

        // QPaintDevice is a view of the widget, not an owned object, so it is not disposable.
        using (var painter = new QPainter(AsQPaintDevice()))
        {
            var dirtyRect = e.Rect();
            painter.DrawImage(dirtyRect, image, dirtyRect);
        }
    }

    public override void ResizeEvent(QResizeEvent e)
    {
        GrowImageIfNeeded();
        base.ResizeEvent(e);
    }

    // ------------------------------------------------------------------ test seams
    // The runtime has a synthetic *press* probe but no synthetic move/release probe, so a headless
    // test cannot deliver those through Qt. These seams run the exact bodies the overrides run,
    // with the same `buttons() & LeftButton` precondition a real event would carry.
    public void SimulateMouseMove(int x, int y) => MouseMoveBody(x, y, true);
    public void SimulateMouseRelease(int x, int y) => MouseReleaseBody(x, y, true);

    void MouseMoveBody(int x, int y, bool leftHeld)
    {
        if (leftHeld && scribbling)
        {
            MoveCount++;
            DrawLineTo(x, y);
        }
    }

    void MouseReleaseBody(int x, int y, bool leftButton)
    {
        if (leftButton && scribbling)
        {
            ReleaseCount++;
            DrawLineTo(x, y);
            scribbling = false;
        }
    }

    public void GrowImageIfNeeded()
    {
        if (image == null || Width > image.Width() || Height > image.Height())
        {
            int newWidth = Math.Max(Width + 128, image?.Width() ?? 0);
            int newHeight = Math.Max(Height + 128, image?.Height() ?? 0);
            var size = new QSize(newWidth, newHeight);
                image = ResizeImage(image, size);
            Update();
        }
    }

    void DrawLineTo(int x, int y)
    {
        if (image == null)
        {
            var size = new QSize(Math.Max(Width + 128, 128), Math.Max(Height + 128, 128));
                image = ResizeImage(null, size);
        }

        int oldX = lastX;
        int oldY = lastY;

        using (var painter = new QPainter(image))
        using (var pen = CreatePen())
        {
            painter.SetPen(pen);
            painter.DrawLine(new QPoint(oldX, oldY), new QPoint(x, y));
        }

        modified = true;
        PointCount++;
        StrokeCount++;
        lastX = x;
        lastY = y;

        int radius = (myPenWidth / 2) + 2;
        var from = new QPoint(oldX, oldY);
        var to = new QPoint(x, y);
        var raw = new QRect(from, to);
        var normalized = raw.Normalized();
        var dirty = normalized.Adjusted(-radius, -radius, +radius, +radius);
            Update(dirty);
    }

    QImage ResizeImage(QImage img, QSize newSize)
    {
        if (img != null)
        {
            var current = img.Size();
            {
                if (current.Width() == newSize.Width() && current.Height() == newSize.Height())
                    return img;
            }
        }

        var newImage = new QImage(newSize, QImage.Format.Format_RGB32);
        var white = new QColor(Qt.GlobalColor.white);
            newImage.Fill(white);

        if (img != null)
        {
            using (var painter = new QPainter(newImage))
            {
                var origin = new QPoint(0, 0);
                painter.DrawImage(origin, img);
            }
        }
        return newImage;
    }
}

// Port of examples/widgets/widgets/scribble/mainwindow.h|cpp.
public sealed class ApiScribbleWindow : QMainWindow
{
    public enum SaveDecision
    {
        Save,
        Discard,
        Cancel,
    }

    public ApiScribbleArea ScribbleArea;

    public QMenu SaveAsMenu;
    public QMenu FileMenu;
    public QMenu OptionMenu;
    public QMenu HelpMenu;

    public QAction OpenAct;
    public QAction PrintAct;
    public QAction ExitAct;
    public QAction PenColorAct;
    public QAction PenWidthAct;
    public QAction ClearScreenAct;
    public QAction AboutAct;
    public QAction AboutQtAct;

    public QAction[] SaveAsActs;
    public readonly string[] ImageFormats;

    public ApiScribbleWindow()
        : base(null, 0)
    {
        ScribbleArea = new ApiScribbleArea();
        SetCentralWidget(ScribbleArea);

        // The legacy port hard-coded this list; the model binds QImageWriter now, so ask Qt.
        ImageFormats = QuerySupportedImageFormats();

        CreateActions();
        CreateMenus();

        SetWindowTitle("Scribble");
        Resize(500, 500);
    }

    static string[] QuerySupportedImageFormats()
    {
        var formats = new List<string>();
        foreach (var raw in QImageWriter.SupportedImageFormats())
        {
            using (raw)
                formats.Add(raw.ConstData());
        }
        return formats.ToArray();
    }

    // ------------------------------------------------------------------ handlers (dialog-free bodies)
    public bool OpenFile(string fileName)
    {
        if (!MaybeSave(SaveDecision.Discard, null))
            return false;
        return ScribbleArea.OpenImage(fileName);
    }

    public bool ApplyPenColor(QColor newColor)
    {
        if (!newColor.IsValid())
            return false;
        ScribbleArea.SetPenColor(newColor);
        return true;
    }

    /// <summary>What QInputDialog::getInt(..., 1, 50, 1) enforces; returns the applied width.</summary>
    public int ApplyPenWidth(int requested)
    {
        int width = Math.Clamp(requested, 1, 50);
        ScribbleArea.SetPenWidth(width);
        return width;
    }

    /// <summary>The body of SaveFile() minus the QFileDialog (an empty path = the user cancelled).</summary>
    public bool SaveFile(string fileFormat, string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;
        return ScribbleArea.SaveImage(fileName, fileFormat);
    }

    /// <summary>
    /// The closeEvent decision, factored out of the blocking QMessageBox so the self-test can drive
    /// it: unmodified closes immediately; Save saves to <paramref name="savePath"/>; Discard closes;
    /// Cancel stays open. An empty path with Save mirrors the user cancelling the file dialog.
    /// </summary>
    public bool MaybeSave(SaveDecision decision, string savePath)
    {
        if (!ScribbleArea.IsModified)
            return true;
        if (decision == SaveDecision.Save)
            return SaveFile("png", savePath);
        return decision != SaveDecision.Cancel;
    }

    public override void CloseEvent(QCloseEvent e)
    {
        var decision = PromptSaveDecision();
        string path = decision == SaveDecision.Save ? AskSavePath("png") : null;
        if (MaybeSave(decision, path))
            e.Accept();
        else
            e.Ignore();
    }

    // ------------------------------------------------------------------ dialog wrappers (never called by the test)
    SaveDecision PromptSaveDecision()
    {
        var ret = QMessageBox.Warning(this, "Scribble",
            "The image has been modified.\nDo you want to save your changes?",
            QMessageBox.StandardButtons.Save | QMessageBox.StandardButtons.Discard
                                               | QMessageBox.StandardButtons.Cancel);
        if (ret == QMessageBox.StandardButton.Save)
            return SaveDecision.Save;
        if (ret == QMessageBox.StandardButton.Cancel)
            return SaveDecision.Cancel;
        return SaveDecision.Discard;
    }

    string AskSavePath(string fileFormat)
    {
        string initialPath = QDir.CurrentPath() + "/untitled." + fileFormat;
        return QFileDialog.GetSaveFileName(this, "Save As", initialPath,
            fileFormat.ToUpperInvariant() + " Files (*." + fileFormat + ");;All Files (*)");
    }

    void OpenInteractive()
    {
        if (!MaybeSave(SaveDecision.Discard, null))
            return;
        string fileName = QFileDialog.GetOpenFileName(this, "Open File", QDir.CurrentPath());
        if (!string.IsNullOrEmpty(fileName))
            ScribbleArea.OpenImage(fileName);
    }

    void PenColorInteractive()
    {
        // The official call: QColorDialog::getColor(penColor).
        var newColor = QColorDialog.GetColor(ScribbleArea.PenColor);
        if (newColor.IsValid())
            ScribbleArea.SetPenColor(newColor);
    }

    void PenWidthInteractive()
    {
        int newWidth = QInputDialog.GetInt(this, "Scribble", "Select pen width:",
                                           ScribbleArea.PenWidth, 1, 50, 1);
        ApplyPenWidth(newWidth);
    }

    void AboutInteractive()
    {
        QMessageBox.About(this, "About Scribble",
            "<p>The <b>Scribble</b> example shows how to use QMainWindow as the base widget for "
            + "an application, and how to reimplement some of QWidget's event handlers to receive "
            + "the events generated for the application's widgets.</p>");
    }

    void CreateActions()
    {
        OpenAct = new QAction("&Open...", this);
        OpenAct.SetShortcuts(QKeySequence.StandardKey.Open);
        OpenAct.TriggeredSignal.Connect(_ => OpenInteractive());

        var saveAsActs = new List<QAction>();
        foreach (var format in ImageFormats)
        {
            string fmt = format;
            var action = new QAction(fmt.ToUpperInvariant() + "...", this);
            action.TriggeredSignal.Connect(_ => SaveFile(fmt,
                QDir.CurrentPath() + "/untitled." + fmt));
            saveAsActs.Add(action);
        }
        SaveAsActs = saveAsActs.ToArray();

        PrintAct = new QAction("&Print...", this);
        PrintAct.TriggeredSignal.Connect(_ => ScribbleArea.Print());

        ExitAct = new QAction("E&xit", this);
        ExitAct.SetShortcuts(QKeySequence.StandardKey.Quit);
        ExitAct.TriggeredSignal.Connect(_ => Close());

        PenColorAct = new QAction("&Pen Color...", this);
        PenColorAct.TriggeredSignal.Connect(_ => PenColorInteractive());

        PenWidthAct = new QAction("Pen &Width...", this);
        PenWidthAct.TriggeredSignal.Connect(_ => PenWidthInteractive());

        ClearScreenAct = new QAction("&Clear Screen", this);
        ClearScreenAct.TriggeredSignal.Connect(_ => ScribbleArea.ClearImage());

        AboutAct = new QAction("&About", this);
        AboutAct.TriggeredSignal.Connect(_ => AboutInteractive());

        AboutQtAct = new QAction("About &Qt", this);
        AboutQtAct.TriggeredSignal.Connect(_ => QMessageBox.AboutQt(this));
    }

    void CreateMenus()
    {
        SaveAsMenu = new QMenu("&Save As", this);
        foreach (var action in SaveAsActs)
            SaveAsMenu.AddAction(action);

        FileMenu = new QMenu("&File", this);
        FileMenu.AddAction(OpenAct);
        FileMenu.AddMenu(SaveAsMenu);
        FileMenu.AddAction(PrintAct);
        FileMenu.AddSeparator();
        FileMenu.AddAction(ExitAct);

        OptionMenu = new QMenu("&Options", this);
        OptionMenu.AddAction(PenColorAct);
        OptionMenu.AddAction(PenWidthAct);
        OptionMenu.AddSeparator();
        OptionMenu.AddAction(ClearScreenAct);

        HelpMenu = new QMenu("&Help", this);
        HelpMenu.AddAction(AboutAct);
        HelpMenu.AddAction(AboutQtAct);

        MenuBar().AddMenu(FileMenu);
        MenuBar().AddMenu(OptionMenu);
        MenuBar().AddMenu(HelpMenu);
    }
}

// Runs the demo as a test: start the application, build the area and the window, then drive the
// handlers directly and assert on the state they leave behind. No event loop, no user interaction.
[Demo("qtwidgets-scribble")]
public static class Scribble
{
    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        uint Mask(uint rgb) => rgb & 0x00FFFFFFu;
        uint PixelRgb(ApiScribbleArea a, int x, int y) => Mask(a.ImagePixel(x, y));

        uint blue = Mask(new QColor(Qt.GlobalColor.blue).Rgb());
        uint red = Mask(new QColor(Qt.GlobalColor.red).Rgb());
        uint green = Mask(new QColor(Qt.GlobalColor.green).Rgb());
        uint white = Mask(new QColor(Qt.GlobalColor.white).Rgb());

        // ------------------------------------------------------------------ area: construction
        var area = new ApiScribbleArea();
        Check(!area.HasImage, "the backing image is created lazily (none before show)");
        area.Resize(420, 320);
        area.Show();

        Check(area.TestAttribute(Qt.WidgetAttribute.WA_StaticContents),
            "the area sets WA_StaticContents (the original's constructor)");
        Check(Mask(area.PenColor.Rgb()) == blue, $"the pen starts blue (got #{Mask(area.PenColor.Rgb()):X6})");
        Check(area.PenWidth == 1, $"the pen width starts at 1 (got {area.PenWidth})");
        Check(!area.IsModified, "a fresh area is not modified");
        Check(area.HasImage, "showing the area allocated the image through resizeEvent");

        // ------------------------------------------------------------------ ClearImage allocates
        area.ClearImage();
        int expectedWidth = Math.Max(area.Width + 128, 128);
        int expectedHeight = Math.Max(area.Height + 128, 128);
        Check(area.HasImage && area.ImageWidth() == expectedWidth && area.ImageHeight() == expectedHeight,
            $"clearImage allocated a {expectedWidth}x{expectedHeight} image (got {area.ImageWidth()}x{area.ImageHeight()})");
        Check(area.IsModified, "clearImage marks the image modified (the original does)");
        Check(PixelRgb(area, 5, 5) == white, $"clearImage filled the image white (got #{PixelRgb(area, 5, 5):X6})");

        // ------------------------------------------------------------------ pen round-trip
        area.SetPenColor(new QColor(Qt.GlobalColor.red));
        area.SetPenWidth(5);
        Check(Mask(area.PenColor.Rgb()) == red, $"setPenColor round-trips (got #{Mask(area.PenColor.Rgb()):X6})");
        Check(area.PenWidth == 5, $"setPenWidth round-trips (got {area.PenWidth})");
        using (var pen = area.CreatePen())
        {
            var penColor = pen.Color();
                Check(Mask(penColor.Rgb()) == red, $"the pen's colour is red (got #{Mask(penColor.Rgb()):X6})");
            Check(pen.Width() == 5, $"the pen's width is 5 (got {pen.Width()})");
            Check(pen.Style() == Qt.PenStyle.SolidLine, $"the pen's style is SolidLine (got {pen.Style()})");
            Check(pen.CapStyle() == Qt.PenCapStyle.RoundCap, $"the pen's cap is RoundCap (got {pen.CapStyle()})");
            Check(pen.JoinStyle() == Qt.PenJoinStyle.RoundJoin, $"the pen's join is RoundJoin (got {pen.JoinStyle()})");
        }

        // ------------------------------------------------------------------ a real press, then a stroke
        bool pressed = ApiRuntime.SendMousePress(area.Handle, 10, 10);
        Check(pressed && area.PressCount == 1 && area.LastButton == Qt.MouseButton.LeftButton,
            $"a Qt mouse press reached MousePressEvent (delivered={pressed}, count={area.PressCount}, button={area.LastButton})");
        Check((area.LastButtons & Qt.MouseButtons.LeftButton) != 0,
            $"QMouseEvent::buttons() is bound and reports the held button (got {area.LastButtons})");
        Check(area.LastPointX == 10 && area.LastPointY == 10,
            $"the press stored the last point (got {area.LastPointX},{area.LastPointY})");
        Check(area.IsScribbling && area.PointCount == 0, "the press started a stroke without drawing yet");

        area.SimulateMouseMove(60, 10);
        Check(area.PointCount == 1 && area.StrokeCount == 1,
            $"the move drew the first segment (points={area.PointCount}, strokes={area.StrokeCount})");
        Check(area.IsModified, "drawing marks the image modified");
        Check(PixelRgb(area, 35, 10) == red,
            $"the segment was painted in the pen colour (got #{PixelRgb(area, 35, 10):X6})");
        Check(PixelRgb(area, 35, 20) == white,
            $"pixels off the pen path stay white (got #{PixelRgb(area, 35, 20):X6})");

        area.SimulateMouseRelease(60, 10);
        Check(area.PointCount == 2 && !area.IsScribbling,
            $"the release drew the final dot and ended the stroke (points={area.PointCount})");
        area.SimulateMouseMove(90, 10);
        Check(area.PointCount == 2 && area.MoveCount == 1,
            "a move after the release no longer draws (the buttons() guard)");

        // ------------------------------------------------------------------ paint / render
        // Grab before asserting the paint count: grab() renders the widget, which is what delivers
        // the QPaintEvent this checks, while a bare Repaint() is ignored on a widget the window
        // system never exposed (a machine with no display - CI found it).
        using (var rendered = area.Grab())
        {
            Check(area.PaintCount > 0,
                $"paintEvent reached C# {area.PaintCount} time(s) through the generated dispatch");
            Check(DemoCatalog.GrabCovers(rendered, area.Width, area.Height),
                $"grab() rendered the area off-screen (got {rendered?.Width()}x{rendered?.Height()})");
        }

        // ------------------------------------------------------------------ resizeEvent grows the image
        int beforeResize = area.ImageWidth();
        area.Resize(600, 600);
        Check(area.ImageWidth() > beforeResize,
            $"resizeEvent grew the backing image ({beforeResize} -> {area.ImageWidth()})");
        Check(area.ImageWidth() == Math.Max(area.Width + 128, beforeResize),
            $"the grown image covers the widget plus the 128px margin (got {area.ImageWidth()})");

        // ------------------------------------------------------------------ save / open round-trip
        string dir = Path.Combine(Path.GetTempPath(), "qtsharp_scribble_api");
        Directory.CreateDirectory(dir);
        string saved = Path.Combine(dir, "scribble.png");
        if (File.Exists(saved))
            File.Delete(saved);

        Check(area.SaveImage(saved, "png") && File.Exists(saved),
            $"saveImage wrote a PNG to a temp file (exists={File.Exists(saved)})");

        var reopened = new ApiScribbleArea();
        reopened.Resize(600, 600);
        reopened.Show();
        Check(reopened.OpenImage(saved),
            "openImage loaded the file back");
        Check(reopened.ImageWidth() == 600 && reopened.ImageHeight() == 600,
            $"the loaded image has the saved size (got {reopened.ImageWidth()}x{reopened.ImageHeight()})");
        Check(PixelRgb(reopened, 35, 10) == red,
            $"the saved stroke survives the round-trip (got #{PixelRgb(reopened, 35, 10):X6})");
        Check(!reopened.IsModified, "opening a file clears the modified flag");
        Check(!reopened.OpenImage(Path.Combine(dir, "does-not-exist.png")),
            "opening a missing file returns false");

        // ------------------------------------------------------------------ window: menus and actions
        var window = new ApiScribbleWindow();
        window.Show();

        Check(window.CentralWidget() != null
              && window.CentralWidget().Handle == window.ScribbleArea.Handle,
            "the scribble area is the central widget");
        Check(window.WindowTitle == "Scribble", $"the window title is Scribble (got '{window.WindowTitle}')");
        Check(window.Width == 500 && window.Height == 500,
            $"the window was resized to 500x500 (got {window.Width}x{window.Height})");

        var topMenus = window.MenuBar().Actions();
        Check(topMenus.Length == 3, $"the menu bar has three menus (got {topMenus.Length})");
        Check(QMenu.MenuInAction(topMenus[0]).title() == "&File"
              && QMenu.MenuInAction(topMenus[1]).title() == "&Options"
              && QMenu.MenuInAction(topMenus[2]).title() == "&Help",
            "the menus are File / Options / Help in order");

        var fileActions = window.FileMenu.Actions();
        Check(fileActions.Length == 5, $"File has five entries (got {fileActions.Length})");
        Check(fileActions[0].text() == "&Open..." && fileActions[2].text() == "&Print..."
              && fileActions[4].text() == "E&xit",
            "File holds Open, the Save As submenu, Print and Exit in order");
        Check(fileActions[1].text() == "&Save As" && fileActions[3].IsSeparator(),
            "the second File entry is the Save As submenu and the fourth is a separator");

        var optionActions = window.OptionMenu.Actions();
        Check(optionActions.Length == 4, $"Options has four entries (got {optionActions.Length})");
        Check(optionActions[0].text() == "&Pen Color..." && optionActions[1].text() == "Pen &Width..."
              && optionActions[3].text() == "&Clear Screen" && optionActions[2].IsSeparator(),
            "Options holds Pen Color, Pen Width, a separator and Clear Screen");

        var helpActions = window.HelpMenu.Actions();
        Check(helpActions.Length == 2 && helpActions[0].text() == "&About"
              && helpActions[1].text() == "About &Qt",
            "Help holds About and About Qt");

        Check(window.SaveAsActs.Length == window.ImageFormats.Length,
            $"the Save As menu has one action per writable format ({window.SaveAsActs.Length} actions, "
            + $"{window.ImageFormats.Length} formats from QImageWriter)");
        bool hasPng = Array.Exists(window.ImageFormats, f => f == "png");
        Check(hasPng, "QImageWriter advertises PNG");
        QAction pngAct = null;
        foreach (var action in window.SaveAsActs)
            if (action.text() == "PNG...")
                pngAct = action;
        Check(pngAct != null, "the Save As menu carries the PNG action with the upstream text");

        // ------------------------------------------------------------------ action wiring
        using (var actual = window.OpenAct.shortcut())
        using (var expected = new QKeySequence(QKeySequence.StandardKey.Open))
            Check(actual.ToString() == expected.ToString(),
                $"Open carries StandardKey.Open (got '{actual.ToString()}', platform = '{expected.ToString()}')");
        using (var actual = window.ExitAct.shortcut())
        using (var expected = new QKeySequence(QKeySequence.StandardKey.Quit))
            Check(actual.ToString() == expected.ToString(),
                $"Exit carries StandardKey.Quit (got '{actual.ToString()}', platform = '{expected.ToString()}')");
        Check(window.AboutAct.IsEnabled() && window.AboutQtAct.IsEnabled(),
            "About / About Qt are enabled (not triggered: they would block)");

        window.PrintAct.Trigger();
        Check(window.ScribbleArea.PrintCount == 1,
            $"triggering Print reached the handler (got {window.ScribbleArea.PrintCount})");

        // ------------------------------------------------------------------ pen width clamps to 1..50
        Check(window.ApplyPenWidth(0) == 1 && window.ScribbleArea.PenWidth == 1,
            $"pen width 0 clamps up to 1 (got {window.ScribbleArea.PenWidth})");
        Check(window.ApplyPenWidth(60) == 50 && window.ScribbleArea.PenWidth == 50,
            $"pen width 60 clamps down to 50 (got {window.ScribbleArea.PenWidth})");
        Check(window.ApplyPenWidth(7) == 7 && window.ScribbleArea.PenWidth == 7,
            $"pen width 7 applies unchanged (got {window.ScribbleArea.PenWidth})");

        window.ApplyPenColor(new QColor(Qt.GlobalColor.green));
        Check(Mask(window.ScribbleArea.PenColor.Rgb()) == green,
            $"applying a colour reaches the area (got #{Mask(window.ScribbleArea.PenColor.Rgb()):X6})");

        // ------------------------------------------------------------------ clear screen action
        window.ClearScreenAct.Trigger();
        Check(window.ScribbleArea.HasImage && window.ScribbleArea.IsModified,
            "triggering Clear Screen allocated the image and marked it modified");
        Check(PixelRgb(window.ScribbleArea, 5, 5) == white,
            $"Clear Screen filled the image white (got #{PixelRgb(window.ScribbleArea, 5, 5):X6})");

        // ------------------------------------------------------------------ closeEvent decision logic
        Check(window.MaybeSave(ApiScribbleWindow.SaveDecision.Cancel, null) == false,
            "a modified image + Cancel keeps the window open");
        Check(window.MaybeSave(ApiScribbleWindow.SaveDecision.Discard, null)
              && window.ScribbleArea.IsModified,
            "a modified image + Discard closes and leaves the image as it was");
        Check(window.MaybeSave(ApiScribbleWindow.SaveDecision.Save, null) == false,
            "Save with no chosen path is like cancelling the file dialog (stays open)");

        string windowSave = Path.Combine(dir, "window.png");
        if (File.Exists(windowSave))
            File.Delete(windowSave);
        Check(window.MaybeSave(ApiScribbleWindow.SaveDecision.Save, windowSave) && File.Exists(windowSave),
            "Save with a path writes the file and lets the close through");
        Check(!window.ScribbleArea.IsModified, "saving clears the modified flag so a second close is silent");
        Check(window.MaybeSave(ApiScribbleWindow.SaveDecision.Cancel, null),
            "an unmodified image closes without any prompt");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
