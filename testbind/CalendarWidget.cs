using System;
using QtSharp.QtCore;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

public class CalendarWindow : QWidget
{
    QCalendarWidget calendar;

    public CalendarWindow() : base(null, 0)
    {
        var previewGroupBox = new QGroupBox("Preview");

        calendar = new QCalendarWidget();
        calendar.MinimumDate = new QDate(1900, 1, 1);
        calendar.MaximumDate = new QDate(3000, 1, 1);
        calendar.GridVisible = true;

        var previewLayout = new QGridLayout();
        previewLayout.AddWidget(calendar, 0, 0);
        previewGroupBox.Layout = previewLayout;

        var generalOptionsGroupBox = new QGroupBox("General Options");

        var firstDayLabel = new QLabel("Week starts on:");
        var firstDayCombo = new QComboBox();
        firstDayCombo.AddItems(new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" });

        var selectionModeLabel = new QLabel("Selection mode:");
        var selectionModeCombo = new QComboBox();
        selectionModeCombo.AddItems(new[] { "Single selection", "No selection" });

        var gridCheckBox = new QCheckBox("Show grid");
        gridCheckBox.Checked = true;

        var navigationCheckBox = new QCheckBox("Show navigation bar");
        navigationCheckBox.Checked = true;

        var horizontalHeaderLabel = new QLabel("Horizontal header:");
        var horizontalHeaderCombo = new QComboBox();
        horizontalHeaderCombo.AddItems(new[] { "Single letter day names", "Short day names", "Long day names", "None" });
        horizontalHeaderCombo.CurrentIndex = (int)QCalendarWidget.HorizontalHeaderFormat.ShortDayNames;

        var verticalHeaderLabel = new QLabel("Vertical header:");
        var verticalHeaderCombo = new QComboBox();
        verticalHeaderCombo.AddItems(new[] { "ISO week numbers", "None" });

        var generalLayout = new QGridLayout();
        generalLayout.AddWidget(firstDayLabel, 0, 0);
        generalLayout.AddWidget(firstDayCombo, 0, 1);
        generalLayout.AddWidget(selectionModeLabel, 1, 0);
        generalLayout.AddWidget(selectionModeCombo, 1, 1);
        generalLayout.AddWidget(horizontalHeaderLabel, 2, 0);
        generalLayout.AddWidget(horizontalHeaderCombo, 2, 1);
        generalLayout.AddWidget(verticalHeaderLabel, 3, 0);
        generalLayout.AddWidget(verticalHeaderCombo, 3, 1);
        generalLayout.AddWidget(gridCheckBox, 4, 0, 1, 2);
        generalLayout.AddWidget(navigationCheckBox, 5, 0, 1, 2);
        generalOptionsGroupBox.Layout = generalLayout;

        firstDayCombo.CurrentIndexChangedSignal.Connect(index =>
            calendar.FirstDayOfWeek = (Qt.DayOfWeek)(index + 1));
        selectionModeCombo.CurrentIndexChangedSignal.Connect(index =>
            calendar.selectionMode = index == 0
                ? QCalendarWidget.SelectionMode.SingleSelection
                : QCalendarWidget.SelectionMode.NoSelection);
        horizontalHeaderCombo.CurrentIndexChangedSignal.Connect(index =>
            calendar.horizontalHeaderFormat = (QCalendarWidget.HorizontalHeaderFormat)index);
        verticalHeaderCombo.CurrentIndexChangedSignal.Connect(index =>
            calendar.verticalHeaderFormat = index == 0
                ? QCalendarWidget.VerticalHeaderFormat.ISOWeekNumbers
                : QCalendarWidget.VerticalHeaderFormat.NoVerticalHeader);
        gridCheckBox.ToggledSignal.Connect(b => calendar.GridVisible = b);
        navigationCheckBox.ToggledSignal.Connect(b => calendar.NavigationBarVisible = b);

        firstDayCombo.CurrentIndex = 0;
        selectionModeCombo.CurrentIndex = 0;
        verticalHeaderCombo.CurrentIndex = 0;

        var layout = new QGridLayout();
        layout.AddWidget(previewGroupBox, 0, 0);
        layout.AddWidget(generalOptionsGroupBox, 0, 1);
        Layout = layout;

        WindowTitle = "Calendar Widget";
        Resize(500, 400);
    }
}
