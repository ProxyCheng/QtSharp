using System;

// Port of examples/widgets/widgets/calendarwidget/{window.h,window.cpp} onto the *new* pipeline.
// Everything (group boxes, grid/hbox layouts, labels, combo boxes, date edits, the calendar and
// the rich-text formats) comes from model/qtsharp.api.json - nothing here is hand-written glue.
//
// Fidelity improvements over the legacy C# port:
//   * QDateTimeEdit::dateChanged / timeChanged / dateTimeChanged are modelled now, so the date
//     edits drive the calendar through the *real* signal instead of the editingFinished()
//     substitute the legacy binding had to use (see CreateDatesGroupBox);
//   * QTextFormat::setForeground(QBrush) is modelled natively, so the legacy QtCompat
//     SetForeground shim is gone.
//
// Deviations from the C++ original, and why:
//   * the colour combos store Qt::GlobalColor as the item data instead of a QColor: QVariant has
//     no QColor constructor in the model, so the same colour is stored as its enum value;
//   * a small preview label under the calendar is updated from selectionChanged() so the signal
//     can be observed as text (the Sliders port does the same for its valueChanged echo).
//
// Naming notes for the generated binding:
//   * Q_PROPERTY becomes a PascalCase property while the C++ accessor keeps its name, so we write
//     calendar.GridVisible / calendar.SelectedDate / calendar.FirstDayOfWeek, but
//     calendar.selectionMode() / SetSelectionMode(...) (the selectionMode property was skipped
//     because its name collides with the nested SelectionMode enum) and
//     calendar.horizontalHeaderFormat() / SetHorizontalHeaderFormat(...);
//   * QDate / QTextCharFormat / QBrush / QColor are classes with accessor methods, and QDate has
//     no operator==/Equals on the binding, so dates are compared by ToJulianDay().
public sealed class ApiCalendarWidgetWindow : QWidget
{
    public QGroupBox PreviewGroupBox;
    public QCalendarWidget Calendar;
    public QGridLayout PreviewLayout;
    public QLabel PreviewLabel;

    QGroupBox generalOptionsGroupBox;
    public QComboBox LocaleCombo;
    QLabel localeLabel;
    public QComboBox FirstDayCombo;
    public QLabel FirstDayLabel;
    public QComboBox SelectionModeCombo;
    QLabel selectionModeLabel;
    public QCheckBox GridCheckBox;
    public QCheckBox NavigationCheckBox;
    public QComboBox HorizontalHeaderCombo;
    public QLabel HorizontalHeaderLabel;
    public QComboBox VerticalHeaderCombo;
    QLabel verticalHeaderLabel;

    QGroupBox datesGroupBox;
    public QDateEdit MinimumDateEdit;
    public QDateEdit CurrentDateEdit;
    public QDateEdit MaximumDateEdit;

    QGroupBox textFormatsGroupBox;
    public QComboBox WeekdayColorCombo;
    public QComboBox WeekendColorCombo;
    public QComboBox HeaderTextFormatCombo;
    public QCheckBox FirstFridayCheckBox;
    public QCheckBox MayFirstCheckBox;

    public QGridLayout OuterLayout;

    QColor weekdayColor = new QColor(Qt.GlobalColor.black);
    QColor weekendColor = new QColor(Qt.GlobalColor.red);

    public ApiCalendarWidgetWindow()
    {
        CreatePreviewGroupBox();
        CreateGeneralOptionsGroupBox();
        CreateDatesGroupBox();
        CreateTextFormatsGroupBox();

        OuterLayout = new QGridLayout();
        OuterLayout.AddWidget(PreviewGroupBox, 0, 0);
        OuterLayout.AddWidget(generalOptionsGroupBox, 0, 1);
        OuterLayout.AddWidget(datesGroupBox, 1, 0);
        OuterLayout.AddWidget(textFormatsGroupBox, 1, 1);
        OuterLayout.SetSizeConstraint(QLayout.SizeConstraint.SetFixedSize);
        SetLayout(OuterLayout);

        PreviewLayout.SetRowMinimumHeight(0, Calendar.SizeHint.Height());
        PreviewLayout.SetColumnMinimumWidth(0, Calendar.SizeHint.Width());

        SetWindowTitle("Calendar Widget (new pipeline)");
    }

    void CreatePreviewGroupBox()
    {
        PreviewGroupBox = new QGroupBox("Preview");

        Calendar = new QCalendarWidget();
        Calendar.SetMinimumDate(new QDate(1900, 1, 1));
        Calendar.SetMaximumDate(new QDate(3000, 1, 1));
        Calendar.SetGridVisible(true);
        Calendar.CurrentPageChangedSignal.Connect((year, month) => ReformatCalendarPage());

        PreviewLabel = new QLabel("Selected: (none)");
        Calendar.SelectionChangedSignal.Connect(() =>
            PreviewLabel.SetText($"Selected: {Calendar.SelectedDate.ToString("yyyy-MM-dd")}"));

        PreviewLayout = new QGridLayout();
        PreviewLayout.AddWidget(Calendar, 0, 0, Qt.Alignment.AlignCenter);
        PreviewLayout.AddWidget(PreviewLabel, 1, 0, Qt.Alignment.AlignCenter);
        PreviewGroupBox.SetLayout(PreviewLayout);
    }

