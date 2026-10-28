using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Port of the official Qt "Document Viewer" demo
// (qtdoc/examples/demos/documentviewer) onto the new pipeline.
//
// The official demo loads its text/JSON/image/PDF viewers as native plugins at runtime
// (QPluginLoader). Native plugins cannot be loaded from managed code, so the equivalent viewers
// are implemented inline here and selected by content type. PDF and printing stay unavailable
// because the shipped Qt runtime has neither Qt6Pdf.dll nor Qt6PrintSupport.dll.
//
// With the new pipeline there is no keepAlive list: a widget handed to Qt (a layout, a parent, a
// scroll area) is owned by Qt, and the generated destructor asks the QObject parent before
// deleting. The managed wrappers stay honest through the runtime's invalidation registry.

// A read-write tree model for the JSON viewer, mirroring the official JsonItemModel: the document
// is parsed into a two-column (Key/Value) tree. Building a *hierarchical* model means creating
// model indexes by hand, which is why QAbstractItemModel::createIndex and the
// beginResetModel/endResetModel pair are published as protected members on the new pipeline.
public sealed class JsonTreeModelApi : QAbstractItemModel
{
    sealed class Node
    {
        public readonly int Id;
        public readonly string Key;
        public string Value;
        public readonly Node ParentItem;
        public readonly List<Node> Children = new List<Node>();

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
    static readonly string[] Headers = { "Key", "Value" };

    readonly List<Node> nodes = new List<Node>();
    Node root;

    public JsonTreeModelApi() : base() { }

    /// <summary>Parses a JSON document and announces the whole-tree replacement.</summary>
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

    // The quote character is built without a backslash escape so this file survives any tool that
    // is picky about string literals.
    static string FormatValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return ((char)34).ToString() + element.GetString() + (char)34;
            case JsonValueKind.Null:
                return "null";
            default:
                return element.GetRawText();
        }
    }

    Node NewNode(string key, Node parent)
    {
        var node = new Node(nodes.Count, key, parent);
        nodes.Add(node);
        return node;
    }

    Node NodeAt(QModelIndex index)
    {
        if (!index.IsValid())
            return null;
        ulong id = index.InternalId();
        return id < (ulong)nodes.Count ? nodes[(int)id] : null;
    }

    public override QModelIndex Index(int row, int column, QModelIndex parent = default)
    {
        if (row < 0 || column < 0 || column >= 2)
            return new QModelIndex();

        var parentNode = !parent.IsValid() ? root : NodeAt(parent);
        if (parentNode == null || row >= parentNode.Children.Count)
            return new QModelIndex();

        return CreateIndex(row, column, (ulong)parentNode.Children[row].Id);
    }

    public override QModelIndex Parent(QModelIndex child)
    {
        if (!child.IsValid())
            return new QModelIndex();

        var parentNode = NodeAt(child)?.ParentItem;
        if (parentNode == null || parentNode == root)
            return new QModelIndex();

        return CreateIndex(parentNode.Row, 0, (ulong)parentNode.Id);
    }

    public override int RowCount(QModelIndex parent = default)
    {
        if (parent.IsValid() && parent.Column() > 0)
            return 0;
        var node = !parent.IsValid() ? root : NodeAt(parent);
        return node?.Children.Count ?? 0;
    }

    public override int ColumnCount(QModelIndex parent = default) => 2;

    public override object Data(QModelIndex index, int role = DisplayRole)
    {
        if (!index.IsValid() || role != DisplayRole)
            return null;

        var node = NodeAt(index);
        if (node == null)
            return null;

        if (index.Column() == 0)
            return node.Key;
        return node.IsLeaf ? node.Value : null;
    }

    public override object HeaderData(int section, Qt.Orientation orientation, int role = DisplayRole)
    {
        if (orientation == Qt.Orientation.Horizontal && role == DisplayRole
            && section >= 0 && section < Headers.Length)
            return Headers[section];
        return null;
    }

    public override Qt.ItemFlags Flags(QModelIndex index)
    {
        if (!index.IsValid())
            return Qt.ItemFlags.NoItemFlags;
        return Qt.ItemFlags.ItemIsEnabled | Qt.ItemFlags.ItemIsSelectable;
    }

    /// <summary>Number of nodes the last Load produced, root included (test hook).</summary>
    public int NodeCount => nodes.Count;
}

// Manages previously opened files, persisted through QSettings (the official ApiRecentFiles).
public sealed class ApiRecentFiles
{
    public const int MaxFiles = 10;

    readonly List<string> files = new List<string>();

    public IReadOnlyList<string> Files => files;

    public event Action Changed;

    public void Restore(QSettings settings, string key)
    {
        files.Clear();
        var stored = settings.Value(key) as string[];
        if (stored == null)
            return;
        foreach (string fileName in stored)
            if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
                files.Add(fileName);
    }

