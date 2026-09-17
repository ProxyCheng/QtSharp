using System;
using System.Collections.Generic;
using QtSharp.Api;

// A QtWidgets tour on the new pipeline: one window that touches most of the widget families the
// model now covers - menus, tool bars, status bar, tabs, splitters, buttons, numeric input, item
// views, a model/view/proxy chain, a data-widget mapper, a calendar, a dialog button box and an
// undo stack. Everything comes from model/qtsharp.api.json; nothing here is hand-written glue.
//
// The point is not the layout: Run() drives each widget through the generated API and checks the
// state that action should have produced, so a missing or mis-marshalled member shows up as a
// FAIL instead of a silent difference.
public sealed class ApiWidgetsWindow : QMainWindow
{
    public readonly QTabWidget Tabs = new QTabWidget();
    public readonly QProgressBar Progress = new QProgressBar();

    public readonly QMenu FileMenu;
    public readonly QAction ExitAction;
    public readonly QToolBar ToolBar;
    public readonly QAction ToolAction;

    public readonly QLineEdit NameEdit = new QLineEdit();
    public readonly QSpinBox CountSpin = new QSpinBox();
    public readonly QSlider LevelSlider = new QSlider(Qt.Orientation.Horizontal);
    public readonly QCheckBox BoldCheck = new QCheckBox("Bold");
    public readonly QRadioButton LeftRadio = new QRadioButton("Left");
    public readonly QRadioButton RightRadio = new QRadioButton("Right");
    public readonly QComboBox ModeCombo = new QComboBox();
    public readonly QFontComboBox FontCombo = new QFontComboBox();

    public readonly QListWidget List = new QListWidget();
    public readonly QTreeWidget Tree = new QTreeWidget();
    public readonly QTableWidget Table = new QTableWidget();

    public readonly QTextEdit Text = new QTextEdit();
    public readonly QPlainTextEdit Plain = new QPlainTextEdit();

    public int SliderChanges;
    public int ExitTriggers;

