using System;
using QtSharp.QtCore;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

public class PreviewWindow : QWidget
{
    QTextEdit textEdit;
    QPushButton closeButton;

    public PreviewWindow(QWidget parent = null) : base(parent, 0)
    {
        textEdit = new QTextEdit();
        textEdit.ReadOnly = true;
        textEdit.lineWrapMode = QTextEdit.LineWrapMode.NoWrap;

        closeButton = new QPushButton("&Close");
        closeButton.ClickedSignal.Connect(_ => Close());

        var layout = new QVBoxLayout();
        layout.AddWidget(textEdit);
        layout.AddWidget(closeButton);
        Layout = layout;

        WindowTitle = "Preview";
    }

    public new void SetWindowFlags(int flags)
    {
        base.SetWindowFlags(flags);

        string text = "";
        int type = flags & (int)Qt.WindowType.WindowTypeMask;
        if (type == (int)Qt.WindowType.Window) text = "Qt::Window";
        else if (type == (int)Qt.WindowType.Dialog) text = "Qt::Dialog";
        else if (type == (int)Qt.WindowType.Sheet) text = "Qt::Sheet";
        else if (type == (int)Qt.WindowType.Drawer) text = "Qt::Drawer";
        else if (type == (int)Qt.WindowType.Popup) text = "Qt::Popup";
        else if (type == (int)Qt.WindowType.Tool) text = "Qt::Tool";
        else if (type == (int)Qt.WindowType.ToolTip) text = "Qt::ToolTip";
        else if (type == (int)Qt.WindowType.SplashScreen) text = "Qt::SplashScreen";

        if ((flags & (int)Qt.WindowType.MSWindowsFixedSizeDialogHint) != 0) text += "\n| Qt::MSWindowsFixedSizeDialogHint";
        if ((flags & (int)Qt.WindowType.X11BypassWindowManagerHint) != 0) text += "\n| Qt::X11BypassWindowManagerHint";
        if ((flags & (int)Qt.WindowType.FramelessWindowHint) != 0) text += "\n| Qt::FramelessWindowHint";
        if ((flags & (int)Qt.WindowType.NoDropShadowWindowHint) != 0) text += "\n| Qt::NoDropShadowWindowHint";
        if ((flags & (int)Qt.WindowType.WindowTitleHint) != 0) text += "\n| Qt::WindowTitleHint";
        if ((flags & (int)Qt.WindowType.WindowSystemMenuHint) != 0) text += "\n| Qt::WindowSystemMenuHint";
        if ((flags & (int)Qt.WindowType.WindowMinimizeButtonHint) != 0) text += "\n| Qt::WindowMinimizeButtonHint";
        if ((flags & (int)Qt.WindowType.WindowMaximizeButtonHint) != 0) text += "\n| Qt::WindowMaximizeButtonHint";
        if ((flags & (int)Qt.WindowType.WindowCloseButtonHint) != 0) text += "\n| Qt::WindowCloseButtonHint";
        if ((flags & (int)Qt.WindowType.WindowContextHelpButtonHint) != 0) text += "\n| Qt::WindowContextHelpButtonHint";
        if ((flags & (int)Qt.WindowType.WindowShadeButtonHint) != 0) text += "\n| Qt::WindowShadeButtonHint";
        if ((flags & (int)Qt.WindowType.WindowStaysOnTopHint) != 0) text += "\n| Qt::WindowStaysOnTopHint";
        if ((flags & (int)Qt.WindowType.WindowStaysOnBottomHint) != 0) text += "\n| Qt::WindowStaysOnBottomHint";
        if ((flags & (int)Qt.WindowType.CustomizeWindowHint) != 0) text += "\n| Qt::CustomizeWindowHint";

        textEdit.SetPlainText(text);
    }
}

public class WindowFlagsWindow : QWidget
{
    PreviewWindow previewWindow;

    QGroupBox typeGroupBox;
    QGroupBox hintsGroupBox;
    QPushButton quitButton;

    QRadioButton windowRadioButton;
    QRadioButton dialogRadioButton;
    QRadioButton sheetRadioButton;
    QRadioButton drawerRadioButton;
    QRadioButton popupRadioButton;
    QRadioButton toolRadioButton;
    QRadioButton toolTipRadioButton;
    QRadioButton splashScreenRadioButton;

