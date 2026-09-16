using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// Port of the official Qt "Document Viewer" demo
// (qtdoc/examples/demos/documentviewer).

// The official demo loads its text/JSON/image/PDF viewers as native plugins at runtime
// (QPluginLoader). Native plugins cannot be loaded from managed code, so the equivalent
// viewers are implemented inline here and selected by content type. PDF is unavailable
// because the QtPdf module is not part of the shipped Qt runtime.

// Read-write tree model for the JSON viewer, mirroring the official JsonItemModel:
// the document is parsed into a two-column (Key/Value) tree.
public class JsonTreeModel : QAbstractItemModel
{
    sealed class Node
    {
        public readonly int Id;
        public readonly string Key;
        public string Value;
        public readonly Node ParentItem;
        public readonly List<Node> Children = new();

        public Node(int id, string key, Node parent)
        {
            Id = id;
            Key = key;
            ParentItem = parent;
            parent?.Children.Add(this);
        }

        public int Row => ParentItem == null ? 0 : ParentItem.Children.IndexOf(this);
        public bool IsLeaf => Value != null;
    }

    const int DisplayRole = (int)Qt.ItemDataRole.DisplayRole;
    static readonly string[] headers = { "Key", "Value" };

    readonly List<Node> nodes = new();
    Node root;

    public JsonTreeModel(QObject parent = null) : base(parent) { }

    public void Load(string json)
    {
        BeginResetModel();
        nodes.Clear();
        root = NewNode(null, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement;
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                    BuildFrom(property.Name, property.Value, root);
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in element.EnumerateArray())
                    BuildFrom("[" + index++ + "]", item, root);
            }
            else
                NewNode("value", root).Value = FormatValue(element);
        }
        catch (JsonException ex)
        {
            NewNode("<parse error>", root).Value = ex.Message;
        }
        EndResetModel();
    }

    void BuildFrom(string key, JsonElement element, Node parent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var node = NewNode(key, parent);
                foreach (var property in element.EnumerateObject())
                    BuildFrom(property.Name, property.Value, node);
                break;
            }
            case JsonValueKind.Array:
            {
                var node = NewNode(key, parent);
                int index = 0;
                foreach (var item in element.EnumerateArray())
                    BuildFrom("[" + index++ + "]", item, node);
                break;
            }
            default:
                NewNode(key, parent).Value = FormatValue(element);
                break;
        }
    }

    static string FormatValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => "\"" + element.GetString() + "\"",
        JsonValueKind.Null => "null",
        _ => element.GetRawText(),
    };

    Node NewNode(string key, Node parent)
    {
        var node = new Node(nodes.Count, key, parent);
        nodes.Add(node);
        return node;
    }

    Node NodeAt(QModelIndex index)
    {
        if (index == null || !index.IsValid)
            return null;
        long id = (long)index.InternalId;
        return id >= 0 && id < nodes.Count ? nodes[(int)id] : null;
    }

    public override QModelIndex Index(int row, int column, QModelIndex parent)
    {
        if (!HasIndex(row, column, parent))
            return new QModelIndex();

        var parentItem = !parent.IsValid ? root : NodeAt(parent);
        if (parentItem == null || row < 0 || row >= parentItem.Children.Count)
            return new QModelIndex();

        return CreateIndex(row, column, (ulong)parentItem.Children[row].Id);
    }

    public override QModelIndex Index(int row, int column) => Index(row, column, new QModelIndex());

    public override QModelIndex parent(QModelIndex child)
    {
        if (!child.IsValid)
            return new QModelIndex();

        var parentItem = NodeAt(child)?.ParentItem;
        if (parentItem == null || parentItem == root)
            return new QModelIndex();

        return CreateIndex(parentItem.Row, 0, (ulong)parentItem.Id);
    }

    public override int RowCount(QModelIndex parent)
    {
        var item = !parent.IsValid ? root : NodeAt(parent);
        return item?.Children.Count ?? 0;
    }

    public override int RowCount() => RowCount(new QModelIndex());

    public override int ColumnCount(QModelIndex parent) => 2;

    public override int ColumnCount() => 2;

    public override QVariant Data(QModelIndex index, int role = DisplayRole)
    {
        if (!index.IsValid || role != DisplayRole)
            return new QVariant();

        var item = NodeAt(index);
        if (item == null)
            return new QVariant();

        if (index.Column == 0)
            return new QVariant(item.Key);
        return item.IsLeaf ? new QVariant(item.Value) : new QVariant();
    }

    public override QVariant HeaderData(int section, Qt.Orientation orientation, int role = DisplayRole)
    {
        if (orientation == Qt.Orientation.Horizontal && role == DisplayRole &&
            section >= 0 && section < headers.Length)
            return new QVariant(headers[section]);
        return new QVariant();
    }

    public override int Flags(QModelIndex index)
    {
        if (!index.IsValid)
            return (int)Qt.ItemFlag.NoItemFlags;
        return (int)Qt.ItemFlag.ItemIsEnabled | (int)Qt.ItemFlag.ItemIsSelectable;
    }
}

