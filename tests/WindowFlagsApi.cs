using System;
using System.Collections.Generic;

// PARKED - ported but deliberately NOT wired into testbind/Program.cs or the gate yet.
//
// All 115 assertions pass, but the process then exits with 0xC000041D about 3 times in 10: the
// faulting instruction is inside qwindows.dll, reached from PeekMessageW -> CallWindowProcW ->
// KiUserCallbackDispatcher while the runtime tears the application down, i.e. Windows is still
// pumping a message for a window whose Qt-side data is already gone. It only shows up for this
// demo, which walks the preview window through every Qt::WindowType (including the ones the
// official example leaves out: Desktop, SubWindow, ForeignWindow, CoverWindow) and shows/hides it.
// Re-wire it once that teardown hazard is understood; a demo that fails a third of the time must
// not sit in the gate.
//
// The official widgets/windowflags example, ported onto the *new* pipeline. Everything
// (QWidget subclassing, the typed Qt.WindowFlags union, radio buttons, check boxes, group
// boxes, layouts, the read-only preview text edit) comes from model/qtsharp.api.json.
//
// Differences from the C++ original:
//   * the legacy binding hid QWidget.SetWindowFlags behind an int-typed overload; the new
//     model exposes the real typed signature QWidget.SetWindowFlags(Qt.WindowFlags), so the
//     preview drives the flags value directly (bitwise OR / HasFlag) instead of int masks.
//   * the official PreviewWindow overrides the (non-virtual) QWidget::setWindowFlags; the
//     managed port uses an ordinary "new" method of the same name plus a distinct accessor.
//   * the type list is extended from the official 8 radio buttons to all 12 Qt::WindowType
//     values that are valid to set on an ordinary widget (Desktop/SubWindow/ForeignWindow/
//     CoverWindow are included as the task asked; Qt may normalise some of them, which the
//     self test accounts for).
//   * no event loop: the demo builds and drives the whole window synchronously. Show/Hide is
//     exercised with QWidget.SetVisible / IsVisible, Quit is only wired (not clicked).
public sealed class WindowFlagsPreviewWindow : QWidget
{
    readonly QTextEdit textEdit;
    readonly QPushButton closeButton;

    public WindowFlagsPreviewWindow(QWidget parent = null) : base(parent)
    {
        SetWindowTitle("Preview");

        textEdit = new QTextEdit();
        textEdit.SetReadOnly(true);
        textEdit.SetLineWrapMode(QTextEdit.LineWrapMode.NoWrap);

        closeButton = new QPushButton("&Close");
        closeButton.ClickedSignal.Connect(_ => Close());

        var layout = new QVBoxLayout();
        layout.AddWidget(textEdit);
        layout.AddWidget(closeButton);
        SetLayout(layout);
    }

    // Hides QWidget.SetWindowFlags on purpose, exactly like the official PreviewWindow hides
    // the base method: after the flags are applied the description is refreshed.
    public new void SetWindowFlags(Qt.WindowFlags flags)
    {
        base.SetWindowFlags(flags);
        textEdit.SetPlainText(Describe(flags));
    }

    public string PlainText => textEdit.ToPlainText();

    public static string TypeName(int type)
    {
        if (type == (int)Qt.WindowType.Window) return "Qt::Window";
        if (type == (int)Qt.WindowType.Dialog) return "Qt::Dialog";
        if (type == (int)Qt.WindowType.Sheet) return "Qt::Sheet";
        if (type == (int)Qt.WindowType.Drawer) return "Qt::Drawer";
        if (type == (int)Qt.WindowType.Popup) return "Qt::Popup";
        if (type == (int)Qt.WindowType.Tool) return "Qt::Tool";
        if (type == (int)Qt.WindowType.ToolTip) return "Qt::ToolTip";
        if (type == (int)Qt.WindowType.SplashScreen) return "Qt::SplashScreen";
        if (type == (int)Qt.WindowType.Desktop) return "Qt::Desktop";
        if (type == (int)Qt.WindowType.SubWindow) return "Qt::SubWindow";
        if (type == (int)Qt.WindowType.ForeignWindow) return "Qt::ForeignWindow";
        if (type == (int)Qt.WindowType.CoverWindow) return "Qt::CoverWindow";
        return "";
    }

