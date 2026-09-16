using System;
using System.Collections.Generic;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtWidgets;
using Qt = QtSharp.QtCore.Qt;

// Ports of the official Qt "Layouts" examples.
//   - Basic Layouts (examples/widgets/layouts/basiclayouts)

// examples/widgets/layouts/basiclayouts
public class BasicLayoutsDialog : QDialog
{
    const int NumGridRows = 3;
    const int NumButtons = 4;

    readonly List<object> keepAlive = new();

    QMenuBar menuBar;
    QGroupBox horizontalGroupBox;
    QGroupBox gridGroupBox;
    QGroupBox formGroupBox;
    QTextEdit bigEditor;
    QTextEdit smallEditor;
    QDialogButtonBox buttonBox;
    readonly QPushButton[] buttons = new QPushButton[NumButtons];
    readonly QLabel[] labels = new QLabel[NumGridRows];
    readonly QLineEdit[] lineEdits = new QLineEdit[NumGridRows];

    public BasicLayoutsDialog() : base(null, 0)
    {
        CreateMenu();
        CreateHorizontalGroupBox();
        CreateGridGroupBox();
        CreateFormGroupBox();

        bigEditor = keep(new QTextEdit());
        bigEditor.SetPlainText("This widget takes up all the remaining space in the top-level layout.");

        buttonBox = keep(new QDialogButtonBox((int)QDialogButtonBox.StandardButton.Ok | (int)QDialogButtonBox.StandardButton.Cancel));
        buttonBox.AcceptedSignal.Connect(Accept);
        buttonBox.RejectedSignal.Connect(Reject);

        var mainLayout = keep(new QVBoxLayout());
        mainLayout.MenuBar = menuBar;
        mainLayout.AddWidget(horizontalGroupBox);
        mainLayout.AddWidget(gridGroupBox);
        mainLayout.AddWidget(formGroupBox);
        mainLayout.AddWidget(bigEditor);
        mainLayout.AddWidget(buttonBox);

        var scrollAreaContent = keep(new QWidget());
        scrollAreaContent.Layout = mainLayout;

        var scrollArea = keep(new QScrollArea());
        scrollArea.FrameShape = QFrame.Shape.NoFrame;
        scrollArea.HorizontalScrollBarPolicy = Qt.ScrollBarPolicy.ScrollBarAlwaysOff;
        scrollArea.WidgetResizable = true;
        scrollArea.VerticalScrollBar.StyleSheet = "QScrollBar:vertical {width: 20px;}";
        scrollArea.Widget = scrollAreaContent;

        var scrollLayout = keep(new QVBoxLayout());
        scrollLayout.SetContentsMargins(0, 0, 0, 0);
        scrollLayout.AddWidget(scrollArea);

        Layout = scrollLayout;
        WindowTitle = "Basic Layouts";
    }

    void CreateMenu()
    {
        menuBar = keep(new QMenuBar());

        var fileMenu = keep(new QMenu("&File", this));
        var exitAction = fileMenu.AddAction("E&xit");
        menuBar.AddMenu(fileMenu);

        exitAction.TriggeredSignal.Connect(_ => Accept());
    }

    void CreateHorizontalGroupBox()
    {
        horizontalGroupBox = keep(new QGroupBox("Horizontal layout"));
        var layout = keep(new QHBoxLayout());

        for (int i = 0; i < NumButtons; ++i)
        {
            buttons[i] = keep(new QPushButton("Button " + (i + 1)));
            layout.AddWidget(buttons[i]);
        }
        horizontalGroupBox.Layout = layout;
    }

    void CreateGridGroupBox()
    {
        gridGroupBox = keep(new QGroupBox("Grid layout"));
        var layout = keep(new QGridLayout());

        for (int i = 0; i < NumGridRows; ++i)
        {
            labels[i] = keep(new QLabel("Line " + (i + 1) + ":"));
            lineEdits[i] = keep(new QLineEdit());
            layout.AddWidget(labels[i], i + 1, 0);
            layout.AddWidget(lineEdits[i], i + 1, 1);
        }

        smallEditor = keep(new QTextEdit());
        smallEditor.SetPlainText("This widget takes up about two thirds of the grid layout.");
        layout.AddWidget(smallEditor, 0, 2, 4, 1);

        layout.SetColumnStretch(1, 10);
        layout.SetColumnStretch(2, 20);
        gridGroupBox.Layout = layout;
    }

    void CreateFormGroupBox()
    {
        formGroupBox = keep(new QGroupBox("Form layout"));
        var layout = keep(new QFormLayout());
        layout.AddRow(keep(new QLabel("Line 1:")), keep(new QLineEdit()));
        layout.AddRow(keep(new QLabel("Line 2, long text:")), keep(new QComboBox()));
        layout.AddRow(keep(new QLabel("Line 3:")), keep(new QSpinBox()));
        formGroupBox.Layout = layout;
    }

    T keep<T>(T obj) where T : class
    {
        keepAlive.Add(obj);
        return obj;
    }
}
