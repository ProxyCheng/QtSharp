using System;

// The official examples/widgets/gallery (Qt Widget Gallery) ported onto the *new* pipeline.
// The legacy adapter (testbind/WidgetsGallery.cs) is the structural reference: a QDialog with a
// Help/Close button box at the bottom, a top row with the style combo box + "Disable widgets"
// check box + help label, and the Buttons / Item Views / Simple Input Widgets / Text Controls /
// Progress Bar panels the legacy version assembled.
//
// Differences from the C++ original, all forced by what the model can express today:
//   * QStyleFactory is not in the model (see the note on StyleNames below), so the style list is
//     hard-coded - exactly what the legacy adapter did;
//   * QCommandLinkButton stays out (it never worked in the old binding and the official gallery
//     does not use it either);
//   * the style combo is driven through currentTextChanged rather than textActivated, because
//     textActivated is only emitted for real user interaction, which a self-test cannot produce
//     (the handler itself is unchanged);
//   * the tree/table/list widgets are added to the tab widget directly instead of being wrapped in
//     an extra margin widget; the wrapped widget carried no state worth reproducing.
public sealed class ApiWidgetsGalleryWindow : QDialog
{
    // QStyleFactory::keys() is not bound (QStyleFactory is not in model/qtsharp.api.json), so the
    // style names are hard-coded, the same way the legacy adapter hard-coded QStyleFactory.Keys.
    // QApplication.SetStyle(string) creates the style and applies it; the round-trip is verified
    // against QApplication.Style().ObjectName in the self-test below.
    public static readonly string[] StyleNames = { "windows11", "windowsvista", "Windows", "Fusion" };

    public readonly QLabel StyleLabel = new QLabel("&Style:");
    public readonly QComboBox StyleCombo = new QComboBox();
    public readonly QCheckBox DisableCheck = new QCheckBox("&Disable widgets");
    public readonly QDialogButtonBox ButtonBox;

    public readonly QGroupBox ButtonsGroup;
    public readonly QTabWidget ItemViews;
    public readonly QGroupBox SimpleInput;
    public readonly QToolBox TextToolBox;
    public readonly QProgressBar Progress;
    public QTimer ProgressTimer;

    // Buttons panel.
    public readonly QPushButton DefaultPush = new QPushButton("Default Push Button");
    public readonly QPushButton TogglePush = new QPushButton("Toggle Push Button");
    public readonly QPushButton FlatPush = new QPushButton("Flat Push Button");
    public readonly QToolButton ToolButton = new QToolButton();
    public readonly QToolButton MenuToolButton = new QToolButton();
    public readonly QMenu ToolMenu = new QMenu();
    public readonly QRadioButton Radio1 = new QRadioButton("Radio button 1");
    public readonly QRadioButton Radio2 = new QRadioButton("Radio button 2");
    public readonly QRadioButton Radio3 = new QRadioButton("Radio button 3");
    public readonly QCheckBox TriState = new QCheckBox("Tri-state check box");

    // Item views panel.
    public readonly QTreeWidget Tree = new QTreeWidget();
    public readonly QTableWidget Table = new QTableWidget(10, 10);
    public readonly QListWidget List = new QListWidget();
    public readonly QListWidget IconList = new QListWidget();
    public QTreeWidgetItem TreeRoot;

    // Simple input panel.
    public readonly QLineEdit PasswordEdit = new QLineEdit("s3cRe7");
    public readonly QSpinBox Spin = new QSpinBox();
    public readonly QDateTimeEdit DateTimeEdit = new QDateTimeEdit();
    public readonly QSlider Slider = new QSlider(Qt.Orientation.Horizontal);
    public readonly QScrollBar ScrollBar = new QScrollBar(Qt.Orientation.Horizontal);
    public readonly QDial Dial = new QDial();

    // Text controls panel.
    public QTextEdit TextEdit;
    public QPlainTextEdit PlainEdit;
    public QTextBrowser Browser;

    public int PaintCount;
    public int RejectedCount;