    // The official PreviewWindow::setWindowFlags text, reproduced line for line.
    public static string Describe(Qt.WindowFlags flags)
    {
        int type = (int)flags & (int)Qt.WindowType.WindowType_Mask;
        string text = TypeName(type);

        if (flags.HasFlag(Qt.WindowFlags.MSWindowsFixedSizeDialogHint))
            text += "\n| Qt::MSWindowsFixedSizeDialogHint";
        if (flags.HasFlag(Qt.WindowFlags.X11BypassWindowManagerHint))
            text += "\n| Qt::X11BypassWindowManagerHint";
        if (flags.HasFlag(Qt.WindowFlags.FramelessWindowHint))
            text += "\n| Qt::FramelessWindowHint";
        if (flags.HasFlag(Qt.WindowFlags.NoDropShadowWindowHint))
            text += "\n| Qt::NoDropShadowWindowHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowTitleHint))
            text += "\n| Qt::WindowTitleHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowSystemMenuHint))
            text += "\n| Qt::WindowSystemMenuHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowMinimizeButtonHint))
            text += "\n| Qt::WindowMinimizeButtonHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowMaximizeButtonHint))
            text += "\n| Qt::WindowMaximizeButtonHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowCloseButtonHint))
            text += "\n| Qt::WindowCloseButtonHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowContextHelpButtonHint))
            text += "\n| Qt::WindowContextHelpButtonHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowShadeButtonHint))
            text += "\n| Qt::WindowShadeButtonHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowStaysOnTopHint))
            text += "\n| Qt::WindowStaysOnTopHint";
        if (flags.HasFlag(Qt.WindowFlags.WindowStaysOnBottomHint))
            text += "\n| Qt::WindowStaysOnBottomHint";
        if (flags.HasFlag(Qt.WindowFlags.CustomizeWindowHint))
            text += "\n| Qt::CustomizeWindowHint";

        return text;
    }
}

public sealed class WindowFlagsDemoWindow : QWidget
{
    readonly WindowFlagsPreviewWindow previewWindow;
    readonly QGroupBox typeGroupBox;
    readonly QGroupBox hintsGroupBox;
    readonly QPushButton quitButton;
    readonly QPushButton showButton;

    readonly List<QRadioButton> typeButtons = new List<QRadioButton>();
    readonly List<Qt.WindowType> typeValues = new List<Qt.WindowType>();
    readonly List<QCheckBox> hintBoxes = new List<QCheckBox>();
    readonly List<Qt.WindowFlags> hintValues = new List<Qt.WindowFlags>();

    public WindowFlagsDemoWindow()
    {
        previewWindow = new WindowFlagsPreviewWindow();

        typeGroupBox = new QGroupBox("Type");
        hintsGroupBox = new QGroupBox("Hints");
        CreateTypeGroupBox();
        CreateHintsGroupBox();

        quitButton = new QPushButton("&Quit");
        // Quit from a menu action, with no application reference in reach - the PyQt
        // spelling QApplication.instance().quit(), which reaches the one application object.
        quitButton.ClickedSignal.Connect(_ => QCoreApplication.Instance().Quit());

        showButton = new QPushButton("Hide");
        showButton.ClickedSignal.Connect(_ => TogglePreview());

        var bottomLayout = new QHBoxLayout();
        bottomLayout.AddStretch();
        bottomLayout.AddWidget(showButton);
        bottomLayout.AddWidget(quitButton);

        var mainLayout = new QHBoxLayout();
        mainLayout.AddWidget(typeGroupBox);
        mainLayout.AddWidget(hintsGroupBox);
        mainLayout.AddLayout(bottomLayout);
        SetLayout(mainLayout);

        SetWindowTitle("Window Flags (new pipeline)");
        UpdatePreview();
    }

