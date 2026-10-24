using System;
using System.IO;

// Port of examples/widgets/tools/completer on the new pipeline.
//
// The example is the QCompleter tour: four different models behind one line edit (a plain and a
// full-path QFileSystemModel, a two-column country model and a word list), three completion modes,
// case sensitivity, the visible-item count, wrap-around and a custom popup view.
//
// Two deliberate differences from the official source:
//   * the country/word lists come from the .txt files under :/resources, which need a qrc the
//     repository does not have; the same data is spelled out in code here;
//   * the completers are created without a QObject parent and the previous one is disposed, which
//     is what the official `delete completer` does (a parented wrapper is never released by
//     Dispose - the generated destructor asks Qt first).
public sealed class ApiFullPathFileSystemModel : QFileSystemModel
{
    public int DataCalls;

    public ApiFullPathFileSystemModel(QObject parent = null) : base(parent) { }

    // The official FileSystemModel: column 0 shows the whole path instead of the file name. This
    // only works because QFileSystemModel sits in the QAbstractItemModel subtree, so `data` is an
    // extension point even though QFileSystemModel itself is not an extension class.
    public override QVariant Data(QModelIndex index, int role = default)
    {
        DataCalls++;
        if (role == (int)Qt.ItemDataRole.DisplayRole && index.Column() == 0)
            return new QVariant(QDir.ToNativeSeparators(FilePath(index)));
        return base.Data(index, role);
    }
}

public sealed class ApiCompleterWindow : QMainWindow
{
    public static readonly string[] Words =
    {
        "auto", "automobile", "automatic", "autumn",
        "bear", "beast", "beat", "beetle",
        "grace", "graceful", "gravel", "gravity",
    };

    public static readonly string[,] Countries =
    {
        { "Germany", "DE" }, { "Greece", "GR" }, { "Greenland", "GL" },
        { "Grenada", "GD" }, { "Norway", "NO" },
    };

    public readonly QComboBox ModelComboBox;
    public readonly QComboBox ModeComboBox;
    public readonly QComboBox CaseComboBox;
    public readonly QSpinBox MaxVisibleSpinBox;
    public readonly QCheckBox WrapCheckBox;
    public readonly QLabel ContentsLabel;
    public readonly QLineEdit LineEdit;

    public QCompleter Completer;
    public ApiFullPathFileSystemModel FullPathModel;
    public QStringListModel WordModel;
    public QTreeView CountryPopup;

    public readonly string FileRoot;

    public ApiCompleterWindow(string fileRoot, QWidget parent = null) : base(parent)
    {
        FileRoot = fileRoot;

        CreateMenu();

        var centralWidget = new QWidget();
        SetCentralWidget(centralWidget);

        ModelComboBox = new QComboBox();
        ModelComboBox.AddItems(new[]
        {
            "QFileSystemModel", "QFileSystemModel that shows full path", "Country list", "Word list",
        });

        ModeComboBox = new QComboBox();
        ModeComboBox.AddItems(new[] { "Inline", "Filtered Popup", "Unfiltered Popup" });
        ModeComboBox.SetCurrentIndex(1);

        CaseComboBox = new QComboBox();
        CaseComboBox.AddItems(new[] { "Case Insensitive", "Case Sensitive" });

        MaxVisibleSpinBox = new QSpinBox();
        MaxVisibleSpinBox.SetRange(3, 25);
        MaxVisibleSpinBox.SetValue(10);

        WrapCheckBox = new QCheckBox("Wrap around completions");
        WrapCheckBox.SetChecked(true);
        // Connected once and aimed at the *current* completer: ChangeModel() replaces it.
        WrapCheckBox.ToggledSignal.Connect(on =>
        {
            if (Completer != null)
                Completer.SetWrapAround(on);
        });

        ContentsLabel = new QLabel();
        ContentsLabel.SetSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed);

        LineEdit = new QLineEdit();

        ModelComboBox.CurrentIndexChangedSignal.Connect(_ => ChangeModel());
        ModeComboBox.CurrentIndexChangedSignal.Connect(index => ChangeMode(index));
        CaseComboBox.CurrentIndexChangedSignal.Connect(index => ChangeCase(index));
        MaxVisibleSpinBox.ValueChangedSignal.Connect(max =>
        {
            if (Completer != null)
                Completer.SetMaxVisibleItems(max);
        });