    void CreateGeneralOptionsGroupBox()
    {
        generalOptionsGroupBox = new QGroupBox("General Options");

        // The official Locale row: every locale Qt knows, labelled language/territory, with the
        // QLocale itself in the item data. matchingLocales was the API this row needed; it is
        // bound now, so the row is the upstream one.
        LocaleCombo = new QComboBox();
        int currentLocaleIndex = -1;
        int localeIndex = 0;
        using (var applicationLocale = new QLocale())
        {
            for (int lang = (int)QLocale.Language.C; lang <= (int)QLocale.Language.LastLanguage; lang++)
            {
                foreach (var locale in QLocale.MatchingLocales((QLocale.Language)lang,
                                                               QLocale.Script.AnyScript,
                                                               QLocale.Country.AnyTerritory))
                {
                    using (locale)
                    {
                        string label = QLocale.LanguageToString((QLocale.Language)lang) + "/"
                                     + QLocale.TerritoryToString(locale.Territory());
                        LocaleCombo.AddItem(label, locale);
                        if (applicationLocale.language() == locale.language()
                            && applicationLocale.Territory() == locale.Territory())
                            currentLocaleIndex = localeIndex;
                    }
                    localeIndex++;
                }
            }
        }
        if (currentLocaleIndex != -1)
            LocaleCombo.SetCurrentIndex(currentLocaleIndex);

        localeLabel = new QLabel("&Locale:");
        localeLabel.SetBuddy(LocaleCombo);

        FirstDayCombo = new QComboBox();
        FirstDayCombo.AddItem("Sunday");
        FirstDayCombo.AddItem("Monday");
        FirstDayCombo.AddItem("Tuesday");
        FirstDayCombo.AddItem("Wednesday");
        FirstDayCombo.AddItem("Thursday");
        FirstDayCombo.AddItem("Friday");
        FirstDayCombo.AddItem("Saturday");

        FirstDayLabel = new QLabel("Wee&k starts on:");
        FirstDayLabel.SetBuddy(FirstDayCombo);

        SelectionModeCombo = new QComboBox();
        SelectionModeCombo.AddItem("Single selection");
        SelectionModeCombo.AddItem("None");

        selectionModeLabel = new QLabel("&Selection mode:");
        selectionModeLabel.SetBuddy(SelectionModeCombo);

        GridCheckBox = new QCheckBox("&Grid");
        GridCheckBox.SetChecked(Calendar.GridVisible);

        NavigationCheckBox = new QCheckBox("&Navigation bar");
        NavigationCheckBox.SetChecked(true);

        HorizontalHeaderCombo = new QComboBox();
        HorizontalHeaderCombo.AddItem("Single letter day names");
        HorizontalHeaderCombo.AddItem("Short day names");
        HorizontalHeaderCombo.AddItem("None");
        HorizontalHeaderCombo.SetCurrentIndex(1);

        HorizontalHeaderLabel = new QLabel("&Horizontal header:");
        HorizontalHeaderLabel.SetBuddy(HorizontalHeaderCombo);

        VerticalHeaderCombo = new QComboBox();
        VerticalHeaderCombo.AddItem("ISO week numbers");
        VerticalHeaderCombo.AddItem("None");

        verticalHeaderLabel = new QLabel("&Vertical header:");
        verticalHeaderLabel.SetBuddy(VerticalHeaderCombo);

        LocaleCombo.CurrentIndexChangedSignal.Connect(LocaleChanged);
        FirstDayCombo.CurrentIndexChangedSignal.Connect(FirstDayChanged);
        SelectionModeCombo.CurrentIndexChangedSignal.Connect(SelectionModeChanged);
        GridCheckBox.ToggledSignal.Connect(b => Calendar.GridVisible = b);
        NavigationCheckBox.ToggledSignal.Connect(b => Calendar.NavigationBarVisible = b);
        HorizontalHeaderCombo.CurrentIndexChangedSignal.Connect(HorizontalHeaderChanged);
        VerticalHeaderCombo.CurrentIndexChangedSignal.Connect(VerticalHeaderChanged);

        var checkBoxLayout = new QHBoxLayout();
        checkBoxLayout.AddWidget(GridCheckBox);
        checkBoxLayout.AddStretch();
        checkBoxLayout.AddWidget(NavigationCheckBox);

        var outerLayout = new QGridLayout();
        outerLayout.AddWidget(localeLabel, 0, 0);
        outerLayout.AddWidget(LocaleCombo, 0, 1);
        outerLayout.AddWidget(FirstDayLabel, 1, 0);
        outerLayout.AddWidget(FirstDayCombo, 1, 1);
        outerLayout.AddWidget(selectionModeLabel, 2, 0);
        outerLayout.AddWidget(SelectionModeCombo, 2, 1);
        outerLayout.AddLayout(checkBoxLayout, 3, 0, 1, 2);
        outerLayout.AddWidget(HorizontalHeaderLabel, 4, 0);
        outerLayout.AddWidget(HorizontalHeaderCombo, 4, 1);
        outerLayout.AddWidget(verticalHeaderLabel, 5, 0);
        outerLayout.AddWidget(VerticalHeaderCombo, 5, 1);
        generalOptionsGroupBox.SetLayout(outerLayout);

        FirstDayChanged(FirstDayCombo.CurrentIndex);
        SelectionModeChanged(SelectionModeCombo.CurrentIndex);
        HorizontalHeaderChanged(HorizontalHeaderCombo.CurrentIndex);
        VerticalHeaderChanged(VerticalHeaderCombo.CurrentIndex);
    }