    QCheckBox msWindowsFixedSizeDialogCheckBox;
    QCheckBox x11BypassWindowManagerCheckBox;
    QCheckBox framelessWindowCheckBox;
    QCheckBox windowNoShadowCheckBox;
    QCheckBox windowTitleCheckBox;
    QCheckBox windowSystemMenuCheckBox;
    QCheckBox windowMinimizeButtonCheckBox;
    QCheckBox windowMaximizeButtonCheckBox;
    QCheckBox windowCloseButtonCheckBox;
    QCheckBox windowContextHelpButtonCheckBox;
    QCheckBox windowShadeButtonCheckBox;
    QCheckBox windowStaysOnTopCheckBox;
    QCheckBox windowStaysOnBottomCheckBox;
    QCheckBox customizeWindowHintCheckBox;

    public WindowFlagsWindow() : base(null, 0)
    {
        previewWindow = new PreviewWindow(this);

        CreateTypeGroupBox();
        CreateHintsGroupBox();

        quitButton = new QPushButton("&Quit");
        quitButton.ClickedSignal.Connect(_ => QApplication.Quit());

        var bottomLayout = new QHBoxLayout();
        bottomLayout.AddStretch();
        bottomLayout.AddWidget(quitButton);

        var mainLayout = new QHBoxLayout();
        mainLayout.AddWidget(typeGroupBox);
        mainLayout.AddWidget(hintsGroupBox);
        mainLayout.AddLayout(bottomLayout);
        Layout = mainLayout;

        WindowTitle = "Window Flags";
        UpdatePreview();
    }

    void UpdatePreview()
    {
        int flags = 0;

        if (windowRadioButton.Checked) flags = (int)Qt.WindowType.Window;
        else if (dialogRadioButton.Checked) flags = (int)Qt.WindowType.Dialog;
        else if (sheetRadioButton.Checked) flags = (int)Qt.WindowType.Sheet;
        else if (drawerRadioButton.Checked) flags = (int)Qt.WindowType.Drawer;
        else if (popupRadioButton.Checked) flags = (int)Qt.WindowType.Popup;
        else if (toolRadioButton.Checked) flags = (int)Qt.WindowType.Tool;
        else if (toolTipRadioButton.Checked) flags = (int)Qt.WindowType.ToolTip;
        else if (splashScreenRadioButton.Checked) flags = (int)Qt.WindowType.SplashScreen;

        if (msWindowsFixedSizeDialogCheckBox.Checked) flags |= (int)Qt.WindowType.MSWindowsFixedSizeDialogHint;
        if (x11BypassWindowManagerCheckBox.Checked) flags |= (int)Qt.WindowType.X11BypassWindowManagerHint;
        if (framelessWindowCheckBox.Checked) flags |= (int)Qt.WindowType.FramelessWindowHint;
        if (windowNoShadowCheckBox.Checked) flags |= (int)Qt.WindowType.NoDropShadowWindowHint;
        if (windowTitleCheckBox.Checked) flags |= (int)Qt.WindowType.WindowTitleHint;
        if (windowSystemMenuCheckBox.Checked) flags |= (int)Qt.WindowType.WindowSystemMenuHint;
        if (windowMinimizeButtonCheckBox.Checked) flags |= (int)Qt.WindowType.WindowMinimizeButtonHint;
        if (windowMaximizeButtonCheckBox.Checked) flags |= (int)Qt.WindowType.WindowMaximizeButtonHint;
        if (windowCloseButtonCheckBox.Checked) flags |= (int)Qt.WindowType.WindowCloseButtonHint;
        if (windowContextHelpButtonCheckBox.Checked) flags |= (int)Qt.WindowType.WindowContextHelpButtonHint;
        if (windowShadeButtonCheckBox.Checked) flags |= (int)Qt.WindowType.WindowShadeButtonHint;
        if (windowStaysOnTopCheckBox.Checked) flags |= (int)Qt.WindowType.WindowStaysOnTopHint;
        if (windowStaysOnBottomCheckBox.Checked) flags |= (int)Qt.WindowType.WindowStaysOnBottomHint;
        if (customizeWindowHintCheckBox.Checked) flags |= (int)Qt.WindowType.CustomizeWindowHint;

        previewWindow.SetWindowFlags(flags);
        previewWindow.ShowNormal();
    }