        var layout = new QGridLayout();
        layout.AddWidget(new QLabel("Model"), 0, 0);
        layout.AddWidget(ModelComboBox, 0, 1);
        layout.AddWidget(new QLabel("Completion Mode"), 1, 0);
        layout.AddWidget(ModeComboBox, 1, 1);
        layout.AddWidget(new QLabel("Case Sensitivity"), 2, 0);
        layout.AddWidget(CaseComboBox, 2, 1);
        layout.AddWidget(new QLabel("Max Visible Items"), 3, 0);
        layout.AddWidget(MaxVisibleSpinBox, 3, 1);
        layout.AddWidget(WrapCheckBox, 4, 0);
        layout.AddWidget(ContentsLabel, 5, 0, 1, 2);
        layout.AddWidget(LineEdit, 6, 0, 1, 2);
        centralWidget.SetLayout(layout);

        ChangeModel();

        SetWindowTitle("Completer");
        Resize(460, 300);
    }

    void CreateMenu()
    {
        var exitAction = new QAction("Exit", this);
        var aboutAct = new QAction("About", this);
        var aboutQtAct = new QAction("About Qt", this);

        // Quit from a menu action, with no application reference in reach - the PyQt
        // spelling QApplication.instance().quit(), which reaches the one application object.
        exitAction.TriggeredSignal.Connect(_ => QCoreApplication.Instance().Quit());

        var fileMenu = MenuBar().AddMenu("File");
        fileMenu.AddAction(exitAction);

        var helpMenu = MenuBar().AddMenu("About");
        helpMenu.AddAction(aboutAct);
        helpMenu.AddAction(aboutQtAct);
    }

    public void ChangeMode(int index)
    {
        QCompleter.CompletionMode mode;
        if (index == 0)
            mode = QCompleter.CompletionMode.InlineCompletion;
        else if (index == 1)
            mode = QCompleter.CompletionMode.PopupCompletion;
        else
            mode = QCompleter.CompletionMode.UnfilteredPopupCompletion;

        if (Completer != null)
            Completer.SetCompletionMode(mode);
    }

    public void ChangeCase(int index)
    {
        if (Completer != null)
            Completer.SetCaseSensitivity(index == 1 ? Qt.CaseSensitivity.CaseSensitive
                                                   : Qt.CaseSensitivity.CaseInsensitive);
    }

    /// <summary>Builds the model/completer for the chosen entry, the official changeModel().</summary>
    public void ChangeModel()
    {
        var previous = Completer;
        var completer = new QCompleter();
        // Assigned before the helpers below run, like the official changeModel(): ChangeMode and
        // ChangeCase go through the member, and QCompleter's case sensitivity defaults to
        // CaseSensitive, so the combo box's "Case Insensitive" only takes effect if they see the
        // new completer.
        Completer = completer;
        completer.SetMaxVisibleItems(MaxVisibleSpinBox.value());

        switch (ModelComboBox.currentIndex())
        {
            case 1:
            {
                var fsModel = new ApiFullPathFileSystemModel(completer);
                fsModel.SetRootPath(FileRoot);
                completer.SetModel(fsModel);
                FullPathModel = fsModel;
                ContentsLabel.SetText("Enter file path");
                break;
            }
            case 2:
            {
                var model = new QStandardItemModel(Countries.GetLength(0), 2, completer);
                for (int i = 0; i < Countries.GetLength(0); i++)
                {
                    model.SetItem(i, 0, new QStandardItem(Countries[i, 0]));
                    model.SetItem(i, 1, new QStandardItem(Countries[i, 1]));
                }
                completer.SetModel(model);

                var treeView = new QTreeView();
                completer.SetPopup(treeView);
                treeView.SetRootIsDecorated(false);
                treeView.Header().Hide();
                treeView.Header().SetStretchLastSection(false);
                CountryPopup = treeView;
                ContentsLabel.SetText("Enter name of your country");
                break;
            }
            case 3:
            {
                var model = new QStringListModel(Words, completer);
                completer.SetModel(model);
                WordModel = model;
                ContentsLabel.SetText("Enter a word");
                break;
            }
            default:
            {
                var fsModel = new QFileSystemModel(completer);
                fsModel.SetRootPath(FileRoot);
                completer.SetModel(fsModel);
                ContentsLabel.SetText("Enter file path");
                break;
            }
        }

        ChangeMode(ModeComboBox.currentIndex());
        ChangeCase(CaseComboBox.currentIndex());
        completer.SetWrapAround(WrapCheckBox.IsChecked());
        LineEdit.SetCompleter(completer);

        // Qt does not own a completer set on a line edit, so the previous one has to go by hand -
        // and only after the line edit has been pointed at the replacement.
        previous?.Dispose();
    }
}

