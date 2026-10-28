using System;

// The official widgets/mainwindows/menus example, ported onto the *new* pipeline. It is a
// QMainWindow (not a dialog): a menu bar with File / Edit / Help, a Format submenu under Edit
// holding an exclusive set of alignment actions, status-bar tips, a central "current action"
// label, and a right-click context menu built with QMenu and shown with QMenu.Exec(QPoint).
//
// Improvements the new model enables (the legacy MenusWindow in MainWindowExamples.cs had to work
// around all of these):
//   * QActionGroup is bound now, so the four alignment actions are made mutually exclusive by an
//     exclusive QActionGroup instead of the legacy hand-rolled "uncheck every sibling" loop.
//   * QAction.setShortcut(QKeySequence) and the QKeySequence(string) constructor exist, so the
//     shortcuts are real QKeySequences instead of the legacy SetShortcutString shim.
//   * QMenu.exec(QPoint) and QContextMenuEvent.globalPos() exist, so the context menu override is
//     the real call rather than a no-op.
//   * QAction.showStatusText() lets the test drive the status-bar tip synchronously (it is what Qt
//     itself calls when a menu action is highlighted), so no hover/event-loop machinery is needed.
//
// Deviations from the C++ original, all forced by what the model can express today:
//   * the demo never runs an event loop (Run() returns on its own), so the context menu is built
//     and inspected through CreateContextMenu() instead of calling ContextMenuEvent() (whose
//     QMenu.Exec would block). The override itself is the real implementation below.
//   * the fillers around the central label are QBoxLayout stretches instead of widgets with a
//     QSizePolicy, which avoids dragging an unrelated type into the demo.
public sealed class ApiMenusWindow : QMainWindow
{
    public QLabel InfoLabel;

    public QMenu FileMenu;
    public QMenu EditMenu;
    public QMenu FormatMenu;
    public QMenu HelpMenu;

    public QActionGroup AlignmentGroup;

    public QAction NewAct;
    public QAction OpenAct;
    public QAction SaveAct;
    public QAction PrintAct;
    public QAction ExitAct;

    public QAction UndoAct;
    public QAction RedoAct;
    public QAction CutAct;
    public QAction CopyAct;
    public QAction PasteAct;

    public QAction BoldAct;
    public QAction ItalicAct;
    public QAction SetLineSpacingAct;
    public QAction SetParagraphSpacingAct;
    public QAction AboutAct;
    public QAction AboutQtAct;

    public QAction LeftAlignAct;
    public QAction RightAlignAct;
    public QAction JustifyAct;
    public QAction CenterAct;

    public int ExitRequests;

    public ApiMenusWindow()
    {
        var widget = new QWidget();
        SetCentralWidget(widget);

        InfoLabel = new QLabel("<i>Choose a menu option, or right-click to invoke a context menu</i>");
        InfoLabel.FrameShape = QFrame.Shape.StyledPanel;
        InfoLabel.FrameShadow = QFrame.Shadow.Sunken;
        InfoLabel.SetAlignment(Qt.Alignment.AlignCenter);

        var layout = new QVBoxLayout();
        layout.SetContentsMargins(5, 5, 5, 5);
        layout.AddStretch(1);
        layout.AddWidget(InfoLabel);
        layout.AddStretch(1);
        widget.SetLayout(layout);

        // The legacy version built the whole exclusivity by hand; here it is one group.
        AlignmentGroup = new QActionGroup(this);
        AlignmentGroup.SetExclusive(true);
        AlignmentGroup.SetExclusionPolicy(QActionGroup.ExclusionPolicy.Exclusive);

        CreateActions();
        CreateMenus();

        StatusBar().ShowMessage("A context menu is available by right-clicking");

        WindowTitle = "Menus (new pipeline)";
        SetMinimumSize(160, 160);
        Resize(480, 320);
    }

    QAction MakeAction(string text, string shortcut = null)
    {
        var action = new QAction(text, this);
        if (shortcut != null)
            action.SetShortcut(new QKeySequence(shortcut));
        return action;
    }

