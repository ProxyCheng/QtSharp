using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// Port of examples/widgets/widgets/calendarwidget/window.h|cpp
//
// Deviation: the "&Locale" row of the General Options group relies on
// QLocale::matchingLocales()/territory(), which are not part of the current bindings,
// so that single row is omitted. Everything else mirrors the original.
public class CalendarWindow : QWidget
{
    QGroupBox previewGroupBox;
    QCalendarWidget calendar;
    QGridLayout previewLayout;

    QGroupBox generalOptionsGroupBox;
    QComboBox firstDayCombo;
    QLabel firstDayLabel;
    QComboBox selectionModeCombo;
    QLabel selectionModeLabel;
    QCheckBox gridCheckBox;
    QCheckBox navigationCheckBox;
    QComboBox horizontalHeaderCombo;
    QLabel horizontalHeaderLabel;
    QComboBox verticalHeaderCombo;
    QLabel verticalHeaderLabel;

    QGroupBox datesGroupBox;
    QDateEdit minimumDateEdit;
    QDateEdit currentDateEdit;
    QDateEdit maximumDateEdit;
    QLabel minimumDateLabel;
    QLabel currentDateLabel;
    QLabel maximumDateLabel;

    QGroupBox textFormatsGroupBox;
    QComboBox weekdayColorCombo;
    QLabel weekdayColorLabel;
    QComboBox weekendColorCombo;
    QLabel weekendColorLabel;
    QComboBox headerTextFormatCombo;
    QLabel headerTextFormatLabel;
    QCheckBox firstFridayCheckBox;
    QCheckBox mayFirstCheckBox;

    QColor weekdayColor = new QColor(Qt.GlobalColor.Black);
    QColor weekendColor = new QColor(Qt.GlobalColor.Red);

    public CalendarWindow(QWidget parent = null) : base(parent, 0)
    {
        CreatePreviewGroupBox();
        CreateGeneralOptionsGroupBox();
        CreateDatesGroupBox();
        CreateTextFormatsGroupBox();

        var layout = new QGridLayout();
        layout.AddWidget(previewGroupBox, 0, 0);
        layout.AddWidget(generalOptionsGroupBox, 0, 1);
        layout.AddWidget(datesGroupBox, 1, 0);
        layout.AddWidget(textFormatsGroupBox, 1, 1);
        layout.sizeConstraint = QLayout.SizeConstraint.SetFixedSize;
        Layout = layout;

        previewLayout.SetRowMinimumHeight(0, calendar.SizeHint.Height);
        previewLayout.SetColumnMinimumWidth(0, calendar.SizeHint.Width);

        WindowTitle = "Calendar Widget";
    }

    void CreatePreviewGroupBox()
    {
        previewGroupBox = new QGroupBox("Preview");

        calendar = new QCalendarWidget();
        calendar.MinimumDate = new QDate(1900, 1, 1);
        calendar.MaximumDate = new QDate(3000, 1, 1);
        calendar.GridVisible = true;
        calendar.CurrentPageChangedSignal.Connect((year, month) => ReformatCalendarPage());

        previewLayout = new QGridLayout();
        previewLayout.AddWidget(calendar, 0, 0, (int)Qt.AlignmentFlag.AlignCenter);
        previewGroupBox.Layout = previewLayout;
    }