    public ApiWidgetsGalleryWindow()
    {
        // ---------------------------------------------------------------- top row: style + disable
        StyleCombo.AddItems(StyleNames);
        StyleLabel.SetBuddy(StyleCombo);
        StyleCombo.CurrentTextChangedSignal.Connect(ChangeStyle);
        DisableCheck.ToggledSignal.Connect(DisableWidgets);

        var helpLabel = new QLabel("Hover over the widgets to explore standard Qt Widgets.");

        var appearanceLayout = new QGridLayout();
        appearanceLayout.AddWidget(StyleLabel, 0, 0);
        appearanceLayout.AddWidget(StyleCombo, 0, 1);
        appearanceLayout.AddWidget(DisableCheck, 1, 0, 1, 2);

        var topLayout = new QHBoxLayout();
        topLayout.AddLayout(appearanceLayout);
        topLayout.AddStretch(1);
        topLayout.AddWidget(helpLabel);
        topLayout.AddStretch(1);

        // ---------------------------------------------------------------- panels
        ButtonsGroup = CreateButtonsGroupBox();
        ItemViews = CreateItemViewTabWidget();
        SimpleInput = CreateSimpleInputWidgetsGroupBox();
        TextToolBox = CreateTextToolBox();
        Progress = CreateProgressBar();

        // ---------------------------------------------------------------- bottom button box
        ButtonBox = new QDialogButtonBox(
            QDialogButtonBox.StandardButtons.Help | QDialogButtonBox.StandardButtons.Close);
        ButtonBox.RejectedSignal.Connect(() =>
        {
            RejectedCount++;
            Close();
        });

        // ---------------------------------------------------------------- assemble
        var mainLayout = new QGridLayout();
        mainLayout.AddLayout(topLayout, 0, 0, 1, 2);
        mainLayout.AddWidget(ButtonsGroup, 1, 0);
        mainLayout.AddWidget(SimpleInput, 1, 1);
        mainLayout.AddWidget(ItemViews, 2, 0);
        mainLayout.AddWidget(TextToolBox, 2, 1);
        mainLayout.AddWidget(Progress, 3, 0, 1, 2);
        mainLayout.AddWidget(ButtonBox, 4, 0, 1, 2);
        SetLayout(mainLayout);

        SetWindowTitle("Widget Gallery (new pipeline)");
        Resize(760, 640);
    }

    public override void PaintEvent(QPaintEvent e) => PaintCount++;

    void ChangeStyle(string styleName) => QApplication.SetStyle(styleName);

    // The "Disable widgets" check box disables every content panel (the legacy wiring).
    void DisableWidgets(bool disabled)
    {
        ButtonsGroup.SetEnabled(!disabled);
        SimpleInput.SetEnabled(!disabled);
        ItemViews.SetEnabled(!disabled);
        TextToolBox.SetEnabled(!disabled);
    }

    // The official timer ticks this once a second; the self-test steps it by hand instead of
    // starting a timer, so no event loop is needed to observe the progress animation.
    public void StepProgress()
    {
        int cur = Progress.value();
        int max = Progress.maximum();
        int next = cur + (max - cur) / 100;
        Progress.SetValue(next >= max ? 0 : next);
    }