// Manages previously opened files, persisted through QSettings (official RecentFiles).
public class RecentFiles
{
    const int MaxFiles = 10;

    readonly List<string> files = new();

    public IReadOnlyList<string> Files => files;

    public event Action Changed;

    public void Restore(QSettings settings, string key)
    {
        files.Clear();
        string[] stored = settings.SettingsValue(key).ToStringList();
        if (stored == null)
            return;
        foreach (string fileName in stored)
            if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
                files.Add(fileName);
    }

    public void Save(QSettings settings, string key)
    {
        settings.SettingsSetValue(key, new QVariant(files.ToArray()));
    }

    public void Add(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            return;

        files.Remove(fileName);
        files.Insert(0, fileName);
        while (files.Count > MaxFiles)
            files.RemoveAt(files.Count - 1);

        Changed?.Invoke();
    }
}

public class DocumentViewerWindow : QMainWindow
{
    const string OrganizationName = "QtProject";
    const string ApplicationName = "DocumentViewer";
    const string SettingsWorkingDir = "WorkingDir";
    const string SettingsRecentFiles = "RecentFiles";

    readonly List<object> keepAlive = new();
    readonly RecentFiles recentFiles = new();

    string currentDir = Environment.CurrentDirectory;

    QScrollArea scrollArea;
    QTabWidget tabWidget;
    QMenu recentMenu;
    QStatusBar status;

    QAction openAct;
    QAction printAct;
    QAction backAct;
    QAction forwardAct;
    QAction aboutAct;
    QAction aboutQtAct;
    QAction exitAct;
    QAction cutAct;
    QAction copyAct;
    QAction pasteAct;

    QPlainTextEdit textViewer;
    QLabel imageLabel;
    QPixmap imagePixmap;
    int imageWidth;
    int imageHeight;
    double zoom = 1.0;

    public DocumentViewerWindow() : base(null, 0)
    {
        WindowTitle = "Document Viewer Demo";
        Resize(983, 602);

        BuildActions();
        BuildMenus();
        BuildToolBar();
        BuildCentralWidget();

        status = StatusBar;

        recentFiles.Changed += RebuildRecentMenu;
        ReadSettings();
        RebuildRecentMenu();

        LoadText("Open a text, JSON or image file with File > Open...");
        status.ShowMessage("Ready");
    }

    QAction MakeAction(string text, string shortcut)
    {
        var action = keep(new QAction(text, this));
        action.SetShortcutString(shortcut);
        return action;
    }

