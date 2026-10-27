using System;

// Port of examples/widgets/desktop/systray/{window.h,window.cpp} onto the *new* pipeline.
// Everything (the dialog, its group boxes, layouts, labels, combo boxes, spin box, line edit,
// text edit, push button, actions, menu and the tray icon) comes from model/qtsharp.api.json -
// nothing here is hand-written glue.
//
// Fidelity *improvements* over the legacy C# port (testbind/SystemTray.cs):
//   * QSystemTrayIcon::activated() is modelled now, so IconActivated() is connected for real
//     (the legacy binding could only leave a comment saying it was unreachable);
//   * QStyle::standardIcon() is modelled now, so the tray/type icons are the real style icons
//     instead of the QMessageBox::standardIcon() stand-in;
//   * the message-icon combo stores its value with SetItemData (the same substitute the legacy
//     port used), but the value is read back through the typed enum.
//
// Deviations from the C++ original, and why:
//   * the example's badge/heart/trash PNG resources (.qrc) are not shipped with the binding, so
//     QStyle::standardIcon() stands in for them (the icons' *identity* is asserted, not their art);
//   * the original closeEvent() asks a *modal* QMessageBox for confirmation. A self-test must not
//     block on a dialog, so the message box is behind SuppressModalDialogs and the decision itself
//     (spontaneous + visible + tray visible => hide and ignore the close) is exposed through
//     HandleCloseRequest(), which CloseEvent() delegates to;
//   * MessageClicked() likewise counts and returns instead of showing the modal "ask a human"
//     box while SuppressModalDialogs is set;
//   * a real system tray may not exist in this environment, so the self-test asserts the window's
//     *configuration* (icons, tooltip, visibility flag, menu contents, message arguments, signal
//     wiring, close logic) rather than OS behaviour. It never invokes the modal actions.
//
// Naming notes for the generated binding: the Q_PROPERTY getters/setters keep their
// C++ names (titleEdit.Text, durationSpinBox.Value, trayIcon.ToolTip, ...), signals are XxxSignal
// properties, and the enums are nested (QSystemTrayIcon.MessageIcon.Information,
// QStyle.StandardPixmap.SP_MessageBoxInformation, Qt.ItemDataRole.UserRole).
public sealed class ApiSystemTrayWindow : QDialog
{
    // ---------------------------------------------------------------- icon group box
    public QGroupBox IconGroupBox;
    public QLabel IconLabel;
    public QComboBox IconComboBox;
    public QCheckBox ShowIconCheckBox;

    // ---------------------------------------------------------------- balloon message group box
    public QGroupBox MessageGroupBox;
    public QLabel TypeLabel;
    public QComboBox TypeComboBox;
    public QLabel DurationLabel;
    public QSpinBox DurationSpinBox;
    public QLabel DurationWarningLabel;
    public QLabel TitleLabel;
    public QLineEdit TitleEdit;
    public QLabel BodyLabel;
    public QTextEdit BodyEdit;
    public QPushButton ShowMessageButton;

    // ---------------------------------------------------------------- actions, menu, tray icon
    public QAction MinimizeAction;
    public QAction MaximizeAction;
    public QAction RestoreAction;
    public QAction QuitAction;
    public QMenu TrayIconMenu;
    public QSystemTrayIcon TrayIcon;

    // ---------------------------------------------------------------- observation for the self-test
    // While this is set neither closeEvent nor messageClicked may raise a modal QMessageBox (the
    // self-test has no user to dismiss it); the decision/counters below are still exercised.
    public bool SuppressModalDialogs;
    public int MessageClickedCount;
    public int ShowEventCount;
    public string LastMessageTitle;
    public string LastMessageBody;
    public QSystemTrayIcon.MessageIcon LastMessageIconType;
    public QIcon LastMessageCustomIcon;
    public int LastMessageMsecs;
    public bool LastMessageUsedCustomIcon;