    void CreateActions()
    {
        NewAct = MakeAction("&New", "Ctrl+N");
        NewAct.StatusTip = "Create a new file";
        NewAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>File|New</b>");

        OpenAct = MakeAction("&Open...", "Ctrl+O");
        OpenAct.StatusTip = "Open an existing file";
        OpenAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>File|Open</b>");

        SaveAct = MakeAction("&Save", "Ctrl+S");
        SaveAct.StatusTip = "Save the document to disk";
        SaveAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>File|Save</b>");

        PrintAct = MakeAction("&Print...", "Ctrl+P");
        PrintAct.StatusTip = "Print the document";
        PrintAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>File|Print</b>");

        ExitAct = MakeAction("E&xit", "Ctrl+Q");
        ExitAct.StatusTip = "Exit the application";
        ExitAct.TriggeredSignal.Connect(_ =>
        {
            ExitRequests++;
            Close();
        });

        UndoAct = MakeAction("&Undo", "Ctrl+Z");
        UndoAct.StatusTip = "Undo the last operation";
        UndoAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Undo</b>");

        RedoAct = MakeAction("&Redo", "Ctrl+Y");
        RedoAct.StatusTip = "Redo the last operation";
        RedoAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Redo</b>");

        CutAct = MakeAction("Cu&t", "Ctrl+X");
        CutAct.StatusTip = "Cut the current selection's contents to the clipboard";
        CutAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Cut</b>");

        CopyAct = MakeAction("&Copy", "Ctrl+C");
        CopyAct.StatusTip = "Copy the current selection's contents to the clipboard";
        CopyAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Copy</b>");

        PasteAct = MakeAction("&Paste", "Ctrl+V");
        PasteAct.StatusTip = "Paste the clipboard's contents into the current selection";
        PasteAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Paste</b>");

        BoldAct = MakeAction("&Bold", "Ctrl+B");
        BoldAct.Checkable = true;
        BoldAct.StatusTip = "Make the text bold";
        BoldAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Format|Bold</b>");
        var boldFont = new QFont();
        boldFont.SetBold(true);
        BoldAct.Font = boldFont;

        ItalicAct = MakeAction("&Italic", "Ctrl+I");
        ItalicAct.Checkable = true;
        ItalicAct.StatusTip = "Make the text italic";
        ItalicAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Format|Italic</b>");
        var italicFont = new QFont();
        italicFont.SetItalic(true);
        ItalicAct.Font = italicFont;

        SetLineSpacingAct = MakeAction("Set &Line Spacing...");
        SetLineSpacingAct.StatusTip = "Change the gap between the lines of a paragraph";
        SetLineSpacingAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Format|Set Line Spacing</b>");

        SetParagraphSpacingAct = MakeAction("Set &Paragraph Spacing...");
        SetParagraphSpacingAct.StatusTip = "Change the gap between paragraphs";
        SetParagraphSpacingAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Format|Set Paragraph Spacing</b>");

        AboutAct = MakeAction("&About");
        AboutAct.StatusTip = "Show the application's About box";
        AboutAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Help|About</b>");

        AboutQtAct = MakeAction("About &Qt");
        AboutQtAct.StatusTip = "Show the Qt library's About box";
        AboutQtAct.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Help|About Qt</b>");

        LeftAlignAct = AlignmentAction("&Left Align", "Ctrl+L", "Left Align");
        RightAlignAct = AlignmentAction("&Right Align", "Ctrl+R", "Right Align");
        JustifyAct = AlignmentAction("&Justify", "Ctrl+J", "Justify");
        CenterAct = AlignmentAction("&Center", "Ctrl+E", "Center");
        LeftAlignAct.Checked = true;
    }

    QAction AlignmentAction(string text, string shortcut, string label)
    {
        var action = MakeAction(text, shortcut);
        action.Checkable = true;
        action.StatusTip = label.ToLowerInvariant() + " the selected text";
        // No hand-rolled loop: the exclusive group does the unchecking for us.
        action.TriggeredSignal.Connect(_ => InfoLabel.Text = "Invoked <b>Edit|Format|" + label + "</b>");
        AlignmentGroup.AddAction(action);
        return action;
    }