    public WindowFlagsPreviewWindow Preview => previewWindow;
    public QPushButton ShowButton => showButton;
    public QPushButton QuitButton => quitButton;
    public IReadOnlyList<QRadioButton> TypeButtons => typeButtons;
    public IReadOnlyList<Qt.WindowType> TypeValues => typeValues;
    public IReadOnlyList<QCheckBox> HintBoxes => hintBoxes;
    public IReadOnlyList<Qt.WindowFlags> HintValues => hintValues;

    public QRadioButton TypeButton(Qt.WindowType type) => typeButtons[typeValues.IndexOf(type)];
    public QCheckBox HintBox(Qt.WindowFlags hint) => hintBoxes[hintValues.IndexOf(hint)];

    void CreateTypeGroupBox()
    {
        var layout = new QGridLayout();
        AddType(layout, Qt.WindowType.Window, "Window", 0, 0);
        AddType(layout, Qt.WindowType.Dialog, "Dialog", 1, 0);
        AddType(layout, Qt.WindowType.Sheet, "Sheet", 2, 0);
        AddType(layout, Qt.WindowType.Drawer, "Drawer", 3, 0);
        AddType(layout, Qt.WindowType.Popup, "Popup", 0, 1);
        AddType(layout, Qt.WindowType.Tool, "Tool", 1, 1);
        AddType(layout, Qt.WindowType.ToolTip, "Tooltip", 2, 1);
        AddType(layout, Qt.WindowType.SplashScreen, "Splash screen", 3, 1);
        AddType(layout, Qt.WindowType.Desktop, "Desktop", 4, 0);
        AddType(layout, Qt.WindowType.SubWindow, "SubWindow", 5, 0);
        AddType(layout, Qt.WindowType.ForeignWindow, "Foreign window", 4, 1);
        AddType(layout, Qt.WindowType.CoverWindow, "Cover window", 5, 1);
        typeGroupBox.SetLayout(layout);
        typeButtons[0].SetChecked(true);
    }

    void AddType(QGridLayout layout, Qt.WindowType type, string label, int row, int column)
    {
        var button = new QRadioButton(label);
        button.ClickedSignal.Connect(_ => UpdatePreview());
        layout.AddWidget(button, row, column);
        typeButtons.Add(button);
        typeValues.Add(type);
    }

    void CreateHintsGroupBox()
    {
        var layout = new QGridLayout();
        AddHint(layout, Qt.WindowFlags.MSWindowsFixedSizeDialogHint, "MS Windows fixed size dialog", 0, 0);
        AddHint(layout, Qt.WindowFlags.X11BypassWindowManagerHint, "X11 bypass window manager", 1, 0);
        AddHint(layout, Qt.WindowFlags.FramelessWindowHint, "Frameless window", 2, 0);
        AddHint(layout, Qt.WindowFlags.NoDropShadowWindowHint, "No drop shadow", 3, 0);
        AddHint(layout, Qt.WindowFlags.WindowTitleHint, "Window title", 4, 0);
        AddHint(layout, Qt.WindowFlags.WindowSystemMenuHint, "Window system menu", 5, 0);
        AddHint(layout, Qt.WindowFlags.CustomizeWindowHint, "Customize window", 6, 0);
        AddHint(layout, Qt.WindowFlags.WindowMinimizeButtonHint, "Window minimize button", 0, 1);
        AddHint(layout, Qt.WindowFlags.WindowMaximizeButtonHint, "Window maximize button", 1, 1);
        AddHint(layout, Qt.WindowFlags.WindowCloseButtonHint, "Window close button", 2, 1);
        AddHint(layout, Qt.WindowFlags.WindowContextHelpButtonHint, "Window context help button", 3, 1);
        AddHint(layout, Qt.WindowFlags.WindowShadeButtonHint, "Window shade button", 4, 1);
        AddHint(layout, Qt.WindowFlags.WindowStaysOnTopHint, "Window stays on top", 5, 1);
        AddHint(layout, Qt.WindowFlags.WindowStaysOnBottomHint, "Window stays on bottom", 6, 1);
        hintsGroupBox.SetLayout(layout);
    }