    public ApiSystemTrayWindow()
    {
        CreateIconGroupBox();
        CreateMessageGroupBox();

        // The original sizes the "Icon:" label to the width of the "Duration:" label's size hint.
        IconLabel.MinimumWidth = DurationLabel.SizeHint.Width();

        CreateActions();
        CreateTrayIcon();

        ShowMessageButton.ClickedSignal.Connect(_ => ShowMessage());
        ShowIconCheckBox.ToggledSignal.Connect(b => { if (TrayIcon != null) TrayIcon.Visible = b; });
        IconComboBox.CurrentIndexChangedSignal.Connect(SetIcon);
        TrayIcon.MessageClickedSignal.Connect(MessageClicked);
        // The legacy port could not do this: activated() is not part of its signal bindings.
        TrayIcon.ActivatedSignal.Connect(IconActivated);

        var mainLayout = new QVBoxLayout();
        mainLayout.AddWidget(IconGroupBox);
        mainLayout.AddWidget(MessageGroupBox);
        SetLayout(mainLayout);

        IconComboBox.CurrentIndex = 1;
        TrayIcon.Show();

        SetWindowTitle("Systray (new pipeline)");
        Resize(400, 300);
    }

    static QIcon StandardIcon(QStyle.StandardPixmap which)
        => QApplication.Style().StandardIcon(which);

    // QMessageBox::standardIcon() is no longer needed: QStyle::standardIcon() is modelled.
    static void AddMessageIcon(QComboBox comboBox, QIcon icon, string text, int value)
    {
        if (icon == null)
            comboBox.AddItem(text, value);
        else
            comboBox.AddItem(icon, text, value);
    }

    // The official closeEvent(), minus the modal confirmation (see the header comment). Returns
    // true when the window hid itself and the caller must ignore the close.
    public bool HandleCloseRequest(bool spontaneous)
    {
        if (!spontaneous || !IsVisible())
            return false;
        if (TrayIcon.Visible)
        {
            if (!SuppressModalDialogs)
            {
                QMessageBox.Information(this, "Systray",
                    "The program will keep running in the system tray. To terminate the program, "
                    + "choose <b>Quit</b> in the context menu of the system tray entry.");
            }
            Hide();
            return true;
        }
        return false;
    }

    public override void CloseEvent(QCloseEvent @event)
    {
        if (HandleCloseRequest(@event.Spontaneous()))
            @event.Ignore();
    }

    // The official setVisible() override, which the model cannot publish as a virtual: it enables
    // the actions according to the window's visibility and window state.
    // The official override: visibility is the signal for the action states.
    public override void SetVisible(bool visible)
    {
        base.SetVisible(visible);
        MinimizeAction.Enabled = visible;
        MaximizeAction.Enabled = !IsMaximized();
        RestoreAction.Enabled = IsMaximized() || !visible;
    }

    public override void ShowEvent(QShowEvent @event)
    {
        ShowEventCount++;
    }

    void SetIcon(int index)
    {
        var icon = IconComboBox.ItemIcon(index);
        TrayIcon.Icon = icon;
        WindowIcon = icon;
        TrayIcon.ToolTip = IconComboBox.ItemText(index);
    }

    void IconActivated(QSystemTrayIcon.ActivationReason reason)
    {
        switch (reason)
        {
            case QSystemTrayIcon.ActivationReason.Trigger:
            case QSystemTrayIcon.ActivationReason.DoubleClick:
                IconComboBox.CurrentIndex = (IconComboBox.CurrentIndex + 1) % IconComboBox.Count;
                break;
            case QSystemTrayIcon.ActivationReason.MiddleClick:
                ShowMessage();
                break;
        }
    }

