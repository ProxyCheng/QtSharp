using System;
using System.Collections.Generic;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// Ports of the official Qt "Main Windows" examples.
//   - Menus      (examples/widgets/mainwindows/menus)
//   - Toolbars   (examples/widgets/mainwindows/toolbars)
//   - Application (examples/widgets/mainwindows/application)

// examples/widgets/mainwindows/menus
public class MenusWindow : QMainWindow
{
    readonly List<object> keepAlive = new();
    readonly List<QAction> alignmentActions = new();
    QLabel infoLabel;
    QAction newAct, openAct, saveAct, printAct, exitAct;
    QAction undoAct, redoAct, cutAct, copyAct, pasteAct;
    QAction boldAct, italicAct, leftAlignAct, rightAlignAct, justifyAct, centerAct;
    QAction setLineSpacingAct, setParagraphSpacingAct, aboutAct, aboutQtAct;

    public MenusWindow() : base(null, 0)
    {
        var widget = keep(new QWidget());
        CentralWidget = widget;

        var topFiller = keep(new QWidget());
        topFiller.SetSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding);

        infoLabel = keep(new QLabel("<i>Choose a menu option, or right-click to invoke a context menu</i>"));
        infoLabel.FrameShape = QFrame.Shape.StyledPanel;
        infoLabel.FrameShadow = QFrame.Shadow.Sunken;
        infoLabel.SetAlignment((int)(Qt.AlignmentFlag.AlignCenter));

