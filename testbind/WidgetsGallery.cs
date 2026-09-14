using System;
using System.Collections.Generic;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

public class WidgetGalleryWindow : QDialog
{
    readonly List<object> keepAlive = new();
    QProgressBar progressBar;
    QTimer progressTimer;

    public WidgetGalleryWindow() : base(null, 0)
    {
        var styleComboBox = keep(new QComboBox());
        styleComboBox.AddItems(QStyleFactory.Keys);
        var styleLabel = keep(new QLabel("&Style:"));
        styleLabel.Buddy = styleComboBox;

        var helpLabel = keep(new QLabel("Hover over the widgets to explore standard Qt Widgets."));

        var disableWidgetsCheckBox = keep(new QCheckBox("&Disable widgets"));

        var buttonsGroupBox = keep(CreateButtonsGroupBox());
        var itemViewTabWidget = keep(CreateItemViewTabWidget());
        var simpleInputWidgetsGroupBox = keep(CreateSimpleInputWidgetsGroupBox());
        var textToolBox = keep(CreateTextToolBox());
        progressBar = keep(CreateProgressBar());

        styleComboBox.TextActivatedSignal.Connect(ChangeStyle);

        disableWidgetsCheckBox.ToggledSignal.Connect(b =>
        {
            buttonsGroupBox.Enabled = !b;
            simpleInputWidgetsGroupBox.Enabled = !b;
            itemViewTabWidget.Enabled = !b;
            textToolBox.Enabled = !b;
        });

        var topLayout = keep(new QHBoxLayout());
        var appearanceLayout = keep(new QGridLayout());
        appearanceLayout.AddWidget(styleLabel, 0, 0);
        appearanceLayout.AddWidget(styleComboBox, 0, 1);
        appearanceLayout.AddWidget(disableWidgetsCheckBox, 1, 0, 1, 2);
        topLayout.AddLayout(appearanceLayout);
        topLayout.AddStretch(1);
        topLayout.AddWidget(helpLabel);
        topLayout.AddStretch(1);

        var dialogButtonBox = keep(new QDialogButtonBox(
            (int)QDialogButtonBox.StandardButton.Help | (int)QDialogButtonBox.StandardButton.Close));
        dialogButtonBox.RejectedSignal.Connect(() => Close());

        var mainLayout = keep(new QGridLayout());
        mainLayout.AddLayout(topLayout, 0, 0, 1, 2);
        mainLayout.AddWidget(buttonsGroupBox, 1, 0);
        mainLayout.AddWidget(simpleInputWidgetsGroupBox, 1, 1);
        mainLayout.AddWidget(itemViewTabWidget, 2, 0);
        mainLayout.AddWidget(textToolBox, 2, 1);
        mainLayout.AddWidget(progressBar, 3, 0, 1, 2);
        mainLayout.AddWidget(dialogButtonBox, 4, 0, 1, 2);
        Layout = mainLayout;

        WindowTitle = "Widget Gallery";
        Resize(760, 640);
    }

    T keep<T>(T obj) where T : class
    {
        keepAlive.Add(obj);
        return obj;
    }

    void ChangeStyle(string styleName)
    {
        QApplication.SetStyle(styleName);
    }

    void AdvanceProgressBar()
    {
        int curVal = progressBar.Value;
        int maxVal = progressBar.Maximum;
        int next = curVal + (maxVal - curVal) / 100;
        progressBar.Value = next >= maxVal ? 0 : next;
    }

    QGroupBox CreateButtonsGroupBox()
    {
        var result = keep(new QGroupBox("Buttons"));

        var defaultPushButton = keep(new QPushButton("Default Push Button"));
        defaultPushButton.Default = true;

        var togglePushButton = keep(new QPushButton("Toggle Push Button"));
        togglePushButton.Checkable = true;
        togglePushButton.Checked = true;

        var flatPushButton = keep(new QPushButton("Flat Push Button"));
        flatPushButton.Flat = true;

        var toolButton = keep(new QToolButton());
        toolButton.Text = "Tool Button";

        var menuToolButton = keep(new QToolButton());
        menuToolButton.Text = "Menu Button";
        var toolMenu = keep(new QMenu(menuToolButton));
        menuToolButton.PopupMode = QToolButton.ToolButtonPopupMode.InstantPopup;
        keep(toolMenu.AddAction("Option"));
        keep(toolMenu.AddSeparator());
        var action = keep(toolMenu.AddAction("Checkable Option"));
        action.Checkable = true;
        menuToolButton.Menu = toolMenu;

        var toolButtonLayout = keep(new QHBoxLayout());
        toolButtonLayout.AddWidget(toolButton);
        toolButtonLayout.AddWidget(menuToolButton);

        var buttonLayout = keep(new QVBoxLayout());
        buttonLayout.AddWidget(defaultPushButton);
        buttonLayout.AddWidget(togglePushButton);
        buttonLayout.AddWidget(flatPushButton);
        buttonLayout.AddLayout(toolButtonLayout);
        buttonLayout.AddStretch(1);

        var radioButton1 = keep(new QRadioButton("Radio button 1"));
        var radioButton2 = keep(new QRadioButton("Radio button 2"));
        var radioButton3 = keep(new QRadioButton("Radio button 3"));
        radioButton1.Checked = true;

        var checkBox = keep(new QCheckBox("Tri-state check box"));
        checkBox.Tristate = true;
        checkBox.CheckState = Qt.CheckState.PartiallyChecked;

        var checkableLayout = keep(new QVBoxLayout());
        checkableLayout.AddWidget(radioButton1);
        checkableLayout.AddWidget(radioButton2);
        checkableLayout.AddWidget(radioButton3);
        checkableLayout.AddWidget(checkBox);
        checkableLayout.AddStretch(1);

        var mainLayout = keep(new QHBoxLayout());
        mainLayout.AddLayout(buttonLayout);
        mainLayout.AddLayout(checkableLayout);
        mainLayout.AddStretch();
        result.Layout = mainLayout;
        return result;
    }