    // The official showMessage(), with the arguments recorded so the self-test can check them.
    public void ShowMessage()
    {
        ShowIconCheckBox.Checked = true;
        int selectedIcon = (int)TypeComboBox
            .ItemData(TypeComboBox.CurrentIndex, (int)Qt.ItemDataRole.UserRole);

        LastMessageTitle = TitleEdit.Text;
        LastMessageBody = BodyEdit.PlainText;
        LastMessageMsecs = DurationSpinBox.Value * 1000;
        LastMessageUsedCustomIcon = selectedIcon == -1;

        if (selectedIcon == -1)
        {
            // The official "custom icon" path takes the icon from the icon combo box.
            LastMessageCustomIcon = IconComboBox.ItemIcon(IconComboBox.CurrentIndex);
            LastMessageIconType = QSystemTrayIcon.MessageIcon.NoIcon;
            TrayIcon.ShowMessage(LastMessageTitle, LastMessageBody, LastMessageCustomIcon, LastMessageMsecs);
        }
        else
        {
            LastMessageCustomIcon = null;
            LastMessageIconType = (QSystemTrayIcon.MessageIcon)selectedIcon;
            TrayIcon.ShowMessage(LastMessageTitle, LastMessageBody, LastMessageIconType, LastMessageMsecs);
        }
    }

    void MessageClicked()
    {
        MessageClickedCount++;
        if (SuppressModalDialogs)
            return;
        QMessageBox.Information(null, "Systray",
            "Sorry, I already gave what help I could.\nMaybe you should try asking a human?");
    }

    void CreateIconGroupBox()
    {
        IconGroupBox = new QGroupBox("Tray Icon");

        IconLabel = new QLabel("Icon:");

        IconComboBox = new QComboBox();
        IconComboBox.AddItem(StandardIcon(QStyle.StandardPixmap.SP_MessageBoxCritical), "Bad");
        IconComboBox.AddItem(StandardIcon(QStyle.StandardPixmap.SP_MessageBoxInformation), "Heart");
        IconComboBox.AddItem(StandardIcon(QStyle.StandardPixmap.SP_MessageBoxWarning), "Trash");

        ShowIconCheckBox = new QCheckBox("Show icon");
        ShowIconCheckBox.Checked = true;

        var iconLayout = new QHBoxLayout();
        iconLayout.AddWidget(IconLabel);
        iconLayout.AddWidget(IconComboBox);
        iconLayout.AddStretch();
        iconLayout.AddWidget(ShowIconCheckBox);
        IconGroupBox.SetLayout(iconLayout);
    }

    void CreateMessageGroupBox()
    {
        MessageGroupBox = new QGroupBox("Balloon Message");

        TypeLabel = new QLabel("Type:");

        TypeComboBox = new QComboBox();
        AddMessageIcon(TypeComboBox, null, "None", (int)QSystemTrayIcon.MessageIcon.NoIcon);
        AddMessageIcon(TypeComboBox, StandardIcon(QStyle.StandardPixmap.SP_MessageBoxInformation),
            "Information", (int)QSystemTrayIcon.MessageIcon.Information);
        AddMessageIcon(TypeComboBox, StandardIcon(QStyle.StandardPixmap.SP_MessageBoxWarning),
            "Warning", (int)QSystemTrayIcon.MessageIcon.Warning);
        AddMessageIcon(TypeComboBox, StandardIcon(QStyle.StandardPixmap.SP_MessageBoxCritical),
            "Critical", (int)QSystemTrayIcon.MessageIcon.Critical);
        using (var customIcon = new QIcon())
            AddMessageIcon(TypeComboBox, customIcon, "Custom icon", -1);
        TypeComboBox.CurrentIndex = 1;

        DurationLabel = new QLabel("Duration:");

        DurationSpinBox = new QSpinBox();
        DurationSpinBox.SetRange(5, 60);
        DurationSpinBox.Suffix = " s";
        DurationSpinBox.Value = 15;

        DurationWarningLabel = new QLabel("(some systems might ignore this hint)");
        DurationWarningLabel.Indent = 10;

        TitleLabel = new QLabel("Title:");

        TitleEdit = new QLineEdit("Cannot connect to network");

        BodyLabel = new QLabel("Body:");

        BodyEdit = new QTextEdit();
        BodyEdit.SetPlainText("Don't believe me. Honestly, I don't have a clue.\n"
                              + "Click this balloon for details.");

        ShowMessageButton = new QPushButton("Show Message");
        ShowMessageButton.Default = true;

        var messageLayout = new QGridLayout();
        messageLayout.AddWidget(TypeLabel, 0, 0);
        messageLayout.AddWidget(TypeComboBox, 0, 1, 1, 2);
        messageLayout.AddWidget(DurationLabel, 1, 0);
        messageLayout.AddWidget(DurationSpinBox, 1, 1);
        messageLayout.AddWidget(DurationWarningLabel, 1, 2, 1, 3);
        messageLayout.AddWidget(TitleLabel, 2, 0);
        messageLayout.AddWidget(TitleEdit, 2, 1, 1, 4);
        messageLayout.AddWidget(BodyLabel, 3, 0);
        messageLayout.AddWidget(BodyEdit, 3, 1, 2, 4);
        messageLayout.AddWidget(ShowMessageButton, 5, 4);
        messageLayout.SetColumnStretch(3, 1);
        messageLayout.SetRowStretch(4, 1);
        MessageGroupBox.SetLayout(messageLayout);
    }