    public ApiWidgetsWindow()
    {
        SetWindowTitle("Widgets (new pipeline)");

        // ---------------------------------------------------------------- menus and bars
        FileMenu = MenuBar().AddMenu("&File");
        ExitAction = FileMenu.AddAction("E&xit");
        ExitAction.TriggeredSignal.Connect(_ => ExitTriggers++);
        FileMenu.AddSeparator();
        FileMenu.AddAction("&New");

        var help = MenuBar().AddMenu("&Help");
        help.AddAction("&About");

        ToolBar = AddToolBar("Main");
        ToolAction = ToolBar.AddAction("Run");
        ToolBar.AddAction("Stop");

        StatusBar().AddPermanentWidget(Progress, 1);
        Progress.SetRange(0, 100);
        StatusBar().ShowMessage("ready");

        // ---------------------------------------------------------------- inputs tab
        var inputs = new QWidget();
        var form = new QFormLayout(inputs);
        form.AddRow("Name:", NameEdit);
        CountSpin.SetRange(0, 100);
        form.AddRow("Count:", CountSpin);
        form.AddRow("Level:", LevelSlider);
        form.AddRow("Bold:", BoldCheck);
        ModeCombo.AddItem("Normal");
        ModeCombo.AddItem("Password");
        ModeCombo.AddItem("Read only");
        form.AddRow("Mode:", ModeCombo);
        form.AddRow("Font:", FontCombo);
        LevelSlider.ValueChangedSignal.Connect(_ => SliderChanges++);
        new QButtonGroup(inputs);
        LeftRadio.SetChecked(true);
        form.AddRow("Align:", LeftRadio);
        form.AddRow("", RightRadio);

        var calendar = new QCalendarWidget();
        calendar.SetSelectedDate(new QDate(2024, 3, 15));
        form.AddRow("Date:", calendar);

        // ---------------------------------------------------------------- items tab
        var items = new QSplitter(Qt.Orientation.Horizontal);
        for (int i = 0; i < 3; i++)
            List.AddItem("item " + i);
        Tree.SetColumnCount(2);
        Tree.SetHeaderLabel("Files");
        var root = new QTreeWidgetItem();
        root.SetText(0, "root");
        root.SetText(1, "folder");
        root.AddChild(new QTreeWidgetItem());
        root.Child(0).SetText(0, "child");
        Tree.AddTopLevelItem(root);
        Table.SetRowCount(2);
        Table.SetColumnCount(2);
        Table.SetItem(0, 0, new QTableWidgetItem("a0"));
        Table.SetItem(0, 1, new QTableWidgetItem("a1"));
        Table.SetItem(1, 0, new QTableWidgetItem("b0"));
        items.AddWidget(List);
        items.AddWidget(Tree);
        items.AddWidget(Table);

        // ---------------------------------------------------------------- text tab
        var texts = new QSplitter(Qt.Orientation.Vertical);
        texts.AddWidget(Text);
        texts.AddWidget(Plain);

        // ---------------------------------------------------------------- assemble
        Tabs.AddTab(inputs, "Inputs");
        Tabs.AddTab(items, "Items");
        Tabs.AddTab(texts, "Text");
        Tabs.AddTab(calendar, "Calendar");
        Tabs.SetTabPosition(QTabWidget.TabPosition.North);
        SetCentralWidget(Tabs);

        // ---------------------------------------------------------------- model/view
        Model = new QStandardItemModel(2, 2);
        Model.SetItem(0, 0, new QStandardItem("alpha"));
        Model.SetItem(0, 1, new QStandardItem("beta"));
        Model.SetItem(1, 0, new QStandardItem("gamma"));
        Model.SetItem(1, 1, new QStandardItem("delta"));

        View = new QTableView();
        View.SetModel(Model);

        Proxy = new QSortFilterProxyModel();
        Proxy.SetSourceModel(Model);
        Proxy.SetFilterFixedString("a");
        ProxyView = new QTableView();
        ProxyView.SetModel(Proxy);

        Mapper = new QDataWidgetMapper();
        Mapper.SetModel(Model);
        Mapper.AddMapping(NameEdit, 0);
    }

    public readonly QStandardItemModel Model;
    public readonly QTableView View;
    public readonly QSortFilterProxyModel Proxy;
    public readonly QTableView ProxyView;
    public readonly QDataWidgetMapper Mapper;

    public QCalendarWidget Calendar => (QCalendarWidget)Tabs.Widget(3);
}