public static class ApiCompleterDemo
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

        // The file-system models need something small and known to watch.
        string root = Path.Combine(Path.GetTempPath(), "qtsharp-completer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "alpha.txt"), "alpha");
        File.WriteAllText(Path.Combine(root, "beta.txt"), "beta");

        using var window = new ApiCompleterWindow(root);
        window.Show();

        // The controls of the example, with the official ranges and defaults.
        Check(window.ModelComboBox.count() == 4 && window.ModeComboBox.count() == 3
              && window.CaseComboBox.count() == 2,
            $"the combo boxes are populated (models={window.ModelComboBox.count()}, "
            + $"modes={window.ModeComboBox.count()}, cases={window.CaseComboBox.count()})");
        Check(window.MaxVisibleSpinBox.minimum() == 3 && window.MaxVisibleSpinBox.maximum() == 25
              && window.MaxVisibleSpinBox.value() == 10,
            $"the visible-item spin box is 3..25, default 10 "
            + $"({window.MaxVisibleSpinBox.minimum()}..{window.MaxVisibleSpinBox.maximum()}, "
            + $"value {window.MaxVisibleSpinBox.value()})");
        Check(window.WrapCheckBox.IsChecked() && window.ModeComboBox.currentIndex() == 1
              && window.CaseComboBox.currentIndex() == 0,
            "wrap-around starts checked, the mode is Filtered Popup and the case is insensitive");

        // The default model is the plain QFileSystemModel rooted where we pointed it.
        Check(window.Completer.Model() != null
              && window.Completer.Model().MetaObject().ClassName() == "QFileSystemModel",
            $"the default completer model is a QFileSystemModel "
            + $"(got {window.Completer.Model()?.MetaObject().ClassName()})");
        Check(window.ContentsLabel.Text == "Enter file path",
            $"the contents label follows the model (got '{window.ContentsLabel.Text}')");
        Check(window.LineEdit.Completer() != null
              && window.LineEdit.Completer().Handle == window.Completer.Handle,
            "the line edit was given the completer");

        // The full-path model: the managed data() override is what QCompleter reads for column 0.
        window.ModelComboBox.SetCurrentIndex(1);
        Check(window.FullPathModel != null && window.Completer.Model() != null
              && window.Completer.Model().Handle == window.FullPathModel.Handle,
            "choosing the second entry installs the managed QFileSystemModel subclass");

        // The word list: a QStringListModel behind the completer, with the completion API.
        window.ModelComboBox.SetCurrentIndex(3);
        Check(window.ContentsLabel.Text == "Enter a word",
            $"choosing the word list updates the contents label (got '{window.ContentsLabel.Text}')");
        Check(window.Completer.Model().MetaObject().ClassName() == "QStringListModel"
              && window.Completer.Model().RowCount() == ApiCompleterWindow.Words.Length
              && window.WordModel.StringList().Length == ApiCompleterWindow.Words.Length
              && window.WordModel.StringList()[0] == "auto",
            $"the word list is a QStringListModel carrying the C# array "
            + $"({window.Completer.Model().MetaObject().ClassName()}, "
            + $"{window.Completer.Model().RowCount()} rows, first '{window.WordModel.StringList()[0]}')");

        // Completion itself: prefix -> count, and the first completion. The model is a plain,
        // unsorted QStringListModel here, which is what Qt's linear completion engine handles.
        window.Completer.SetCompletionPrefix("gra");
        Check(window.Completer.CompletionCount() == 4,
            $"the prefix 'gra' matches four words (got {window.Completer.CompletionCount()})");
        Check(window.Completer.CurrentCompletion() == "grace",
            $"the first completion is 'grace' (got '{window.Completer.CurrentCompletion()}')");
        window.Completer.SetCompletionPrefix("aut");
        Check(window.Completer.CompletionCount() == 4,
            $"the prefix 'aut' matches four words (got {window.Completer.CompletionCount()})");

        // Case sensitivity.
        window.Completer.SetCompletionPrefix("GRA");
        Check(window.Completer.CompletionCount() == 4,
            $"case-insensitively 'GRA' still matches (got {window.Completer.CompletionCount()})");
        window.CaseComboBox.SetCurrentIndex(1);
        Check(window.Completer.caseSensitivity() == Qt.CaseSensitivity.CaseSensitive,
            $"the case combo box drives the completer (got {window.Completer.caseSensitivity()})");
        window.Completer.SetCompletionPrefix("GRA");
        Check(window.Completer.CompletionCount() == 0,
            $"case-sensitively 'GRA' matches nothing (got {window.Completer.CompletionCount()})");
        window.CaseComboBox.SetCurrentIndex(0);
        window.Completer.SetCompletionPrefix("gra");
        Check(window.Completer.CompletionCount() == 4,
            $"switching back restores the matches (got {window.Completer.CompletionCount()})");

        // modelSorting, the optimisation the official example turns on for its sorted word list.
        // Qt swaps in a binary-search engine for this, so the lowercase prefix still has to
        // complete (it is the case-insensitive *sorted* engine that is selected here).
        window.Completer.SetModelSorting(QCompleter.ModelSorting.CaseInsensitivelySortedModel);
        Check(window.Completer.modelSorting() == QCompleter.ModelSorting.CaseInsensitivelySortedModel,
            $"modelSorting round-trips (got {window.Completer.modelSorting()})");
        window.Completer.SetCompletionPrefix("gra");
        Check(window.Completer.CompletionCount() == 4,
            $"the sorted-model engine completes too (got {window.Completer.CompletionCount()})");
        window.Completer.SetModelSorting(QCompleter.ModelSorting.UnsortedModel);

        // Completion modes and the visible-item count.
        window.ModeComboBox.SetCurrentIndex(0);
        Check(window.Completer.completionMode() == QCompleter.CompletionMode.InlineCompletion,
            $"the mode combo box drives the completer (got {window.Completer.completionMode()})");
        window.ModeComboBox.SetCurrentIndex(2);
        Check(window.Completer.completionMode() == QCompleter.CompletionMode.UnfilteredPopupCompletion,
            $"the third mode is the unfiltered popup (got {window.Completer.completionMode()})");
        window.ModeComboBox.SetCurrentIndex(1);
        window.MaxVisibleSpinBox.SetValue(7);
        Check(window.Completer.maxVisibleItems() == 7,
            $"the spin box drives the visible-item count (got {window.Completer.maxVisibleItems()})");

        // Wrap-around round-trips.
        window.WrapCheckBox.SetChecked(false);
        Check(!window.Completer.wrapAround(), "unchecking wrap-around reaches the completer");
        window.WrapCheckBox.SetChecked(true);
        Check(window.Completer.wrapAround(), "checking it again reaches the completer");

        // The country list: a two-column QStandardItemModel and a QTreeView popup.
        window.ModelComboBox.SetCurrentIndex(2);
        Check(window.Completer.Model() != null && window.Completer.Model().ColumnCount() == 2,
            $"the country model has two columns (got {window.Completer.Model()?.ColumnCount()})");
        using (var country = window.Completer.Model().Index(0, 0))
        using (var symbol = window.Completer.Model().Index(0, 1))
        {
            string countryText = window.Completer.Model().Data(country).ToString();
            string symbolText = window.Completer.Model().Data(symbol).ToString();
            Check(countryText == "Germany" && symbolText == "DE",
                $"the country model carries name and symbol ({countryText}/{symbolText})");
        }
        Check(window.CountryPopup != null && !window.CountryPopup.rootIsDecorated()
              && !window.CountryPopup.Header().IsVisible(),
            "the custom popup view is the undecorated, header-less tree view");
        Check(window.Completer.Popup() != null
              && window.Completer.Popup().Handle == window.CountryPopup.Handle,
            "the completer is using that tree view as its popup");

        // The activated signal of the example (string overload): connected and emitted.
        string activatedText = null;
        window.Completer.ActivatedSignal.Connect(text => activatedText = text);
        window.Completer.ActivatedSignal.Emit("Greece");
        Check(activatedText == "Greece",
            $"the activated(QString) signal reaches managed code (got '{activatedText}')");

        // The menu bar of the official window.
        Check(window.MenuBar().Actions().Length == 2,
            $"the menu bar has File and About (got {window.MenuBar().Actions().Length})");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