    void LocaleChanged(int index)
    {
        if (index < 0)
            return;
        using var newLocale = (QLocale)LocaleCombo.ItemData(index, (int)Qt.ItemDataRole.UserRole);
        Calendar.SetLocale(newLocale);
        // The combo lists Sunday..Saturday; Qt's DayOfWeek is Monday=1..Sunday=7.
        FirstDayCombo.SetCurrentIndex((int)newLocale.FirstDayOfWeek() % 7);
    }

    void FirstDayChanged(int index)
    {
        Qt.DayOfWeek[] days =
        {
            Qt.DayOfWeek.Sunday, Qt.DayOfWeek.Monday, Qt.DayOfWeek.Tuesday,
            Qt.DayOfWeek.Wednesday, Qt.DayOfWeek.Thursday, Qt.DayOfWeek.Friday,
            Qt.DayOfWeek.Saturday
        };
        Calendar.FirstDayOfWeek = days[index];
    }

    void SelectionModeChanged(int index)
        => Calendar.SetSelectionMode(index == 0
            ? QCalendarWidget.SelectionMode.SingleSelection
            : QCalendarWidget.SelectionMode.NoSelection);

    void HorizontalHeaderChanged(int index)
    {
        switch (index)
        {
            case 0:
                Calendar.SetHorizontalHeaderFormat(QCalendarWidget.HorizontalHeaderFormat.SingleLetterDayNames);
                break;
            case 1:
                Calendar.SetHorizontalHeaderFormat(QCalendarWidget.HorizontalHeaderFormat.ShortDayNames);
                break;
            default:
                Calendar.SetHorizontalHeaderFormat(QCalendarWidget.HorizontalHeaderFormat.NoHorizontalHeader);
                break;
        }
    }

    void VerticalHeaderChanged(int index)
        => Calendar.SetVerticalHeaderFormat(index == 0
            ? QCalendarWidget.VerticalHeaderFormat.ISOWeekNumbers
            : QCalendarWidget.VerticalHeaderFormat.NoVerticalHeader);

    void CreateDatesGroupBox()
    {
        datesGroupBox = new QGroupBox("Dates");

        MinimumDateEdit = new QDateEdit();
        MinimumDateEdit.DisplayFormat = "MMM d yyyy";
        MinimumDateEdit.SetDateRange(Calendar.MinimumDate, Calendar.MaximumDate);
        MinimumDateEdit.Date = Calendar.MinimumDate;

        var minimumDateLabel = new QLabel("&Minimum Date:");
        minimumDateLabel.SetBuddy(MinimumDateEdit);

        CurrentDateEdit = new QDateEdit();
        CurrentDateEdit.DisplayFormat = "MMM d yyyy";
        CurrentDateEdit.Date = Calendar.SelectedDate;
        CurrentDateEdit.SetDateRange(Calendar.MinimumDate, Calendar.MaximumDate);

        var currentDateLabel = new QLabel("&Current Date:");
        currentDateLabel.SetBuddy(CurrentDateEdit);

        MaximumDateEdit = new QDateEdit();
        MaximumDateEdit.DisplayFormat = "MMM d yyyy";
        MaximumDateEdit.SetDateRange(Calendar.MinimumDate, Calendar.MaximumDate);
        MaximumDateEdit.Date = Calendar.MaximumDate;

        var maximumDateLabel = new QLabel("Ma&ximum Date:");
        maximumDateLabel.SetBuddy(MaximumDateEdit);

        // Fidelity improvement over the legacy port: QDateTimeEdit::dateChanged is modelled, so
        // the real signal drives the calendar (legacy had to fall back to editingFinished()).
        CurrentDateEdit.DateChangedSignal.Connect(d => Calendar.SetSelectedDate(CurrentDateEdit.Date));
        Calendar.SelectionChangedSignal.Connect(() => CurrentDateEdit.SetDate(Calendar.SelectedDate));
        MinimumDateEdit.DateChangedSignal.Connect(d =>
        {
            Calendar.SetMinimumDate(MinimumDateEdit.Date);
            MaximumDateEdit.SetDate(Calendar.MaximumDate);
        });
        MaximumDateEdit.DateChangedSignal.Connect(d =>
        {
            Calendar.SetMaximumDate(MaximumDateEdit.Date);
            MinimumDateEdit.SetDate(Calendar.MinimumDate);
        });

        var dateBoxLayout = new QGridLayout();
        dateBoxLayout.AddWidget(currentDateLabel, 1, 0);
        dateBoxLayout.AddWidget(CurrentDateEdit, 1, 1);
        dateBoxLayout.AddWidget(minimumDateLabel, 0, 0);
        dateBoxLayout.AddWidget(MinimumDateEdit, 0, 1);
        dateBoxLayout.AddWidget(maximumDateLabel, 2, 0);
        dateBoxLayout.AddWidget(MaximumDateEdit, 2, 1);
        dateBoxLayout.SetRowStretch(3, 1);
        datesGroupBox.SetLayout(dateBoxLayout);
    }

