using System;
using QtSharp.Api;

// The official widgets/sliders example, ported onto the *new* pipeline. The structure mirrors
// examples/widgets/widgets/sliders/{window,slidersgroup}.cpp exactly: a reusable SlidersGroup
// group box (a QSlider, a QScrollBar and a QDial wired value -> value -> value) plus a Controls
// group box driving minimum/maximum/value and the inverted appearance/key-binding switches.
//
// Naming notes for the generated binding:
//   * Q_PROPERTY becomes a PascalCase property while the C++ accessor keeps its name, so we write
//     slider.Value / slider.InvertedAppearance but slider.tickPosition() / SetTickPosition(...);
//   * QSlider::TickPosition is a nested enum, so the tickPosition property was skipped (its name
//     would collide with the enum) and only the accessors survived;
//   * QBoxLayout::Direction is likewise a nested enum, so the layout accessors are direction() and
//     SetDirection(...) - there is no Direction property;
//   * QSize/QRect are classes with accessor methods, not fields.
//
// Deviations from the C++ original:
//   * the official value echo is the "Current value:" QSpinBox; the requested echo *label* is added
//     next to it (updated from SlidersGroup::valueChanged) so the signal can be observed as text;
//   * resizeEvent() is the one place the port cannot rely on the platform delivering an event in a
//     loop-less test, so the handler is also invoked directly through the generated override.
public sealed class ApiSlidersGroup : QGroupBox
{
    readonly QSlider slider;
    readonly QScrollBar scrollBar;
    readonly QDial dial;
    readonly QBoxLayout slidersLayout;

    public event Action<int> ValueChanged;

    public ApiSlidersGroup(string title, QWidget parent = null) : base(title, parent)
    {
        // The C++ original relies on the default constructors (QSlider defaults to Vertical,
        // QScrollBar to Horizontal), which is exactly what the legacy C# demo did too.
        slider = new QSlider();
        slider.FocusPolicy = Qt.FocusPolicy.StrongFocus;
        slider.SetTickPosition(QSlider.TickPosition.TicksBothSides);
        slider.TickInterval = 10;
        slider.SingleStep = 1;

        scrollBar = new QScrollBar();
        scrollBar.FocusPolicy = Qt.FocusPolicy.StrongFocus;

        dial = new QDial();
        dial.FocusPolicy = Qt.FocusPolicy.StrongFocus;

        slider.ValueChangedSignal.Connect(v => scrollBar.Value = v);
        scrollBar.ValueChangedSignal.Connect(v => dial.Value = v);
        dial.ValueChangedSignal.Connect(v =>
        {
            slider.Value = v;
            ValueChanged?.Invoke(v);
        });

        slidersLayout = new QBoxLayout(QBoxLayout.Direction.LeftToRight);
        slidersLayout.AddWidget(slider);
        slidersLayout.AddWidget(scrollBar);
        slidersLayout.AddWidget(dial);
        SetLayout(slidersLayout);
    }

    public void SetValue(int value) => slider.Value = value;

    public void SetMinimum(int value)
    {
        slider.Minimum = value;
        scrollBar.Minimum = value;
        dial.Minimum = value;
    }

    public void SetMaximum(int value)
    {
        slider.Maximum = value;
        scrollBar.Maximum = value;
        dial.Maximum = value;
    }

    public void InvertAppearance(bool invert)
    {
        slider.InvertedAppearance = invert;
        scrollBar.InvertedAppearance = invert;
        dial.InvertedAppearance = invert;
    }

    public void InvertKeyBindings(bool invert)
    {
        slider.InvertedControls = invert;
        scrollBar.InvertedControls = invert;
        dial.InvertedControls = invert;
    }

    public void SetOrientation(Qt.Orientation orientation)
    {
        slidersLayout.SetDirection(orientation == Qt.Orientation.Horizontal
            ? QBoxLayout.Direction.TopToBottom
            : QBoxLayout.Direction.LeftToRight);
        scrollBar.Orientation = orientation;
        slider.Orientation = orientation;
    }

    // Exposed for the self-test.
    public QSlider Slider => slider;
    public QScrollBar Bar => scrollBar;
    public QDial Dial => dial;
    public QBoxLayout InnerLayout => slidersLayout;
}