    void CreateMenus()
    {
        var menuBar = MenuBar();

        FileMenu = menuBar.AddMenu("&File");
        FileMenu.AddAction(NewAct);
        FileMenu.AddAction(OpenAct);
        FileMenu.AddAction(SaveAct);
        FileMenu.AddAction(PrintAct);
        FileMenu.AddSeparator();
        FileMenu.AddAction(ExitAct);

        EditMenu = menuBar.AddMenu("&Edit");
        EditMenu.AddAction(UndoAct);
        EditMenu.AddAction(RedoAct);
        EditMenu.AddSeparator();
        EditMenu.AddAction(CutAct);
        EditMenu.AddAction(CopyAct);
        EditMenu.AddAction(PasteAct);
        EditMenu.AddSeparator();

        HelpMenu = menuBar.AddMenu("&Help");
        HelpMenu.AddAction(AboutAct);
        HelpMenu.AddAction(AboutQtAct);

        FormatMenu = EditMenu.AddMenu("&Format");
        FormatMenu.AddAction(BoldAct);
        FormatMenu.AddAction(ItalicAct);
        FormatMenu.AddSeparator();
        FormatMenu.AddAction(LeftAlignAct);
        FormatMenu.AddAction(RightAlignAct);
        FormatMenu.AddAction(JustifyAct);
        FormatMenu.AddAction(CenterAct);
        FormatMenu.AddSeparator();
        FormatMenu.AddAction(SetLineSpacingAct);
        FormatMenu.AddAction(SetParagraphSpacingAct);
    }

    // The context menu is factored out so the self-test can inspect exactly what the event
    // handler would show. ContextMenuEvent is the real override below.
    public QMenu CreateContextMenu()
    {
        var menu = new QMenu(this);
        menu.AddAction(CutAct);
        menu.AddAction(CopyAct);
        menu.AddAction(PasteAct);
        return menu;
    }

    public override void ContextMenuEvent(QContextMenuEvent e)
    {
        CreateContextMenu().Exec(e.GlobalPos());
    }
}

