using System;
using System.Collections.Generic;

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

// A managed widget that answers Qt's size hint from C#: the override goes through the generated
// native subclass, and Qt's own layout code reads it back (the Calculator example's CalcButton
// sizes itself the same way).
public sealed class SizedWidget : QWidget
{
    public override QSize SizeHint() => new QSize(123, 45);
    public override QSize MinimumSizeHint() => new QSize(60, 20);
}

public static class ApiWidgetsDemo
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

        var window = new ApiWidgetsWindow();
        window.Show();

        // ------------------------------------------------------------------ menus and bars
        Check(window.FileMenu.title() == "&File", $"menu has a title (got '{window.FileMenu.title()}')");
        Check(window.ExitAction.text() == "E&xit", $"action text round-trip (got '{window.ExitAction.text()}')");
        window.ExitAction.Trigger();
        Check(window.ExitTriggers == 1, $"action trigger reached the managed handler (got {window.ExitTriggers})");
        Check(window.StatusBar().CurrentMessage() == "ready", "status bar shows the message");

        // ------------------------------------------------------------------ inputs
        window.NameEdit.SetText("typed");
        Check(window.NameEdit.text() == "typed", $"line edit round-trip (got '{window.NameEdit.text()}')");

        window.CountSpin.SetValue(42);
        Check(window.CountSpin.value() == 42, $"spin box round-trip (got {window.CountSpin.value()})");
        Check(window.CountSpin.maximum() == 100, "spin box kept its range");

        window.LevelSlider.SetValue(70);
        Check(window.LevelSlider.value() == 70, $"slider round-trip (got {window.LevelSlider.value()})");
        Check(window.SliderChanges == 1, $"slider signal delivered once (got {window.SliderChanges})");

        window.BoldCheck.SetChecked(true);
        Check(window.BoldCheck.IsChecked(), "check box round-trip");
        window.LeftRadio.SetChecked(true);
        Check(window.LeftRadio.IsChecked() && !window.RightRadio.IsChecked(), "radio buttons are exclusive");

        window.ModeCombo.SetCurrentIndex(1);
        Check(window.ModeCombo.currentIndex() == 1 && window.ModeCombo.count() == 3,
            $"combo box index/count (got {window.ModeCombo.currentIndex()} of {window.ModeCombo.count()})");
        Check(window.ModeCombo.currentText() == "Password", $"combo box current text (got '{window.ModeCombo.currentText()}')");

        Check(window.FontCombo.currentFont().Family().Length > 0,
            $"font combo reports a family (got '{window.FontCombo.currentFont().Family()}')");

        // ------------------------------------------------------------------ items
        Check(window.List.count() == 3, $"list widget items (got {window.List.count()})");
        Check(window.List.Item(1).Text() == "item 1", $"list item text (got '{window.List.Item(1).Text()}')");
        window.List.SetCurrentRow(2);
        Check(window.List.currentRow() == 2, "list current row");

        Check(window.Tree.topLevelItemCount() == 1, $"tree top level (got {window.Tree.topLevelItemCount()})");
        var treeRoot = window.Tree.TopLevelItem(0);
        Check(treeRoot.Text(0) == "root" && treeRoot.Text(1) == "folder", "tree item text round-trip");
        Check(treeRoot.Child(0).Text(0) == "child", $"tree child text (got '{treeRoot.Child(0).Text(0)}')");
        Check(window.Tree.columnCount() == 2, "tree column count");

        Check(window.Table.rowCount() == 2 && window.Table.columnCount() == 2, "table dimensions");
        Check(window.Table.Item(0, 0).Text() == "a0", $"table item text (got '{window.Table.Item(0, 0).Text()}')");
        Check(window.Table.Item(1, 0).Text() == "b0", "table item text (second row)");

        // ------------------------------------------------------------------ text
        window.Text.SetPlainText("rich text");
        Check(window.Text.ToPlainText() == "rich text", $"text edit round-trip (got '{window.Text.ToPlainText()}')");
        window.Plain.SetPlainText("plain text");
        Check(window.Plain.ToPlainText() == "plain text", "plain text edit round-trip");

        // ------------------------------------------------------------------ tabs and splitters
        Check(window.Tabs.count() == 4, $"tab count (got {window.Tabs.count()})");
        window.Tabs.SetCurrentIndex(2);
        Check(window.Tabs.currentIndex() == 2, "tab selection");
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
        Check(proxy.sourceModel().RowCount() == 2, "proxy exposes its source model");

        // A QList<QStandardItem*> argument, and the QList<QModelIndex> that match() returns.
        model.AppendColumn(new[] { new QStandardItem("c0"), new QStandardItem("c1") });
        Check(model.ColumnCount() == 3, $"QList<QStandardItem*> argument grew the model (got {model.ColumnCount()})");
        // role 0 is Qt::DisplayRole.
        var matches = model.Match(model.Index(0, 0), 0, new QVariant("alpha"));
        Check(matches.Length == 1 && matches[0].Row() == 0,
            $"QList<QModelIndex> returned by match() (got {matches.Length} hit(s))");

        // ------------------------------------------------------------------ data widget mapper
        window.Mapper.ToFirst();
        Check(window.Mapper.currentIndex() == 0, $"mapper moved to the first row (got {window.Mapper.currentIndex()})");

        // ------------------------------------------------------------------ calendar and progress
        var calendar = window.Calendar;
        Check(calendar.selectedDate().Year() == 2024 && calendar.selectedDate().Day() == 15,
            $"calendar date round-trip (got {calendar.selectedDate().Year()}-{calendar.selectedDate().Month()}-{calendar.selectedDate().Day()})");
        window.Progress.SetValue(66);
        Check(window.Progress.value() == 66, $"progress bar round-trip (got {window.Progress.value()})");

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
        Check(!undo.canUndo() && undo.Count() == 0, "undo stack starts empty");
        undo.Undo();
        Check(undo.Index() == 0, "undo on an empty stack is a no-op");

        // ------------------------------------------------------------------ containers, C strings, meta-object
        // A QStringList argument: the managed array becomes a native QStringList for the call.
        var listCombo = new QComboBox();
        listCombo.AddItems(new[] { "one", "two", "three" });
        Check(listCombo.count() == 3, $"string[] argument reached addItems (got {listCombo.count()})");

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

        // ------------------------------------------------------------------ a managed widget's size hint
        var sized = new SizedWidget();
        Check(sized.SizeHint().Width() == 123, $"managed sizeHint override (got {sized.SizeHint().Width()})");
        Check(sized.MinimumSizeHint().Height() == 20, "managed minimumSizeHint override");
        sized.AdjustSize();
        Check(sized.size().Width() == 123 && sized.size().Height() == 45,
            $"Qt sized the widget from the override (got {sized.size().Width()}x{sized.size().Height()})");

        // ------------------------------------------------------------------ byte buffers
        // A byte[] argument crosses as a QByteArray (which the QByteArrayView parameter takes
        // implicitly), and a QByteArrayView return is copied out as a byte[].
        var payload = new QByteArray("hello");
        payload.Append(new byte[] { (byte)'!' });
        Check(payload.Size() == 6, $"byte[] argument reached append (got {payload.Size()})");
        Check(payload.Compare(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', (byte)'!' }) == 0,
            "byte[] argument reached compare");
        var methodName = meta.Method(0).NameView();
        Check(methodName != null && methodName.Length > 0,
            $"QByteArrayView return (got {methodName?.Length ?? 0} byte(s))");

        // ------------------------------------------------------------------ out parameters
        // Qt's "one extra output" convention is a non-const pointer, which surfaces as a C# `out`.
        var rgb = new QColor(255, 128, 0);
        rgb.GetRgb(out int outR, out int outG, out int outB);
        Check(outR == 255 && outG == 128 && outB == 0,
            $"QColor::getRgb out parameters (got {outR},{outG},{outB})");
        rgb.GetHsvF(out float hue, out float sat, out float val);
        Check(hue >= 0 && sat == 1 && val == 1,
            $"QColor::getHsvF out floats (got {hue:F3},{sat:F3},{val:F3})");

        var rect = new QRect(1, 2, 3, 4);
        rect.GetRect(out int rx, out int ry, out int rw, out int rh);
        Check(rx == 1 && ry == 2 && rw == 3 && rh == 4,
            $"QRect::getRect out parameters (got {rx},{ry},{rw},{rh})");

        // A qreal out parameter is a double on the wire.
        var fractional = new QRectF(0.5, 1.5, 2.5, 3.5);
        fractional.GetCoords(out double fx1, out double fy1, out double fx2, out double fy2);
        Check(fx1 == 0.5 && fy1 == 1.5 && fx2 == 3 && fy2 == 5,
            $"QRectF::getCoords out doubles (got {fx1},{fy1},{fx2},{fy2})");

        var date = new QDate(2024, 3, 15);
        date.GetDate(out int dy, out int dm, out int dd);
        Check(dy == 2024 && dm == 3 && dd == 15, $"QDate::getDate out parameters (got {dy}-{dm}-{dd})");

        var transform = QTransform.FromScale(2, 3);
        transform.Map(1.0, 1.0, out double tx, out double ty);
        Check(tx == 2 && ty == 3, $"QTransform::map out parameters (got {tx},{ty})");

        // QLayout's four margins come back through non-const int pointers.
        var boxedLayout = new QVBoxLayout();
        boxedLayout.GetContentsMargins(out int mleft, out int mtop, out int mright, out int mbottom);
        Check(mleft >= 0 && mtop >= 0 && mright >= 0 && mbottom >= 0,
            $"QLayout::getContentsMargins out ints (got {mleft},{mtop},{mright},{mbottom})");

        // ------------------------------------------------------------------ QHash<K, V>
        // QHash is unordered, but it surfaces as the same managed Dictionary (the value here is
        // a QByteArray, which is a boxed class like any other).
        var roleNames = model.RoleNames();
        Check(roleNames.Count > 0, $"QHash<int, QByteArray> return (got {roleNames.Count} role name(s))");
        Check(roleNames.TryGetValue(0, out var displayRole) && displayRole.ConstData() == "display",
            $"roleNames()[DisplayRole] (got '{displayRole?.ConstData()}')");

        // QVariant carries the unordered container too.
        var hashVariant = new QVariant(new Dictionary<string, QVariant> { ["n"] = new QVariant(3) });
        var hash = hashVariant.ToHash();
        Check(hash.Count == 1 && hash["n"].ToInt() == 3, $"QVariant::toHash round-trip (got {hash.Count})");

        // ------------------------------------------------------------------ QMap<int, QVariant>
        // The role map a view hands a model: a managed Dictionary becomes a native QMap for the
        // call, and the map the model hands back is copied out into a Dictionary.
        var mapModel = new QStandardItemModel(1, 1);
        var mapIndex = mapModel.Index(0, 0);
        var roles = new Dictionary<int, QVariant>
        {
            [0] = new QVariant("from-map"),     // Qt::DisplayRole
            [3] = new QVariant("tool tip"),     // Qt::ToolTipRole
        };
        Check(mapModel.SetItemData(mapIndex, roles), "Dictionary<int, QVariant> argument reached setItemData");
        var readBack = mapModel.ItemData(mapIndex);
        Check(readBack.Count == 2 && readBack[0].ToString() == "from-map" && readBack[3].ToString() == "tool tip",
            $"Dictionary<int, QVariant> return (got {readBack.Count} role(s): {string.Join(",", readBack.Keys)})");

        // ------------------------------------------------------------------ QBindable<T>
        // The handle Qt's property-binding API hands out (bindableFoo()): a generic wrapper that
        // reads and writes the property through the codec generated for its value type.
        var boundTimer = new QTimer();
        using (var interval = boundTimer.BindableInterval())
        {
            interval.Value = 250;
            Check(interval.Value == 250 && boundTimer.interval() == 250,
                $"QBindable<int> round-trip (got {interval.Value} / {boundTimer.interval()})");
        }
        using (var active = boundTimer.BindableActive())
            Check(!active.IsDisposed && !active.Value, "QBindable<bool> reads the property");
        using (var timerType = boundTimer.BindableTimerType())
            Check(timerType.Value == Qt.TimerType.CoarseTimer,
                $"QBindable<Qt.TimerType> reads the enum default (got {timerType.Value})");
        using (var name = window.BindableObjectName())
        {
            name.Value = "renamed";
            Check(name.Value == "renamed" && window.objectName() == "renamed",
                $"QBindable<string> round-trip (got '{name.Value}')");
        }

        // QVariant itself carries a QMap<QString, QVariant> (QVariantMap).
        var variantMap = new QVariant(new Dictionary<string, QVariant>
        {
            ["name"] = new QVariant("qtsharp"),
            ["count"] = new QVariant(7),
        });
        var roundTripped = variantMap.ToMap();
        Check(roundTripped.Count == 2 && roundTripped["name"].ToString() == "qtsharp"
              && roundTripped["count"].ToInt() == 7,
            $"QVariantMap round-trip (got {roundTripped.Count} entries)");

        // ------------------------------------------------------------------ Q_PROPERTY properties
        // The properties are sugar over the accessors that were generated anyway: reading one
        // calls the getter, assigning one calls the setter, and the accessors keep working under
        // their own (C++) names.
        var propertyEdit = new QLineEdit();
        propertyEdit.Text = "property";
        Check(propertyEdit.Text == "property" && propertyEdit.text() == "property",
            $"QLineEdit.Text property round-trip (got '{propertyEdit.Text}')");
        Check(propertyEdit.DisplayText == "property", "a read-only property (displayText)");
        propertyEdit.ReadOnly = true;
        Check(propertyEdit.ReadOnly && propertyEdit.IsReadOnly(),
            "a bool property whose getter is isReadOnly()");
        propertyEdit.Frame = false;
        Check(!propertyEdit.Frame && !propertyEdit.HasFrame(),
            "a bool property whose getter is hasFrame()");
        propertyEdit.MaxLength = 8;
        Check(propertyEdit.MaxLength == 8, $"an int property (got {propertyEdit.MaxLength})");
        propertyEdit.Alignment = Qt.Alignment.AlignRight;
        Check(propertyEdit.alignment() == Qt.Alignment.AlignRight, "a flag property");

        // echoMode has no property: its name would collide with the nested EchoMode enum, so the
        // accessor stays the only way in.
        propertyEdit.SetEchoMode(QLineEdit.EchoMode.Password);
        Check(propertyEdit.echoMode() == QLineEdit.EchoMode.Password, "echoMode stays a method");

        // The NOTIFY signal is still the signal property, not a C# event.
        int textChanges = 0;
        propertyEdit.TextChangedSignal.Connect(_ => textChanges++);
        propertyEdit.Text = "changed";
        Check(textChanges == 1, $"the NOTIFY signal still fires (got {textChanges})");

        var propertyTimer = new QTimer();
        propertyTimer.Interval = 500;
        Check(propertyTimer.Interval == 500 && propertyTimer.interval() == 500,
            $"QTimer.Interval property (got {propertyTimer.Interval})");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