public sealed class ApiSlidersWindow : QWidget
{
    readonly ApiSlidersGroup slidersGroup;
    QGroupBox controlsGroup;
    QCheckBox invertedAppearance;
    QCheckBox invertedKeyBindings;
    QSpinBox minimumSpinBox;
    QSpinBox maximumSpinBox;
    QSpinBox valueSpinBox;
    QLabel valueEcho;
    readonly QBoxLayout layout;

    public int ResizeEventCount;

    public ApiSlidersWindow() : base(null)
    {
        slidersGroup = new ApiSlidersGroup("Sliders");
        CreateControls("Controls");

        layout = new QBoxLayout(QBoxLayout.Direction.LeftToRight);
        layout.AddWidget(controlsGroup);
        layout.AddWidget(slidersGroup);
        SetLayout(layout);

        minimumSpinBox.Value = 0;
        maximumSpinBox.Value = 20;
        valueSpinBox.Value = 5;

        SetWindowTitle("Sliders (new pipeline)");
    }

    void CreateControls(string title)
    {
        controlsGroup = new QGroupBox(title);

        var minimumLabel = new QLabel("Minimum value:");
        var maximumLabel = new QLabel("Maximum value:");
        var valueLabel = new QLabel("Current value:");

        invertedAppearance = new QCheckBox("Inverted appearance");
        invertedKeyBindings = new QCheckBox("Inverted key bindings");

        minimumSpinBox = new QSpinBox();
        minimumSpinBox.SetRange(-100, 100);
        minimumSpinBox.SingleStep = 1;

        maximumSpinBox = new QSpinBox();
        maximumSpinBox.SetRange(-100, 100);
        maximumSpinBox.SingleStep = 1;

        valueSpinBox = new QSpinBox();
        valueSpinBox.SetRange(-100, 100);
        valueSpinBox.SingleStep = 1;

        // The echo label asked for by the task: the same value the "Current value:" spin box gets.
        valueEcho = new QLabel("Current value: 0");

        slidersGroup.ValueChanged += v =>
        {
            valueSpinBox.Value = v;
            valueEcho.SetText($"Current value: {v}");
        };
        valueSpinBox.ValueChangedSignal.Connect(_ => slidersGroup.SetValue(valueSpinBox.Value));
        minimumSpinBox.ValueChangedSignal.Connect(_ => slidersGroup.SetMinimum(minimumSpinBox.Value));
        maximumSpinBox.ValueChangedSignal.Connect(_ => slidersGroup.SetMaximum(maximumSpinBox.Value));
        invertedAppearance.ToggledSignal.Connect(b => slidersGroup.InvertAppearance(b));
        invertedKeyBindings.ToggledSignal.Connect(b => slidersGroup.InvertKeyBindings(b));

        var controlsLayout = new QGridLayout();
        controlsLayout.AddWidget(minimumLabel, 0, 0);
        controlsLayout.AddWidget(maximumLabel, 1, 0);
        controlsLayout.AddWidget(valueLabel, 2, 0);
        controlsLayout.AddWidget(minimumSpinBox, 0, 1);
        controlsLayout.AddWidget(maximumSpinBox, 1, 1);
        controlsLayout.AddWidget(valueSpinBox, 2, 1);
        controlsLayout.AddWidget(invertedAppearance, 0, 2);
        controlsLayout.AddWidget(invertedKeyBindings, 1, 2);
        controlsLayout.AddWidget(valueEcho, 3, 0, 1, 3);
        controlsGroup.SetLayout(controlsLayout);
    }

    // QWidget::resizeEvent is on the managed virtual whitelist, so Qt calls this on a real resize.
    // The base call is guarded so the self-test can also drive the handler directly through the
    // public override when no event loop is running (see ApiSlidersDemo).
    public override void ResizeEvent(QResizeEvent e)
    {
        ResizeEventCount++;

        if (Width == 0 || Height == 0)
            return;

        double aspectRatio = (double)Width / Height;

        if (aspectRatio < 1.0)
        {
            layout.SetDirection(QBoxLayout.Direction.TopToBottom);
            slidersGroup.SetOrientation(Qt.Orientation.Horizontal);
        }
        else if (aspectRatio > 1.0)
        {
            layout.SetDirection(QBoxLayout.Direction.LeftToRight);
            slidersGroup.SetOrientation(Qt.Orientation.Vertical);
        }

        if (e != null)
            base.ResizeEvent(e);
    }

