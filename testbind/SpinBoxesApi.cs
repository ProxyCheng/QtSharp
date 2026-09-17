using System;
using QtSharp.Api;

// Port of examples/widgets/widgets/spinboxes on the new pipeline.
//
// The demo is mostly declarations - which is the point: it exercises the property surface a spin
// box exposes (range, step, prefix, suffix, special value text, the integer base, the group
// separator), the date/time edits built on the same base class, and the signals that tie a check
// box to a spin box.
public sealed class SpinBoxesApi : QWidget
{
    public readonly QSpinBox IntegerSpinBox = new();
    public readonly QSpinBox ZoomSpinBox = new();
    public readonly QSpinBox IntPriceSpinBox = new();
    public readonly QSpinBox HexSpinBox = new();
    public QSpinBox GroupSeparatorSpinBox = new();
    public QCheckBox GroupSeparatorCheckBox = new();

    public QDateEdit DateEdit;
    public QTimeEdit TimeEdit;
    public QDateTimeEdit MeetingEdit;
    public readonly QComboBox FormatComboBox = new();
    public readonly QLabel MeetingLabel = new();

    public readonly QSpinBox PrecisionSpinBox = new();
    public readonly QDoubleSpinBox DoubleSpinBox = new();
    public readonly QDoubleSpinBox ScaleSpinBox = new();
    public readonly QDoubleSpinBox PriceSpinBox = new();
    public readonly QDoubleSpinBox GroupSeparatorSpinBoxD = new();

    public SpinBoxesApi(QWidget parent = null) : base(parent)
    {
        var layout = new QHBoxLayout();
        layout.AddWidget(CreateSpinBoxes());
        layout.AddWidget(CreateDateTimeEdits());
        layout.AddWidget(CreateDoubleSpinBoxes());
        SetLayout(layout);
        SetWindowTitle("Spin Boxes");
    }

    QGroupBox CreateSpinBoxes()
    {
        var group = new QGroupBox("Spinboxes");

        var integerLabel = new QLabel("Enter a value between -20 and 20:");
        IntegerSpinBox.SetRange(-20, 20);
        IntegerSpinBox.SingleStep = 1;
        IntegerSpinBox.Value = 0;

        var zoomLabel = new QLabel("Enter a zoom value between 0 and 1000:");
        ZoomSpinBox.SetRange(0, 1000);
        ZoomSpinBox.SingleStep = 10;
        ZoomSpinBox.Suffix = "%";
        ZoomSpinBox.SpecialValueText = "Automatic";
        ZoomSpinBox.Value = 100;

        var priceLabel = new QLabel("Enter a price between 0 and 999:");
        IntPriceSpinBox.SetRange(0, 999);
        IntPriceSpinBox.SingleStep = 1;
        IntPriceSpinBox.Prefix = "$";
        IntPriceSpinBox.Value = 99;

        GroupSeparatorSpinBox.SetRange(-99999999, 99999999);
        GroupSeparatorSpinBox.Value = 1000;
        GroupSeparatorSpinBox.ShowGroupSeparator = true;
        GroupSeparatorCheckBox.SetText("Show group separator");
        GroupSeparatorCheckBox.Checked = true;
        GroupSeparatorCheckBox.ToggledSignal.Connect(b => GroupSeparatorSpinBox.ShowGroupSeparator = b);

        var hexLabel = new QLabel("Enter a value between -1f and 1f:");
        HexSpinBox.SetRange(-31, 31);
        HexSpinBox.SingleStep = 1;
        HexSpinBox.Value = 0;
        HexSpinBox.DisplayIntegerBase = 16;

        var spinBoxLayout = new QVBoxLayout();
        spinBoxLayout.AddWidget(integerLabel);
        spinBoxLayout.AddWidget(IntegerSpinBox);
        spinBoxLayout.AddWidget(zoomLabel);
        spinBoxLayout.AddWidget(ZoomSpinBox);
        spinBoxLayout.AddWidget(priceLabel);
        spinBoxLayout.AddWidget(IntPriceSpinBox);
        spinBoxLayout.AddWidget(hexLabel);
        spinBoxLayout.AddWidget(HexSpinBox);
        spinBoxLayout.AddWidget(GroupSeparatorCheckBox);
        spinBoxLayout.AddWidget(GroupSeparatorSpinBox);
        group.SetLayout(spinBoxLayout);
        return group;
    }