    void CreateTextFormatsGroupBox()
    {
        textFormatsGroupBox = new QGroupBox("Text Formats");

        WeekdayColorCombo = CreateColorComboBox();
        WeekdayColorCombo.SetCurrentIndex(WeekdayColorCombo.FindText("Black"));

        var weekdayColorLabel = new QLabel("&Weekday color:");
        weekdayColorLabel.SetBuddy(WeekdayColorCombo);

        WeekendColorCombo = CreateColorComboBox();
        WeekendColorCombo.SetCurrentIndex(WeekendColorCombo.FindText("Red"));

        var weekendColorLabel = new QLabel("Week&end color:");
        weekendColorLabel.SetBuddy(WeekendColorCombo);

        HeaderTextFormatCombo = new QComboBox();
        HeaderTextFormatCombo.AddItem("Bold");
        HeaderTextFormatCombo.AddItem("Italic");
        HeaderTextFormatCombo.AddItem("Plain");

        var headerTextFormatLabel = new QLabel("&Header text:");
        headerTextFormatLabel.SetBuddy(HeaderTextFormatCombo);

        FirstFridayCheckBox = new QCheckBox("&First Friday in blue");

        MayFirstCheckBox = new QCheckBox("May &1 in red");

        WeekdayColorCombo.CurrentIndexChangedSignal.Connect(WeekdayFormatChanged);
        WeekdayColorCombo.CurrentIndexChangedSignal.Connect(i => ReformatCalendarPage());
        WeekendColorCombo.CurrentIndexChangedSignal.Connect(WeekendFormatChanged);
        WeekendColorCombo.CurrentIndexChangedSignal.Connect(i => ReformatCalendarPage());
        HeaderTextFormatCombo.CurrentIndexChangedSignal.Connect(ReformatHeaders);
        FirstFridayCheckBox.ToggledSignal.Connect(b => ReformatCalendarPage());
        MayFirstCheckBox.ToggledSignal.Connect(b => ReformatCalendarPage());

        var checkBoxLayout = new QHBoxLayout();
        checkBoxLayout.AddWidget(FirstFridayCheckBox);
        checkBoxLayout.AddStretch();
        checkBoxLayout.AddWidget(MayFirstCheckBox);

        var outerLayout = new QGridLayout();
        outerLayout.AddWidget(weekdayColorLabel, 0, 0);
        outerLayout.AddWidget(WeekdayColorCombo, 0, 1);
        outerLayout.AddWidget(weekendColorLabel, 1, 0);
        outerLayout.AddWidget(WeekendColorCombo, 1, 1);
        outerLayout.AddWidget(headerTextFormatLabel, 2, 0);
        outerLayout.AddWidget(HeaderTextFormatCombo, 2, 1);
        outerLayout.AddLayout(checkBoxLayout, 3, 0, 1, 2);
        textFormatsGroupBox.SetLayout(outerLayout);

        WeekdayFormatChanged(0);
        WeekendFormatChanged(0);
        ReformatHeaders(0);
        ReformatCalendarPage();
    }

    // The official stores a QColor in the item data; here the Qt::GlobalColor value is stored and
    // read back through the same enum, which is what the original did before QColor moved through
    // QVariant.
    QComboBox CreateColorComboBox()
    {
        var comboBox = new QComboBox();
        AddColorItem(comboBox, "Red", Qt.GlobalColor.red);
        AddColorItem(comboBox, "Blue", Qt.GlobalColor.blue);
        AddColorItem(comboBox, "Black", Qt.GlobalColor.black);
        AddColorItem(comboBox, "Magenta", Qt.GlobalColor.magenta);
        return comboBox;
    }

    static void AddColorItem(QComboBox comboBox, string text, Qt.GlobalColor color)
        => comboBox.AddItem(text, (int)color);

    static QColor ColorFromCombo(QComboBox comboBox)
        => new QColor((Qt.GlobalColor)(int)comboBox
            .ItemData(comboBox.CurrentIndex, (int)Qt.ItemDataRole.UserRole));