    QTabWidget CreateItemViewTabWidget()
    {
        var result = keep(new QTabWidget());

        var treeWidget = keep(new QTreeWidget());
        treeWidget.SetHeaderLabels(new[] { "Tree View" });
        var root = new QTreeWidgetItem(new[] { "Root" });
        keepAlive.Add(root);
        keepAlive.Add(new QTreeWidgetItem(root, new[] { "Child 1" }));
        keepAlive.Add(new QTreeWidgetItem(root, new[] { "Child 2" }));
        treeWidget.AddTopLevelItem(root);
        treeWidget.ExpandAll();

        var tableWidget = keep(new QTableWidget(10, 10));

        var listWidget = keep(new QListWidget());
        listWidget.AddItem("Directory");
        listWidget.AddItem("Computer");
        listWidget.AddItem("Network");

        var iconModeListWidget = keep(new QListWidget());
        iconModeListWidget.viewMode = QListView.ViewMode.IconMode;
        iconModeListWidget.AddItem("Directory");
        iconModeListWidget.AddItem("Computer");
        iconModeListWidget.AddItem("Network");

        result.AddTab(EmbedIntoHBoxLayout(treeWidget), "&Tree View");
        result.AddTab(EmbedIntoHBoxLayout(tableWidget), "T&able");
        result.AddTab(EmbedIntoHBoxLayout(listWidget), "&List");
        result.AddTab(EmbedIntoHBoxLayout(iconModeListWidget), "&Icon Mode List");
        return result;
    }

    QGroupBox CreateSimpleInputWidgetsGroupBox()
    {
        var result = keep(new QGroupBox("Simple Input Widgets"));
        result.Checkable = true;
        result.Checked = true;

        var lineEdit = keep(new QLineEdit("s3cRe7"));
        lineEdit.ClearButtonEnabled = true;
        lineEdit.echoMode = QLineEdit.EchoMode.Password;

        var spinBox = keep(new QSpinBox());
        spinBox.Value = 50;

        var dateTimeEdit = keep(new QDateTimeEdit());
        dateTimeEdit.DateTime = QDateTime.CurrentDateTime();

        var slider = keep(new QSlider(Qt.Orientation.Horizontal));
        slider.Value = 40;

        var scrollBar = keep(new QScrollBar(Qt.Orientation.Horizontal));
        scrollBar.Value = 60;

        var dial = keep(new QDial());
        dial.Value = 30;
        dial.NotchesVisible = true;

        var layout = keep(new QGridLayout());
        layout.AddWidget(lineEdit, 0, 0, 1, 2);
        layout.AddWidget(spinBox, 1, 0, 1, 2);
        layout.AddWidget(dateTimeEdit, 2, 0, 1, 2);
        layout.AddWidget(slider, 3, 0);
        layout.AddWidget(scrollBar, 4, 0);
        layout.AddWidget(dial, 3, 1, 2, 1);
        result.Layout = layout;
        return result;
    }

    QToolBox CreateTextToolBox()
    {
        var result = keep(new QToolBox());

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

        var textEdit = keep(new QTextEdit(richText));
        var plainTextEdit = keep(new QPlainTextEdit(plainText));

        var systemInfoTextBrowser = keep(new QTextBrowser());
        systemInfoTextBrowser.SetHtml(
            "<html><head/><body><h3>System Info</h3><p>Qt 6.11 / QtSharp binding demo.</p></body></html>");

        result.AddItem(EmbedIntoHBoxLayout(textEdit), "Text Edit");
        result.AddItem(EmbedIntoHBoxLayout(plainTextEdit), "Plain Text Edit");
        result.AddItem(EmbedIntoHBoxLayout(systemInfoTextBrowser), "Text Browser");
        return result;
    }

    QProgressBar CreateProgressBar()
    {
        var result = keep(new QProgressBar());
        result.SetRange(0, 10000);
        result.Value = 0;

        progressTimer = keep(new QTimer(this));
        progressTimer.TimeoutSignal.Connect(AdvanceProgressBar);
        progressTimer.Start(1000);
        return result;
    }

    QWidget EmbedIntoHBoxLayout(QWidget w, int margin = 5)
    {
        var result = keep(new QWidget());
        var layout = keep(new QHBoxLayout());
        layout.SetContentsMargins(margin, margin, margin, margin);
        layout.AddWidget(w);
        result.Layout = layout;
        return result;
    }
}