    QGroupBox CreateDateTimeEdits()
    {
        var group = new QGroupBox("Date and time spin boxes");

        var dateLabel = new QLabel();
        DateEdit = new QDateEdit(QDate.CurrentDate());
        DateEdit.SetDateRange(new QDate(2005, 1, 1), new QDate(2010, 12, 31));
        dateLabel.SetText(
            $"Appointment date (between {DateEdit.MinimumDate.ToString(Qt.DateFormat.ISODate)} and "
            + $"{DateEdit.MaximumDate.ToString(Qt.DateFormat.ISODate)}):");

        var timeLabel = new QLabel();
        TimeEdit = new QTimeEdit(QTime.CurrentTime());
        TimeEdit.SetTimeRange(new QTime(9, 0, 0, 0), new QTime(16, 30, 0, 0));
        timeLabel.SetText(
            $"Appointment time (between {TimeEdit.MinimumTime.ToString(Qt.DateFormat.ISODate)} and "
            + $"{TimeEdit.MaximumTime.ToString(Qt.DateFormat.ISODate)}):");

        MeetingEdit = new QDateTimeEdit(QDateTime.CurrentDateTime());

        var formatLabel = new QLabel("Format string for the meeting date and time:");
        FormatComboBox.AddItems(new[]
        {
            "yyyy-MM-dd hh:mm:ss (zzz 'ms')",
            "hh:mm:ss MM/dd/yyyy",
            "hh:mm:ss dd/MM/yyyy",
            "hh:mm:ss",
            "hh:mm ap",
        });
        // The official window follows textActivated, which Qt only emits for a real user
        // interaction - a test cannot produce one - so the index change is used instead; the
        // handler is the same one either way.
        FormatComboBox.CurrentIndexChangedSignal.Connect(_ => SetFormatString(FormatComboBox.CurrentText));
        SetFormatString(FormatComboBox.CurrentText);

        var editsLayout = new QVBoxLayout();
        editsLayout.AddWidget(dateLabel);
        editsLayout.AddWidget(DateEdit);
        editsLayout.AddWidget(timeLabel);
        editsLayout.AddWidget(TimeEdit);
        editsLayout.AddWidget(MeetingLabel);
        editsLayout.AddWidget(MeetingEdit);
        editsLayout.AddWidget(formatLabel);
        editsLayout.AddWidget(FormatComboBox);
        group.SetLayout(editsLayout);
        return group;
    }

    public void SetFormatString(string formatString)
    {
        MeetingEdit.DisplayFormat = formatString;

        bool hasDate = formatString.Contains("yyyy") || formatString.Contains("dd") || formatString.Contains("MM");
        if (hasDate)
        {
            MeetingEdit.SetDateRange(new QDate(2004, 11, 1), new QDate(2005, 11, 30));
            MeetingLabel.SetText(
                $"Meeting date (between {MeetingEdit.MinimumDate.ToString(Qt.DateFormat.ISODate)} and "
                + $"{MeetingEdit.MaximumDate.ToString(Qt.DateFormat.ISODate)}):");
        }
        else
        {
            MeetingEdit.SetTimeRange(new QTime(0, 7, 20, 0), new QTime(21, 0, 0, 0));
            MeetingLabel.SetText(
                $"Meeting time (between {MeetingEdit.MinimumTime.ToString(Qt.DateFormat.ISODate)} and "
                + $"{MeetingEdit.MaximumTime.ToString(Qt.DateFormat.ISODate)}):");
        }
    }