    void WeekdayFormatChanged(int index)
    {
        weekdayColor = ColorFromCombo(WeekdayColorCombo);

        var format = new QTextCharFormat();
        format.SetForeground(new QBrush(weekdayColor));
        Calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Monday, format);
        Calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Tuesday, format);
        Calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Wednesday, format);
        Calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Thursday, format);
        Calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Friday, format);
    }

    void WeekendFormatChanged(int index)
    {
        weekendColor = ColorFromCombo(WeekendColorCombo);

        var format = new QTextCharFormat();
        format.SetForeground(new QBrush(weekendColor));
        Calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Saturday, format);
        Calendar.SetWeekdayTextFormat(Qt.DayOfWeek.Sunday, format);
    }

    void ReformatHeaders(int index)
    {
        string text = HeaderTextFormatCombo.CurrentText;
        var format = new QTextCharFormat();

        if (text == "Bold")
            format.SetFontWeight((int)QFont.Weight.Bold);
        else if (text == "Italic")
            format.SetFontItalic(true);
        else if (text == "Green")
            format.SetForeground(new QBrush(new QColor(Qt.GlobalColor.green)));
        Calendar.SetHeaderTextFormat(format);
    }

    void ReformatCalendarPage()
    {
        var mayFirst = new QDate(Calendar.YearShown(), 5, 1);

        var firstFriday = new QDate(Calendar.YearShown(), Calendar.MonthShown(), 1);
        while (firstFriday.DayOfWeek() != (int)Qt.DayOfWeek.Friday)
            firstFriday = firstFriday.AddDays(1);

        var mayFirstFormat = new QTextCharFormat();
        var firstFridayFormat = new QTextCharFormat();

        if (FirstFridayCheckBox.Checked)
            firstFridayFormat.SetForeground(new QBrush(new QColor(Qt.GlobalColor.blue)));
        else
            // Revert to the regular colour for this day of the week.
            firstFridayFormat.SetForeground(new QBrush(WeekdayColorFor(firstFriday)));

        Calendar.SetDateTextFormat(firstFriday, firstFridayFormat);

        // When it is checked, "May First in Red" always takes precedence over
        // "First Friday in Blue".
        if (MayFirstCheckBox.Checked)
            mayFirstFormat.SetForeground(new QBrush(new QColor(Qt.GlobalColor.red)));
        else if (!FirstFridayCheckBox.Checked || firstFriday.ToJulianDay() != mayFirst.ToJulianDay())
            // We can now be certain we won't be resetting "May First in Red" when we restore
            // May 1st's regular colour for this day of the week.
            mayFirstFormat.SetForeground(new QBrush(WeekdayColorFor(mayFirst)));

        Calendar.SetDateTextFormat(mayFirst, mayFirstFormat);
    }

    QColor WeekdayColorFor(QDate date)
    {
        int dow = date.DayOfWeek();
        return (dow == (int)Qt.DayOfWeek.Saturday || dow == (int)Qt.DayOfWeek.Sunday)
            ? weekendColor
            : weekdayColor;
    }
}

// Builds the window, drives every group through the generated signal layer, and checks the state
// each handler should have produced. No event loop: every signal in this example is emitted
// synchronously by Qt when a value changes.
[Demo("qtwidgets-calendarwidget")]
public static class CalendarWidget
{
    public static int Run(DemoContext demo)
    {

        #if QTSHARP_DEMO_SMOKE
        uint RgbOf(QTextCharFormat format) => format.Foreground().Color().Rgb();
        #endif
        QDate FirstFridayOf(int year, int month)
        {
            var d = new QDate(year, month, 1);
            while (d.DayOfWeek() != (int)Qt.DayOfWeek.Friday)
                d = d.AddDays(1);
            return d;
        }

        var window = new ApiCalendarWidgetWindow();
        var cal = window.Calendar;
        #if QTSHARP_DEMO_SMOKE
            window.Show();
        #else
            demo.Show(window);
        #endif

        uint black = new QColor(Qt.GlobalColor.black).Rgb();
        uint red = new QColor(Qt.GlobalColor.red).Rgb();
        uint blue = new QColor(Qt.GlobalColor.blue).Rgb();
        uint magenta = new QColor(Qt.GlobalColor.magenta).Rgb();
        int plainWeight = new QTextCharFormat().FontWeight();

        // ---------------------------------------------------------------- Preview group
#if QTSHARP_DEMO_SMOKE

        // ---------------------------------------------------------------- Preview group
        demo.Check(window.PreviewGroupBox.Title == "Preview", $"the preview group box is titled \"Preview\" (got '{window.PreviewGroupBox.Title}')");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.MinimumDate.ToJulianDay() == new QDate(1900, 1, 1).ToJulianDay(),
            $"the calendar minimum date is 1900-01-01 (got {cal.MinimumDate.ToString("yyyy-MM-dd")})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.MaximumDate.ToJulianDay() == new QDate(3000, 1, 1).ToJulianDay(),
            $"the calendar maximum date is 3000-01-01 (got {cal.MaximumDate.ToString("yyyy-MM-dd")})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.GridVisible, "the grid starts visible");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.NavigationBarVisible, "the navigation bar starts visible");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.PreviewLayout.RowMinimumHeight(0) > 0 && window.PreviewLayout.ColumnMinimumWidth(0) > 0,
            $"the preview layout was sized from the calendar's size hint ({cal.SizeHint.Width()}x{cal.SizeHint.Height()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.OuterLayout.sizeConstraint() == QLayout.SizeConstraint.SetFixedSize,
            $"the outer layout is fixed-size (got {window.OuterLayout.sizeConstraint()})");
        #endif