    QGroupBox CreateButtonsGroupBox()
    {
        var result = new QGroupBox("Buttons");

        DefaultPush.SetDefault(true);

        TogglePush.SetCheckable(true);
        TogglePush.SetChecked(true);

        FlatPush.SetFlat(true);

        ToolButton.SetText("Tool Button");

        MenuToolButton.SetText("Menu Button");
        MenuToolButton.SetPopupMode(QToolButton.ToolButtonPopupMode.InstantPopup);
        ToolMenu.AddAction("Option");
        ToolMenu.AddSeparator();
        var checkableOption = ToolMenu.AddAction("Checkable Option");
        checkableOption.SetCheckable(true);
        MenuToolButton.SetMenu(ToolMenu);

        var toolButtonLayout = new QHBoxLayout();
        toolButtonLayout.AddWidget(ToolButton);
        toolButtonLayout.AddWidget(MenuToolButton);

        var buttonLayout = new QVBoxLayout();
        buttonLayout.AddWidget(DefaultPush);
        buttonLayout.AddWidget(TogglePush);
        buttonLayout.AddWidget(FlatPush);
        buttonLayout.AddLayout(toolButtonLayout);
        buttonLayout.AddStretch(1);

        Radio1.SetChecked(true);

        TriState.SetTristate(true);
        TriState.SetCheckState(Qt.CheckState.PartiallyChecked);

        var checkableLayout = new QVBoxLayout();
        checkableLayout.AddWidget(Radio1);
        checkableLayout.AddWidget(Radio2);
        checkableLayout.AddWidget(Radio3);
        checkableLayout.AddWidget(TriState);
        checkableLayout.AddStretch(1);

        var mainLayout = new QHBoxLayout();
        mainLayout.AddLayout(buttonLayout);
        mainLayout.AddLayout(checkableLayout);
        mainLayout.AddStretch(1);
        result.SetLayout(mainLayout);
        return result;
    }

    QTabWidget CreateItemViewTabWidget()
    {
        var result = new QTabWidget();

        Tree.SetHeaderLabels(new[] { "Tree View" });
        TreeRoot = new QTreeWidgetItem(new[] { "Root" });
        new QTreeWidgetItem(TreeRoot, new[] { "Child 1" });
        new QTreeWidgetItem(TreeRoot, new[] { "Child 2" });
        Tree.AddTopLevelItem(TreeRoot);
        Tree.ExpandAll();

        List.AddItem("Directory");
        List.AddItem("Computer");
        List.AddItem("Network");

        IconList.SetViewMode(QListView.ViewMode.IconMode);
        IconList.AddItem("Directory");
        IconList.AddItem("Computer");
        IconList.AddItem("Network");

        result.AddTab(Tree, "&Tree View");
        result.AddTab(Table, "T&able");
        result.AddTab(List, "&List");
        result.AddTab(IconList, "&Icon Mode List");
        return result;
    }

    QGroupBox CreateSimpleInputWidgetsGroupBox()
    {
        var result = new QGroupBox("Simple Input Widgets");
        result.SetCheckable(true);
        result.SetChecked(true);

        PasswordEdit.SetClearButtonEnabled(true);
        PasswordEdit.SetEchoMode(QLineEdit.EchoMode.Password);

        Spin.SetRange(0, 100);
        Spin.SetValue(50);

        DateTimeEdit.SetDisplayFormat("yyyy-MM-dd HH:mm:ss");
        DateTimeEdit.SetDateTime(QDateTime.CurrentDateTime());

        Slider.SetValue(40);
        ScrollBar.SetValue(60);
        Dial.SetValue(30);
        Dial.SetNotchesVisible(true);

        var layout = new QGridLayout();
        layout.AddWidget(PasswordEdit, 0, 0, 1, 2);
        layout.AddWidget(Spin, 1, 0, 1, 2);
        layout.AddWidget(DateTimeEdit, 2, 0, 1, 2);
        layout.AddWidget(Slider, 3, 0);
        layout.AddWidget(ScrollBar, 4, 0);
        layout.AddWidget(Dial, 3, 1, 2, 1);
        result.SetLayout(layout);
        return result;
    }

    QToolBox CreateTextToolBox()
    {
        var result = new QToolBox();

        const string plainText =
            "Twinkle, twinkle, little star,\n" +
            "How I wonder what you are.\n" +
            "Up above the world so high,\n" +
            "Like a diamond in the sky.\n" +
            "Twinkle, twinkle, little star,\n" +
            "How I wonder what you are!";

        var richText = "<html><head/><body><i>";
        foreach (var line in plainText.Split('\n'))
            richText += "<center>" + line + "</center>";
        richText += "</i></body></html>";

        TextEdit = new QTextEdit(richText);
        PlainEdit = new QPlainTextEdit(plainText);
        Browser = new QTextBrowser();
        Browser.SetHtml(
            "<html><head/><body><h3>System Info</h3><p>Qt 6.12 / QtSharp binding demo.</p></body></html>");

        result.AddItem(TextEdit, "Text Edit");
        result.AddItem(PlainEdit, "Plain Text Edit");
        result.AddItem(Browser, "Text Browser");
        return result;
    }

