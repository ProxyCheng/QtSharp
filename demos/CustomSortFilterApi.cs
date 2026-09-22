using System;
using QtSharp.Api;

// Port of examples/widgets/itemviews/customsortfiltermodel on the new pipeline.
//
// The example is the custsom QSortFilterProxyModel: a subclass that filters rows on a regular
// expression *and* a date range, and that also sorts by its own rule. Both hooks are what this
// demo is for - `filterAcceptsRow` and `lessThan` are the two virtuals a real application
// overrides, and they are only reachable from C# because QSortFilterProxyModel is an extension
// class in the model (see gen/ApiModel2.cs).
//
// Two mechanical differences, both because QVariant has no bound date accessor:
//   * the date column is carried as an ISO-8601 string, which compares correctly as text;
//   * the minimum/maximum dates keep the official QDate parameters and are turned into ISO text
//     when the filter stores them.
public sealed class ApiSortFilterProxyModel : QSortFilterProxyModel
{
    public int FilterAcceptsRowCalls;
    public int LessThanCalls;

    string minIso = "1970-01-01";
    string maxIso = "2099-12-31";

    public ApiSortFilterProxyModel(QObject parent = null) : base(parent) { }

    // The official's two custom entry points: a date range the proxy model knows nothing about, so
    // it has to announce the filter change itself.
    public void SetFilterMinimumDate(QDate date)
    {
        BeginFilterChange();
        minIso = date.ToString(Qt.DateFormat.ISODate);
        EndFilterChange(QSortFilterProxyModel.Directions.Rows);
    }

    public void SetFilterMaximumDate(QDate date)
    {
        BeginFilterChange();
        maxIso = date.ToString(Qt.DateFormat.ISODate);
        EndFilterChange(QSortFilterProxyModel.Directions.Rows);
    }

    public string MinimumDateIso => minIso;
    public string MaximumDateIso => maxIso;

    public override bool FilterAcceptsRow(int sourceRow, QModelIndex sourceParent)
    {
        FilterAcceptsRowCalls++;

        var model = SourceModel;
        using var index0 = model.Index(sourceRow, 0, sourceParent);
        using var index1 = model.Index(sourceRow, 1, sourceParent);
        using var index2 = model.Index(sourceRow, 2, sourceParent);
        using var data0 = model.Data(index0);
        using var data1 = model.Data(index1);
        using var data2 = model.Data(index2);

        // `sourceModel()->data(index).toString().contains(FilterRegularExpression)` - the
        // pattern is a regular expression, so this is a match and not a substring test.
        return (Matches(data0.ToString()) || Matches(data1.ToString()))
               && DateInRange(data2.ToString());
    }

    bool Matches(string text)
    {
        using var expression = FilterRegularExpression;
        using var match = expression.Match(text);
        return match.HasMatch();
    }

    bool DateInRange(string iso)
        => string.CompareOrdinal(iso, minIso) > 0 && string.CompareOrdinal(iso, maxIso) < 0;

    public override bool LessThan(QModelIndex left, QModelIndex right)
    {
        LessThanCalls++;

        var model = SourceModel;
        using var leftData = model.Data(left);
        using var rightData = model.Data(right);
        string leftString = leftData.ToString();
        string rightString = rightData.ToString();

        // The date column: the official compares QDateTime values, the port compares the same
        // strings (ISO-8601 sorts correctly as text).
        if (left.Column() == 2)
            return string.CompareOrdinal(leftString, rightString) < 0;

        // The e-mail column sorts by what comes before the '@', which is what the official's
        // `[\w\.]*@[\w\.]*` capture amounts to for a well-formed address.
        if (left.Column() == 1)
        {
            leftString = Before(leftString);
            rightString = Before(rightString);
        }

        return string.Compare(leftString, rightString, StringComparison.OrdinalIgnoreCase) < 0;
    }

    static string Before(string email)
    {
        int at = email.IndexOf('@');
        return at < 0 ? email : email.Substring(0, at);
    }
}

// The official FilterWidget: a line edit with a pattern-syntax choice and a case-sensitivity
// choice. (The official is a QLineEdit subclass with a QToolButton; here they are two controls,
// because the syntax/case options are the part the window actually reads.)
public sealed class ApiFilterWidget : QWidget
{
    public readonly QLineEdit LineEdit;
    public readonly QComboBox SyntaxComboBox;
    public readonly QComboBox CaseComboBox;