        // ---------------------------------------------------------------- Dates group (initial)
#if QTSHARP_DEMO_SMOKE

        // ---------------------------------------------------------------- Dates group (initial)
        demo.Check(window.MinimumDateEdit.Date.ToJulianDay() == cal.MinimumDate.ToJulianDay(),
            "the minimum date edit mirrors the calendar minimum");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.MaximumDateEdit.Date.ToJulianDay() == cal.MaximumDate.ToJulianDay(),
            "the maximum date edit mirrors the calendar maximum");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.CurrentDateEdit.Date.ToJulianDay() == cal.SelectedDate.ToJulianDay(),
            "the current date edit mirrors the selection");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.MinimumDateEdit.DisplayFormat == "MMM d yyyy",
            $"the date edit display format round-trips (got '{window.MinimumDateEdit.DisplayFormat}')");
        #endif

        // ---------------------------------------------------------------- General Options group
        // The official Locale row: the item data carries the QLocale, and choosing one sets the
        // calendar's locale (and follows the locale's first day of the week).
        int germanIndex = -1;
        for (int i = 0; i < window.LocaleCombo.Count; i++)
        {
            using var localeAtI = (QLocale)window.LocaleCombo.ItemData(i, (int)Qt.ItemDataRole.UserRole);
            if (localeAtI.language() == QLocale.Language.German
                && localeAtI.Territory() == QLocale.Country.Germany)
            {
                germanIndex = i;
                break;
            }
        }
        #if QTSHARP_DEMO_SMOKE
        demo.Check(germanIndex >= 0,
            $"the locale combo lists German/Germany among Qt's locales ({window.LocaleCombo.Count})");
        #endif
        window.LocaleCombo.SetCurrentIndex(germanIndex);
        using var german = new QLocale(QLocale.Language.German, QLocale.Country.Germany);
        using var calendarLocale = cal.Locale;
        #if QTSHARP_DEMO_SMOKE
        demo.Check(calendarLocale.language() == QLocale.Language.German,
            $"the locale combo sets the calendar's locale (got {calendarLocale.language()})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.FirstDayCombo.CurrentIndex == (int)german.FirstDayOfWeek() % 7,
            $"the locale's first day follows (combo {window.FirstDayCombo.CurrentIndex}, expected {(int)german.FirstDayOfWeek() % 7})");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.FirstDayCombo.Count == 7, $"the first-day combo has the seven weekdays (got {window.FirstDayCombo.Count})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.SelectionModeCombo.Count == 2, $"the selection-mode combo has two entries (got {window.SelectionModeCombo.Count})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.HorizontalHeaderCombo.Count == 3, $"the horizontal-header combo has three entries (got {window.HorizontalHeaderCombo.Count})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.VerticalHeaderCombo.Count == 2, $"the vertical-header combo has two entries (got {window.VerticalHeaderCombo.Count})");
        #endif

        #if QTSHARP_DEMO_SMOKE
        window.FirstDayCombo.SetCurrentIndex(3);        // Wednesday
        demo.Check(cal.FirstDayOfWeek == Qt.DayOfWeek.Wednesday,
            $"the first-day combo drives firstDayOfWeek (got {cal.FirstDayOfWeek})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        window.FirstDayCombo.SetCurrentIndex(0);        // Sunday
        demo.Check(cal.FirstDayOfWeek == Qt.DayOfWeek.Sunday, "the first day of the week round-trips");
        #endif

        window.SelectionModeCombo.SetCurrentIndex(1);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.selectionMode() == QCalendarWidget.SelectionMode.NoSelection,
            $"the selection-mode combo selects NoSelection (got {cal.selectionMode()})");
        #endif
        window.SelectionModeCombo.SetCurrentIndex(0);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.selectionMode() == QCalendarWidget.SelectionMode.SingleSelection,
            $"the selection-mode combo selects SingleSelection (got {cal.selectionMode()})");
        #endif

        window.GridCheckBox.SetChecked(false);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!cal.GridVisible, "unchecking Grid hides the grid");
        #endif
        window.GridCheckBox.SetChecked(true);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.GridVisible, "checking Grid shows the grid again");
        #endif

        window.NavigationCheckBox.SetChecked(false);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!cal.NavigationBarVisible, "unchecking Navigation bar hides the bar");
        #endif
        window.NavigationCheckBox.SetChecked(true);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.NavigationBarVisible, "checking Navigation bar shows the bar again");
        #endif

        window.HorizontalHeaderCombo.SetCurrentIndex(0);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.horizontalHeaderFormat() == QCalendarWidget.HorizontalHeaderFormat.SingleLetterDayNames,
            $"horizontal header 0 = single letters (got {cal.horizontalHeaderFormat()})");
        #endif
        window.HorizontalHeaderCombo.SetCurrentIndex(1);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.horizontalHeaderFormat() == QCalendarWidget.HorizontalHeaderFormat.ShortDayNames,
            $"horizontal header 1 = short names (got {cal.horizontalHeaderFormat()})");
        #endif
        window.HorizontalHeaderCombo.SetCurrentIndex(2);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.horizontalHeaderFormat() == QCalendarWidget.HorizontalHeaderFormat.NoHorizontalHeader,
            $"horizontal header 2 = none (got {cal.horizontalHeaderFormat()})");
        #endif

        window.VerticalHeaderCombo.SetCurrentIndex(0);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.verticalHeaderFormat() == QCalendarWidget.VerticalHeaderFormat.ISOWeekNumbers,
            $"vertical header 0 = ISO week numbers (got {cal.verticalHeaderFormat()})");
        #endif
        window.VerticalHeaderCombo.SetCurrentIndex(1);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.verticalHeaderFormat() == QCalendarWidget.VerticalHeaderFormat.NoVerticalHeader,
            $"vertical header 1 = none (got {cal.verticalHeaderFormat()})");
        #endif