    public void Save(QSettings settings, string key)
        => settings.SetValue(key, files.ToArray());

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

// The main window skeleton and the text / JSON / image viewers.
public sealed class ApiDocumentViewerWindow : QMainWindow
{
    public const string OrganizationName = "QtSharpTests";
    public const string ApplicationName = "DocumentViewer";
    public const string SettingsWorkingDir = "WorkingDir";
    public const string SettingsApiRecentFiles = "ApiRecentFiles";

    readonly string settingsFile;
    readonly ApiRecentFiles recentFiles = new ApiRecentFiles();
    readonly List<QAction> recentActions = new List<QAction>();

    string currentDir = Environment.CurrentDirectory;

    public readonly QMenu FileMenu;
    public readonly QMenu EditMenu;
    public readonly QMenu HelpMenu;
    public readonly QMenu RecentMenu;
    public readonly QToolBar ToolBar;
    public readonly QTabWidget TabWidget;
    public readonly QScrollArea ScrollArea;
    public readonly QSplitter Splitter;

    public readonly QAction OpenAction;
    public readonly QAction PrintAction;
    public readonly QAction BackAction;
    public readonly QAction ForwardAction;
    public readonly QAction AboutAction;
    public readonly QAction AboutQtAction;
    public readonly QAction ExitAction;
    public readonly QAction CutAction;
    public readonly QAction CopyAction;
    public readonly QAction PasteAction;

    public QPlainTextEdit TextViewer { get; private set; }
    public QLabel ImageLabel { get; private set; }
    public QTreeView JsonViewer { get; private set; }
    public JsonTreeModelApi JsonModel { get; private set; }
    public QPixmap ImagePixmap { get; private set; }
    public QPixmap ScaledPixmap { get; private set; }
    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }
    public double Zoom { get; private set; } = 1.0;

    public string CurrentDirectory => currentDir;
    public ApiRecentFiles Recent => recentFiles;
    public IReadOnlyList<QAction> RecentActions => recentActions;

    // settingsFile == null falls back to the user-scope organization/application pair the official
    // demo uses; the tests point it at an ini file under the temporary directory so the harness
    // never touches real user settings.
    public ApiDocumentViewerWindow(string settingsFile = null) : base()
    {
        this.settingsFile = settingsFile;
        SetWindowTitle("Document Viewer Demo");
        Resize(983, 602);

        OpenAction = MakeAction("&Open", "Ctrl+O");
        OpenAction.TriggeredSignal.Connect(_ => OnOpen());
        PrintAction = MakeAction("&Print", "Ctrl+P");
        PrintAction.SetDisabled(true); // no QtPrintSupport in the shipped runtime
        ExitAction = MakeAction("E&xit", "Ctrl+Q");
        ExitAction.TriggeredSignal.Connect(_ => Close());
        BackAction = MakeAction("Back", "Left");
        BackAction.TriggeredSignal.Connect(_ => ScrollBy(-1));
        ForwardAction = MakeAction("Forward", "Right");
        ForwardAction.TriggeredSignal.Connect(_ => ScrollBy(1));
        AboutAction = MakeAction("About Document Viewer", "Ctrl+H");
        AboutAction.TriggeredSignal.Connect(_ => QMessageBox.About(this, "About Document Viewer Demo", AboutText()));
        AboutQtAction = MakeAction("About Qt", "Ctrl+I");
        AboutQtAction.TriggeredSignal.Connect(_ => QMessageBox.AboutQt(this));
        CutAction = MakeAction("Cu&t", "Ctrl+X");
        CutAction.TriggeredSignal.Connect(_ => TextViewer?.Cut());
        CopyAction = MakeAction("&Copy", "Ctrl+C");
        CopyAction.TriggeredSignal.Connect(_ => TextViewer?.Copy());
        PasteAction = MakeAction("&Paste", "Ctrl+V");
        PasteAction.TriggeredSignal.Connect(_ => TextViewer?.Paste());

        var menuBar = MenuBar();
        FileMenu = menuBar.AddMenu("&File");
        FileMenu.AddAction(OpenAction);
        RecentMenu = FileMenu.AddMenu("&Recently opened...");
        FileMenu.AddAction(PrintAction);
        FileMenu.AddAction(ExitAction);

        EditMenu = menuBar.AddMenu("&Edit");
        EditMenu.AddAction(CutAction);
        EditMenu.AddAction(CopyAction);
        EditMenu.AddAction(PasteAction);

        HelpMenu = menuBar.AddMenu("Help");
        HelpMenu.AddAction(AboutAction);
        HelpMenu.AddAction(AboutQtAction);

        ToolBar = AddToolBar("ToolBar");
        ToolBar.AddAction(OpenAction);
        ToolBar.AddAction(PrintAction);
        ToolBar.AddSeparator();
        ToolBar.AddAction(BackAction);
        ToolBar.AddAction(ForwardAction);

        TabWidget = new QTabWidget();
        TabWidget.AddTab(new QWidget(), "Pages");
        TabWidget.AddTab(new QWidget(), "Bookmarks");

        ScrollArea = new QScrollArea();
        ScrollArea.WidgetResizable = true;

        Splitter = new QSplitter(Qt.Orientation.Horizontal);
        Splitter.AddWidget(TabWidget);
        Splitter.AddWidget(ScrollArea);

        var viewArea = new QWidget();
        var layout = new QVBoxLayout();
        layout.AddWidget(Splitter);
        viewArea.SetLayout(layout);
        SetCentralWidget(viewArea);

        recentFiles.Changed += RebuildRecentMenu;
        ReadSettings();
        RebuildRecentMenu();

        LoadText("Open a text, JSON or image file with File > Open...");
        StatusBar().ShowMessage("Ready");
    }