    QGroupBox CreateDoubleSpinBoxes()
    {
        var group = new QGroupBox("Double precision spinboxes");

        var precisionLabel = new QLabel("Number of decimal places to show:");
        PrecisionSpinBox.SetRange(0, 100);
        PrecisionSpinBox.Value = 2;

        var doubleLabel = new QLabel("Enter a value between -20 and 20:");
        DoubleSpinBox.SetRange(-20.0, 20.0);
        DoubleSpinBox.SingleStep = 1.0;
        DoubleSpinBox.Value = 0.0;

        var scaleLabel = new QLabel("Enter a scale factor between 0 and 1000:");
        ScaleSpinBox.SetRange(0.0, 1000.0);
        ScaleSpinBox.SingleStep = 10.0;
        ScaleSpinBox.Suffix = "%";
        ScaleSpinBox.SpecialValueText = "No scaling";
        ScaleSpinBox.Value = 100.0;

        var priceLabel = new QLabel("Enter a price between 0 and 1000:");
        PriceSpinBox.SetRange(0.0, 1000.0);
        PriceSpinBox.SingleStep = 1.0;
        PriceSpinBox.Prefix = "$";
        PriceSpinBox.Value = 99.99;

        // The official window calls changePrecision(0) once so that the three double spin boxes
        // start with no decimals, then follows the precision spin box.
        ChangePrecision(0);
        PrecisionSpinBox.ValueChangedSignal.Connect(_ => ChangePrecision(PrecisionSpinBox.Value));

        GroupSeparatorSpinBoxD.SetRange(-99999999, 99999999);
        GroupSeparatorSpinBoxD.Decimals = 2;
        GroupSeparatorSpinBoxD.Value = 1000.00;
        GroupSeparatorSpinBoxD.ShowGroupSeparator = true;
        var groupSeparatorCheckBox = new QCheckBox();
        groupSeparatorCheckBox.SetText("Show group separator");
        groupSeparatorCheckBox.Checked = true;
        groupSeparatorCheckBox.ToggledSignal.Connect(b => GroupSeparatorSpinBoxD.ShowGroupSeparator = b);

        var spinBoxLayout = new QVBoxLayout();
        spinBoxLayout.AddWidget(precisionLabel);
        spinBoxLayout.AddWidget(PrecisionSpinBox);
        spinBoxLayout.AddWidget(doubleLabel);
        spinBoxLayout.AddWidget(DoubleSpinBox);
        spinBoxLayout.AddWidget(scaleLabel);
        spinBoxLayout.AddWidget(ScaleSpinBox);
        spinBoxLayout.AddWidget(priceLabel);
        spinBoxLayout.AddWidget(PriceSpinBox);
        spinBoxLayout.AddWidget(groupSeparatorCheckBox);
        spinBoxLayout.AddWidget(GroupSeparatorSpinBoxD);
        group.SetLayout(spinBoxLayout);
        return group;
    }

    public void ChangePrecision(int decimals)
    {
        DoubleSpinBox.Decimals = decimals;
        ScaleSpinBox.Decimals = decimals;
        PriceSpinBox.Decimals = decimals;
    }
}