    void BuildActions()
    {
        openAct = MakeAction("&Open", "Ctrl+O");
        openAct.TriggeredSignal.Connect(_ => OnOpen());
        printAct = MakeAction("&Print", "Ctrl+P");
        printAct.SetDisabled(true); // no QtPrintSupport in the shipped runtime
        exitAct = MakeAction("E&xit", "Ctrl+Q");
        exitAct.TriggeredSignal.Connect(_ => Close());
        backAct = MakeAction("Back", "Left");
        backAct.TriggeredSignal.Connect(_ => ScrollBy(-1));
        forwardAct = MakeAction("Forward", "Right");
        forwardAct.TriggeredSignal.Connect(_ => ScrollBy(1));
        aboutAct = MakeAction("About Document Viewer", "Ctrl+H");
        aboutAct.TriggeredSignal.Connect(_ => QMessageBox.About(this, "About Document Viewer Demo", AboutText()));
        aboutQtAct = MakeAction("About Qt", "Ctrl+I");
        aboutQtAct.TriggeredSignal.Connect(_ => QMessageBox.AboutQt(this));

        cutAct = MakeAction("Cu&t", "Ctrl+X");
        cutAct.TriggeredSignal.Connect(_ => textViewer?.Cut());
        copyAct = MakeAction("&Copy", "Ctrl+C");
        copyAct.TriggeredSignal.Connect(_ => textViewer?.Copy());
        pasteAct = MakeAction("&Paste", "Ctrl+V");
        pasteAct.TriggeredSignal.Connect(_ => textViewer?.Paste());
    }

    void BuildMenus()
    {
        var menuBar = MenuBar;

        var fileMenu = menuBar.AddMenu("&File");
        fileMenu.AddAction(openAct);
        recentMenu = fileMenu.AddMenu("&Recently opened...");
        fileMenu.AddAction(printAct);
        fileMenu.AddAction(exitAct);

        var editMenu = menuBar.AddMenu("&Edit");
        editMenu.AddAction(cutAct);
        editMenu.AddAction(copyAct);
        editMenu.AddAction(pasteAct);

        var helpMenu = menuBar.AddMenu("Help");
        helpMenu.AddAction(aboutAct);
        helpMenu.AddAction(aboutQtAct);
    }

    void BuildToolBar()
    {
        var toolBar = AddToolBar("ToolBar");
        toolBar.AddAction(openAct);
        toolBar.AddAction(printAct);
        toolBar.AddSeparator();
        toolBar.AddAction(backAct);
        toolBar.AddAction(forwardAct);
    }

    void BuildCentralWidget()
    {
        tabWidget = keep(new QTabWidget());
        tabWidget.AddTab(keep(new QWidget()), "Pages");
        tabWidget.AddTab(keep(new QWidget()), "Bookmarks");

        scrollArea = keep(new QScrollArea());
        scrollArea.WidgetResizable = true;

        var splitter = keep(new QSplitter(Qt.Orientation.Horizontal));
        splitter.AddWidget(tabWidget);
        splitter.AddWidget(scrollArea);

        var viewArea = keep(new QWidget());
        var layout = keep(new QVBoxLayout());
        layout.AddWidget(splitter);
        viewArea.Layout = layout;

        CentralWidget = viewArea;
    }

    void RebuildRecentMenu()
    {
        recentMenu.Clear();
        recentMenu.Enabled = recentFiles.Files.Count > 0;
        foreach (string fileName in recentFiles.Files)
        {
            var path = fileName;
            var action = keep(new QAction(Path.GetFileName(path), this));
            action.TriggeredSignal.Connect(_ => OpenFile(path));
            recentMenu.AddAction(action);
        }
    }

    void OnOpen()
    {
        string path = QFileDialog.GetOpenFileName(
            this, "Open Document", currentDir,
            "Documents (*.txt *.json *.md *.html *.png *.jpg *.jpeg *.bmp *.gif);;All Files (*)");
        if (!string.IsNullOrEmpty(path))
            OpenFile(path);
    }

    public bool OpenFile(string fileName)
    {
        if (!File.Exists(fileName))
        {
            status.ShowMessage("File " + QDir.ToNativeSeparators(fileName) + " could not be opened.");
            return false;
        }

        currentDir = Path.GetDirectoryName(fileName);
        recentFiles.Add(fileName);
        return LoadDocument(fileName);
    }