    void CreateGeneralOptionsGroupBox()
    {
        generalOptionsGroupBox = new QGroupBox("General Options");

        firstDayCombo = new QComboBox();
        firstDayCombo.AddItem("Sunday");
        firstDayCombo.AddItem("Monday");
        firstDayCombo.AddItem("Tuesday");
        firstDayCombo.AddItem("Wednesday");
        firstDayCombo.AddItem("Thursday");
        firstDayCombo.AddItem("Friday");
        firstDayCombo.AddItem("Saturday");

        firstDayLabel = new QLabel("Wee&k starts on:");
        firstDayLabel.Buddy = firstDayCombo;

        selectionModeCombo = new QComboBox();
        selectionModeCombo.AddItem("Single selection");
        selectionModeCombo.AddItem("None");

        selectionModeLabel = new QLabel("&Selection mode:");
        selectionModeLabel.Buddy = selectionModeCombo;

        gridCheckBox = new QCheckBox("&Grid");
        gridCheckBox.Checked = calendar.GridVisible;

        navigationCheckBox = new QCheckBox("&Navigation bar");
        navigationCheckBox.Checked = true;

        horizontalHeaderCombo = new QComboBox();
        horizontalHeaderCombo.AddItem("Single letter day names");
        horizontalHeaderCombo.AddItem("Short day names");
        horizontalHeaderCombo.AddItem("None");
        horizontalHeaderCombo.CurrentIndex = 1;

        horizontalHeaderLabel = new QLabel("&Horizontal header:");
        horizontalHeaderLabel.Buddy = horizontalHeaderCombo;

        verticalHeaderCombo = new QComboBox();
        verticalHeaderCombo.AddItem("ISO week numbers");
        verticalHeaderCombo.AddItem("None");

        verticalHeaderLabel = new QLabel("&Vertical header:");
        verticalHeaderLabel.Buddy = verticalHeaderCombo;

        firstDayCombo.CurrentIndexChangedSignal.Connect(FirstDayChanged);
        selectionModeCombo.CurrentIndexChangedSignal.Connect(SelectionModeChanged);
        gridCheckBox.ToggledSignal.Connect(b => calendar.GridVisible = b);
        navigationCheckBox.ToggledSignal.Connect(b => calendar.NavigationBarVisible = b);
        horizontalHeaderCombo.CurrentIndexChangedSignal.Connect(HorizontalHeaderChanged);
        verticalHeaderCombo.CurrentIndexChangedSignal.Connect(VerticalHeaderChanged);

        var checkBoxLayout = new QHBoxLayout();
        checkBoxLayout.AddWidget(gridCheckBox);
        checkBoxLayout.AddStretch();
        checkBoxLayout.AddWidget(navigationCheckBox);

        var outerLayout = new QGridLayout();
        outerLayout.AddWidget(firstDayLabel, 0, 0);
        outerLayout.AddWidget(firstDayCombo, 0, 1);
        outerLayout.AddWidget(selectionModeLabel, 1, 0);
        outerLayout.AddWidget(selectionModeCombo, 1, 1);
        outerLayout.AddLayout(checkBoxLayout, 2, 0, 1, 2);
        outerLayout.AddWidget(horizontalHeaderLabel, 3, 0);
        outerLayout.AddWidget(horizontalHeaderCombo, 3, 1);
        outerLayout.AddWidget(verticalHeaderLabel, 4, 0);
        outerLayout.AddWidget(verticalHeaderCombo, 4, 1);
        generalOptionsGroupBox.Layout = outerLayout;

        FirstDayChanged(firstDayCombo.CurrentIndex);
        SelectionModeChanged(selectionModeCombo.CurrentIndex);
        HorizontalHeaderChanged(horizontalHeaderCombo.CurrentIndex);
        VerticalHeaderChanged(verticalHeaderCombo.CurrentIndex);
    }

    void FirstDayChanged(int index)
    {
        Qt.DayOfWeek[] days =
        {
            Qt.DayOfWeek.Sunday, Qt.DayOfWeek.Monday, Qt.DayOfWeek.Tuesday,
            Qt.DayOfWeek.Wednesday, Qt.DayOfWeek.Thursday, Qt.DayOfWeek.Friday,
            Qt.DayOfWeek.Saturday
        };
        calendar.FirstDayOfWeek = days[index];
    }

    void SelectionModeChanged(int index)
    {
        calendar.selectionMode = index == 0
            ? QCalendarWidget.SelectionMode.SingleSelection
            : QCalendarWidget.SelectionMode.NoSelection;
    }

    void HorizontalHeaderChanged(int index)
    {
        switch (index)
        {
            case 0:
                calendar.horizontalHeaderFormat = QCalendarWidget.HorizontalHeaderFormat.SingleLetterDayNames;
                break;
            case 1:
                calendar.horizontalHeaderFormat = QCalendarWidget.HorizontalHeaderFormat.ShortDayNames;
                break;
            default:
                calendar.horizontalHeaderFormat = QCalendarWidget.HorizontalHeaderFormat.NoHorizontalHeader;
                break;
        }
    }

    void VerticalHeaderChanged(int index)
    {
        calendar.verticalHeaderFormat = index == 0
            ? QCalendarWidget.VerticalHeaderFormat.ISOWeekNumbers
            : QCalendarWidget.VerticalHeaderFormat.NoVerticalHeader;
    }