    QProgressBar CreateProgressBar()
    {
        var result = new QProgressBar();
        result.SetRange(0, 10000);
        result.SetValue(0);

        // The official gallery starts this timer; the self-test steps StepProgress() by hand so
        // that it needs no event loop. The wiring is still real.
        ProgressTimer = new QTimer(this);
        ProgressTimer.TimeoutSignal.Connect(StepProgress);
        return result;
    }
}

// Builds the gallery dialog, drives every panel through the generated API, and checks the widget
// state each action should have produced. No event loop and no user interaction: every assertion
// is deterministic.
[Demo("qtwidgets-gallery")]
public static class WidgetsGallery
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

        var window = new ApiWidgetsGalleryWindow();
        window.Show();

        // ---------------------------------------------------------------- style selection
        Check(window.StyleCombo.count() == ApiWidgetsGalleryWindow.StyleNames.Length,
            $"style combo is populated (got {window.StyleCombo.count()} item(s))");
        Check(window.StyleCombo.ItemText(0) == ApiWidgetsGalleryWindow.StyleNames[0],
            $"style combo first item (got '{window.StyleCombo.ItemText(0)}')");
        Check(window.StyleCombo.ItemText(ApiWidgetsGalleryWindow.StyleNames.Length - 1) == "Fusion",
            "style combo lists Fusion");

        // Selecting "Fusion" walks the combo -> currentTextChanged -> QApplication.SetStyle.
        window.StyleCombo.SetCurrentIndex(ApiWidgetsGalleryWindow.StyleNames.Length - 1);
        Check(window.StyleCombo.CurrentText == "Fusion",
            $"combo current text (got '{window.StyleCombo.CurrentText}')");
        var style = QApplication.Style();
        Check(style != null, "QApplication.Style() returns a style");
        Check(style != null && string.Equals(style.ObjectName, "fusion", StringComparison.OrdinalIgnoreCase),
            $"the combo switched the application style (got '{style?.ObjectName}')");

        // ...and the direct call round-trips too.
        var returned = QApplication.SetStyle("Fusion");
        Check(returned != null && string.Equals(returned.ObjectName, "fusion", StringComparison.OrdinalIgnoreCase),
            $"SetStyle returns the applied style (got '{returned?.ObjectName}')");

        // ---------------------------------------------------------------- disable check box
        Check(!window.DisableCheck.IsChecked(), "the disable check box starts unchecked");
        Check(window.DisableCheck.IsEnabled(), "the disable check box itself stays enabled");
        window.DisableCheck.SetChecked(true);
        Check(!window.ButtonsGroup.IsEnabled(), "disable disables the Buttons group");
        Check(!window.SimpleInput.IsEnabled(), "disable disables the Simple Input group");
        Check(!window.ItemViews.IsEnabled(), "disable disables the Item Views tabs");
        Check(!window.TextToolBox.IsEnabled(), "disable disables the Text Controls toolbox");
        window.DisableCheck.SetChecked(false);
        Check(window.ButtonsGroup.IsEnabled(), "re-enable enables the Buttons group");
        Check(window.SimpleInput.IsEnabled(), "re-enable enables the Simple Input group");
        Check(window.ItemViews.IsEnabled(), "re-enable enables the Item Views tabs");
        Check(window.TextToolBox.IsEnabled(), "re-enable enables the Text Controls toolbox");

        // ---------------------------------------------------------------- buttons panel
        Check(window.ButtonsGroup.Title == "Buttons", "the Buttons group keeps its title");
        Check(window.DefaultPush.IsDefault(), "the default push button is the default");
        Check(!window.DefaultPush.IsCheckable(), "the default push button is not checkable");
        Check(window.TogglePush.IsCheckable() && window.TogglePush.IsChecked(),
            "the toggle push button is checkable and checked");
        Check(window.FlatPush.IsFlat(), "the flat push button is flat");
        Check(window.ToolButton.Text == "Tool Button",
            $"the tool button text (got '{window.ToolButton.Text}')");
        Check(window.MenuToolButton.Text == "Menu Button",
            $"the menu button text (got '{window.MenuToolButton.Text}')");
        Check(window.MenuToolButton.PopupMode == QToolButton.ToolButtonPopupMode.InstantPopup,
            "the menu button pops up instantly");
        Check(window.MenuToolButton.Menu() != null && window.ToolMenu.Actions().Length == 3,
            $"the menu button carries a three-entry menu (got {window.ToolMenu.Actions().Length})");

        Check(window.Radio1.IsChecked() && !window.Radio2.IsChecked() && !window.Radio3.IsChecked(),
            "radio button 1 starts selected");
        window.Radio2.SetChecked(true);
        Check(window.Radio2.IsChecked() && !window.Radio1.IsChecked(),
            "selecting radio button 2 clears radio button 1");

        Check(window.TriState.IsTristate(), "the check box is tri-state");
        Check(window.TriState.CheckState() == Qt.CheckState.PartiallyChecked,
            $"the tri-state box starts partially checked (got {window.TriState.CheckState()})");
        window.TriState.SetCheckState(Qt.CheckState.Unchecked);
        Check(window.TriState.CheckState() == Qt.CheckState.Unchecked,
            "tri-state walks to Unchecked");
        window.TriState.SetCheckState(Qt.CheckState.PartiallyChecked);
        Check(window.TriState.CheckState() == Qt.CheckState.PartiallyChecked,
            "tri-state walks to PartiallyChecked");
        window.TriState.SetCheckState(Qt.CheckState.Checked);
        Check(window.TriState.CheckState() == Qt.CheckState.Checked,
            "tri-state walks to Checked");

        // ---------------------------------------------------------------- item views
        Check(window.ItemViews.count() == 4, $"tab count (got {window.ItemViews.count()})");
        Check(window.ItemViews.TabText(0) == "&Tree View",
            $"tab 0 text (got '{window.ItemViews.TabText(0)}')");
        Check(window.ItemViews.TabText(1) == "T&able",
            $"tab 1 text (got '{window.ItemViews.TabText(1)}')");
        Check(window.ItemViews.TabText(2) == "&List",
            $"tab 2 text (got '{window.ItemViews.TabText(2)}')");
        Check(window.ItemViews.TabText(3) == "&Icon Mode List",
            $"tab 3 text (got '{window.ItemViews.TabText(3)}')");

        Check(window.Tree.topLevelItemCount() == 1,
            $"tree top level items (got {window.Tree.topLevelItemCount()})");
        Check(window.TreeRoot.Text(0) == "Root", $"tree root text (got '{window.TreeRoot.Text(0)}')");
        Check(window.TreeRoot.ChildCount() == 2,
            $"tree root children (got {window.TreeRoot.ChildCount()})");
        Check(window.TreeRoot.Child(0).Text(0) == "Child 1",
            $"first tree child (got '{window.TreeRoot.Child(0).Text(0)}')");
        Check(window.Tree.columnCount() == 1,
            $"tree column count from SetHeaderLabels (got {window.Tree.columnCount()})");

        Check(window.Table.RowCount == 10 && window.Table.ColumnCount == 10,
            $"table dimensions (got {window.Table.RowCount}x{window.Table.ColumnCount})");

        Check(window.List.count() == 3, $"list view item count (got {window.List.count()})");
        Check(window.List.Item(1).Text() == "Computer",
            $"list item text (got '{window.List.Item(1).Text()}')");
        Check(window.IconList.viewMode() == QListView.ViewMode.IconMode,
            $"icon mode list uses IconMode (got {window.IconList.viewMode()})");
        Check(window.IconList.count() == 3,
            $"icon mode list item count (got {window.IconList.count()})");

        // ---------------------------------------------------------------- simple input
        Check(window.SimpleInput.IsCheckable() && window.SimpleInput.IsChecked(),
            "the Simple Input group is checkable and checked");
        Check(window.PasswordEdit.text() == "s3cRe7",
            $"password edit text (got '{window.PasswordEdit.text()}')");
        Check(window.PasswordEdit.echoMode() == QLineEdit.EchoMode.Password,
            $"password edit echo mode (got {window.PasswordEdit.echoMode()})");
        Check(window.PasswordEdit.IsClearButtonEnabled(), "password edit has a clear button");

        Check(window.Spin.value() == 50, $"spin box value (got {window.Spin.value()})");
        Check(window.Spin.minimum() == 0 && window.Spin.maximum() == 100,
            $"spin box range (got {window.Spin.minimum()}..{window.Spin.maximum()})");

        Check(window.DateTimeEdit.displayFormat() == "yyyy-MM-dd HH:mm:ss",
            $"date/time edit format (got '{window.DateTimeEdit.displayFormat()}')");
        Check(window.DateTimeEdit.dateTime().IsValid(), "date/time edit holds a valid date-time");

        Check(window.Slider.value() == 40, $"slider value (got {window.Slider.value()})");
        Check(window.ScrollBar.value() == 60, $"scroll bar value (got {window.ScrollBar.value()})");
        Check(window.Dial.value() == 30, $"dial value (got {window.Dial.value()})");
        Check(window.Dial.NotchesVisible, "the dial shows its notches");

        // ---------------------------------------------------------------- text controls
        Check(window.TextToolBox.count() == 3, $"toolbox page count (got {window.TextToolBox.count()})");
        Check(window.TextToolBox.currentIndex() == 0,
            $"toolbox current index (got {window.TextToolBox.currentIndex()})");
        Check(window.TextToolBox.ItemText(0) == "Text Edit" && window.TextToolBox.ItemText(1) == "Plain Text Edit"
              && window.TextToolBox.ItemText(2) == "Text Browser",
            "toolbox page labels");

        window.TextToolBox.SetCurrentIndex(1);
        Check(window.TextToolBox.currentIndex() == 1, "toolbox page selection");

        Check(window.TextEdit.ToHtml().Contains("Twinkle"),
            "the text edit keeps the rich text it was built from");
        Check(window.TextEdit.ToPlainText().Contains("Twinkle"),
            "the text edit's plain text round-trips");
        Check(window.PlainEdit.ToPlainText().Contains("Twinkle"),
            "the plain text edit round-trips");
        Check(window.Browser.ToPlainText().Contains("System Info"),
            "the text browser renders its HTML");
        Check(!window.TextEdit.IsReadOnly(), "the text edit starts editable");
        window.PlainEdit.SetReadOnly(true);
        Check(window.PlainEdit.IsReadOnly(), "the plain text edit can be made read-only");

        // ---------------------------------------------------------------- progress bar
        Check(window.Progress.value() == 0 && window.Progress.maximum() == 10000
              && window.Progress.minimum() == 0,
            $"progress bar starts at range 0..10000 (got {window.Progress.value()})");
        window.StepProgress();
        Check(window.Progress.value() == 100,
            $"stepping the progress animation advances it (got {window.Progress.value()})");
        window.StepProgress();
        Check(window.Progress.value() == 199,
            $"the second step advances again (got {window.Progress.value()})");

        // ---------------------------------------------------------------- dialog button box
        Check(window.ButtonBox.Buttons().Length == 2,
            $"button box has Help and Close (got {window.ButtonBox.Buttons().Length})");
        Check(window.ButtonBox.Button(QDialogButtonBox.StandardButton.Help) != null, "Help button exists");
        var closeButton = window.ButtonBox.Button(QDialogButtonBox.StandardButton.Close);
        Check(closeButton != null, "Close button exists");
        if (closeButton != null)
        {
            closeButton.Click();
            Check(window.RejectedCount == 1,
                $"clicking Close emitted rejected (got {window.RejectedCount})");
        }

        // ---------------------------------------------------------------- it really renders
        window.Grab();
        Check(window.PaintCount > 0, $"the dialog rendered ({window.PaintCount} paint(s))");

        window.Close();
        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
