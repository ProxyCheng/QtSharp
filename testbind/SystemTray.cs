using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;

public class SystemTrayWindow : QDialog
{
    QGroupBox messageGroupBox;
    QComboBox typeComboBox;
    QSpinBox durationSpinBox;
    QLineEdit titleEdit;
    QTextEdit bodyEdit;
    QCheckBox showIconCheckBox;

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

        CreateActions();
        CreateTrayIcon();

        var mainLayout = new QVBoxLayout();
        mainLayout.AddWidget(iconGroupBox);
        mainLayout.AddWidget(messageGroupBox);
        Layout = mainLayout;

        typeComboBox.CurrentIndex = 0;
        trayIcon.Show();

        WindowTitle = "Systray";
        Resize(400, 300);
    }

    QGroupBox iconGroupBox;

    void CreateIconGroupBox()
    {
        iconGroupBox = new QGroupBox("Tray icon");
        showIconCheckBox = new QCheckBox("Show icon in system tray");
        showIconCheckBox.Checked = true;
        showIconCheckBox.ToggledSignal.Connect(b => { if (trayIcon != null) trayIcon.Visible = b; });

        var layout = new QVBoxLayout();
        layout.AddWidget(showIconCheckBox);
        iconGroupBox.Layout = layout;
    }

    void CreateMessageGroupBox()
    {
        messageGroupBox = new QGroupBox("Balloon message");

        var typeLabel = new QLabel("Type:");
        typeComboBox = new QComboBox();
        typeComboBox.AddItems(new[] { "Information", "Warning", "Critical" });

        var durationLabel = new QLabel("Duration (seconds):");
        durationSpinBox = new QSpinBox();
        durationSpinBox.SetRange(5, 60);
        durationSpinBox.Value = 15;
        durationSpinBox.Suffix = " s";

        var titleLabel = new QLabel("Title:");
        titleEdit = new QLineEdit("Cannot connect to network");

        var bodyLabel = new QLabel("Body:");
        bodyEdit = new QTextEdit();
        bodyEdit.SetPlainText("Don't believe me. Honestly, I don't have a clue.\nClick this balloon for details.");

        var showMessageButton = new QPushButton("Show message");
        showMessageButton.ClickedSignal.Connect(_ => ShowMessage());

        var layout = new QGridLayout();
        layout.AddWidget(typeLabel, 0, 0);
        layout.AddWidget(typeComboBox, 0, 1);
        layout.AddWidget(durationLabel, 1, 0);
        layout.AddWidget(durationSpinBox, 1, 1);
        layout.AddWidget(titleLabel, 2, 0);
        layout.AddWidget(titleEdit, 2, 1);
        layout.AddWidget(bodyLabel, 3, 0);
        layout.AddWidget(bodyEdit, 3, 1, 1, 1);
        layout.AddWidget(showMessageButton, 4, 0, 1, 2);
        messageGroupBox.Layout = layout;
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
        trayIconMenu = new QMenu();
        trayIconMenu.AddAction(minimizeAction);
        trayIconMenu.AddAction(maximizeAction);
        trayIconMenu.AddAction(restoreAction);
        trayIconMenu.AddSeparator();
        trayIconMenu.AddAction(quitAction);

        trayIcon = new QSystemTrayIcon(this);
        trayIcon.ContextMenu = trayIconMenu;
        trayIcon.ToolTip = "Systray";
        trayIcon.MessageClickedSignal.Connect(MessageClicked);

        using (var pm = QMessageBox.StandardIcon(QMessageBox.Icon.Information))
            trayIcon.Icon = new QIcon(pm);
    }

    void ShowMessage()
    {
        showIconCheckBox.Checked = true;
        var msgIcon = (QSystemTrayIcon.MessageIcon)(typeComboBox.CurrentIndex + 1);
        trayIcon.ShowMessage(titleEdit.Text, bodyEdit.ToPlainText(), msgIcon, durationSpinBox.Value * 1000);
    }

    void MessageClicked()
    {
        QMessageBox.Information(this, "Systray",
            "Sorry, I already gave what help I could.\nMaybe you should try asking a human?",
            (int)QMessageBox.StandardButton.Ok);
    }
}