#if QTSHARP_DEMO_SMOKE

        demo.Check(window.FirstDayLabel.Buddy().Handle == window.FirstDayCombo.Handle, "the first-day label's buddy is the combo box");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.HorizontalHeaderLabel.Buddy().Handle == window.HorizontalHeaderCombo.Handle, "the horizontal-header label's buddy is the combo box");
        #endif

        // ---------------------------------------------------------------- Dates group (signals)
        // Real dateChanged drives the calendar selection (the legacy port used editingFinished).
        window.CurrentDateEdit.SetDate(new QDate(2025, 6, 15));
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.SelectedDate.ToJulianDay() == new QDate(2025, 6, 15).ToJulianDay(),
            $"editing the current date edit moves the selection through dateChanged (got {cal.SelectedDate.ToString("yyyy-MM-dd")})");
        #endif

        // ...and selectionChanged drives the date edit.
        cal.SetSelectedDate(new QDate(2025, 7, 20));
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.CurrentDateEdit.Date.ToJulianDay() == new QDate(2025, 7, 20).ToJulianDay(),
            "selectionChanged moves the current date edit");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.PreviewLabel.text() == "Selected: 2025-07-20",
            $"the preview label follows selectionChanged (got '{window.PreviewLabel.text()}')");
        #endif

        // Raising the minimum through the date edit clamps the calendar selection.
        cal.SetSelectedDate(new QDate(2000, 1, 1));
        window.MinimumDateEdit.SetDate(new QDate(2020, 1, 1));
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.MinimumDate.ToJulianDay() == new QDate(2020, 1, 1).ToJulianDay(),
            "the minimum date edit sets the calendar minimum");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.SelectedDate.ToJulianDay() >= new QDate(2020, 1, 1).ToJulianDay(),
            $"the calendar clamps the selection to the new minimum (got {cal.SelectedDate.ToString("yyyy-MM-dd")})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.CurrentDateEdit.Date.ToJulianDay() == cal.SelectedDate.ToJulianDay(),
            "the current date edit followed the clamp");
        #endif

        window.MaximumDateEdit.SetDate(new QDate(2100, 12, 31));
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.MaximumDate.ToJulianDay() == new QDate(2100, 12, 31).ToJulianDay(),
            "the maximum date edit sets the calendar maximum");
        #endif

        // Restore the wide range for the remaining checks.
        window.MinimumDateEdit.SetDate(new QDate(1900, 1, 1));
        window.MaximumDateEdit.SetDate(new QDate(3000, 1, 1));
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.MinimumDate.ToJulianDay() == new QDate(1900, 1, 1).ToJulianDay()
              && cal.MaximumDate.ToJulianDay() == new QDate(3000, 1, 1).ToJulianDay(),
            "the date range restores to 1900..3000");
        #endif

        // ---------------------------------------------------------------- Text Formats group
#if QTSHARP_DEMO_SMOKE

        // ---------------------------------------------------------------- Text Formats group
        demo.Check(window.WeekdayColorCombo.CurrentIndex == window.WeekdayColorCombo.FindText("Black"),
            "the weekday colour defaults to Black");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.WeekendColorCombo.CurrentIndex == window.WeekendColorCombo.FindText("Red"),
            "the weekend colour defaults to Red");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(window.WeekdayColorCombo.Count == 4, $"the colour combo has four entries (got {window.WeekdayColorCombo.Count})");
        #endif

        // The colour is the combo item's user data, as addItem(text, variant) stores it.
#if QTSHARP_DEMO_SMOKE

        // The colour is the combo item's user data, as addItem(text, variant) stores it.
        demo.Check((int)window.WeekdayColorCombo.ItemData(0, (int)Qt.ItemDataRole.UserRole) == (int)Qt.GlobalColor.red,
            "combo item 0 carries the Red colour as Qt::UserRole data");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check((int)window.WeekdayColorCombo.ItemData(2, (int)Qt.ItemDataRole.UserRole) == (int)Qt.GlobalColor.black,
            "combo item 2 carries the Black colour as Qt::UserRole data");
        #endif

        // Weekday/weekend formats were applied in the constructor.