    void CreateDatesGroupBox()
    {
        datesGroupBox = new QGroupBox("Dates");

        minimumDateEdit = new QDateEdit();
        minimumDateEdit.DisplayFormat = "MMM d yyyy";
        minimumDateEdit.SetDateRange(calendar.MinimumDate, calendar.MaximumDate);
        minimumDateEdit.Date = calendar.MinimumDate;

        minimumDateLabel = new QLabel("&Minimum Date:");
        minimumDateLabel.Buddy = minimumDateEdit;

        currentDateEdit = new QDateEdit();
        currentDateEdit.DisplayFormat = "MMM d yyyy";
        currentDateEdit.Date = calendar.SelectedDate;
        currentDateEdit.SetDateRange(calendar.MinimumDate, calendar.MaximumDate);

        currentDateLabel = new QLabel("&Current Date:");
        currentDateLabel.Buddy = currentDateEdit;

        maximumDateEdit = new QDateEdit();
        maximumDateEdit.DisplayFormat = "MMM d yyyy";
        maximumDateEdit.SetDateRange(calendar.MinimumDate, calendar.MaximumDate);
        maximumDateEdit.Date = calendar.MaximumDate;

        maximumDateLabel = new QLabel("Ma&ximum Date:");
        maximumDateLabel.Buddy = maximumDateEdit;

        // QDateTimeEdit::dateChanged() is not part of the current signal bindings, so
        // editingFinished() is used to propagate edits back to the calendar.
        currentDateEdit.EditingFinishedSignal.Connect(() => calendar.SelectedDate = currentDateEdit.Date);
        calendar.SelectionChangedSignal.Connect(() => currentDateEdit.Date = calendar.SelectedDate);
        minimumDateEdit.EditingFinishedSignal.Connect(() =>
        {
            calendar.MinimumDate = minimumDateEdit.Date;
            maximumDateEdit.Date = calendar.MaximumDate;
        });
        maximumDateEdit.EditingFinishedSignal.Connect(() =>
        {
            calendar.MaximumDate = maximumDateEdit.Date;
            minimumDateEdit.Date = calendar.MinimumDate;
        });

        var dateBoxLayout = new QGridLayout();
        dateBoxLayout.AddWidget(currentDateLabel, 1, 0);
        dateBoxLayout.AddWidget(currentDateEdit, 1, 1);
        dateBoxLayout.AddWidget(minimumDateLabel, 0, 0);
        dateBoxLayout.AddWidget(minimumDateEdit, 0, 1);
        dateBoxLayout.AddWidget(maximumDateLabel, 2, 0);
        dateBoxLayout.AddWidget(maximumDateEdit, 2, 1);
        dateBoxLayout.SetRowStretch(3, 1);

        datesGroupBox.Layout = dateBoxLayout;
    }

    void CreateTextFormatsGroupBox()
    {
        textFormatsGroupBox = new QGroupBox("Text Formats");

        weekdayColorCombo = CreateColorComboBox();
        weekdayColorCombo.CurrentIndex = weekdayColorCombo.FindText("Black");

        weekdayColorLabel = new QLabel("&Weekday color:");
        weekdayColorLabel.Buddy = weekdayColorCombo;

        weekendColorCombo = CreateColorComboBox();
        weekendColorCombo.CurrentIndex = weekendColorCombo.FindText("Red");

        weekendColorLabel = new QLabel("Week&end color:");
        weekendColorLabel.Buddy = weekendColorCombo;

        headerTextFormatCombo = new QComboBox();
        headerTextFormatCombo.AddItem("Bold");
        headerTextFormatCombo.AddItem("Italic");
        headerTextFormatCombo.AddItem("Plain");

        headerTextFormatLabel = new QLabel("&Header text:");
        headerTextFormatLabel.Buddy = headerTextFormatCombo;

        firstFridayCheckBox = new QCheckBox("&First Friday in blue");

        mayFirstCheckBox = new QCheckBox("May &1 in red");

        weekdayColorCombo.CurrentIndexChangedSignal.Connect(WeekdayFormatChanged);
        weekdayColorCombo.CurrentIndexChangedSignal.Connect(i => ReformatCalendarPage());
        weekendColorCombo.CurrentIndexChangedSignal.Connect(WeekendFormatChanged);
        weekendColorCombo.CurrentIndexChangedSignal.Connect(i => ReformatCalendarPage());
        headerTextFormatCombo.CurrentIndexChangedSignal.Connect(ReformatHeaders);
        firstFridayCheckBox.ToggledSignal.Connect(b => ReformatCalendarPage());
        mayFirstCheckBox.ToggledSignal.Connect(b => ReformatCalendarPage());

        var checkBoxLayout = new QHBoxLayout();
        checkBoxLayout.AddWidget(firstFridayCheckBox);
        checkBoxLayout.AddStretch();
        checkBoxLayout.AddWidget(mayFirstCheckBox);

        var outerLayout = new QGridLayout();
        outerLayout.AddWidget(weekdayColorLabel, 0, 0);
        outerLayout.AddWidget(weekdayColorCombo, 0, 1);
        outerLayout.AddWidget(weekendColorLabel, 1, 0);
        outerLayout.AddWidget(weekendColorCombo, 1, 1);
        outerLayout.AddWidget(headerTextFormatLabel, 2, 0);
        outerLayout.AddWidget(headerTextFormatCombo, 2, 1);
        outerLayout.AddLayout(checkBoxLayout, 3, 0, 1, 2);
        textFormatsGroupBox.Layout = outerLayout;

        WeekdayFormatChanged(0);
        WeekendFormatChanged(0);
        ReformatHeaders(0);
        ReformatCalendarPage();
    }