    QAction MakeAction(string text, string shortcut)
    {
        var action = new QAction(text, this);
        if (!string.IsNullOrEmpty(shortcut))
        {
            using var sequence = new QKeySequence(shortcut);
            action.SetShortcut(sequence);
        }
        return action;
    }

    /// <summary>The QSettings the window reads and writes (see the constructor note).</summary>
    public QSettings MakeSettings()
        => settingsFile != null
            ? new QSettings(settingsFile, QSettings.Format.IniFormat)
            : new QSettings(QSettings.Scope.UserScope, OrganizationName, ApplicationName);

    public void ReadSettings()
    {
        using var settings = MakeSettings();
        if (settings.Contains(SettingsWorkingDir))
        {
            string stored = settings.Value(SettingsWorkingDir)?.ToString();
            if (!string.IsNullOrEmpty(stored))
                currentDir = stored;
        }
        recentFiles.Restore(settings, SettingsApiRecentFiles);
    }

    public void SaveSettings()
    {
        using var settings = MakeSettings();
        settings.SetValue(SettingsWorkingDir, currentDir);
        recentFiles.Save(settings, SettingsApiRecentFiles);
        settings.Sync();
    }

    void RebuildRecentMenu()
    {
        RecentMenu.Clear();
        recentActions.Clear();
        RecentMenu.Enabled = recentFiles.Files.Count > 0;
        foreach (string fileName in recentFiles.Files)
        {
            var path = fileName;
            var action = new QAction(Path.GetFileName(path), this);
            action.TriggeredSignal.Connect(_ => OpenFile(path));
            RecentMenu.AddAction(action);
            recentActions.Add(action);
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
            StatusBar().ShowMessage("File " + QDir.ToNativeSeparators(fileName) + " could not be opened.");
            return false;
        }

        string directory = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrEmpty(directory))
            currentDir = directory;
        recentFiles.Add(fileName);