    void AddHint(QGridLayout layout, Qt.WindowFlags hint, string label, int row, int column)
    {
        var box = new QCheckBox(label);
        box.ClickedSignal.Connect(_ => UpdatePreview());
        layout.AddWidget(box, row, column);
        hintBoxes.Add(box);
        hintValues.Add(hint);
    }

    // The official ControllerWindow::updatePreview() merging: one type, then OR the hints.
    public Qt.WindowFlags CurrentFlags()
    {
        Qt.WindowFlags flags = 0;
        for (int i = 0; i < typeButtons.Count; i++)
        {
            if (typeButtons[i].IsChecked())
            {
                flags = (Qt.WindowFlags)(int)typeValues[i];
                break;
            }
        }

        for (int i = 0; i < hintBoxes.Count; i++)
        {
            if (hintBoxes[i].IsChecked())
                flags |= hintValues[i];
        }

        return flags;
    }

    public void UpdatePreview()
    {
        previewWindow.SetWindowFlags(CurrentFlags());

        var pos = previewWindow.Pos;
        if (pos.X() < 0)
            pos.SetX(0);
        if (pos.Y() < 0)
            pos.SetY(0);
        previewWindow.Move(pos);
        previewWindow.ShowNormal();
    }

    public void TogglePreview()
    {
        previewWindow.SetVisible(!previewWindow.IsVisible());
        showButton.SetText(previewWindow.IsVisible() ? "Hide" : "Show");
    }
}