    static bool IsImage(string extension) => extension switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".ico" => true,
        _ => false,
    };

    bool LoadDocument(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (IsImage(extension))
            return LoadImage(fileName);

        string text;
        try
        {
            text = File.ReadAllText(fileName);
        }
        catch (Exception ex)
        {
            status.ShowMessage("Cannot read file " + QDir.ToNativeSeparators(fileName) + ": " + ex.Message);
            return false;
        }

        string trimmed = text.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            LoadJson(text);
        else
            LoadText(text);

        status.ShowMessage("File " + QDir.ToNativeSeparators(fileName) + " loaded.");
        return true;
    }

    void LoadText(string text)
    {
        var editor = keep(new QPlainTextEdit());
        editor.SetPlainText(text);
        textViewer = editor;
        imageLabel = null;
        scrollArea.Widget = editor;
    }

    void LoadJson(string json)
    {
        var view = keep(new QTreeView());
        var model = keep(new JsonTreeModel(view));
        view.Model = model;
        model.Load(json);
        view.ExpandAll();
        textViewer = null;
        imageLabel = null;
        scrollArea.Widget = view;
    }

    bool LoadImage(string fileName)
    {
        var image = new QImage();
        if (!image.Load(fileName))
        {
            status.ShowMessage("Cannot read image " + QDir.ToNativeSeparators(fileName) + ".");
            return false;
        }

        imagePixmap = QtCompat.PixmapFromImage(image);
        imageWidth = image.Width;
        imageHeight = image.Height;
        zoom = 1.0;

        var label = keep(new QLabel());
        imageLabel = label;
        textViewer = null;
        ApplyZoom();

        scrollArea.Widget = label;
        status.ShowMessage("Image " + QDir.ToNativeSeparators(fileName) + " loaded (" +
                           imageWidth + "x" + imageHeight + ").");
        return true;
    }

    void ApplyZoom()
    {
        if (imageLabel == null || imagePixmap == null)
            return;

        int width = Math.Max(1, (int)(imageWidth * zoom));
        int height = Math.Max(1, (int)(imageHeight * zoom));
        var scaled = QtCompat.PixmapScaled(imagePixmap, width, height,
            Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation);
        keep(scaled);
        imageLabel.Pixmap = scaled;
        imageLabel.Resize(width, height);
    }

    void ScrollBy(int delta)
    {
        if (textViewer == null)
            return;

        var scrollBar = textViewer.VerticalScrollBar;
        int value = scrollBar.Value + delta;
        if (value < scrollBar.Minimum)
            value = scrollBar.Minimum;
        if (value > scrollBar.Maximum)
            value = scrollBar.Maximum;
        scrollBar.Value = value;
    }

    string AboutText()
    {
        return "A Widgets application to display and print JSON, text and PDF files.\n\n" +
               "This C# port inlines the text, JSON and image viewers instead of loading " +
               "native plugins; PDF is unavailable because the QtPdf module is not part " +
               "of the shipped Qt runtime.\n\n" +
               "Supported mime types:\ntext/plain\napplication/json\nimage/*";
    }

    void ReadSettings()
    {
        var settings = new QSettings(QSettings.Scope.UserScope, OrganizationName, ApplicationName);
        if (settings.SettingsContains(SettingsWorkingDir))
            currentDir = settings.SettingsValue(SettingsWorkingDir).ToString();
        recentFiles.Restore(settings, SettingsRecentFiles);
    }

    protected override void CloseEvent(QCloseEvent @event)
    {
        var settings = new QSettings(QSettings.Scope.UserScope, OrganizationName, ApplicationName);
        settings.SettingsSetValue(SettingsWorkingDir, currentDir);
        recentFiles.Save(settings, SettingsRecentFiles);
        settings.Sync();
    }

    T keep<T>(T obj) where T : class
    {
        keepAlive.Add(obj);
        return obj;
    }
}