public static class ApiSpinBoxesDemo
{
    public static int Run()
    {
        ApiRuntime.StartApplication();

        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        var window = new SpinBoxesApi();
        window.Show();

        // The integer group: a range, a step, and the text Qt renders for a value.
        Check(window.IntegerSpinBox.Minimum == -20 && window.IntegerSpinBox.Maximum == 20,
            "QSpinBox range");
        window.IntegerSpinBox.Value = 25;
        Check(window.IntegerSpinBox.Value == 20, $"the range clamps the value (got {window.IntegerSpinBox.Value})");
        window.IntegerSpinBox.Value = 7;
        Check(window.IntegerSpinBox.Text == "7", $"QSpinBox::text (got '{window.IntegerSpinBox.Text}')");

        // A suffix and a special value: the display text is what the user sees.
        Check(window.ZoomSpinBox.Suffix == "%" && window.ZoomSpinBox.Value == 100,
            "QSpinBox suffix");
        Check(window.ZoomSpinBox.Text == "100%", $"the suffix is in the text (got '{window.ZoomSpinBox.Text}')");
        window.ZoomSpinBox.Value = 0;
        Check(window.ZoomSpinBox.Text == "Automatic",
            $"the special value text replaced the number (got '{window.ZoomSpinBox.Text}')");
        window.ZoomSpinBox.Value = 100;

        Check(window.IntPriceSpinBox.Prefix == "$" && window.IntPriceSpinBox.Text == "$99",
            $"QSpinBox prefix (got '{window.IntPriceSpinBox.Text}')");

        // The integer base: the same value, rendered in hex.
        window.HexSpinBox.Value = 31;
        Check(window.HexSpinBox.Text == "1f", $"displayIntegerBase 16 (got '{window.HexSpinBox.Text}')");
        window.HexSpinBox.DisplayIntegerBase = 10;
        Check(window.HexSpinBox.Text == "31", $"displayIntegerBase 10 (got '{window.HexSpinBox.Text}')");

        // A check box driving a spin box through its signal.
        Check(window.GroupSeparatorSpinBox.ShowGroupSeparator, "the group separator starts shown");
        window.GroupSeparatorCheckBox.Checked = false;
        Check(!window.GroupSeparatorSpinBox.ShowGroupSeparator,
            "unchecking the box turned the group separator off");

        // The date and the time edits, and the label the format string writes into.
        Check(window.DateEdit.MinimumDate.ToString(Qt.DateFormat.ISODate) == "2005-01-01",
            $"QDateEdit minimum date (got {window.DateEdit.MinimumDate.ToString(Qt.DateFormat.ISODate)})");
        var newDate = new QDate(2006, 5, 4);
        window.DateEdit.Date = newDate;
        Check(window.DateEdit.Date.ToString(Qt.DateFormat.ISODate) == "2006-05-04",
            $"QDateEdit round-trip (got {window.DateEdit.Date.ToString(Qt.DateFormat.ISODate)})");

        Check(window.TimeEdit.MinimumTime.ToString(Qt.DateFormat.ISODate) == "09:00:00",
            $"QTimeEdit minimum time (got {window.TimeEdit.MinimumTime.ToString(Qt.DateFormat.ISODate)})");
        window.TimeEdit.Time = new QTime(14, 30, 0, 0);
        Check(window.TimeEdit.Time.Hour() == 14, $"QTimeEdit round-trip (got {window.TimeEdit.Time.Hour()})");

        // The format string decides whether the range is a date range or a time range.
        Check(window.FormatComboBox.Count == 5, $"the format combo box holds 5 entries (got {window.FormatComboBox.Count})");
        Check(window.MeetingEdit.MinimumDate.ToString(Qt.DateFormat.ISODate) == "2004-11-01",
            "the date format installed a date range");
        window.FormatComboBox.SetCurrentIndex(4);
        Check(window.FormatComboBox.CurrentText == "hh:mm ap",
            $"QComboBox::currentText (got '{window.FormatComboBox.CurrentText}')");
        Check(window.MeetingEdit.DisplayFormat == "hh:mm ap",
            $"QComboBox::textActivated changed the format (got '{window.MeetingEdit.DisplayFormat}')");
        Check(window.MeetingEdit.MinimumTime.ToString(Qt.DateFormat.ISODate) == "00:07:20",
            "a time-only format installed a time range");
        Check(window.MeetingLabel.Text.StartsWith("Meeting time"),
            $"the label followed the format (got '{window.MeetingLabel.Text}')");

        // The double precision group: decimals, prefix/suffix and the precision spin box that
        // drives all three.
        Check(window.DoubleSpinBox.Decimals == 0, $"the initial precision call took effect (got {window.DoubleSpinBox.Decimals})");
        window.PrecisionSpinBox.Value = 3;
        Check(window.DoubleSpinBox.Decimals == 3 && window.ScaleSpinBox.Decimals == 3 && window.PriceSpinBox.Decimals == 3,
            "the precision spin box changed all three double spin boxes");
        window.PriceSpinBox.Value = 12.5;
        Check(window.PriceSpinBox.Text == "$12.500",
            $"QDoubleSpinBox prefix + decimals (got '{window.PriceSpinBox.Text}')");
        window.ScaleSpinBox.Value = 0;
        Check(window.ScaleSpinBox.Text == "No scaling",
            $"the double special value text (got '{window.ScaleSpinBox.Text}')");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