        var bottomFiller = keep(new QWidget());
        bottomFiller.SetSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding);

        var layout = keep(new QVBoxLayout());
        layout.SetContentsMargins(5, 5, 5, 5);
        layout.AddWidget(topFiller);
        layout.AddWidget(infoLabel);
        layout.AddWidget(bottomFiller);
        widget.Layout = layout;

        CreateActions();
        CreateMenus();

        StatusBar.ShowMessage("A context menu is available by right-clicking");

        WindowTitle = "Menus";
        SetMinimumSize(160, 160);
        Resize(480, 320);
    }

    QAction Action(string text, string shortcut = null)
    {
        var action = keep(new QAction(text, this));
        if (shortcut != null)
            action.SetShortcutString(shortcut);
        return action;
    }

    void CreateActions()
    {
        newAct = Action("&New", "Ctrl+N");
        newAct.StatusTip = "Create a new file";
        newAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>File|New</b>");

        openAct = Action("&Open...", "Ctrl+O");
        openAct.StatusTip = "Open an existing file";
        openAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>File|Open</b>");

        saveAct = Action("&Save", "Ctrl+S");
        saveAct.StatusTip = "Save the document to disk";
        saveAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>File|Save</b>");

        printAct = Action("&Print...", "Ctrl+P");
        printAct.StatusTip = "Print the document";
        printAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>File|Print</b>");

        exitAct = Action("E&xit", "Ctrl+Q");
        exitAct.StatusTip = "Exit the application";
        exitAct.TriggeredSignal.Connect(_ => Close());

        undoAct = Action("&Undo", "Ctrl+Z");
        undoAct.StatusTip = "Undo the last operation";
        undoAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Undo</b>");

        redoAct = Action("&Redo", "Ctrl+Y");
        redoAct.StatusTip = "Redo the last operation";
        redoAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Redo</b>");

        cutAct = Action("Cu&t", "Ctrl+X");
        cutAct.StatusTip = "Cut the current selection's contents to the clipboard";
        cutAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Cut</b>");

        copyAct = Action("&Copy", "Ctrl+C");
        copyAct.StatusTip = "Copy the current selection's contents to the clipboard";
        copyAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Copy</b>");

        pasteAct = Action("&Paste", "Ctrl+V");
        pasteAct.StatusTip = "Paste the clipboard's contents into the current selection";
        pasteAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Paste</b>");

        boldAct = Action("&Bold", "Ctrl+B");
        boldAct.Checkable = true;
        boldAct.StatusTip = "Make the text bold";
        boldAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Format|Bold</b>");
        var boldFont = new QFont();
        boldFont.Bold = true;
        boldAct.Font = boldFont;

        italicAct = Action("&Italic", "Ctrl+I");
        italicAct.Checkable = true;
        italicAct.StatusTip = "Make the text italic";
        italicAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Format|Italic</b>");
        var italicFont = new QFont();
        italicFont.Italic = true;
        italicAct.Font = italicFont;

        setLineSpacingAct = Action("Set &Line Spacing...");
        setLineSpacingAct.StatusTip = "Change the gap between the lines of a paragraph";
        setLineSpacingAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Format|Set Line Spacing</b>");

        setParagraphSpacingAct = Action("Set &Paragraph Spacing...");
        setParagraphSpacingAct.StatusTip = "Change the gap between paragraphs";
        setParagraphSpacingAct.TriggeredSignal.Connect(_ => infoLabel.Text = "Invoked <b>Edit|Format|Set Paragraph Spacing</b>");

        aboutAct = Action("&About");
        aboutAct.StatusTip = "Show the application's About box";
        aboutAct.TriggeredSignal.Connect(_ =>
        {
            infoLabel.Text = "Invoked <b>Help|About</b>";
            QMessageBox.About(this, "About Menu",
                "The <b>Menu</b> example shows how to create menu-bar menus and context menus.");
        });

        aboutQtAct = Action("About &Qt");
        aboutQtAct.StatusTip = "Show the Qt library's About box";
        aboutQtAct.TriggeredSignal.Connect(_ =>
        {
            infoLabel.Text = "Invoked <b>Help|About Qt</b>";
            QMessageBox.AboutQt(this);
        });

        leftAlignAct = AlignmentAction("&Left Align", "Ctrl+L", "Left Align");
        rightAlignAct = AlignmentAction("&Right Align", "Ctrl+R", "Right Align");
        justifyAct = AlignmentAction("&Justify", "Ctrl+J", "Justify");
        centerAct = AlignmentAction("&Center", "Ctrl+E", "Center");
        leftAlignAct.Checked = true;
    }

    QAction AlignmentAction(string text, string shortcut, string label)
    {
        var action = Action(text, shortcut);
        action.Checkable = true;
        action.StatusTip = label.ToLowerInvariant() + " the selected text";
        // QActionGroup::addAction is not part of the bindings, so exclusivity is done by hand.
        action.TriggeredSignal.Connect(_ =>
        {
            foreach (var other in alignmentActions)
                if (!ReferenceEquals(other, action))
                    other.Checked = false;
            action.Checked = true;
            infoLabel.Text = "Invoked <b>Edit|Format|" + label + "</b>";
        });
        alignmentActions.Add(action);
        return action;
    }

    void CreateMenus()
    {
        var menuBar = MenuBar;

        var fileMenu = menuBar.AddMenu("&File");
        fileMenu.AddAction(newAct);
        fileMenu.AddAction(openAct);
        fileMenu.AddAction(saveAct);
        fileMenu.AddAction(printAct);
        fileMenu.AddSeparator();
        fileMenu.AddAction(exitAct);

        var editMenu = menuBar.AddMenu("&Edit");
        editMenu.AddAction(undoAct);
        editMenu.AddAction(redoAct);
        editMenu.AddSeparator();
        editMenu.AddAction(cutAct);
        editMenu.AddAction(copyAct);
        editMenu.AddAction(pasteAct);
        editMenu.AddSeparator();

        var helpMenu = menuBar.AddMenu("&Help");
        helpMenu.AddAction(aboutAct);
        helpMenu.AddAction(aboutQtAct);

        var formatMenu = editMenu.AddMenu("&Format");
        formatMenu.AddAction(boldAct);
        formatMenu.AddAction(italicAct);
        formatMenu.AddSeparator();
        formatMenu.AddAction(leftAlignAct);
        formatMenu.AddAction(rightAlignAct);
        formatMenu.AddAction(justifyAct);
        formatMenu.AddAction(centerAct);
        formatMenu.AddSeparator();
        formatMenu.AddAction(setLineSpacingAct);
        formatMenu.AddAction(setParagraphSpacingAct);
    }

    protected override void ContextMenuEvent(QContextMenuEvent e)
    {
        var menu = keep(new QMenu(this));
        menu.AddAction(cutAct);
        menu.AddAction(copyAct);
        menu.AddAction(pasteAct);
        menu.Exec(e.GlobalPos);
    }

    T keep<T>(T obj) where T : class
    {
        keepAlive.Add(obj);
        return obj;
    }
}