        bool loaded = LoadDocument(fileName);
        if (loaded)
            SetWindowTitle(Path.GetFileName(fileName));
        return loaded;
    }

    static bool IsImage(string extension)
    {
        switch (extension)
        {
            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".bmp":
            case ".gif":
            case ".webp":
            case ".ico":
                return true;
            default:
                return false;
        }
    }

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
            StatusBar().ShowMessage("Cannot read file " + QDir.ToNativeSeparators(fileName) + ": " + ex.Message);
            return false;
        }

        string trimmed = text.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            LoadJson(text);
        else
            LoadText(text);

        StatusBar().ShowMessage("File " + QDir.ToNativeSeparators(fileName) + " loaded.");
        return true;
    }

    void LoadText(string text)
    {
        var editor = new QPlainTextEdit();
        editor.SetPlainText(text);
        TextViewer = editor;
        ImageLabel = null;
        JsonViewer = null;
        JsonModel = null;
        ScrollArea.SetWidget(editor);
        UpdateNavigation();
    }

    void LoadJson(string json)
    {
        var view = new QTreeView();
        var model = new JsonTreeModelApi();
        view.SetModel(model);
        model.Load(json);
        view.ExpandAll();
        TextViewer = null;
        ImageLabel = null;
        JsonViewer = view;
        JsonModel = model;
        ScrollArea.SetWidget(view);
        UpdateNavigation();
    }

    bool LoadImage(string fileName)
    {
        var image = new QImage();
        if (!image.Load(fileName))
        {
            StatusBar().ShowMessage("Cannot read image " + QDir.ToNativeSeparators(fileName) + ".");
            return false;
        }

        ImagePixmap = QPixmap.FromImage(image);
        ImageWidth = image.Width();
        ImageHeight = image.Height();
        Zoom = 1.0;

        var label = new QLabel();
        ImageLabel = label;
        TextViewer = null;
        JsonViewer = null;
        JsonModel = null;
        ApplyZoom();

        ScrollArea.SetWidget(label);
        StatusBar().ShowMessage("Image " + QDir.ToNativeSeparators(fileName) + " loaded ("
                                + ImageWidth + "x" + ImageHeight + ").");
        return true;
    }

    void ApplyZoom()
    {
        if (ImageLabel == null || ImagePixmap == null)
            return;

        int width = Math.Max(1, (int)(ImageWidth * Zoom));
        int height = Math.Max(1, (int)(ImageHeight * Zoom));
        ScaledPixmap = ImagePixmap.Scaled(width, height,
            Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation);
        ImageLabel.Pixmap = ScaledPixmap;
        ImageLabel.Resize(width, height);
    }

    public void ZoomIn()
    {
        if (ImagePixmap == null)
            return;
        Zoom = Math.Min(Zoom * 2.0, 4.0);
        ApplyZoom();
        UpdateNavigation();
    }

    public void ZoomOut()
    {
        if (ImagePixmap == null)
            return;
        Zoom = Math.Max(Zoom / 2.0, 0.25);
        ApplyZoom();
        UpdateNavigation();
    }

    public void ScrollBy(int delta)
    {
        if (TextViewer == null)
        {
            UpdateNavigation();
            return;
        }

        var scrollBar = TextViewer.VerticalScrollBar();
        int value = scrollBar.value() + delta;
        if (value < scrollBar.minimum())
            value = scrollBar.minimum();
        if (value > scrollBar.maximum())
            value = scrollBar.maximum();
        scrollBar.SetValue(value);
        UpdateNavigation();
    }

    /// <summary>Back/forward walk the text viewer's scroll range; both are disabled elsewhere.</summary>
    public void UpdateNavigation()
    {
        if (TextViewer == null)
        {
            BackAction.SetDisabled(true);
            ForwardAction.SetDisabled(true);
            return;
        }

        var scrollBar = TextViewer.VerticalScrollBar();
        BackAction.SetEnabled(scrollBar.value() > scrollBar.minimum());
        ForwardAction.SetEnabled(scrollBar.value() < scrollBar.maximum());
    }

    string AboutText()
    {
        return "A Widgets application to display and print JSON, text and PDF files.\n\n"
             + "This C# port inlines the text, JSON and image viewers instead of loading native "
             + "plugins; PDF is unavailable because the QtPdf module is not part of the shipped "
             + "Qt runtime.\n\nSupported mime types:\ntext/plain\napplication/json\nimage/*";
    }

    public override void CloseEvent(QCloseEvent @event)
    {
        SaveSettings();
        base.CloseEvent(@event);
    }
}