    void CreateTypeGroupBox()
    {
        typeGroupBox = new QGroupBox("Type");

        windowRadioButton = CreateRadioButton("Window");
        dialogRadioButton = CreateRadioButton("Dialog");
        sheetRadioButton = CreateRadioButton("Sheet");
        drawerRadioButton = CreateRadioButton("Drawer");
        popupRadioButton = CreateRadioButton("Popup");
        toolRadioButton = CreateRadioButton("Tool");
        toolTipRadioButton = CreateRadioButton("Tooltip");
        splashScreenRadioButton = CreateRadioButton("Splash screen");
        windowRadioButton.Checked = true;

        var layout = new QGridLayout();
        layout.AddWidget(windowRadioButton, 0, 0);
        layout.AddWidget(dialogRadioButton, 1, 0);
        layout.AddWidget(sheetRadioButton, 2, 0);
        layout.AddWidget(drawerRadioButton, 3, 0);
        layout.AddWidget(popupRadioButton, 0, 1);
        layout.AddWidget(toolRadioButton, 1, 1);
        layout.AddWidget(toolTipRadioButton, 2, 1);
        layout.AddWidget(splashScreenRadioButton, 3, 1);
        typeGroupBox.Layout = layout;
    }

    void CreateHintsGroupBox()
    {
        hintsGroupBox = new QGroupBox("Hints");

        msWindowsFixedSizeDialogCheckBox = CreateCheckBox("MS Windows fixed size dialog");
        x11BypassWindowManagerCheckBox = CreateCheckBox("X11 bypass window manager");
        framelessWindowCheckBox = CreateCheckBox("Frameless window");
        windowNoShadowCheckBox = CreateCheckBox("No drop shadow");
        windowTitleCheckBox = CreateCheckBox("Window title");
        windowSystemMenuCheckBox = CreateCheckBox("Window system menu");
        windowMinimizeButtonCheckBox = CreateCheckBox("Window minimize button");
        windowMaximizeButtonCheckBox = CreateCheckBox("Window maximize button");
        windowCloseButtonCheckBox = CreateCheckBox("Window close button");
        windowContextHelpButtonCheckBox = CreateCheckBox("Window context help button");
        windowShadeButtonCheckBox = CreateCheckBox("Window shade button");
        windowStaysOnTopCheckBox = CreateCheckBox("Window stays on top");
        windowStaysOnBottomCheckBox = CreateCheckBox("Window stays on bottom");
        customizeWindowHintCheckBox = CreateCheckBox("Customize window");

        var layout = new QGridLayout();
        layout.AddWidget(msWindowsFixedSizeDialogCheckBox, 0, 0);
        layout.AddWidget(x11BypassWindowManagerCheckBox, 1, 0);
        layout.AddWidget(framelessWindowCheckBox, 2, 0);
        layout.AddWidget(windowNoShadowCheckBox, 3, 0);
        layout.AddWidget(windowTitleCheckBox, 4, 0);
        layout.AddWidget(windowSystemMenuCheckBox, 5, 0);
        layout.AddWidget(customizeWindowHintCheckBox, 6, 0);
        layout.AddWidget(windowMinimizeButtonCheckBox, 0, 1);
        layout.AddWidget(windowMaximizeButtonCheckBox, 1, 1);
        layout.AddWidget(windowCloseButtonCheckBox, 2, 1);
        layout.AddWidget(windowContextHelpButtonCheckBox, 3, 1);
        layout.AddWidget(windowShadeButtonCheckBox, 4, 1);
        layout.AddWidget(windowStaysOnTopCheckBox, 5, 1);
        layout.AddWidget(windowStaysOnBottomCheckBox, 6, 1);
        hintsGroupBox.Layout = layout;
    }

    QCheckBox CreateCheckBox(string text)
    {
        var checkBox = new QCheckBox(text);
        checkBox.ClickedSignal.Connect(_ => UpdatePreview());
        return checkBox;
    }

    QRadioButton CreateRadioButton(string text)
    {
        var button = new QRadioButton(text);
        button.ClickedSignal.Connect(_ => UpdatePreview());
        return button;
    }
}