    public ApiFilterWidget(QWidget parent = null) : base(parent)
    {
        LineEdit = new QLineEdit(this);
        SyntaxComboBox = new QComboBox(this);
        SyntaxComboBox.AddItems(new[] { "Regular expression", "Fixed string", "Wildcard" });
        CaseComboBox = new QComboBox(this);
        CaseComboBox.AddItems(new[] { "Case Insensitive", "Case Sensitive" });

        var layout = new QHBoxLayout();
        layout.AddWidget(LineEdit, 1);
        layout.AddWidget(SyntaxComboBox);
        layout.AddWidget(CaseComboBox);
        SetLayout(layout);
    }

    public string Text => LineEdit.Text;
    public QSignal<string> TextChangedSignal => LineEdit.TextChangedSignal;
}

public sealed class ApiCustomSortFilterWindow : QWidget
{
    public readonly ApiSortFilterProxyModel ProxyModel;
    public readonly QStandardItemModel SourceModel;
    public readonly QTreeView SourceView;
    public readonly QTreeView ProxyView;
    public readonly ApiFilterWidget FilterWidget;
    public readonly QDateEdit FromDateEdit;
    public readonly QDateEdit ToDateEdit;

    // The official's address book: name, e-mail, date.
    public static readonly (string Name, string Email, string Date)[] Addresses =
    {
        ("Grace",   "grace@example.com",   "2020-01-15"),
        ("gregory", "gregory@example.com", "2019-06-01"),
        ("Sports",  "sports@example.com",  "2021-03-10"),
        ("sporty",  "sporty@example.com",  "2022-12-31"),
        ("Zoe",     "zoe@example.com",     "2018-05-05"),
    };

    public ApiCustomSortFilterWindow(QWidget parent = null) : base(parent)
    {
        SourceModel = new QStandardItemModel(Addresses.Length, 3, this);
        for (int i = 0; i < Addresses.Length; i++)
        {
            SourceModel.SetItem(i, 0, new QStandardItem(Addresses[i].Name));
            SourceModel.SetItem(i, 1, new QStandardItem(Addresses[i].Email));
            SourceModel.SetItem(i, 2, new QStandardItem(Addresses[i].Date));
        }

        ProxyModel = new ApiSortFilterProxyModel(this);
        ProxyModel.SetSourceModel(SourceModel);

        SourceView = new QTreeView(this);
        SourceView.SetRootIsDecorated(false);
        SourceView.SetAlternatingRowColors(true);
        SourceView.SetModel(SourceModel);

        ProxyView = new QTreeView(this);
        ProxyView.SetRootIsDecorated(false);
        ProxyView.SetAlternatingRowColors(true);
        ProxyView.SetModel(ProxyModel);
        ProxyView.SetSortingEnabled(true);
        ProxyView.SortByColumn(1, Qt.SortOrder.AscendingOrder);

        FilterWidget = new ApiFilterWidget(this);
        FilterWidget.LineEdit.SetText("Grace|Sports");
        FilterWidget.TextChangedSignal.Connect(_ => TextFilterChanged());
        FilterWidget.SyntaxComboBox.CurrentIndexChangedSignal.Connect(_ => TextFilterChanged());
        FilterWidget.CaseComboBox.CurrentIndexChangedSignal.Connect(_ => TextFilterChanged());

        FromDateEdit = new QDateEdit(this);
        FromDateEdit.SetDate(new QDate(1970, 1, 1));
        ToDateEdit = new QDateEdit(this);
        ToDateEdit.SetDate(new QDate(2099, 12, 31));
        FromDateEdit.DateChangedSignal.Connect(_ => DateFilterChanged());
        ToDateEdit.DateChangedSignal.Connect(_ => DateFilterChanged());

        var proxyLayout = new QGridLayout();
        proxyLayout.AddWidget(ProxyView, 0, 0, 1, 3);
        proxyLayout.AddWidget(new QLabel("Filter pattern:", this), 1, 0);
        proxyLayout.AddWidget(FilterWidget, 1, 1, 1, 2);
        proxyLayout.AddWidget(new QLabel("From:", this), 3, 0);
        proxyLayout.AddWidget(FromDateEdit, 3, 1, 1, 2);
        proxyLayout.AddWidget(new QLabel("To:", this), 4, 0);
        proxyLayout.AddWidget(ToDateEdit, 4, 1, 1, 2);

        var sourceGroup = new QGroupBox("Original Model", this);
        var sourceLayout = new QHBoxLayout();
        sourceLayout.AddWidget(SourceView);
        sourceGroup.SetLayout(sourceLayout);

        var proxyGroup = new QGroupBox("Sorted/Filtered Model", this);
        proxyGroup.SetLayout(proxyLayout);

        var mainLayout = new QVBoxLayout();
        mainLayout.AddWidget(sourceGroup);
        mainLayout.AddWidget(proxyGroup);
        SetLayout(mainLayout);

        SetWindowTitle("Custom Sort/Filter Model");
        Resize(500, 450);

        TextFilterChanged();
        DateFilterChanged();
    }