[Demo("qtwidgets-documentviewer")]
public static class DocumentViewer
{
    public static int Run(DemoContext demo)
    {

        string tempDir = Path.Combine(Path.GetTempPath(), "qtsharp-docviewer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string settingsFile = Path.Combine(tempDir, "docviewer.ini");

        var window = new ApiDocumentViewerWindow(settingsFile);

#if QTSHARP_DEMO_VIEW
        // The window as the user sees it, with the welcome text. The scenario below drives it
        // through files, scroll positions and the settings round-trip, which is the automated half.
        demo.Show(window);
        return 0;
#else

        // ---------------------------------------------------------------- the main window skeleton
#if QTSHARP_DEMO_SMOKE

        // ---------------------------------------------------------------- the main window skeleton
        demo.Check(window.WindowTitle == "Document Viewer Demo",
            $"the window title is the demo's (got '{window.WindowTitle}')");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.size().Width() == 983 && window.size().Height() == 602,
            $"the window starts at 983x602 (got {window.size().Width()}x{window.size().Height()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.CentralWidget() != null, "the central widget is installed");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.MenuBar().Actions().Length == 3, $"the menu bar has three menus (got {window.MenuBar().Actions().Length})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.FileMenu.title() == "&File", $"File menu title (got '{window.FileMenu.title()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.EditMenu.title() == "&Edit", $"Edit menu title (got '{window.EditMenu.title()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.HelpMenu.title() == "Help", $"Help menu title (got '{window.HelpMenu.title()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.RecentMenu.title() == "&Recently opened...", $"Recently opened submenu title (got '{window.RecentMenu.title()}')");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.FileMenu.Actions().Length == 4,
            $"File holds Open / Recently opened / Print / Exit (got {window.FileMenu.Actions().Length})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.EditMenu.Actions().Length == 3, $"Edit holds Cut / Copy / Paste (got {window.EditMenu.Actions().Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.HelpMenu.Actions().Length == 2, $"Help holds About / About Qt (got {window.HelpMenu.Actions().Length})");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.OpenAction.text() == "&Open" && window.OpenAction.Shortcut.ToString() == "Ctrl+O",
            $"Open action text + shortcut (got '{window.OpenAction.text()}' / '{window.OpenAction.Shortcut.ToString()}')");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.PrintAction.text() == "&Print" && window.PrintAction.Shortcut.ToString() == "Ctrl+P"
              && !window.PrintAction.IsEnabled(),
            $"Print action is present but disabled (enabled={window.PrintAction.IsEnabled()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ExitAction.text() == "E&xit" && window.ExitAction.Shortcut.ToString() == "Ctrl+Q",
            $"Exit action text + shortcut (got '{window.ExitAction.Shortcut.ToString()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.BackAction.text() == "Back" && window.BackAction.Shortcut.ToString() == "Left",
            $"Back action shortcut (got '{window.BackAction.Shortcut.ToString()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ForwardAction.text() == "Forward" && window.ForwardAction.Shortcut.ToString() == "Right",
            $"Forward action shortcut (got '{window.ForwardAction.Shortcut.ToString()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.AboutAction.text() == "About Document Viewer" && window.AboutAction.Shortcut.ToString() == "Ctrl+H",
            $"About action (got '{window.AboutAction.text()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.AboutQtAction.text() == "About Qt" && window.AboutQtAction.Shortcut.ToString() == "Ctrl+I",
            $"About Qt action (got '{window.AboutQtAction.text()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.CutAction.text() == "Cu&t" && window.CutAction.Shortcut.ToString() == "Ctrl+X"
              && window.CopyAction.text() == "&Copy" && window.CopyAction.Shortcut.ToString() == "Ctrl+C"
              && window.PasteAction.text() == "&Paste" && window.PasteAction.Shortcut.ToString() == "Ctrl+V",
            "Cut / Copy / Paste texts and shortcuts");
        #endif

        var toolActions = window.ToolBar.Actions();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ToolBar.windowTitle() == "ToolBar", $"the tool bar is named (got '{window.ToolBar.windowTitle()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(toolActions.Length == 5, $"the tool bar holds Open/Print/separator/Back/Forward (got {toolActions.Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(toolActions[0].text() == "&Open" && toolActions[1].text() == "&Print" && toolActions[2].IsSeparator()
              && toolActions[3].text() == "Back" && toolActions[4].text() == "Forward",
            "the tool bar action order and the separator");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.StatusBar().CurrentMessage() == "Ready",
            $"the status bar shows Ready (got '{window.StatusBar().CurrentMessage()}')");
#endif

        // ---------------------------------------------------------------- splitter / tabs / scroll
#if QTSHARP_DEMO_SMOKE

        // ---------------------------------------------------------------- splitter / tabs / scroll
        demo.Check(window.Splitter.orientation() == Qt.Orientation.Horizontal, "the splitter is horizontal");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.Splitter.Count() == 2, $"the splitter holds two panes (got {window.Splitter.Count()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.Splitter.Widget(0) is QTabWidget && window.Splitter.Widget(1) is QScrollArea,
            "the splitter's west pane is the tab widget and its east pane the scroll area");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.TabWidget.count() == 2 && window.TabWidget.TabText(0) == "Pages"
              && window.TabWidget.TabText(1) == "Bookmarks",
            $"the tab widget shows Pages and Bookmarks (got {window.TabWidget.count()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ScrollArea.WidgetResizable && window.ScrollArea.widgetResizable(),
            "the scroll area is widget-resizable");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ScrollArea.Widget() is QPlainTextEdit,
            "the initial viewer in the scroll area is the text viewer");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.TextViewer != null && window.TextViewer.ToPlainText().Contains("Open a text"),
            "the text viewer holds the welcome text");
        #endif

        // ---------------------------------------------------------------- QSettings basics
        using (var settings = new QSettings(settingsFile, QSettings.Format.IniFormat))
        {
            #if QTSHARP_DEMO_SMOKE
            demo.Check(settings.FileName().ToLowerInvariant().EndsWith("docviewer.ini"),
                $"an ini-file QSettings targets the temporary file (got '{settings.FileName()}')");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(settings.format() == QSettings.Format.IniFormat, "the settings object reports the ini format");
            #endif
        }

        using (var scoped = new QSettings(QSettings.Scope.UserScope, ApiDocumentViewerWindow.OrganizationName,
                                          ApiDocumentViewerWindow.ApplicationName))
        {
            #if QTSHARP_DEMO_SMOKE
            demo.Check(scoped.OrganizationName() == ApiDocumentViewerWindow.OrganizationName
                  && scoped.ApplicationName() == ApiDocumentViewerWindow.ApplicationName
                  && scoped.scope() == QSettings.Scope.UserScope,
                "the user-scope organization/application constructor works");
            #endif
        }

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.CurrentDirectory == Environment.CurrentDirectory,
            "a fresh ini leaves the working directory alone");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.Recent.Files.Count == 0, "no recent files on a fresh install");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!window.RecentMenu.Enabled, "the Recently opened submenu is disabled while empty");
        #endif

        // ---------------------------------------------------------------- open a text document
        string textFile = Path.Combine(tempDir, "sample.txt");
        File.WriteAllText(textFile, "hello document viewer");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.OpenFile(textFile), "opening a temporary text file succeeds");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.TextViewer != null && window.TextViewer.ToPlainText() == "hello document viewer",
            "the text viewer shows the file's contents");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ScrollArea.Widget() is QPlainTextEdit, "the scroll area was switched to the text viewer");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.WindowTitle == "sample.txt", $"the window title follows the file (got '{window.WindowTitle}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.StatusBar().CurrentMessage().Contains("loaded"),
            $"the status bar reports the load (got '{window.StatusBar().CurrentMessage()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.CurrentDirectory == tempDir, $"the working directory follows the file (got '{window.CurrentDirectory}')");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(!window.OpenFile(Path.Combine(tempDir, "missing.txt")),
            "opening a missing file is refused");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.StatusBar().CurrentMessage().Contains("could not be opened"),
            "the status bar reports the refusal");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.Recent.Files.Count == 1 && window.Recent.Files[0] == textFile,
            $"the opened file entered the recent list (got {window.Recent.Files.Count})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.RecentMenu.Enabled && window.RecentMenu.Actions().Length == 1,
            "the Recently opened submenu was rebuilt and enabled");
        #endif

        // ---------------------------------------------------------------- back / forward enablement
        string longFile = Path.Combine(tempDir, "long.txt");
        var longText = new System.Text.StringBuilder();
        for (int i = 0; i < 400; i++)
            longText.Append("line ").Append(i).Append(Environment.NewLine);
        File.WriteAllText(longFile, longText.ToString());
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.OpenFile(longFile), "a 400-line document opens");
        #endif

        var scrollBar = window.TextViewer.VerticalScrollBar();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollBar.maximum() > 0,
            $"the long document gives the text viewer a scroll range (max={scrollBar.maximum()})");
        #endif

        scrollBar.SetValue(scrollBar.maximum());
        window.UpdateNavigation();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.BackAction.IsEnabled() && !window.ForwardAction.IsEnabled(),
            "scrolled to the bottom: Back is enabled, Forward is not");
        #endif

        int beforeBack = scrollBar.value();
        window.BackAction.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollBar.value() == beforeBack - 1 && window.ForwardAction.IsEnabled(),
            $"the Back action's real handler stepped one line up (got {scrollBar.value()})");
        #endif

        scrollBar.SetValue(scrollBar.minimum());
        window.UpdateNavigation();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!window.BackAction.IsEnabled() && window.ForwardAction.IsEnabled(),
            "scrolled to the top: Forward is enabled, Back is not");
        #endif

        int beforeForward = scrollBar.value();
        window.ForwardAction.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollBar.value() == beforeForward + 1 && window.BackAction.IsEnabled(),
            $"the Forward action's real handler stepped one line down (got {scrollBar.value()})");
        #endif

        // A recent-file action drives the real OpenFile handler too.
        var newest = window.RecentActions[0];
        #if QTSHARP_DEMO_SMOKE
        demo.Check(newest.text() == "long.txt", $"the newest recent action is labelled with the file name (got '{newest.text()}')");
        #endif
        newest.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.TextViewer.ToPlainText().Contains("line 399"),
            "triggering the recent action reloaded the document through OpenFile");
        #endif

        // ---------------------------------------------------------------- the cap on recent entries
        for (int i = 0; i < 12; i++)
        {
            string capFile = Path.Combine(tempDir, "cap" + i + ".txt");
            File.WriteAllText(capFile, "cap " + i);
            window.OpenFile(capFile);
        }
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.Recent.Files.Count == ApiRecentFiles.MaxFiles,
            $"the recent list is capped at {ApiRecentFiles.MaxFiles} (got {window.Recent.Files.Count})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(Path.GetFileName(window.Recent.Files[0]) == "cap11.txt",
            $"the newest file is first (got '{Path.GetFileName(window.Recent.Files[0])}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(Path.GetFileName(window.Recent.Files[ApiRecentFiles.MaxFiles - 1]) == "cap2.txt",
            $"the oldest file kept is cap2.txt (got '{Path.GetFileName(window.Recent.Files[ApiRecentFiles.MaxFiles - 1])}')");
        #endif

        // ---------------------------------------------------------------- persistence round-trip
        window.SaveSettings();
        using (var settings = new QSettings(settingsFile, QSettings.Format.IniFormat))
        {
            #if QTSHARP_DEMO_SMOKE
            demo.Check(settings.Contains(ApiDocumentViewerWindow.SettingsWorkingDir),
                "the working directory was written to QSettings");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(settings.Value(ApiDocumentViewerWindow.SettingsWorkingDir)?.ToString() == tempDir,
                $"the working directory round-trips (got '{settings.Value(ApiDocumentViewerWindow.SettingsWorkingDir)}')");
            #endif

            var stored = settings.Value(ApiDocumentViewerWindow.SettingsApiRecentFiles) as string[];
            #if QTSHARP_DEMO_SMOKE
            demo.Check(stored != null && stored.Length == ApiRecentFiles.MaxFiles,
                $"the recent array round-trips {ApiRecentFiles.MaxFiles} entries (got {stored?.Length ?? 0})");
            #endif
            #if QTSHARP_DEMO_SMOKE
            demo.Check(stored != null && Path.GetFileName(stored[0]) == "cap11.txt",
                $"the persisted list keeps the order (got '{Path.GetFileName(stored?[0])}')");
            #endif
        }

        var restoredWindow = new ApiDocumentViewerWindow(settingsFile);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(restoredWindow.CurrentDirectory == tempDir,
            $"a second window restores the working directory (got '{restoredWindow.CurrentDirectory}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(restoredWindow.Recent.Files.Count == ApiRecentFiles.MaxFiles,
            $"a second window restores the recent list (got {restoredWindow.Recent.Files.Count})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(restoredWindow.RecentMenu.Enabled && restoredWindow.RecentMenu.Actions().Length == ApiRecentFiles.MaxFiles,
            "the restored window rebuilds an enabled Recently opened submenu");
        #endif

        // A file that no longer exists is filtered out during restore (the official ApiRecentFiles).
        File.Delete(Path.Combine(tempDir, "cap3.txt"));
        var thirdWindow = new ApiDocumentViewerWindow(settingsFile);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(thirdWindow.Recent.Files.Count == ApiRecentFiles.MaxFiles - 1,
            $"a deleted file is dropped on restore (got {thirdWindow.Recent.Files.Count})");
        #endif

        // ---------------------------------------------------------------- the JSON viewer
        string jsonFile = Path.Combine(tempDir, "doc.json");
        File.WriteAllText(jsonFile,
            "{" + Environment.NewLine +
            "  \"name\": \"qtsharp\"," + Environment.NewLine +
            "  \"count\": 7," + Environment.NewLine +
            "  \"nested\": { \"inner\": true, \"deep\": { \"x\": 1 } }," + Environment.NewLine +
            "  \"items\": [10, 20, 30]" + Environment.NewLine +
            "}");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.OpenFile(jsonFile), "opening a JSON document succeeds");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.JsonViewer is QTreeView && window.ScrollArea.Widget() is QTreeView,
            "the scroll area switched to the tree view");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.JsonModel != null, "the tree view is driven by the managed JSON model");
        #endif

        var model = window.JsonModel;
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.RowCount() == 4, $"the root has the four top-level keys (got {model.RowCount()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.ColumnCount() == 2, "the JSON model has the Key/Value columns");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.NodeCount == 1 + 4 + 2 + 1 + 3,
            $"the model allocated one node per JSON value (got {model.NodeCount})");
        #endif

        var nameKey = model.Index(0, 0);
        var nameValue = model.Index(0, 1);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Data(nameKey, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "name",
            $"row 0 column 0 is the key (got '{model.Data(nameKey, 0)}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Data(nameValue, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == ((char)34 + "qtsharp" + (char)34),
            $"row 0 column 1 is the quoted string value (got '{model.Data(nameValue, 0)}')");
        #endif

        var countValue = model.Index(1, 1);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Data(countValue, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "7",
            $"a number is rendered raw (got '{model.Data(countValue, 0)}')");
        #endif

        var nestedIndex = model.Index(2, 0);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Data(nestedIndex, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "nested",
            $"row 2 is the nested object (got '{model.Data(nestedIndex, 0)}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Data(model.Index(2, 1), (int)Qt.ItemDataRole.DisplayRole) == null,
            "a non-leaf object has no value in the Value column");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.RowCount(nestedIndex) == 2, $"nested holds inner and deep (got {model.RowCount(nestedIndex)})");
        #endif

        var deepIndex = model.Index(1, 0, nestedIndex);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Data(deepIndex, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "deep",
            $"walking a child index (got '{model.Data(deepIndex, 0)}')");
        #endif

        var deepParent = model.Parent(deepIndex);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(deepParent.IsValid() && deepParent.Row() == nestedIndex.Row()
              && deepParent.InternalId() == nestedIndex.InternalId(),
            $"an index walks back to its parent through the stored id (row {deepParent.Row()}, id {deepParent.InternalId()})");
        #endif

        var deepChild = model.Index(0, 1, deepIndex);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.RowCount(deepIndex) == 1
              && model.Data(deepChild, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "1",
            $"two levels down the value is 1 (got '{model.Data(deepChild, 0)}')");
        #endif

        var itemsIndex = model.Index(3, 0);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Data(itemsIndex, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "items"
              && model.RowCount(itemsIndex) == 3,
            $"the array has three elements (got {model.RowCount(itemsIndex)})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Data(model.Index(0, 0, itemsIndex), (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "[0]"
              && model.Data(model.Index(0, 1, itemsIndex), (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "10"
              && model.Data(model.Index(2, 1, itemsIndex), (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "30",
            "array elements are keyed [0],[1],[2] and carry their values");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(model.HeaderData(0, Qt.Orientation.Horizontal, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "Key"
              && model.HeaderData(1, Qt.Orientation.Horizontal, (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "Value",
            "the horizontal headers are Key and Value");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.HeaderData(0, Qt.Orientation.Vertical, (int)Qt.ItemDataRole.DisplayRole) == null,
            "a vertical header is not provided");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.HeaderData(0, Qt.Orientation.Horizontal, (int)Qt.ItemDataRole.ToolTipRole) == null,
            "a non-display role has no header data");
        #endif

        var validFlags = model.Flags(nameKey);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(validFlags == (Qt.ItemFlags.ItemIsEnabled | Qt.ItemFlags.ItemIsSelectable),
            $"a value row is enabled and selectable (got {validFlags})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.Flags(new QModelIndex()) == Qt.ItemFlags.NoItemFlags,
            "an invalid index carries no flags");
        #endif

        int rowsBeforeError = model.RowCount();
        model.Load("{ this is not json");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.RowCount() == 1
              && model.Data(model.Index(0, 0), (int)Qt.ItemDataRole.DisplayRole)?.ToString() == "<parse error>",
            "a broken document surfaces as a parse-error row");
        #endif
        model.Load(File.ReadAllText(jsonFile));
        #if QTSHARP_DEMO_SMOKE
        demo.Check(model.RowCount() == rowsBeforeError, "re-loading a valid document restores the tree");
        #endif

        // ---------------------------------------------------------------- the image viewer
        string imageFile = Path.Combine(tempDir, "pixels.png");
        bool saved;
        using (var image = new QImage(4, 3, QImage.Format.Format_ARGB32))
        {
            image.Fill(new QColor(10, 120, 200));
            saved = image.Save(imageFile);
        }
        #if QTSHARP_DEMO_SMOKE
        demo.Check(saved && File.Exists(imageFile), "a 4x3 PNG was generated on disk");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.OpenFile(imageFile), "opening the PNG succeeds");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ImageLabel != null && window.ScrollArea.Widget() is QLabel,
            "the scroll area switched to the image label");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ImagePixmap != null && !window.ImagePixmap.IsNull()
              && window.ImageWidth == 4 && window.ImageHeight == 3,
            $"the pixmap carries the image dimensions ({window.ImageWidth}x{window.ImageHeight})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ScaledPixmap != null && window.ScaledPixmap.Width() == 4 && window.ScaledPixmap.Height() == 3,
            "at 100 percent the scaled pixmap is the original size");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.Zoom == 1.0, $"the viewer starts at 100 percent (got {window.Zoom})");
        #endif

        window.ZoomIn();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.Zoom == 2.0 && window.ScaledPixmap.Width() == 8 && window.ScaledPixmap.Height() == 6,
            $"zoom-in doubles through QPixmap::scaled (got {window.ScaledPixmap.Width()}x{window.ScaledPixmap.Height()})");
        #endif
        window.ZoomOut();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.Zoom == 1.0 && window.ScaledPixmap.Width() == 4,
            "zoom-out returns to the original size");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ImageLabel.Pixmap != null && !window.ImageLabel.Pixmap.IsNull(),
            "the label actually holds the scaled pixmap");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.StatusBar().CurrentMessage().Contains("loaded (4x3)"),
            $"the status bar reports the image size (got '{window.StatusBar().CurrentMessage()}')");
#endif

        // Back/forward only apply to the text viewer, so an image disables both.
#if QTSHARP_DEMO_SMOKE

        // Back/forward only apply to the text viewer, so an image disables both.
        demo.Check(!window.BackAction.IsEnabled() && !window.ForwardAction.IsEnabled(),
            "back and forward are disabled while an image is shown");
#endif

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        return 0;
        #endif
#endif
    }
}