    QComboBox CreateColorComboBox()
    {
        var comboBox = new QComboBox();
        comboBox.AddItem("Red", new QVariant((int)Qt.GlobalColor.Red));
        comboBox.AddItem("Blue", new QVariant((int)Qt.GlobalColor.Blue));
        comboBox.AddItem("Black", new QVariant((int)Qt.GlobalColor.Black));
        comboBox.AddItem("Magenta", new QVariant((int)Qt.GlobalColor.Magenta));
        return comboBox;
    }

    static QColor ColorFromCombo(QComboBox comboBox)
        => new QColor((Qt.GlobalColor)comboBox.ItemData(comboBox.CurrentIndex).ToInt());

    void WeekdayFormatChanged(int index)
    {
        weekdayColor = ColorFromCombo(weekdayColorCombo);

        var format = new QTextCharFormat();
        format.SetForeground(weekdayColor);
        calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Monday, format);
        calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Tuesday, format);
        calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Wednesday, format);
        calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Thursday, format);
        calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Friday, format);
    }

    void WeekendFormatChanged(int index)
    {
        weekendColor = ColorFromCombo(weekendColorCombo);

        var format = new QTextCharFormat();
        format.SetForeground(weekendColor);
        calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Saturday, format);
        calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Sunday, format);
    }

    void ReformatHeaders(int index)
    {
        string text = headerTextFormatCombo.CurrentText;
        var format = new QTextCharFormat();

        if (text == "Bold")
            format.FontWeight = (int)QFont.Weight.Bold;
        else if (text == "Italic")
            format.FontItalic = true;
        else if (text == "Green")
            format.SetForeground(new QColor(Qt.GlobalColor.Green));
        calendar.HeaderTextFormat = format;
    }

    void ReformatCalendarPage()
    {
        var mayFirst = new QDate(calendar.YearShown, 5, 1);

        var firstFriday = new QDate(calendar.YearShown, calendar.MonthShown, 1);
        while (firstFriday.DayOfWeek() != (int)Qt.DayOfWeek.Friday)
            firstFriday = firstFriday.AddDays(1);

        var mayFirstFormat = new QTextCharFormat();
        var firstFridayFormat = new QTextCharFormat();

        if (firstFridayCheckBox.Checked)
        {
            firstFridayFormat.SetForeground(new QColor(Qt.GlobalColor.Blue));
        }
        else
        {
            // Revert to the regular colour for this day of the week.
            firstFridayFormat.SetForeground(WeekdayColorFor(firstFriday));
        }

        calendar.SetDateTextFormat(firstFriday, firstFridayFormat);

        // When it is checked, "May First in Red" always takes precedence over
        // "First Friday in Blue".
        if (mayFirstCheckBox.Checked)
        {
            mayFirstFormat.SetForeground(new QColor(Qt.GlobalColor.Red));
        }
        else if (!firstFridayCheckBox.Checked || firstFriday != mayFirst)
        {
            // We can now be certain we won't be resetting "May First in Red" when we
            // restore may 1st's regular colour for this day of the week.
            mayFirstFormat.SetForeground(WeekdayColorFor(mayFirst));
        }

        calendar.SetDateTextFormat(mayFirst, mayFirstFormat);
    }

    QColor WeekdayColorFor(QDate date)
    {
        int dow = date.DayOfWeek();
        return (dow == (int)Qt.DayOfWeek.Saturday || dow == (int)Qt.DayOfWeek.Sunday)
            ? weekendColor
            : weekdayColor;
    }
}