#if QTSHARP_DEMO_SMOKE

        // Weekday/weekend formats were applied in the constructor.
        demo.Check(RgbOf(cal.WeekdayTextFormat(Qt.DayOfWeek.Monday)) == black,
            $"Monday uses the weekday colour (got {RgbOf(cal.WeekdayTextFormat(Qt.DayOfWeek.Monday)):X8})");
#endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(RgbOf(cal.WeekdayTextFormat(Qt.DayOfWeek.Sunday)) == red,
            $"Sunday uses the weekend colour (got {RgbOf(cal.WeekdayTextFormat(Qt.DayOfWeek.Sunday)):X8})");
        #endif

        // Changing the colour combo reformats the whole week.
        window.WeekdayColorCombo.SetCurrentIndex(window.WeekdayColorCombo.FindText("Blue"));
        #if QTSHARP_DEMO_SMOKE
        demo.Check(RgbOf(cal.WeekdayTextFormat(Qt.DayOfWeek.Tuesday)) == blue,
            $"changing the weekday colour reformats Tuesday (got {RgbOf(cal.WeekdayTextFormat(Qt.DayOfWeek.Tuesday)):X8})");
        #endif
        window.WeekendColorCombo.SetCurrentIndex(window.WeekendColorCombo.FindText("Magenta"));
        #if QTSHARP_DEMO_SMOKE
        demo.Check(RgbOf(cal.WeekdayTextFormat(Qt.DayOfWeek.Saturday)) == magenta,
            $"changing the weekend colour reformats Saturday (got {RgbOf(cal.WeekdayTextFormat(Qt.DayOfWeek.Saturday)):X8})");
        #endif

        // Header text format.
        window.HeaderTextFormatCombo.SetCurrentIndex(0);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.HeaderTextFormat().FontWeight() == (int)QFont.Weight.Bold,
            $"Bold sets the header weight (got {cal.HeaderTextFormat().FontWeight()})");
        #endif
        window.HeaderTextFormatCombo.SetCurrentIndex(1);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.HeaderTextFormat().FontItalic() && cal.HeaderTextFormat().FontWeight() != (int)QFont.Weight.Bold,
            "Italic sets the header italic flag and clears the weight");
        #endif
        window.HeaderTextFormatCombo.SetCurrentIndex(2);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!cal.HeaderTextFormat().FontItalic() && cal.HeaderTextFormat().FontWeight() == plainWeight,
            $"Plain clears the header formatting (weight {cal.HeaderTextFormat().FontWeight()} vs the default {plainWeight})");
        #endif

        // First Friday / May 1, on a page where they fall on different days.
        window.WeekdayColorCombo.SetCurrentIndex(window.WeekdayColorCombo.FindText("Black"));
        window.WeekendColorCombo.SetCurrentIndex(window.WeekendColorCombo.FindText("Red"));
        cal.SetCurrentPage(2026, 6);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.YearShown() == 2026 && cal.MonthShown() == 6,
            $"setCurrentPage round-trips (got {cal.YearShown()}-{cal.MonthShown():D2})");
        #endif

        var juneFirstFriday = FirstFridayOf(2026, 6);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(juneFirstFriday.ToJulianDay() == new QDate(2026, 6, 5).ToJulianDay(),
            $"the first Friday of June 2026 is the 5th (got {juneFirstFriday.ToString("yyyy-MM-dd")})");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(RgbOf(cal.DateTextFormat(juneFirstFriday)) == black,
            $"the first Friday keeps the weekday colour when unchecked (got {RgbOf(cal.DateTextFormat(juneFirstFriday)):X8})");
        #endif

        window.FirstFridayCheckBox.SetChecked(true);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(RgbOf(cal.DateTextFormat(juneFirstFriday)) == blue,
            $"checking \"First Friday in blue\" turns it blue (got {RgbOf(cal.DateTextFormat(juneFirstFriday)):X8})");
        #endif

        window.MayFirstCheckBox.SetChecked(true);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(RgbOf(cal.DateTextFormat(new QDate(2026, 5, 1))) == red,
            "checking \"May 1 in red\" turns May 1 red");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(RgbOf(cal.DateTextFormat(juneFirstFriday)) == blue,
            "the first Friday stays blue when May 1 is a different day");
        #endif

        // Colliding case: May 1st 2026 is itself a Friday, so red must win.
        cal.SetCurrentPage(2026, 5);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(FirstFridayOf(2026, 5).ToJulianDay() == new QDate(2026, 5, 1).ToJulianDay(),
            "May 1st 2026 is a Friday, so first-Friday and May-1 collide");
        #endif
        #if QTSHARP_DEMO_SMOKE
        demo.Check(RgbOf(cal.DateTextFormat(new QDate(2026, 5, 1))) == red,
            $"on a colliding day May 1 in red wins over first Friday in blue (got {RgbOf(cal.DateTextFormat(new QDate(2026, 5, 1))):X8})");
        #endif

        window.MayFirstCheckBox.SetChecked(false);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(cal.DateTextFormat(new QDate(2026, 5, 1)).Foreground().Style() == Qt.BrushStyle.NoBrush,
            "unchecking the red override on a colliding day clears the date format (upstream behaviour)");
        #endif

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        return 0;
        #endif
    }
}