public static class ApiWidgetsDemo
{
    public static int Run()
    {
        ApiRuntime.StartApplication();

        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var window = new ApiWidgetsWindow();
        window.Show();

        // ------------------------------------------------------------------ menus and bars
        Check(window.FileMenu.Title() == "&File", $"menu has a title (got '{window.FileMenu.Title()}')");
        Check(window.ExitAction.Text() == "E&xit", $"action text round-trip (got '{window.ExitAction.Text()}')");
        window.ExitAction.Trigger();
        Check(window.ExitTriggers == 1, $"action trigger reached the managed handler (got {window.ExitTriggers})");
        Check(window.StatusBar().CurrentMessage() == "ready", "status bar shows the message");

        // ------------------------------------------------------------------ inputs
        window.NameEdit.SetText("typed");
        Check(window.NameEdit.Text() == "typed", $"line edit round-trip (got '{window.NameEdit.Text()}')");

        window.CountSpin.SetValue(42);
        Check(window.CountSpin.Value() == 42, $"spin box round-trip (got {window.CountSpin.Value()})");
        Check(window.CountSpin.Maximum() == 100, "spin box kept its range");

        window.LevelSlider.SetValue(70);
        Check(window.LevelSlider.Value() == 70, $"slider round-trip (got {window.LevelSlider.Value()})");
        Check(window.SliderChanges == 1, $"slider signal delivered once (got {window.SliderChanges})");

        window.BoldCheck.SetChecked(true);
        Check(window.BoldCheck.IsChecked(), "check box round-trip");
        window.LeftRadio.SetChecked(true);
        Check(window.LeftRadio.IsChecked() && !window.RightRadio.IsChecked(), "radio buttons are exclusive");

        window.ModeCombo.SetCurrentIndex(1);
        Check(window.ModeCombo.CurrentIndex() == 1 && window.ModeCombo.Count() == 3,
            $"combo box index/count (got {window.ModeCombo.CurrentIndex()} of {window.ModeCombo.Count()})");
        Check(window.ModeCombo.CurrentText() == "Password", $"combo box current text (got '{window.ModeCombo.CurrentText()}')");

        Check(window.FontCombo.CurrentFont().Family().Length > 0,
            $"font combo reports a family (got '{window.FontCombo.CurrentFont().Family()}')");

        // ------------------------------------------------------------------ items
        Check(window.List.Count() == 3, $"list widget items (got {window.List.Count()})");
        Check(window.List.Item(1).Text() == "item 1", $"list item text (got '{window.List.Item(1).Text()}')");
        window.List.SetCurrentRow(2);
        Check(window.List.CurrentRow() == 2, "list current row");

        Check(window.Tree.TopLevelItemCount() == 1, $"tree top level (got {window.Tree.TopLevelItemCount()})");
        var treeRoot = window.Tree.TopLevelItem(0);
        Check(treeRoot.Text(0) == "root" && treeRoot.Text(1) == "folder", "tree item text round-trip");
        Check(treeRoot.Child(0).Text(0) == "child", $"tree child text (got '{treeRoot.Child(0).Text(0)}')");
        Check(window.Tree.ColumnCount() == 2, "tree column count");

        Check(window.Table.RowCount() == 2 && window.Table.ColumnCount() == 2, "table dimensions");
        Check(window.Table.Item(0, 0).Text() == "a0", $"table item text (got '{window.Table.Item(0, 0).Text()}')");
        Check(window.Table.Item(1, 0).Text() == "b0", "table item text (second row)");

        // ------------------------------------------------------------------ text
        window.Text.SetPlainText("rich text");
        Check(window.Text.ToPlainText() == "rich text", $"text edit round-trip (got '{window.Text.ToPlainText()}')");
        window.Plain.SetPlainText("plain text");
        Check(window.Plain.ToPlainText() == "plain text", "plain text edit round-trip");

        // ------------------------------------------------------------------ tabs and splitters
        Check(window.Tabs.Count() == 4, $"tab count (got {window.Tabs.Count()})");
        window.Tabs.SetCurrentIndex(2);
        Check(window.Tabs.CurrentIndex() == 2, "tab selection");
        Check(window.Tabs.TabText(1) == "Items", $"tab text (got '{window.Tabs.TabText(1)}')");
        var splitter = (QSplitter)window.Tabs.Widget(1);
        Check(splitter.Count() == 3, $"splitter children (got {splitter.Count()})");

        // ------------------------------------------------------------------ model / view / proxy
        var model = window.Model;
        Check(model.RowCount() == 2 && model.ColumnCount() == 2, "model dimensions");
        Check(model.Item(0, 0).Text() == "alpha", $"model item text (got '{model.Item(0, 0).Text()}')");
        Check(model.Index(0, 1).IsValid() && model.Index(0, 1).Column() == 1,
            "QModelIndex from the model");
        Check(model.Data(model.Index(1, 0)) != null, "model data through QVariant");
        Check(model.Index(0, 0).Data().ToString() == "alpha", $"QVariant carries the text (got '{model.Index(0, 0).Data().ToString()}')");

        var proxy = window.Proxy;
        Check(proxy.RowCount() == 2, $"proxy row count before filtering (got {proxy.RowCount()})");
        proxy.SetFilterFixedString("a");
        Check(proxy.SourceModel().RowCount() == 2, "proxy exposes its source model");

        // A QList<QStandardItem*> argument, and the QList<QModelIndex> that match() returns.
        model.AppendColumn(new[] { new QStandardItem("c0"), new QStandardItem("c1") });
        Check(model.ColumnCount() == 3, $"QList<QStandardItem*> argument grew the model (got {model.ColumnCount()})");
        // role 0 is Qt::DisplayRole.
        var matches = model.Match(model.Index(0, 0), 0, new QVariant("alpha"));
        Check(matches.Length == 1 && matches[0].Row() == 0,
            $"QList<QModelIndex> returned by match() (got {matches.Length} hit(s))");

        // ------------------------------------------------------------------ data widget mapper
        window.Mapper.ToFirst();
        Check(window.Mapper.CurrentIndex() == 0, $"mapper moved to the first row (got {window.Mapper.CurrentIndex()})");

        // ------------------------------------------------------------------ calendar and progress
        var calendar = window.Calendar;
        Check(calendar.SelectedDate().Year() == 2024 && calendar.SelectedDate().Day() == 15,
            $"calendar date round-trip (got {calendar.SelectedDate().Year()}-{calendar.SelectedDate().Month()}-{calendar.SelectedDate().Day()})");
        window.Progress.SetValue(66);
        Check(window.Progress.Value() == 66, $"progress bar round-trip (got {window.Progress.Value()})");

        // ------------------------------------------------------------------ dialog button box
        var dialog = new QDialog();
        var buttons = new QDialogButtonBox(
            QDialogButtonBox.StandardButtons.Ok | QDialogButtonBox.StandardButtons.Cancel);
        buttons.AcceptedSignal.Connect(() => dialog.Accept());
        int rejected = 0;
        buttons.RejectedSignal.Connect(() => rejected++);
        dialog.SetWindowTitle("Dialog");
        buttons.Button(QDialogButtonBox.StandardButton.Cancel).Click();
        Check(rejected == 1, $"cancel button emitted rejected (got {rejected})");

        // ------------------------------------------------------------------ completer and undo
        var completer = new QCompleter(model);
        completer.SetCaseSensitivity(Qt.CaseSensitivity.CaseInsensitive);
        completer.SetWidget(window.NameEdit);   // complete() needs a widget to pop up against
        completer.SetCompletionPrefix("al");
        completer.Complete();
        Check(completer.CompletionCount() == 1, $"completer matched one entry (got {completer.CompletionCount()})");

        var undo = new QUndoStack();
        Check(!undo.CanUndo() && undo.Count() == 0, "undo stack starts empty");
        undo.Undo();
        Check(undo.Index() == 0, "undo on an empty stack is a no-op");

        // ------------------------------------------------------------------ containers, C strings, meta-object
        // A QStringList argument: the managed array becomes a native QStringList for the call.
        var listCombo = new QComboBox();
        listCombo.AddItems(new[] { "one", "two", "three" });
        Check(listCombo.Count() == 3, $"string[] argument reached addItems (got {listCombo.Count()})");

        // ...and a QStringList return: the native list is copied into a managed array.
        var families = QFontDatabase.Families();
        Check(families.Length > 0, $"string[] return carried the font families (got {families.Length})");

        // A const char* argument and a qsizetype return on the same call.
        var bytes = new QByteArray("hello");
        Check(bytes.Size() == 5, $"const char* argument + qsizetype return (got {bytes.Size()})");

        // The meta-object is a bound class now, and className() is a const char* return.
        var meta = window.MetaObject();
        Check(meta.ClassName() == "QMainWindow", $"metaObject().className() (got '{meta.ClassName()}')");
        Check(meta.IndexOfSignal("destroyed(QObject*)") >= 0,
            $"the meta-object resolves a signal signature (got {meta.IndexOfSignal("destroyed(QObject*)")})");

        // QCalendar is a value class with QDate in and out.
        var cal = new QCalendar();
        Check(cal.DateFromParts(2024, 3, 15).Day() == 15, "QCalendar round-trip through QDate");

        // QMimeData: a QObject whose payload is a string.
        var mime = new QMimeData();
        mime.SetText("payload");
        Check(mime.HasText() && mime.Text() == "payload", $"QMimeData text round-trip (got '{mime.Text()}')");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
