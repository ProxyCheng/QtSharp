using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// Port of examples/widgets/desktop/systray/window.h|cpp
//
// Deviations (all caused by missing bindings/resources, not by design):
//  - the example's badge/heart/trash PNG resources are not shipped with the binding,
//    so QMessageBox standard icons stand in for the tray/icon-combo icons.
//  - QStyle::standardIcon() is not bound; QMessageBox::standardIcon() is used instead.
//  - QDialog::setVisible() cannot be overridden from the bindings, so the
//    enable/disable logic for the minimise/maximise/restore actions is not applied.
public class SystemTrayWindow : QDialog
{
    QGroupBox iconGroupBox;
    QLabel iconLabel;
    QComboBox iconComboBox;
    QCheckBox showIconCheckBox;

    QGroupBox messageGroupBox;
    QLabel typeLabel;
    QComboBox typeComboBox;
    QLabel durationLabel;
    QSpinBox durationSpinBox;
    QLabel durationWarningLabel;
    QLabel titleLabel;
    QLineEdit titleEdit;
    QLabel bodyLabel;
    QTextEdit bodyEdit;
    QPushButton showMessageButton;

    QAction minimizeAction;
    QAction maximizeAction;
    QAction restoreAction;
    QAction quitAction;

    QSystemTrayIcon trayIcon;
    QMenu trayIconMenu;

    public SystemTrayWindow() : base(null, 0)
    {
        CreateIconGroupBox();
        CreateMessageGroupBox();

        iconLabel.MinimumWidth = durationLabel.SizeHint.Width;

        CreateActions();
        CreateTrayIcon();

        showMessageButton.ClickedSignal.Connect(_ => ShowMessage());
        showIconCheckBox.ToggledSignal.Connect(b => { if (trayIcon != null) trayIcon.Visible = b; });
        iconComboBox.CurrentIndexChangedSignal.Connect(SetIcon);
        trayIcon.MessageClickedSignal.Connect(MessageClicked);
        // QSystemTrayIcon::activated() is not part of the current signal bindings, so
        // IconActivated() is not reachable from the tray icon.

        var mainLayout = new QVBoxLayout();
        mainLayout.AddWidget(iconGroupBox);
        mainLayout.AddWidget(messageGroupBox);
        Layout = mainLayout;

        iconComboBox.CurrentIndex = 1;
        trayIcon.Show();

        WindowTitle = "Systray";
        Resize(400, 300);
    }

    static QIcon StandardIcon(QMessageBox.Icon icon)
        => new QIcon(QMessageBox.StandardIcon(icon));

    protected override void CloseEvent(QCloseEvent @event)
    {
        if (!@event.Spontaneous || !Visible)
            return;
        if (trayIcon.Visible)
        {
            QMessageBox.Information(this, "Systray",
                "The program will keep running in the system tray. To terminate the program, "
                + "choose <b>Quit</b> in the context menu of the system tray entry.");
            Hide();
            @event.Ignore();
        }
    }

    void SetIcon(int index)
    {
        var icon = iconComboBox.ItemIcon(index);
        trayIcon.Icon = icon;
        WindowIcon = icon;

        trayIcon.ToolTip = iconComboBox.ItemText(index);
    }

    void IconActivated(QSystemTrayIcon.ActivationReason reason)
    {
        switch (reason)
        {
            case QSystemTrayIcon.ActivationReason.Trigger:
            case QSystemTrayIcon.ActivationReason.DoubleClick:
                iconComboBox.CurrentIndex = (iconComboBox.CurrentIndex + 1) % iconComboBox.Count;
                break;
            case QSystemTrayIcon.ActivationReason.MiddleClick:
                ShowMessage();
                break;
        }
    }

    void ShowMessage()
    {
        showIconCheckBox.Checked = true;
        int selectedIcon = typeComboBox.ItemData(typeComboBox.CurrentIndex).ToInt();

        if (selectedIcon == -1)
        {
            using var customIcon = new QIcon(iconComboBox.ItemIcon(iconComboBox.CurrentIndex));
            trayIcon.ShowMessage(titleEdit.Text, bodyEdit.ToPlainText(), customIcon,
                                 durationSpinBox.Value * 1000);
        }
        else
        {
            trayIcon.ShowMessage(titleEdit.Text, bodyEdit.ToPlainText(),
                                 (QSystemTrayIcon.MessageIcon)selectedIcon,
                                 durationSpinBox.Value * 1000);
        }
    }

    void MessageClicked()
    {
        QMessageBox.Information(null, "Systray",
            "Sorry, I already gave what help I could.\nMaybe you should try asking a human?");
    }

