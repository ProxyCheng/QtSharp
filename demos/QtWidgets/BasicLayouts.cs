using System;

// The official widgets/layouts/basiclayouts example, ported onto the *new* pipeline.
//
// The whole dialog is built from model/qtsharp.api.json: an embedded QMenuBar (through
// QLayout.setMenuBar), a horizontal-layout group box, a grid-layout group box, a form-layout
// group box, a big editor, a QDialogButtonBox with Ok/Cancel, and a QScrollArea whose vertical
// scroll bar gets a custom stylesheet. The self-test drives the real accepted()/rejected()
// signals of the button box (no dialog is shown, no event loop is run).
//
// Q_PROPERTY note: a property is exposed under its PascalCase name while the C++ accessor keeps
// its own name, so this file mixes both deliberately - `Title`/`WidgetResizable`/`StyleSheet`
// are properties, `title()`/`widgetResizable()`/`styleSheet()` are the accessors.
[Demo("qtwidgets-basiclayouts")]
public static class BasicLayouts
{
    public static int Run(DemoContext demo)
    {

        // ------------------------------------------------------------------ the dialog
        var dialog = new QDialog();
        dialog.SetWindowTitle("Basic Layouts");

        // ------------------------------------------------------------------ menu bar
        var menuBar = new QMenuBar();
        var fileMenu = new QMenu("&File", dialog);
        var exitAction = fileMenu.AddAction("E&xit");
        menuBar.AddMenu(fileMenu);
        int exitTriggers = 0;
        exitAction.TriggeredSignal.Connect(_ => exitTriggers++);

        // ------------------------------------------------------------------ horizontal group box
        var horizontalGroupBox = new QGroupBox("Horizontal layout");
        var horizontalLayout = new QHBoxLayout();
        var hButtons = new QPushButton[4];
        for (int i = 0; i < hButtons.Length; i++)
        {
            hButtons[i] = new QPushButton("Button " + (i + 1));
            horizontalLayout.AddWidget(hButtons[i]);
        }
        horizontalGroupBox.SetLayout(horizontalLayout);

        // ------------------------------------------------------------------ grid group box
        var gridGroupBox = new QGroupBox("Grid layout");
        var gridLayout = new QGridLayout();
        var gridLabels = new QLabel[3];
        var gridEdits = new QLineEdit[3];
        for (int i = 0; i < 3; i++)
        {
            gridLabels[i] = new QLabel("Line " + (i + 1) + ":");
            gridEdits[i] = new QLineEdit();
            gridLayout.AddWidget(gridLabels[i], i + 1, 0);
            gridLayout.AddWidget(gridEdits[i], i + 1, 1);
        }
        var smallEditor = new QTextEdit();
        smallEditor.SetPlainText("This widget takes up about two thirds of the grid layout.");
        gridLayout.AddWidget(smallEditor, 0, 2, 4, 1);
        gridLayout.SetColumnStretch(1, 10);
        gridLayout.SetColumnStretch(2, 20);
        gridGroupBox.SetLayout(gridLayout);

        // ------------------------------------------------------------------ form group box
        var formGroupBox = new QGroupBox("Form layout");
        var formLayout = new QFormLayout();
        formLayout.AddRow(new QLabel("Line 1:"), new QLineEdit());
        formLayout.AddRow(new QLabel("Line 2, long text:"), new QComboBox());
        formLayout.AddRow(new QLabel("Line 3:"), new QSpinBox());
        formGroupBox.SetLayout(formLayout);

        // ------------------------------------------------------------------ big editor
        var bigEditor = new QTextEdit();
        bigEditor.SetPlainText("This widget takes up all the remaining space in the top-level layout.");

        // ------------------------------------------------------------------ dialog buttons
        var buttonBox = new QDialogButtonBox(
            QDialogButtonBox.StandardButtons.Ok | QDialogButtonBox.StandardButtons.Cancel);

        // ------------------------------------------------------------------ top-level layout
        var mainLayout = new QVBoxLayout();
        mainLayout.SetMenuBar(menuBar);
        mainLayout.AddWidget(horizontalGroupBox);
        mainLayout.AddWidget(gridGroupBox);
        mainLayout.AddWidget(formGroupBox);
        mainLayout.AddWidget(bigEditor);
        mainLayout.AddWidget(buttonBox);

        var content = new QWidget();
        content.SetLayout(mainLayout);

        var scrollArea = new QScrollArea();
        scrollArea.FrameShape = QFrame.Shape.NoFrame;
        scrollArea.HorizontalScrollBarPolicy = Qt.ScrollBarPolicy.ScrollBarAlwaysOff;
        scrollArea.WidgetResizable = true;
        const string scrollBarStyle = "QScrollBar:vertical { width: 20px; }";
        scrollArea.VerticalScrollBar().StyleSheet = scrollBarStyle;
        scrollArea.SetWidget(content);

        var scrollLayout = new QVBoxLayout();
        scrollLayout.SetContentsMargins(0, 0, 0, 0);
        scrollLayout.AddWidget(scrollArea);
        dialog.SetLayout(scrollLayout);

#if QTSHARP_DEMO_VIEW
        // The dialog as the user sees it. The checks below drive its layouts and buttons directly,
        // which is the automated half.
        demo.Show(dialog);
        return 0;
#else

        // ------------------------------------------------------------------ dialog / layout
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ dialog / layout
        demo.Check(dialog.windowTitle() == "Basic Layouts",
            $"dialog title (got '{dialog.windowTitle()}')");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(dialog.Layout() != null && dialog.Layout().Handle == scrollLayout.Handle,
            "the dialog's top-level layout is the scroll layout");
        #endif

        var layoutMenuBar = mainLayout.MenuBar();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(layoutMenuBar != null && layoutMenuBar.Handle == menuBar.Handle,
            "the menu bar is actually set on the top-level layout");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(mainLayout.Count() == 5,
            $"the top-level layout carries the five widgets, menu bar excluded (got {mainLayout.Count()})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollLayout.Count() == 1, $"the scroll layout holds one widget (got {scrollLayout.Count()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(content.Layout() != null && content.Layout().Handle == mainLayout.Handle,
            "the content widget owns the top-level layout");
        #endif

        // ------------------------------------------------------------------ menu contents
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ menu contents
        demo.Check(fileMenu.Title == "&File", $"file menu title (got '{fileMenu.Title}')");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(exitAction.text() == "E&xit", $"exit action text (got '{exitAction.text()}')");
        #endif
        exitAction.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(exitTriggers == 1, $"exit action triggered the managed handler (got {exitTriggers})");
        #endif

        // ------------------------------------------------------------------ group box titles
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ group box titles
        demo.Check(horizontalGroupBox.Title == "Horizontal layout",
            $"horizontal group title (got '{horizontalGroupBox.Title}')");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(gridGroupBox.Title == "Grid layout",
            $"grid group title (got '{gridGroupBox.Title}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formGroupBox.Title == "Form layout",
            $"form group title (got '{formGroupBox.Title}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(horizontalGroupBox.Layout().Handle == horizontalLayout.Handle,
            "the horizontal group box owns the hbox layout");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(gridGroupBox.Layout().Handle == gridLayout.Handle,
            "the grid group box owns the grid layout");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formGroupBox.Layout().Handle == formLayout.Handle,
            "the form group box owns the form layout");
        #endif

        // ------------------------------------------------------------------ horizontal layout
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ horizontal layout
        demo.Check(horizontalLayout.Count() == 4, $"the hbox has four buttons (got {horizontalLayout.Count()})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(((QPushButton)horizontalLayout.ItemAt(0).Widget()).Text == "Button 1",
            $"hbox order: first is Button 1 (got '{((QPushButton)horizontalLayout.ItemAt(0).Widget()).Text}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(((QPushButton)horizontalLayout.ItemAt(3).Widget()).Text == "Button 4",
            $"hbox order: last is Button 4 (got '{((QPushButton)horizontalLayout.ItemAt(3).Widget()).Text}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(horizontalLayout.ItemAt(1).Widget().Handle == hButtons[1].Handle,
            "hbox item 1 wraps the same native button as the managed handle");
        #endif

        // ------------------------------------------------------------------ grid layout
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ grid layout
        demo.Check(gridLayout.RowCount() == 4, $"grid row count (got {gridLayout.RowCount()})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(gridLayout.ColumnCount() == 3, $"grid column count (got {gridLayout.ColumnCount()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(gridLayout.Count() == 7, $"grid item count (6 fields + the editor, got {gridLayout.Count()})");
        #endif

        gridLayout.GetItemPosition(0, out int r0, out int c0, out int rs0, out int cs0);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(r0 == 1 && c0 == 0 && rs0 == 1 && cs0 == 1,
            $"grid: first label sits at (1,0) 1x1 (got ({r0},{c0}) {rs0}x{cs0})");
        #endif
        gridLayout.GetItemPosition(1, out int r1, out int c1, out int rs1, out int cs1);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(r1 == 1 && c1 == 1 && rs1 == 1 && cs1 == 1,
            $"grid: first line edit sits at (1,1) 1x1 (got ({r1},{c1}) {rs1}x{cs1})");
        #endif
        gridLayout.GetItemPosition(6, out int r6, out int c6, out int rs6, out int cs6);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(r6 == 0 && c6 == 2 && rs6 == 4 && cs6 == 1,
            $"grid: the small editor spans (0,2) 4x1 (got ({r6},{c6}) {rs6}x{cs6})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(gridLayout.ItemAtPosition(1, 0).Widget().Handle == gridLabels[0].Handle,
            "grid: ItemAtPosition(1,0) is the first label");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(gridLayout.ItemAt(6).Widget().Handle == smallEditor.Handle,
            "grid: item 6 is the small editor");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(gridLayout.ColumnStretch(1) == 10 && gridLayout.ColumnStretch(2) == 20,
            $"grid column stretches (got {gridLayout.ColumnStretch(1)}/{gridLayout.ColumnStretch(2)})");
        #endif

        // ------------------------------------------------------------------ form layout
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ form layout
        demo.Check(formLayout.RowCount() == 3, $"form row count (got {formLayout.RowCount()})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formLayout.Count() == 6, $"form item count (3 rows x label+field, got {formLayout.Count()})");
        #endif
        var row0Label = formLayout.ItemAt(0, QFormLayout.ItemRole.LabelRole);
        var row0Field = formLayout.ItemAt(0, QFormLayout.ItemRole.FieldRole);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(row0Label != null && row0Label.Widget() is QLabel label0 && label0.Text == "Line 1:",
            "form: row 0 carries the \"Line 1:\" label");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(row0Field != null && row0Field.Widget() is QLineEdit,
            "form: row 0 field is a line edit");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formLayout.ItemAt(2, QFormLayout.ItemRole.FieldRole).Widget() is QSpinBox,
            "form: row 2 field is a spin box");
        #endif

        // ------------------------------------------------------------------ big editor
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ big editor
        demo.Check(bigEditor.ToPlainText().StartsWith("This widget takes up all the remaining space"),
            $"big editor content (got '{bigEditor.ToPlainText()}')");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(bigEditor.MinimumSize.Width() == 0 && bigEditor.MinimumSize.Height() == 0,
            $"big editor default minimum size (got {bigEditor.MinimumSize.Width()}x{bigEditor.MinimumSize.Height()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(bigEditor.SizePolicy.HorizontalPolicy() == QSizePolicy.Policy.Expanding
              && bigEditor.SizePolicy.VerticalPolicy() == QSizePolicy.Policy.Expanding,
            $"big editor size policy (got {bigEditor.SizePolicy.HorizontalPolicy()}/{bigEditor.SizePolicy.VerticalPolicy()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(mainLayout.ItemAt(3).Widget().Handle == bigEditor.Handle,
            "the big editor is item 3 of the top-level layout");
        #endif

        // ------------------------------------------------------------------ dialog button box
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ dialog button box
        demo.Check(buttonBox.Button(QDialogButtonBox.StandardButton.Ok) != null, "button box has an Ok button");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(buttonBox.Button(QDialogButtonBox.StandardButton.Cancel) != null, "button box has a Cancel button");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(((int)buttonBox.standardButtons() & (int)QDialogButtonBox.StandardButtons.Ok) != 0,
            "standardButtons() contains Ok");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(((int)buttonBox.standardButtons() & (int)QDialogButtonBox.StandardButtons.Cancel) != 0,
            "standardButtons() contains Cancel");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(buttonBox.Orientation == Qt.Orientation.Horizontal,
            $"button box orientation (got {buttonBox.Orientation})");
        #endif

        int accepted = 0;
        int rejected = 0;
        buttonBox.AcceptedSignal.Connect(() => accepted++);
        buttonBox.RejectedSignal.Connect(() => rejected++);
        buttonBox.Button(QDialogButtonBox.StandardButton.Ok).Click();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(accepted == 1, $"clicking Ok emitted accepted() exactly once (got {accepted})");
        #endif
        buttonBox.AcceptedSignal.Emit();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(accepted == 2, $"emitting accepted() reached the handler again (got {accepted})");
        #endif
        buttonBox.Button(QDialogButtonBox.StandardButton.Cancel).Click();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(rejected == 1, $"clicking Cancel emitted rejected() exactly once (got {rejected})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(accepted == 2, "the rejected path did not fire accepted()");
        #endif

        // ------------------------------------------------------------------ scroll area
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ scroll area
        demo.Check(scrollArea.widgetResizable(), "the scroll area resizes its widget");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollArea.frameShape() == QFrame.Shape.NoFrame,
            $"the scroll area has no frame (got {scrollArea.frameShape()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollArea.horizontalScrollBarPolicy() == Qt.ScrollBarPolicy.ScrollBarAlwaysOff,
            $"horizontal scroll bar is always off (got {scrollArea.horizontalScrollBarPolicy()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollArea.verticalScrollBarPolicy() == Qt.ScrollBarPolicy.ScrollBarAsNeeded,
            $"vertical scroll bar is as-needed (got {scrollArea.verticalScrollBarPolicy()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollArea.VerticalScrollBar().StyleSheet == scrollBarStyle,
            $"custom scroll bar stylesheet round-trip (got '{scrollArea.VerticalScrollBar().StyleSheet}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(scrollArea.Widget() != null && scrollArea.Widget().Handle == content.Handle,
            "the scroll area wraps the content widget");
        #endif

        // ------------------------------------------------------------------ resizing stays consistent
        dialog.Resize(800, 600);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(dialog.size().Width() == 800 && dialog.size().Height() == 600,
            $"dialog resize (got {dialog.size().Width()}x{dialog.size().Height()})");
        #endif
        scrollLayout.Activate();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(mainLayout.Count() == 5 && scrollLayout.Count() == 1,
            $"layout item counts survive a resize ({mainLayout.Count()}/{scrollLayout.Count()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(mainLayout.ItemAt(3).Widget().Handle == bigEditor.Handle
              && mainLayout.ItemAt(4).Widget().Handle == buttonBox.Handle,
            "the big editor and button box are still in place after the resize");
        #endif

        return demo.ExitCode;
#endif
    }
}