// Builds the window, then drives the menu bar, the action group, the shortcuts, the status tips
// and the context menu through the generated API, checking the state each step should have
// produced. No event loop is started: every assertion is synchronous.
[Demo("qtwidgets-menus")]
public static class Menus
{
    public static int Run(DemoContext demo)
    {

        var window = new ApiMenusWindow();
        #if QTSHARP_DEMO_SMOKE
            window.Show();
        #else
            demo.Show(window);
        #endif

        // ------------------------------------------------------------------ menu bar
        var bar = window.MenuBar();
        var menus = bar.Actions();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(menus.Length == 3, $"menu bar has three menus (got {menus.Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(menus[0].text() == "&File" && menus[1].text() == "&Edit" && menus[2].text() == "&Help",
            $"menu bar titles in order (got '{menus[0].text()}', '{menus[1].text()}', '{menus[2].text()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(QMenu.MenuInAction(menus[0]).title() == "&File",
            $"MenuInAction resolves the File menu (got '{QMenu.MenuInAction(menus[0]).title()}')");
        #endif

        // ------------------------------------------------------------------ File menu
        var fileActions = window.FileMenu.Actions();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(fileActions.Length == 6, $"File has six entries (got {fileActions.Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(fileActions[0].text() == "&New" && fileActions[1].text() == "&Open..."
              && fileActions[2].text() == "&Save" && fileActions[3].text() == "&Print...",
            "File texts New/Open/Save/Print in order");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(fileActions[4].IsSeparator() && fileActions[5].text() == "E&xit",
            "File ends with a separator then Exit");
        #endif

        // ------------------------------------------------------------------ Edit menu
        var editActions = window.EditMenu.Actions();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(editActions.Length == 8, $"Edit has eight entries (got {editActions.Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(editActions[0].text() == "&Undo" && editActions[1].text() == "&Redo",
            "Edit starts with Undo/Redo");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(editActions[2].IsSeparator(), "Edit has a separator after Redo");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(editActions[3].text() == "Cu&t" && editActions[4].text() == "&Copy" && editActions[5].text() == "&Paste",
            "Edit has Cut/Copy/Paste");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(editActions[6].IsSeparator(), "Edit has a separator before Format");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(editActions[7].text() == "&Format", $"Edit ends with the Format submenu (got '{editActions[7].text()}')");
        #endif

        // ------------------------------------------------------------------ Format menu
        var formatActions = window.FormatMenu.Actions();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formatActions.Length == 10, $"Format has ten entries (got {formatActions.Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formatActions[0].text() == "&Bold" && formatActions[1].text() == "&Italic",
            "Format starts with Bold/Italic");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formatActions[2].IsSeparator(), "Format has a separator after Italic");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formatActions[3].text() == "&Left Align" && formatActions[4].text() == "&Right Align"
              && formatActions[5].text() == "&Justify" && formatActions[6].text() == "&Center",
            "Format holds the four alignment actions in order");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formatActions[7].IsSeparator(), "Format has a separator before the spacing actions");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(formatActions[8].text() == "Set &Line Spacing..." && formatActions[9].text() == "Set &Paragraph Spacing...",
            "Format ends with the spacing actions");
        #endif

        // ------------------------------------------------------------------ Help menu
        var helpActions = window.HelpMenu.Actions();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(helpActions.Length == 2 && helpActions[0].text() == "&About" && helpActions[1].text() == "About &Qt",
            $"Help holds About/About Qt (got {helpActions.Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.AboutAct.IsEnabled() && window.AboutQtAct.IsEnabled(),
            "About / About Qt actions are enabled (not triggered: they would block)");
        #endif

        // ------------------------------------------------------------------ the alignment group
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ the alignment group
        demo.Check(window.LeftAlignAct.IsCheckable() && window.RightAlignAct.IsCheckable()
              && window.JustifyAct.IsCheckable() && window.CenterAct.IsCheckable(),
            "all four alignment actions are checkable");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.AlignmentGroup.Actions().Length == 4,
            $"the group owns four actions (got {window.AlignmentGroup.Actions().Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.AlignmentGroup.IsExclusive(), "the alignment group is exclusive");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.AlignmentGroup.exclusionPolicy() == QActionGroup.ExclusionPolicy.Exclusive,
            "the alignment group's exclusion policy is Exclusive");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.LeftAlignAct.IsChecked(), "Left Align starts out checked");
        #endif

        window.RightAlignAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.RightAlignAct.IsChecked(), "triggering Right Align checks it");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!window.LeftAlignAct.IsChecked(), "triggering Right Align unchecks Left Align");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!window.JustifyAct.IsChecked() && !window.CenterAct.IsChecked(),
            "triggering Right Align leaves the other two unchecked");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(ReferenceEquals(window.AlignmentGroup.CheckedAction(), window.RightAlignAct),
            "the group reports Right Align as the checked action");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.InfoLabel.Text == "Invoked <b>Edit|Format|Right Align</b>",
            $"the label follows the alignment trigger (got '{window.InfoLabel.Text}')");
        #endif

        window.JustifyAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.JustifyAct.IsChecked() && !window.RightAlignAct.IsChecked(),
            "triggering Justify moves the exclusive check");
        #endif
        window.LeftAlignAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.LeftAlignAct.IsChecked() && !window.JustifyAct.IsChecked(),
            "triggering Left Align moves the exclusive check again");
        #endif

        // ------------------------------------------------------------------ status-bar tips
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ status-bar tips
        demo.Check(window.StatusBar().CurrentMessage() == "A context menu is available by right-clicking",
            $"the status bar starts with the help message (got '{window.StatusBar().CurrentMessage()}')");