    void CreateIconGroupBox()
    {
        iconGroupBox = new QGroupBox("Tray Icon");

        iconLabel = new QLabel("Icon:");

        iconComboBox = new QComboBox();
        iconComboBox.AddItem(StandardIcon(QMessageBox.Icon.Critical), "Bad");
        iconComboBox.AddItem(StandardIcon(QMessageBox.Icon.Information), "Heart");
        iconComboBox.AddItem(StandardIcon(QMessageBox.Icon.Warning), "Trash");

        showIconCheckBox = new QCheckBox("Show icon");
        showIconCheckBox.Checked = true;

        var iconLayout = new QHBoxLayout();
        iconLayout.AddWidget(iconLabel);
        iconLayout.AddWidget(iconComboBox);
        iconLayout.AddStretch();
        iconLayout.AddWidget(showIconCheckBox);
        iconGroupBox.Layout = iconLayout;
    }

    void CreateMessageGroupBox()
    {
        messageGroupBox = new QGroupBox("Balloon Message");

        typeLabel = new QLabel("Type:");

        typeComboBox = new QComboBox();
        typeComboBox.AddItem("None", new QVariant((int)QSystemTrayIcon.MessageIcon.NoIcon));
        typeComboBox.AddItem(StandardIcon(QMessageBox.Icon.Information), "Information",
            new QVariant((int)QSystemTrayIcon.MessageIcon.Information));
        typeComboBox.AddItem(StandardIcon(QMessageBox.Icon.Warning), "Warning",
            new QVariant((int)QSystemTrayIcon.MessageIcon.Warning));
        typeComboBox.AddItem(StandardIcon(QMessageBox.Icon.Critical), "Critical",
            new QVariant((int)QSystemTrayIcon.MessageIcon.Critical));
        typeComboBox.AddItem(new QIcon(), "Custom icon", new QVariant(-1));
        typeComboBox.CurrentIndex = 1;

        durationLabel = new QLabel("Duration:");

        durationSpinBox = new QSpinBox();
        durationSpinBox.SetRange(5, 60);
        durationSpinBox.Suffix = " s";
        durationSpinBox.Value = 15;

        durationWarningLabel = new QLabel("(some systems might ignore this hint)");
        durationWarningLabel.Indent = 10;

        titleLabel = new QLabel("Title:");

        titleEdit = new QLineEdit("Cannot connect to network");

        bodyLabel = new QLabel("Body:");

        bodyEdit = new QTextEdit();
        bodyEdit.SetPlainText("Don't believe me. Honestly, I don't have a clue.\n"
                              + "Click this balloon for details.");

        showMessageButton = new QPushButton("Show Message");
        showMessageButton.Default = true;

        var messageLayout = new QGridLayout();
        messageLayout.AddWidget(typeLabel, 0, 0);
        messageLayout.AddWidget(typeComboBox, 0, 1, 1, 2);
        messageLayout.AddWidget(durationLabel, 1, 0);
        messageLayout.AddWidget(durationSpinBox, 1, 1);
        messageLayout.AddWidget(durationWarningLabel, 1, 2, 1, 3);
        messageLayout.AddWidget(titleLabel, 2, 0);
        messageLayout.AddWidget(titleEdit, 2, 1, 1, 4);
        messageLayout.AddWidget(bodyLabel, 3, 0);
        messageLayout.AddWidget(bodyEdit, 3, 1, 2, 4);
        messageLayout.AddWidget(showMessageButton, 5, 4);
        messageLayout.SetColumnStretch(3, 1);
        messageLayout.SetRowStretch(4, 1);
        messageGroupBox.Layout = messageLayout;
    }

    void CreateActions()
    {
        minimizeAction = new QAction("Mi&nimize", this);
        minimizeAction.TriggeredSignal.Connect(_ => Hide());

        maximizeAction = new QAction("Ma&ximize", this);
        maximizeAction.TriggeredSignal.Connect(_ => ShowMaximized());

        restoreAction = new QAction("&Restore", this);
        restoreAction.TriggeredSignal.Connect(_ => ShowNormal());

        quitAction = new QAction("&Quit", this);
        quitAction.TriggeredSignal.Connect(_ => QApplication.Quit());
    }

    void CreateTrayIcon()
    {
        trayIconMenu = new QMenu(this);
        trayIconMenu.AddAction(minimizeAction);
        trayIconMenu.AddAction(maximizeAction);
        trayIconMenu.AddAction(restoreAction);
        trayIconMenu.AddSeparator();
        trayIconMenu.AddAction(quitAction);

        trayIcon = new QSystemTrayIcon(this);
        trayIcon.ContextMenu = trayIconMenu;
    }
}