    public void TextFilterChanged()
    {
        string pattern = FilterWidget.LineEdit.Text;
        switch (FilterWidget.SyntaxComboBox.currentIndex())
        {
            case 1:
                pattern = QRegularExpression.Escape(pattern);
                break;
            case 2:
                pattern = QRegularExpression.WildcardToRegularExpression(pattern);
                break;
        }

        var options = QRegularExpression.PatternOptions.NoPatternOption;
        if (FilterWidget.CaseComboBox.currentIndex() == 0)
            options |= QRegularExpression.PatternOptions.CaseInsensitiveOption;

        using var expression = new QRegularExpression(pattern, options);
        ProxyModel.SetFilterRegularExpression(expression);
    }

    public void DateFilterChanged()
    {
        using var from = FromDateEdit.date();
        using var to = ToDateEdit.date();
        ProxyModel.SetFilterMinimumDate(from);
        ProxyModel.SetFilterMaximumDate(to);
    }

    /// <summary>The proxy's rows, as (name, email, date) triples, in proxy order.</summary>
    public (string Name, string Email, string Date)[] ProxyRows()
    {
        var rows = new (string, string, string)[ProxyModel.RowCount()];
        for (int i = 0; i < rows.Length; i++)
        {
            using var index = ProxyModel.Index(i, 0);
            using var name = ProxyModel.Data(index);
            rows[i] = (name.ToString(), Column(i, 1), Column(i, 2));
        }
        return rows;
    }

    string Column(int row, int column)
    {
        using var index = ProxyModel.Index(row, column);
        using var value = ProxyModel.Data(index);
        return value.ToString();
    }
}

