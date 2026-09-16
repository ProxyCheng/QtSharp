using System;
using QtSharp.QtCore;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

public class SpinBoxesWindow : QWidget
{
    QDateTimeEdit meetingEdit;
    QDoubleSpinBox doubleSpinBox;
    QDoubleSpinBox priceSpinBox;
    QDoubleSpinBox scaleSpinBox;
    QLabel meetingLabel;
    QGroupBox spinBoxesGroup;
    QGroupBox editsGroup;
    QGroupBox doubleSpinBoxesGroup;

    public SpinBoxesWindow() : base(null, 0)
    {
        CreateSpinBoxes();
        CreateDateTimeEdits();
        CreateDoubleSpinBoxes();

        var layout = new QHBoxLayout();
        layout.AddWidget(spinBoxesGroup);
        layout.AddWidget(editsGroup);
        layout.AddWidget(doubleSpinBoxesGroup);
        Layout = layout;

        WindowTitle = "Spin Boxes";
    }

    void CreateSpinBoxes()
    {
        spinBoxesGroup = new QGroupBox("Spinboxes");

        var integerLabel = new QLabel("Enter a value between -20 and 20:");
        var integerSpinBox = new QSpinBox();
        integerSpinBox.SetRange(-20, 20);
        integerSpinBox.SingleStep = 1;
        integerSpinBox.Value = 0;

        var zoomLabel = new QLabel("Enter a zoom value between 0 and 1000:");
        var zoomSpinBox = new QSpinBox();
        zoomSpinBox.SetRange(0, 1000);
        zoomSpinBox.SingleStep = 10;
        zoomSpinBox.Suffix = "%";
        zoomSpinBox.SpecialValueText = "Automatic";
        zoomSpinBox.Value = 100;

        var priceLabel = new QLabel("Enter a price between 0 and 999:");
        var intPriceSpinBox = new QSpinBox();
        intPriceSpinBox.SetRange(0, 999);
        intPriceSpinBox.SingleStep = 1;
        intPriceSpinBox.Prefix = "$";
        intPriceSpinBox.Value = 99;

        var groupSeparatorSpinBox = new QSpinBox();
        groupSeparatorSpinBox.SetRange(-99999999, 99999999);
        groupSeparatorSpinBox.Value = 1000;
        groupSeparatorSpinBox.GroupSeparatorShown = true;
        var groupSeparatorChkBox = new QCheckBox();
        groupSeparatorChkBox.Text = "Show group separator";
        groupSeparatorChkBox.Checked = true;
        groupSeparatorChkBox.ToggledSignal.Connect(b => groupSeparatorSpinBox.GroupSeparatorShown = b);

        var hexLabel = new QLabel("Enter a value between -1f and 1f:");
        var hexSpinBox = new QSpinBox();
        hexSpinBox.SetRange(-31, 31);
        hexSpinBox.SingleStep = 1;
        hexSpinBox.Value = 0;
        hexSpinBox.DisplayIntegerBase = 16;

        var spinBoxLayout = new QVBoxLayout();
        spinBoxLayout.AddWidget(integerLabel);
        spinBoxLayout.AddWidget(integerSpinBox);
        spinBoxLayout.AddWidget(zoomLabel);
        spinBoxLayout.AddWidget(zoomSpinBox);
        spinBoxLayout.AddWidget(priceLabel);
        spinBoxLayout.AddWidget(intPriceSpinBox);
        spinBoxLayout.AddWidget(hexLabel);
        spinBoxLayout.AddWidget(hexSpinBox);
        spinBoxLayout.AddWidget(groupSeparatorChkBox);
        spinBoxLayout.AddWidget(groupSeparatorSpinBox);
        spinBoxesGroup.Layout = spinBoxLayout;
    }

    void CreateDateTimeEdits()
    {
        editsGroup = new QGroupBox("Date and time spin boxes");

        var dateLabel = new QLabel();
        var dateEdit = new QDateEdit(QDate.CurrentDate);
        dateEdit.SetDateRange(new QDate(2005, 1, 1), new QDate(2010, 12, 31));
        dateLabel.Text =
            $"Appointment date (between {dateEdit.MinimumDate.ToString(Qt.DateFormat.ISODate)} and {dateEdit.MaximumDate.ToString(Qt.DateFormat.ISODate)}):";

        var timeLabel = new QLabel();
        var timeEdit = new QTimeEdit(QTime.CurrentTime);
        timeEdit.SetTimeRange(new QTime(9, 0, 0, 0), new QTime(16, 30, 0, 0));
        timeLabel.Text =
            $"Appointment time (between {timeEdit.MinimumTime.ToString(Qt.DateFormat.ISODate)} and {timeEdit.MaximumTime.ToString(Qt.DateFormat.ISODate)}):";

        meetingLabel = new QLabel();
        meetingEdit = new QDateTimeEdit(QDateTime.CurrentDateTime());

        var formatLabel = new QLabel("Format string for the meeting date and time:");
        var formatComboBox = new QComboBox();
        formatComboBox.AddItems(new[]
        {
            "yyyy-MM-dd hh:mm:ss (zzz 'ms')",
            "hh:mm:ss MM/dd/yyyy",
            "hh:mm:ss dd/MM/yyyy",
            "hh:mm:ss",
            "hh:mm ap",
        });

        formatComboBox.TextActivatedSignal.Connect(SetFormatString);

        SetFormatString(formatComboBox.CurrentText);

        var editsLayout = new QVBoxLayout();
        editsLayout.AddWidget(dateLabel);
        editsLayout.AddWidget(dateEdit);
        editsLayout.AddWidget(timeLabel);
        editsLayout.AddWidget(timeEdit);
        editsLayout.AddWidget(meetingLabel);
        editsLayout.AddWidget(meetingEdit);
        editsLayout.AddWidget(formatLabel);
        editsLayout.AddWidget(formatComboBox);
        editsGroup.Layout = editsLayout;
    }