    void CreateActions()
    {
        MinimizeAction = new QAction("Mi&nimize", this);
        MinimizeAction.TriggeredSignal.Connect(_ => Hide());

        MaximizeAction = new QAction("Ma&ximize", this);
        MaximizeAction.TriggeredSignal.Connect(_ => ShowMaximized());

        RestoreAction = new QAction("&Restore", this);
        RestoreAction.TriggeredSignal.Connect(_ => ShowNormal());

        QuitAction = new QAction("&Quit", this);
        // Quit from a menu action, with no application reference in reach - the PyQt
        // spelling QApplication.instance().quit(), which reaches the one application object.
        QuitAction.TriggeredSignal.Connect(_ => QCoreApplication.Instance().Quit());
    }

    void CreateTrayIcon()
    {
        TrayIconMenu = new QMenu(this);
        TrayIconMenu.AddAction(MinimizeAction);
        TrayIconMenu.AddAction(MaximizeAction);
        TrayIconMenu.AddAction(RestoreAction);
        TrayIconMenu.AddSeparator();
        TrayIconMenu.AddAction(QuitAction);

        TrayIcon = new QSystemTrayIcon(this);
        TrayIcon.SetContextMenu(TrayIconMenu);
    }
}

// Builds the dialog, drives every group through the generated signal layer, and checks the state
// each handler should have produced. No event loop and no user interaction: every signal used here
// is either emitted synchronously by Qt when a value changes or activated explicitly through the
// generated Emit(). The modal QMessageBoxes of closeEvent/messageClicked are suppressed.
[Demo("qtwidgets-systray")]
public static class SystemTray
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

        var window = new ApiSystemTrayWindow();
        // Set before anything can raise the modal box (closeEvent / messageClicked).
        window.SuppressModalDialogs = true;

        var tray = window.TrayIcon;
        var menu = window.TrayIconMenu;
        var iconCombo = window.IconComboBox;
        var typeCombo = window.TypeComboBox;

        // A tray icon may or may not be available in this environment; that is not asserted.
        Console.WriteLine($"[INFO] system tray available={QSystemTrayIcon.IsSystemTrayAvailable()}, "
                          + $"supportsMessages={QSystemTrayIcon.SupportsMessages()} (OS behaviour, not asserted)");

        // ---------------------------------------------------------------- icon group box
        Check(window.IconGroupBox.Title == "Tray Icon",
            $"the icon group box is titled \"Tray Icon\" (got '{window.IconGroupBox.Title}')");
        Check(window.IconLabel.text() == "Icon:",
            $"the icon label reads \"Icon:\" (got '{window.IconLabel.text()}')");
        Check(iconCombo.Count == 3, $"the icon combo has three entries (got {iconCombo.Count})");
        Check(iconCombo.ItemText(0) == "Bad" && iconCombo.ItemText(1) == "Heart" && iconCombo.ItemText(2) == "Trash",
            "the icon combo texts are Bad/Heart/Trash");
        Check(!iconCombo.ItemIcon(0).IsNull() && !iconCombo.ItemIcon(1).IsNull() && !iconCombo.ItemIcon(2).IsNull(),
            "every icon combo entry carries a real icon");

        // The icons come from QStyle::standardIcon() (the .qrc art is not shipped); each entry must
        // be a distinct real icon. (QIcon::cacheKey identifies the shared icon data, so two copies
        // of the *same* icon compare equal but two different standard icons do not.)
        long criticalKey = iconCombo.ItemIcon(0).CacheKey();
        long heartKey = iconCombo.ItemIcon(1).CacheKey();
        long trashKey = iconCombo.ItemIcon(2).CacheKey();
        Check(criticalKey != heartKey && heartKey != trashKey && criticalKey != trashKey,
            "the three icon entries are three distinct style icons");

        Check(window.ShowIconCheckBox.text() == "Show icon",
            $"the show-icon check box reads \"Show icon\" (got '{window.ShowIconCheckBox.text()}')");
        Check(window.ShowIconCheckBox.Checked, "the show-icon check box starts checked");

        // ---------------------------------------------------------------- balloon message group box
        Check(window.MessageGroupBox.Title == "Balloon Message",
            $"the message group box is titled \"Balloon Message\" (got '{window.MessageGroupBox.Title}')");
        Check(typeCombo.Count == 5, $"the type combo has five entries (got {typeCombo.Count})");
        Check(typeCombo.ItemText(0) == "None" && typeCombo.ItemText(1) == "Information"
              && typeCombo.ItemText(2) == "Warning" && typeCombo.ItemText(3) == "Critical"
              && typeCombo.ItemText(4) == "Custom icon",
            "the type combo texts are None/Information/Warning/Critical/Custom icon");

        // The per-item user-role data the official addItem(text, variant) calls store.
        int[] expectedIconData =
        {
            (int)QSystemTrayIcon.MessageIcon.NoIcon,
            (int)QSystemTrayIcon.MessageIcon.Information,
            (int)QSystemTrayIcon.MessageIcon.Warning,
            (int)QSystemTrayIcon.MessageIcon.Critical,
            -1
        };
        bool itemDataOk = true;
        for (int i = 0; i < expectedIconData.Length; i++)
            itemDataOk &= (int)typeCombo.ItemData(i, (int)Qt.ItemDataRole.UserRole) == expectedIconData[i];
        Check(itemDataOk, "the type combo carries NoIcon/Information/Warning/Critical/-1 as user-role data");
        Check(typeCombo.CurrentIndex == 1,
            $"the type combo defaults to Information (got index {typeCombo.CurrentIndex})");
        Check(typeCombo.ItemIcon(4).IsNull(), "the \"Custom icon\" entry has a null icon (the official empty QIcon)");
        Check(!typeCombo.ItemIcon(1).IsNull(), "the Information entry carries an icon");

        Check(window.DurationLabel.text() == "Duration:",
            $"the duration label reads \"Duration:\" (got '{window.DurationLabel.text()}')");
        Check(window.DurationSpinBox.Minimum == 5 && window.DurationSpinBox.Maximum == 60,
            $"the duration range is 5..60 s (got {window.DurationSpinBox.Minimum}..{window.DurationSpinBox.Maximum})");
        Check(window.DurationSpinBox.Suffix == " s",
            $"the duration suffix is \" s\" (got '{window.DurationSpinBox.Suffix}')");
        Check(window.DurationSpinBox.Value == 15,
            $"the duration defaults to 15 s (got {window.DurationSpinBox.Value})");
        Check(window.DurationWarningLabel.text() == "(some systems might ignore this hint)"
              && window.DurationWarningLabel.Indent == 10,
            "the duration warning label has the official text and an indent of 10");

        Check(window.TitleEdit.Text == "Cannot connect to network",
            $"the title edit is pre-filled (got '{window.TitleEdit.Text}')");
        Check(window.BodyEdit.PlainText.Contains("Click this balloon for details."),
            $"the body edit is pre-filled (got '{window.BodyEdit.PlainText}')");

        Check(window.ShowMessageButton.Text == "Show Message",
            $"the message button reads \"Show Message\" (got '{window.ShowMessageButton.Text}')");
        Check(window.ShowMessageButton.Default, "the message button is the dialog's default button");

        // ---------------------------------------------------------------- the tray icon itself
        Check(tray.ToolTip == "Heart",
            $"the tray tooltip follows the default icon entry (got '{tray.ToolTip}')");
        Check(tray.Icon.CacheKey() == heartKey, "the tray icon is the default Heart icon");
        Check(window.WindowIcon.CacheKey() == heartKey, "the window icon follows the tray icon");
        Check(tray.Visible, "trayIcon.show() was called and the visibility flag is set");
        Check(tray.ContextMenu() != null && tray.ContextMenu().Handle == menu.Handle,
            "the tray icon's context menu is the menu that was built");

        // Changing the icon entry drives setIcon through currentIndexChanged.
        iconCombo.CurrentIndex = 2;
        Check(tray.ToolTip == "Trash" && tray.Icon.CacheKey() == trashKey,
            $"selecting \"Trash\" updates the tooltip and icon (tooltip '{tray.ToolTip}')");
        Check(window.WindowIcon.CacheKey() == trashKey, "the window icon follows the selection");
        iconCombo.CurrentIndex = 0;
        Check(tray.ToolTip == "Bad" && tray.Icon.CacheKey() == criticalKey,
            $"selecting \"Bad\" updates the tooltip and icon (tooltip '{tray.ToolTip}')");

        // ---------------------------------------------------------------- menu contents
        var actions = menu.Actions();
        Check(actions.Length == 5, $"the tray menu has five entries (got {actions.Length})");
        Check(actions[0].Text == "Mi&nimize" && actions[1].Text == "Ma&ximize"
              && actions[2].Text == "&Restore" && actions[4].Text == "&Quit",
            "the menu entries are Minimize/Maximize/Restore/-/Quit, in order");
        Check(actions[3].IsSeparator(), "the fourth entry is the separator");
        Check(actions[0].Handle == window.MinimizeAction.Handle
              && actions[1].Handle == window.MaximizeAction.Handle
              && actions[2].Handle == window.RestoreAction.Handle
              && actions[4].Handle == window.QuitAction.Handle,
            "the menu holds the four actions that were created");
        Check(window.MinimizeAction.Parent() != null && window.MinimizeAction.Parent().Handle == window.Handle,
            "the actions are parented to the window");
        Check(window.QuitAction.Enabled, "the quit action starts enabled");

        // ---------------------------------------------------------------- window visibility / actions
        // Showing the window runs the official setVisible() override, which sets the action states.
        window.Show();
        Check(window.ShowEventCount >= 1, $"showing the window delivered the show event ({window.ShowEventCount} time(s))");
        Check(window.MinimizeAction.Enabled && window.MaximizeAction.Enabled && !window.RestoreAction.Enabled,
            "a visible, normal window enables minimize/maximize and disables restore");
        Check(!window.RestoreAction.Enabled, "restore stays disabled while the window is normal");

        // ---------------------------------------------------------------- ShowMessage arguments
        Check(window.TypeComboBox.CurrentIndex == 1, "the type combo is on Information before the first message");
        window.DurationSpinBox.Value = 20;
        window.ShowMessage();
        Check(window.LastMessageUsedCustomIcon == false && window.LastMessageIconType == QSystemTrayIcon.MessageIcon.Information,
            $"the Information entry selects the enum overload (got {window.LastMessageIconType})");
        Check(window.LastMessageMsecs == 20000,
            $"the duration is converted to milliseconds (got {window.LastMessageMsecs})");
        Check(window.LastMessageTitle == "Cannot connect to network"
              && window.LastMessageBody.Contains("Click this balloon for details."),
            $"the message uses the title/body edits (title '{window.LastMessageTitle}')");
        Check(window.LastMessageCustomIcon == null, "the enum overload passes no custom icon");

        // The "Custom icon" entry (-1) selects the QIcon overload.
        typeCombo.CurrentIndex = 4;
        window.ShowMessage();
        Check(window.LastMessageUsedCustomIcon && window.LastMessageIconType == QSystemTrayIcon.MessageIcon.NoIcon,
            "the Custom icon entry selects the QIcon overload");
        Check(window.LastMessageCustomIcon != null && !window.LastMessageCustomIcon.IsNull()
              && window.LastMessageCustomIcon.CacheKey() == iconCombo.ItemIcon(iconCombo.CurrentIndex).CacheKey(),
            "the custom message icon is the currently selected tray icon");

        typeCombo.CurrentIndex = 2;
        window.ShowMessage();
        Check(window.LastMessageIconType == QSystemTrayIcon.MessageIcon.Warning,
            $"the Warning entry selects Warning (got {window.LastMessageIconType})");

        // ShowMessage() re-checks the "show icon" box (the official behaviour).
        window.ShowIconCheckBox.SetChecked(false);
        Check(!tray.Visible, "unchecking \"Show icon\" hides the tray icon through toggled()");
        window.ShowMessage();
        Check(window.ShowIconCheckBox.Checked && tray.Visible,
            "showing a message re-checks the box and shows the tray icon again");

        // ---------------------------------------------------------------- activated() wiring
        // activated() is bound now: emitting it must reach IconActivated().
        typeCombo.CurrentIndex = 1;
        iconCombo.CurrentIndex = 1;
        tray.ActivatedSignal.Emit(QSystemTrayIcon.ActivationReason.Trigger);
        Check(iconCombo.CurrentIndex == 2, $"Trigger cycles the icon selection (got {iconCombo.CurrentIndex})");
        tray.ActivatedSignal.Emit(QSystemTrayIcon.ActivationReason.DoubleClick);
        Check(iconCombo.CurrentIndex == 0, $"DoubleClick cycles it again (got {iconCombo.CurrentIndex})");
        tray.ActivatedSignal.Emit(QSystemTrayIcon.ActivationReason.Context);
        Check(iconCombo.CurrentIndex == 0, "Context is not handled and leaves the selection alone");

        int messagesBefore = window.MessageClickedCount;
        tray.ActivatedSignal.Emit(QSystemTrayIcon.ActivationReason.MiddleClick);
        Check(window.LastMessageUsedCustomIcon == false && window.LastMessageMsecs == 20000,
            "MiddleClick routes to ShowMessage() (message arguments recorded)");

        // ---------------------------------------------------------------- messageClicked() wiring
        tray.MessageClickedSignal.Emit();
        Check(window.MessageClickedCount == messagesBefore + 1,
            $"messageClicked() reaches the handler (count {window.MessageClickedCount})");

        // ---------------------------------------------------------------- close logic (no modal box)
        // HandleCloseRequest() is exactly what CloseEvent() calls.
        window.Show();
        Check(window.HandleCloseRequest(true) && !window.IsVisible(),
            "a spontaneous close request on a visible window with a visible tray hides it");
        Check(!window.HandleCloseRequest(true), "a second close request is ignored because the window is hidden");

        window.Show();
        Check(!window.HandleCloseRequest(false),
            "a non-spontaneous (programmatic) close request is left to Qt");
        Check(window.IsVisible(), "the window stayed visible for the non-spontaneous request");

        tray.Visible = false;
        Check(!window.HandleCloseRequest(true), "with the tray hidden the close request is not intercepted");
        Check(window.IsVisible(), "the window stayed visible when the tray is hidden");
        tray.Visible = true;

        // ---------------------------------------------------------------- the minimize action is wired
        window.Show();
        window.MinimizeAction.Trigger();
        Check(!window.IsVisible(), "triggering the minimize action hides the window (its handler ran)");

        // Leave the shell nothing to talk to. A live QSystemTrayIcon (with a balloon message the
        // shell still owns) makes the process teardown unreliable: the runtime destroys the
        // application at process exit, and Windows can then dispatch a message to a window whose
        // Qt-side data is already gone, which shows up as 0xC000041D. Hiding the icon and the
        // window first keeps the shutdown path the plain one every other demo takes.
        tray.Visible = false;
        window.Hide();

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