public static class ApiCustomSortFilterDemo
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

        using var window = new ApiCustomSortFilterWindow();
        window.Show();

        // The source model is untouched by the proxy: the window shows both side by side.
        Check(window.SourceModel.RowCount() == 5 && window.SourceModel.ColumnCount() == 3,
            $"the source model has five rows and three columns "
            + $"({window.SourceModel.RowCount()}x{window.SourceModel.ColumnCount()})");
        Check(window.SourceView.Model() != null
              && window.SourceView.Model().Handle == window.SourceModel.Handle,
            "the source view shows the original model");

        // The initial filter: the pattern "Grace|Sports" as a regular expression.
        var rows = window.ProxyRows();
        Check(rows.Length == 2,
            $"the pattern Grace|Sports keeps two rows (got {rows.Length}: {string.Join(", ", System.Array.ConvertAll(rows, r => r.Name))})");
        bool graceSports = Array.Exists(rows, r => r.Name == "Grace") && Array.Exists(rows, r => r.Name == "Sports");
        Check(graceSports, "the two rows kept are Grace and Sports");

        // The managed override is what filtered them.
        Check(window.ProxyModel.FilterAcceptsRowCalls >= 5,
            $"Qt called the managed filterAcceptsRow({window.ProxyModel.FilterAcceptsRowCalls} times)");

        // Fixed string syntax: QRegularExpression::escape makes the pattern literal.
        window.FilterWidget.SyntaxComboBox.SetCurrentIndex(1);
        window.FilterWidget.LineEdit.SetText("greg");
        rows = window.ProxyRows();
        Check(rows.Length == 1 && rows[0].Name == "gregory",
            $"a fixed-string pattern keeps only gregory (got {rows.Length})");

        // Wildcard syntax: QRegularExpression::wildcardToRegularExpression.
        window.FilterWidget.SyntaxComboBox.SetCurrentIndex(2);
        window.FilterWidget.LineEdit.SetText("spor*");
        rows = window.ProxyRows();
        Check(rows.Length == 2, $"the wildcard 'spor*' keeps Sports and sporty (got {rows.Length})");

        // Case sensitivity is a pattern option, and it reaches the proxy.
        window.FilterWidget.CaseComboBox.SetCurrentIndex(1);
        window.FilterWidget.LineEdit.SetText("SPORT*");
        rows = window.ProxyRows();
        Check(rows.Length == 0, $"case-sensitively 'SPORT*' matches nothing (got {rows.Length})");
        window.FilterWidget.CaseComboBox.SetCurrentIndex(0);
        rows = window.ProxyRows();
        Check(rows.Length == 2, $"case-insensitively it matches again (got {rows.Length})");

        // Back to a pattern that matches everything, then the date range: the custom minimum and
        // maximum dates are what the official example adds on top of the pattern.
        window.FilterWidget.SyntaxComboBox.SetCurrentIndex(0);
        window.FilterWidget.LineEdit.SetText(".");
        rows = window.ProxyRows();
        Check(rows.Length == 5, $"the pattern '.' keeps every row (got {rows.Length})");

        window.FromDateEdit.SetDate(new QDate(2020, 1, 1));
        rows = window.ProxyRows();
        Check(rows.Length == 3,
            $"a minimum date of 2020-01-01 drops the two older rows (got {rows.Length})");

        window.ToDateEdit.SetDate(new QDate(2021, 12, 31));
        rows = window.ProxyRows();
        Check(rows.Length == 2,
            $"a maximum date of 2021-12-31 drops the 2022 row (got {rows.Length})");
        Check(window.ProxyModel.MinimumDateIso == "2020-01-01"
              && window.ProxyModel.MaximumDateIso == "2021-12-31",
            $"the custom dates reached the proxy model "
            + $"({window.ProxyModel.MinimumDateIso} .. {window.ProxyModel.MaximumDateIso})");

        // The second hook: the managed lessThan sorts the e-mail column by the name before the '@'
        // (case-insensitively), so Grace comes before Sports - an order that only our override
        // produces, since Qt's default would compare the whole address.
        rows = window.ProxyRows();
        Check(window.ProxyModel.LessThanCalls > 0,
            $"Qt called the managed lessThan({window.ProxyModel.LessThanCalls} times)");
        Check(rows.Length == 2 && rows[0].Email == "grace@example.com" && rows[1].Email == "sports@example.com",
            $"the proxy is ordered by the managed lessThan "
            + $"({string.Join(", ", System.Array.ConvertAll(rows, r => r.Email))})");

        // The proxy's own model API, mapped back to the source.
        Check(window.ProxyModel.ColumnCount() == 3 && window.ProxyModel.RowCount() == 2,
            $"the proxy reports three columns and its filtered row count "
            + $"({window.ProxyModel.ColumnCount()}x{window.ProxyModel.RowCount()})");
        using (var proxyIndex = window.ProxyModel.Index(0, 0))
        using (var sourceIndex = window.ProxyModel.MapToSource(proxyIndex))
        {
            using var sourceValue = window.SourceModel.Data(sourceIndex);
            Check(sourceIndex.IsValid() && sourceValue.ToString() == rows[0].Name,
                $"mapToSource turns the first proxy index into the matching source row "
                + $"(row {sourceIndex.Row}, '{sourceValue}' vs proxy '{rows[0].Name}')");
        }

        window.ProxyModel.SetFilterKeyColumn(1);
        Check(window.ProxyModel.filterKeyColumn() == 1,
            $"the filter key column round-trips (got {window.ProxyModel.filterKeyColumn()})");
        window.ProxyModel.SetDynamicSortFilter(false);
        Check(!window.ProxyModel.dynamicSortFilter(),
            "the dynamic sort filter flag round-trips");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