    void SetFormatString(string formatString)
    {
        meetingEdit.DisplayFormat = formatString;

        bool hasDate = formatString.Contains("yyyy") || formatString.Contains("dd") || formatString.Contains("MM");
        if (hasDate)
        {
            meetingEdit.SetDateRange(new QDate(2004, 11, 1), new QDate(2005, 11, 30));
            meetingLabel.Text =
                $"Meeting date (between {meetingEdit.MinimumDate.ToString(Qt.DateFormat.ISODate)} and {meetingEdit.MaximumDate.ToString(Qt.DateFormat.ISODate)}):";
        }
        else
        {
            meetingEdit.SetTimeRange(new QTime(0, 7, 20, 0), new QTime(21, 0, 0, 0));
            meetingLabel.Text =
                $"Meeting time (between {meetingEdit.MinimumTime.ToString(Qt.DateFormat.ISODate)} and {meetingEdit.MaximumTime.ToString(Qt.DateFormat.ISODate)}):";
        }
    }

    void CreateDoubleSpinBoxes()
    {
        doubleSpinBoxesGroup = new QGroupBox("Double precision spinboxes");

        var precisionLabel = new QLabel("Number of decimal places to show:");
        var precisionSpinBox = new QSpinBox();
        precisionSpinBox.SetRange(0, 100);
        precisionSpinBox.Value = 2;

        var doubleLabel = new QLabel("Enter a value between -20 and 20:");
        doubleSpinBox = new QDoubleSpinBox();
        doubleSpinBox.SetRange(-20.0, 20.0);
        doubleSpinBox.SingleStep = 1.0;
        doubleSpinBox.Value = 0.0;

        var scaleLabel = new QLabel("Enter a scale factor between 0 and 1000:");
        scaleSpinBox = new QDoubleSpinBox();
        scaleSpinBox.SetRange(0.0, 1000.0);
        scaleSpinBox.SingleStep = 10.0;
        scaleSpinBox.Suffix = "%";
        scaleSpinBox.SpecialValueText = "No scaling";
        scaleSpinBox.Value = 100.0;

        var priceLabel = new QLabel("Enter a price between 0 and 1000:");
        priceSpinBox = new QDoubleSpinBox();
        priceSpinBox.SetRange(0.0, 1000.0);
        priceSpinBox.SingleStep = 1.0;
        priceSpinBox.Prefix = "$";
        priceSpinBox.Value = 99.99;

        precisionSpinBox.ValueChangedSignal.Connect(_ => ChangePrecision(precisionSpinBox.Value));

        var groupSeparatorSpinBoxD = new QDoubleSpinBox();
        groupSeparatorSpinBoxD.SetRange(-99999999, 99999999);
        groupSeparatorSpinBoxD.Decimals = 2;
        groupSeparatorSpinBoxD.Value = 1000.00;
        groupSeparatorSpinBoxD.GroupSeparatorShown = true;
        var groupSeparatorChkBox = new QCheckBox();
        groupSeparatorChkBox.Text = "Show group separator";
        groupSeparatorChkBox.Checked = true;
        groupSeparatorChkBox.ToggledSignal.Connect(b => groupSeparatorSpinBoxD.GroupSeparatorShown = b);

        var spinBoxLayout = new QVBoxLayout();
        spinBoxLayout.AddWidget(precisionLabel);
        spinBoxLayout.AddWidget(precisionSpinBox);
        spinBoxLayout.AddWidget(doubleLabel);
        spinBoxLayout.AddWidget(doubleSpinBox);
        spinBoxLayout.AddWidget(scaleLabel);
        spinBoxLayout.AddWidget(scaleSpinBox);
        spinBoxLayout.AddWidget(priceLabel);
        spinBoxLayout.AddWidget(priceSpinBox);
        spinBoxLayout.AddWidget(groupSeparatorChkBox);
        spinBoxLayout.AddWidget(groupSeparatorSpinBoxD);
        doubleSpinBoxesGroup.Layout = spinBoxLayout;
    }

    void ChangePrecision(int decimals)
    {
        doubleSpinBox.Decimals = decimals;
        scaleSpinBox.Decimals = decimals;
        priceSpinBox.Decimals = decimals;
    }
}