    // Exposed for the self-test.
    public ApiSlidersGroup Group => slidersGroup;
    public QBoxLayout OuterLayout => layout;
    public QCheckBox InvertedAppearance => invertedAppearance;
    public QCheckBox InvertedKeyBindings => invertedKeyBindings;
    public QSpinBox MinimumSpin => minimumSpinBox;
    public QSpinBox MaximumSpin => maximumSpinBox;
    public QSpinBox ValueSpin => valueSpinBox;
    public QLabel ValueEcho => valueEcho;
}

// Builds the window, drives it through the generated signal layer and checks the state each
// handler should have produced. No event loop: every signal in this example is emitted
// synchronously by Qt when a value changes.
public static class ApiSlidersDemo
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

        var window = new ApiSlidersWindow();
        var group = window.Group;
        var slider = group.Slider;
        var bar = group.Bar;
        var dial = group.Dial;

        window.Show();

        // The spin boxes in the window constructor must have propagated through the signals.
        Check(slider.Value == 5, $"valueSpinBox(5) drove the slider's value (got {slider.Value})");
        Check(bar.Value == 5, $"valueSpinBox(5) drove the scroll bar's value (got {bar.Value})");
        Check(dial.Value == 5, $"valueSpinBox(5) drove the dial's value (got {dial.Value})");

        Check(slider.Minimum == 0 && slider.Maximum == 20,
            $"minimumSpinBox/maximumSpinBox set the slider's range (got {slider.Minimum}..{slider.Maximum})");
        Check(bar.Minimum == 0 && bar.Maximum == 20,
            $"the scroll bar's range followed (got {bar.Minimum}..{bar.Maximum})");
        Check(dial.Minimum == 0 && dial.Maximum == 20,
            $"the dial's range followed (got {dial.Minimum}..{dial.Maximum})");

        // The group's tick / step configuration, as in the constructor.
        Check(slider.tickPosition() == QSlider.TickPosition.TicksBothSides,
            $"tick position is TicksBothSides (got {slider.tickPosition()})");
        Check(slider.TickInterval == 10, $"tick interval is 10 (got {slider.TickInterval})");
        Check(slider.SingleStep == 1, $"single step is 1 (got {slider.SingleStep})");

        // Focus policy of all three widgets.
        Check(slider.FocusPolicy == Qt.FocusPolicy.StrongFocus,
            $"slider keeps StrongFocus (got {slider.FocusPolicy})");
        Check(bar.FocusPolicy == Qt.FocusPolicy.StrongFocus,
            $"scroll bar has StrongFocus (got {bar.FocusPolicy})");
        Check(dial.FocusPolicy == Qt.FocusPolicy.StrongFocus,
            $"dial has StrongFocus (got {dial.FocusPolicy})");

        // Dial-specific state.
        Check(!dial.Wrapping, "dial wrapping defaults to off");
        dial.Wrapping = true;
        Check(dial.Wrapping, "dial wrapping round-trips (set true)");
        dial.Wrapping = false;
        Check(!dial.Wrapping, "dial wrapping round-trips (set false)");
        Check(!dial.NotchesVisible, "dial notches default to hidden");
        dial.NotchesVisible = true;
        Check(dial.NotchesVisible, "dial notches round-trip (set visible)");

        // The slider -> scroll bar -> dial -> (group) valueChanged chain, and the echo label.
        slider.Value = 13;
        Check(bar.Value == 13, $"slider valueChanged reached the scroll bar (got {bar.Value})");
        Check(dial.Value == 13, $"scroll bar valueChanged reached the dial (got {dial.Value})");
        Check(window.ValueSpin.Value == 13, $"the window's value spin box followed the dial (got {window.ValueSpin.Value})");
        Check(window.ValueEcho.text() == "Current value: 13",
            $"the echo label followed the dial (got '{window.ValueEcho.text()}')");

        // ...and the other direction: the dial drives the slider.
        dial.Value = 17;
        Check(slider.Value == 17, $"dial valueChanged reached the slider (got {slider.Value})");
        Check(window.ValueEcho.text() == "Current value: 17",
            $"the echo label followed the dial upwards (got '{window.ValueEcho.text()}')");

        // Value spin box drives the group.
        window.ValueSpin.Value = 7;
        Check(slider.Value == 7, $"valueSpinBox drove the slider (got {slider.Value})");
        Check(dial.Value == 7, $"valueSpinBox drove the dial through the chain (got {dial.Value})");

        // Inverted appearance / key bindings through the check boxes.
        window.InvertedAppearance.SetChecked(true);
        Check(slider.InvertedAppearance && bar.InvertedAppearance && dial.InvertedAppearance,
            "invertedAppearance check box inverted all three widgets");
        window.InvertedAppearance.SetChecked(false);
        Check(!slider.InvertedAppearance && !bar.InvertedAppearance && !dial.InvertedAppearance,
            "invertedAppearance check box restored all three widgets");

        window.InvertedKeyBindings.SetChecked(true);
        Check(slider.InvertedControls && bar.InvertedControls && dial.InvertedControls,
            "invertedKeyBindings check box inverted the controls of all three widgets");
        window.InvertedKeyBindings.SetChecked(false);
        Check(!slider.InvertedControls && !bar.InvertedControls && !dial.InvertedControls,
            "invertedKeyBindings check box restored the controls of all three widgets");

        // Minimum / maximum spin boxes drive all three widgets.
        window.MinimumSpin.Value = -5;
        Check(slider.Minimum == -5 && bar.Minimum == -5 && dial.Minimum == -5,
            $"minimumSpinBox(-5) reached all three widgets (got {slider.Minimum}/{bar.Minimum}/{dial.Minimum})");
        window.MaximumSpin.Value = 50;
        Check(slider.Maximum == 50 && bar.Maximum == 50 && dial.Maximum == 50,
            $"maximumSpinBox(50) reached all three widgets (got {slider.Maximum}/{bar.Maximum}/{dial.Maximum})");

        // Tick position / interval round-trips on the slider.
        slider.SetTickPosition(QSlider.TickPosition.TicksBelow);
        Check(slider.tickPosition() == QSlider.TickPosition.TicksBelow,
            $"tick position round-trips (got {slider.tickPosition()})");
        slider.TickInterval = 25;
        Check(slider.TickInterval == 25, $"tick interval round-trips (got {slider.TickInterval})");

        // resizeEvent flips the outer layout (and the group's inner layout + orientation).
        int before = window.ResizeEventCount;
        window.Resize(300, 700);            // aspect < 1  =>  TopToBottom / Horizontal
        if (window.ResizeEventCount == before)
            window.ResizeEvent(null);       // no event loop: drive the override directly
        Check(window.OuterLayout.direction() == QBoxLayout.Direction.TopToBottom,
            $"a tall window flips the outer layout to TopToBottom (got {window.OuterLayout.direction()})");
        Check(group.InnerLayout.direction() == QBoxLayout.Direction.TopToBottom,
            $"a tall window flips the group layout to TopToBottom (got {group.InnerLayout.direction()})");
        Check(slider.Orientation == Qt.Orientation.Horizontal,
            $"a tall window makes the slider horizontal (got {slider.Orientation})");

        before = window.ResizeEventCount;
        window.Resize(1000, 150);           // aspect > 1  =>  LeftToRight / Vertical
        if (window.ResizeEventCount == before)
            window.ResizeEvent(null);
        Check(window.OuterLayout.direction() == QBoxLayout.Direction.LeftToRight,
            $"a wide window flips the outer layout to LeftToRight (got {window.OuterLayout.direction()})");
        Check(group.InnerLayout.direction() == QBoxLayout.Direction.LeftToRight,
            $"a wide window flips the group layout to LeftToRight (got {group.InnerLayout.direction()})");
        Check(slider.Orientation == Qt.Orientation.Vertical,
            $"a wide window makes the slider vertical (got {slider.Orientation})");
        Check(window.ResizeEventCount > 0, $"resizeEvent ran at least once ({window.ResizeEventCount})");

        // The window is left on screen for a moment if asked, but nothing needs the event loop.
        if (int.TryParse(Environment.GetEnvironmentVariable("QTSHARP_DEMO_MS"), out var ms) && ms > 0)
        {
            var stop = new QTimer();
            stop.SetSingleShot(true);
            stop.TimeoutSignal.Connect(() => app.Quit());
            stop.Start(ms);
            app.Exec();
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
