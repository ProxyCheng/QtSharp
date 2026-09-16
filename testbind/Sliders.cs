using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

public class SlidersGroup : QGroupBox
{
    QSlider slider;
    QScrollBar scrollBar;
    QDial dial;
    QBoxLayout slidersLayout;

    public event Action<int> ValueChanged;

    public SlidersGroup(string title, QWidget parent = null) : base(title, parent)
    {
        slider = new QSlider();
        slider.FocusPolicy = Qt.FocusPolicy.StrongFocus;
        slider.tickPosition = QSlider.TickPosition.TicksBothSides;
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
        Layout = slidersLayout;
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
        slidersLayout.direction = orientation == Qt.Orientation.Horizontal
            ? QBoxLayout.Direction.TopToBottom
            : QBoxLayout.Direction.LeftToRight;
        scrollBar.Orientation = orientation;
        slider.Orientation = orientation;
    }
}

public class SlidersWindow : QWidget
{
    SlidersGroup slidersGroup;
    QGroupBox controlsGroup;
    QCheckBox invertedAppearance;
    QCheckBox invertedKeyBindings;
    QSpinBox minimumSpinBox;
    QSpinBox maximumSpinBox;
    QSpinBox valueSpinBox;
    QBoxLayout layout;

    public SlidersWindow() : base(null, 0)
    {
        slidersGroup = new SlidersGroup("Sliders");
        CreateControls("Controls");

        layout = new QBoxLayout(QBoxLayout.Direction.LeftToRight);
        layout.AddWidget(controlsGroup);
        layout.AddWidget(slidersGroup);
        Layout = layout;

        minimumSpinBox.Value = 0;
        maximumSpinBox.Value = 20;
        valueSpinBox.Value = 5;

        WindowTitle = "Sliders";
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

        slidersGroup.ValueChanged += v => valueSpinBox.Value = v;
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
        controlsGroup.Layout = controlsLayout;
    }

    protected override void ResizeEvent(QResizeEvent @event)
    {
        if (Width == 0 || Height == 0)
            return;

        double aspectRatio = (double)Width / Height;

        if (aspectRatio < 1.0)
        {
            layout.direction = QBoxLayout.Direction.TopToBottom;
            slidersGroup.SetOrientation(Qt.Orientation.Horizontal);
        }
        else if (aspectRatio > 1.0)
        {
            layout.direction = QBoxLayout.Direction.LeftToRight;
            slidersGroup.SetOrientation(Qt.Orientation.Vertical);
        }

        base.ResizeEvent(@event);
    }
}