// Builds the window, drives every radio button and check box through the generated signal
// layer, reads the typed flags back from the native QWidget and checks the preview text.
public static class WindowFlagsApi
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
        if (!ok)
            failures++;
    }

    static string HintText(Qt.WindowFlags hint)
    {
        if (hint == Qt.WindowFlags.MSWindowsFixedSizeDialogHint) return "Qt::MSWindowsFixedSizeDialogHint";
        if (hint == Qt.WindowFlags.X11BypassWindowManagerHint) return "Qt::X11BypassWindowManagerHint";
        if (hint == Qt.WindowFlags.FramelessWindowHint) return "Qt::FramelessWindowHint";
        if (hint == Qt.WindowFlags.NoDropShadowWindowHint) return "Qt::NoDropShadowWindowHint";
        if (hint == Qt.WindowFlags.WindowTitleHint) return "Qt::WindowTitleHint";
        if (hint == Qt.WindowFlags.WindowSystemMenuHint) return "Qt::WindowSystemMenuHint";
        if (hint == Qt.WindowFlags.WindowMinimizeButtonHint) return "Qt::WindowMinimizeButtonHint";
        if (hint == Qt.WindowFlags.WindowMaximizeButtonHint) return "Qt::WindowMaximizeButtonHint";
        if (hint == Qt.WindowFlags.WindowCloseButtonHint) return "Qt::WindowCloseButtonHint";
        if (hint == Qt.WindowFlags.WindowContextHelpButtonHint) return "Qt::WindowContextHelpButtonHint";
        if (hint == Qt.WindowFlags.WindowShadeButtonHint) return "Qt::WindowShadeButtonHint";
        if (hint == Qt.WindowFlags.WindowStaysOnTopHint) return "Qt::WindowStaysOnTopHint";
        if (hint == Qt.WindowFlags.WindowStaysOnBottomHint) return "Qt::WindowStaysOnBottomHint";
        if (hint == Qt.WindowFlags.CustomizeWindowHint) return "Qt::CustomizeWindowHint";
        return "";
    }

    static int TypeBits(Qt.WindowFlags flags) => (int)flags & (int)Qt.WindowType.WindowType_Mask;

    public static int Run(QApplication app)
    {
        failures = 0;
        var window = new WindowFlagsDemoWindow();
        var preview = window.Preview;

        Check(window.TypeButtons.Count == 12, $"the Type group has 12 radio buttons (got {window.TypeButtons.Count})");
        Check(window.HintBoxes.Count == 14, $"the Hints group has 14 check boxes (got {window.HintBoxes.Count})");

        // The official default: the Window radio button is checked and the preview describes it.
        Check(window.TypeButton(Qt.WindowType.Window).IsChecked(), "Window is the default type");
        Check(TypeBits(preview.WindowFlags()) == (int)Qt.WindowType.Window,
            $"default flags carry Qt::Window (got 0x{TypeBits(preview.WindowFlags()):X})");
        Check(preview.WindowFlags().HasFlag(Qt.WindowFlags.Window),
            "the Window flag bit is set on the default flags");
        Check(preview.PlainText == "Qt::Window", $"the preview describes Qt::Window (got '{preview.PlainText}')");

        // Every type radio button: clicking it must make exactly that type appear, and the
        // mutually exclusive type nibble must equal the chosen value (no bit from another type).
        // Qt itself normalises two of the twelve on an ordinary top-level widget: Qt::Desktop
        // is coerced to Qt::Window, and Qt::SubWindow gains the Qt::Window bit. Those two are
        // checked against what Qt actually reports; the other ten round-trip untouched.
        Qt.WindowType[] types =
        {
            Qt.WindowType.Window, Qt.WindowType.Dialog, Qt.WindowType.Sheet, Qt.WindowType.Drawer,
            Qt.WindowType.Popup, Qt.WindowType.Tool, Qt.WindowType.ToolTip, Qt.WindowType.SplashScreen,
            Qt.WindowType.Desktop, Qt.WindowType.SubWindow, Qt.WindowType.ForeignWindow, Qt.WindowType.CoverWindow,
        };

        foreach (var type in types)
        {
            window.TypeButton(type).Click();
            int got = TypeBits(preview.WindowFlags());
            int expectedNibble = type == Qt.WindowType.Desktop
                ? (int)Qt.WindowType.Window
                : type == Qt.WindowType.SubWindow
                    ? (int)Qt.WindowType.SubWindow | (int)Qt.WindowType.Window
                    : (int)type;
            Check(got == expectedNibble,
                $"selecting {WindowFlagsPreviewWindow.TypeName((int)type)} yields type nibble 0x{expectedNibble:X} (got 0x{got:X})");
            Check(preview.PlainText == WindowFlagsPreviewWindow.TypeName((int)type),
                $"the preview records the requested {WindowFlagsPreviewWindow.TypeName((int)type)} (got '{preview.PlainText}')");
        }

        // Back to a known, well-behaved type before the hint sweep. The window the platform
        // hands back for a plain Qt::Window is the baseline every "off" toggle must restore.
        window.TypeButton(Qt.WindowType.Window).Click();
        int baseline = (int)preview.WindowFlags();
        Check(baseline != 0, $"the platform gives a plain Qt::Window a non-empty flag set (0x{baseline:X8})");

        // Each hint toggles its own bit in the native QWidget and its own line in the preview.
        // Note that Windows injects a default set of hints for a window, so "toggled off" means
        // "back to the platform baseline", not "bit cleared".
        Qt.WindowFlags[] hints =
        {
            Qt.WindowFlags.MSWindowsFixedSizeDialogHint, Qt.WindowFlags.X11BypassWindowManagerHint,
            Qt.WindowFlags.FramelessWindowHint, Qt.WindowFlags.NoDropShadowWindowHint,
            Qt.WindowFlags.WindowTitleHint, Qt.WindowFlags.WindowSystemMenuHint,
            Qt.WindowFlags.WindowMinimizeButtonHint, Qt.WindowFlags.WindowMaximizeButtonHint,
            Qt.WindowFlags.WindowCloseButtonHint, Qt.WindowFlags.WindowContextHelpButtonHint,
            Qt.WindowFlags.WindowShadeButtonHint, Qt.WindowFlags.WindowStaysOnTopHint,
            Qt.WindowFlags.WindowStaysOnBottomHint, Qt.WindowFlags.CustomizeWindowHint,
        };

        foreach (var hint in hints)
        {
            var box = window.HintBox(hint);
            box.Click();
            int on = (int)preview.WindowFlags();
            Check(preview.WindowFlags().HasFlag(hint),
                $"{HintText(hint)} is set after toggling the check box on");
            Check(on != baseline,
                $"{HintText(hint)} changes the native flags (0x{baseline:X8} -> 0x{on:X8})");
            Check(preview.PlainText.Contains("| " + HintText(hint)),
                $"{HintText(hint)} appears in the preview text");

            box.Click();
            int off = (int)preview.WindowFlags();
            Check(off == baseline,
                $"{HintText(hint)} returns the native flags to the baseline (got 0x{off:X8})");
            Check(!preview.PlainText.Contains("| " + HintText(hint)),
                $"{HintText(hint)} disappears from the preview text");
        }

        // A deliberate combination: Dialog plus three hints, checked bit for bit.
        window.TypeButton(Qt.WindowType.Dialog).Click();
        window.HintBox(Qt.WindowFlags.FramelessWindowHint).Click();
        window.HintBox(Qt.WindowFlags.WindowStaysOnTopHint).Click();
        window.HintBox(Qt.WindowFlags.NoDropShadowWindowHint).Click();

        var expected = (Qt.WindowFlags)(int)Qt.WindowType.Dialog
            | Qt.WindowFlags.FramelessWindowHint
            | Qt.WindowFlags.WindowStaysOnTopHint
            | Qt.WindowFlags.NoDropShadowWindowHint;
        var combined = preview.WindowFlags();
        Check(TypeBits(combined) == (int)Qt.WindowType.Dialog, "the combined flags keep exactly Qt::Dialog");
        Check(combined.HasFlag(Qt.WindowFlags.FramelessWindowHint), "the combined flags carry FramelessWindowHint");
        Check(combined.HasFlag(Qt.WindowFlags.WindowStaysOnTopHint), "the combined flags carry WindowStaysOnTopHint");
        Check(combined.HasFlag(Qt.WindowFlags.NoDropShadowWindowHint), "the combined flags carry NoDropShadowWindowHint");
        Check((int)combined == (int)expected, $"the combined value is exactly the OR of the chosen bits (got 0x{(int)combined:X})");
        Check(combined.HasFlag(Qt.WindowFlags.Window), "Dialog still reports the Window bit (Qt::Dialog includes it)");
        Check(preview.PlainText ==
              "Qt::Dialog\n| Qt::FramelessWindowHint\n| Qt::NoDropShadowWindowHint\n| Qt::WindowStaysOnTopHint",
            "the preview lists the type and every hint in order");

        // Clear the combination again so the Show/Hide test starts from a plain window.
        window.HintBox(Qt.WindowFlags.FramelessWindowHint).Click();
        window.HintBox(Qt.WindowFlags.WindowStaysOnTopHint).Click();
        window.HintBox(Qt.WindowFlags.NoDropShadowWindowHint).Click();

        // The Show/Hide button toggles the preview and relabels itself, exactly like the official.
        Check(preview.IsVisible(), "the preview is shown after updatePreview()");
        Check(window.ShowButton.Text == "Hide", $"the Show/Hide button starts as Hide (got '{window.ShowButton.Text}')");
        window.ShowButton.Click();
        Check(!preview.IsVisible(), "clicking Show/Hide hides the preview");
        Check(window.ShowButton.Text == "Show", $"the button relabels to Show (got '{window.ShowButton.Text}')");
        window.ShowButton.Click();
        Check(preview.IsVisible(), "clicking Show/Hide again shows the preview");
        Check(window.ShowButton.Text == "Hide", $"the button relabels back to Hide (got '{window.ShowButton.Text}')");

        // Quit stays wired to the runtime; the self test does not click it.
        Check(window.QuitButton.Text == "&Quit" && window.QuitButton.IsEnabled(),
            "the Quit button is present and enabled");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