#endif
        window.NewAct.ShowStatusText(window);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.StatusBar().CurrentMessage() == "Create a new file",
            $"a File action's status tip reaches the status bar (got '{window.StatusBar().CurrentMessage()}')");
        #endif
        window.LeftAlignAct.ShowStatusText(window);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.StatusBar().CurrentMessage() == "left align the selected text",
            $"an alignment action's status tip reaches the status bar (got '{window.StatusBar().CurrentMessage()}')");
        #endif
        window.SetLineSpacingAct.ShowStatusText(window);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.StatusBar().CurrentMessage() == "Change the gap between the lines of a paragraph",
            $"a Format action's status tip reaches the status bar (got '{window.StatusBar().CurrentMessage()}')");
        #endif

        // ------------------------------------------------------------------ shortcuts as QKeySequence
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ shortcuts as QKeySequence
        demo.Check(window.NewAct.shortcut().ToString() == "Ctrl+N",
            $"New's shortcut round-trips (got '{window.NewAct.shortcut().ToString()}')");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.OpenAct.shortcut().ToString() == "Ctrl+O",
            $"Open's shortcut round-trips (got '{window.OpenAct.shortcut().ToString()}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.JustifyAct.shortcut().ToString() == "Ctrl+J",
            $"Justify's shortcut round-trips (got '{window.JustifyAct.shortcut().ToString()}')");
        #endif
        var ctrlShiftZ = new QKeySequence("Ctrl+Shift+Z");
        #if QTSHARP_DEMO_SMOKE
        demo.Check(ctrlShiftZ.ToString() == "Ctrl+Shift+Z" && ctrlShiftZ.Count() == 1,
            $"QKeySequence(string) round-trips and has one chord (got '{ctrlShiftZ.ToString()}', {ctrlShiftZ.Count()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ExitAct.shortcut().Matches(new QKeySequence("Ctrl+Q")) == QKeySequence.SequenceMatch.ExactMatch,
            "Exit's shortcut exactly matches Ctrl+Q");
        #endif

        // ------------------------------------------------------------------ the current-action label
        window.NewAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.InfoLabel.Text == "Invoked <b>File|New</b>",
            $"triggering New updates the label (got '{window.InfoLabel.Text}')");
        #endif
        window.OpenAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.InfoLabel.Text == "Invoked <b>File|Open</b>",
            $"triggering Open updates the label (got '{window.InfoLabel.Text}')");
        #endif
        window.UndoAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.InfoLabel.Text == "Invoked <b>Edit|Undo</b>",
            $"triggering Undo updates the label (got '{window.InfoLabel.Text}')");
        #endif
        window.BoldAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.InfoLabel.Text == "Invoked <b>Edit|Format|Bold</b>",
            $"triggering Bold updates the label (got '{window.InfoLabel.Text}')");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.BoldAct.IsChecked(), "Bold toggles itself (checkable but outside the group)");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.BoldAct.Font.Bold(), "Bold's action font is bold");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ItalicAct.Font.Italic(), "Italic's action font is italic");
        #endif
        window.SetLineSpacingAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.InfoLabel.Text == "Invoked <b>Edit|Format|Set Line Spacing</b>",
            $"triggering the spacing action updates the label (got '{window.InfoLabel.Text}')");
        #endif

        // ------------------------------------------------------------------ context menu
        var context = window.CreateContextMenu();
        var contextActions = context.Actions();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(contextActions.Length == 3, $"the context menu holds three actions (got {contextActions.Length})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(contextActions[0].text() == "Cu&t" && contextActions[1].text() == "&Copy" && contextActions[2].text() == "&Paste",
            "the context menu is Cut/Copy/Paste");
        #endif
        // Actions() hands back transient wrappers (container elements are owned elsewhere), so the
        // identity check compares native handles rather than object references.
        #if QTSHARP_DEMO_SMOKE
        // Actions() hands back transient wrappers (container elements are owned elsewhere), so the
        // identity check compares native handles rather than object references.
        demo.Check(contextActions[0].Handle == window.CutAct.Handle
              && contextActions[1].Handle == window.CopyAct.Handle
              && contextActions[2].Handle == window.PasteAct.Handle,
            "the context menu reuses the window's own actions");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(contextActions[0].shortcut().ToString() == "Ctrl+X",
            $"the context menu carries the shortcuts (got '{contextActions[0].shortcut().ToString()}')");
        #endif

        // ------------------------------------------------------------------ exit wiring
#if QTSHARP_DEMO_SMOKE

        // ------------------------------------------------------------------ exit wiring
        demo.Check(window.ExitAct.text() == "E&xit" && window.ExitAct.statusTip() == "Exit the application",
            "Exit action's text/status tip are wired");
#endif
        window.ExitAct.Trigger();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.ExitRequests == 1, $"triggering Exit reached the handler (got {window.ExitRequests})");
        #endif

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        return 0;
        #endif
    }
}
