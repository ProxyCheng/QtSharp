using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CppSharp;
using CppSharp.AST;
using CppSharp.AST.Extensions;
using CppSharp.Generators;
using CppSharp.Parser;
using AstType = CppSharp.AST.Type;

// The single place that defines how a model type is surfaced in managed code. The model
// needs it to fold overloads that would collapse to one C# signature, and the code
// generator uses it to emit that signature.
public static class ApiTypes
{
    // The class set is what distinguishes a real handle ("QPaintDevice*" -> QPaintDevice)
    // from a deliberately opaque pointer ("QEvent*" -> IntPtr).
    public static string Managed(string modelType, ICollection<string> classes = null)
    {
        if (modelType == null)
            return "IntPtr";

        // The special cases come first: "char*" also ends in '*', but it is a string rather
        // than a handle, and the scalar typedefs would otherwise keep their C++ spelling.
        switch (modelType)
        {
            case "QString":
            case "QAnyStringView":
            case "QStringView":
            case "QLatin1StringView":
            case "char*":
                return "string";
            case "QStringList":
                return "string[]";
            case "long long":
                return "long";
            case "unsigned long long":
                return "ulong";
            case "unsigned int":
                return "uint";
            // The small scalars, including the byte/character conventions Qt uses.
            case "short":
                return "short";
            case "unsigned short":
                return "ushort";
            case "long":
                return "int";           // 32-bit on the MSVC target we bind
            case "unsigned long":
                return "uint";
            case "char":
                return "sbyte";
            case "unsigned char":
                return "byte";
            case "char16_t":
                return "char";
        }

        // A generic container: the managed surface is an array of the managed element type
        // (QList<int> -> int[], QList<QStandardItem*> -> QStandardItem[]).
        if (modelType.StartsWith("QList<", StringComparison.Ordinal) && modelType.EndsWith(">"))
            return Managed(modelType.Substring("QList<".Length, modelType.Length - "QList<".Length - 1), classes) + "[]";

        // An associative container: the managed surface is a dictionary
        // (QMap<int, QVariant> / QHash<int, QByteArray> -> Dictionary<int, ...>).
        foreach (var container in new[] { "QMap", "QHash" })
            if (modelType.StartsWith(container + "<", StringComparison.Ordinal) && modelType.EndsWith(">"))
            {
                var (key, value) = SplitPair(modelType.Substring(container.Length + 1, modelType.Length - container.Length - 2));
                return $"Dictionary<{Managed(key, classes)}, {Managed(value, classes)}>";
            }

        // A handle to a Qt property (the bindableFoo() accessors): a wrapper the generated code
        // publishes once, generic over the value type.
        if (modelType.StartsWith("QBindable<", StringComparison.Ordinal) && modelType.EndsWith(">"))
            return $"QBindable<{Managed(modelType.Substring("QBindable<".Length, modelType.Length - "QBindable<".Length - 1), classes)}>";

        // Byte buffers: a uchar* whose length is passed separately, and the Qt6 byte view that
        // carries its own length.
        if (modelType == "uchar*" || modelType == "QByteArrayView")
            return "byte[]";
        if (modelType.EndsWith("*"))
        {
            var pointee = modelType.Substring(0, modelType.Length - 1);
            // A pointer to a scalar is an array of that scalar (QPolygon's int*, and the out
            // parameters QColor::getRgb writes back).
            if (IsScalar(pointee))
                return Managed(pointee, classes) + "[]";
            return classes != null && classes.Contains(pointee) ? pointee : "IntPtr";
        }
        if (modelType.Contains("::"))
            return modelType.Replace("::", ".");
        return modelType switch
        {
            "void" => "void",
            "bool" => "bool",
            "int" => "int",
            "float" => "float",
            "double" => "double",
            _ => modelType,     // a bound class, boxed by value
        };
    }

    // The scalar vocabulary of the model, which is what tells an array of values apart from a
    // handle ("int*" vs "QWidget*").
    static readonly HashSet<string> ScalarModelTypes = new(StringComparer.Ordinal)
    {
        "bool", "int", "float", "double",
        "long long", "unsigned long long", "unsigned int",
        "short", "unsigned short", "long", "unsigned long",
        "char", "unsigned char", "char16_t", "void",
    };

    public static bool IsScalar(string modelType) => ScalarModelTypes.Contains(modelType);

    // "K,V" -> K and V, honouring nested angle brackets (QMap<int, QList<int>>).
    public static (string Key, string Value) SplitPair(string inner)
    {
        int depth = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '<')
                depth++;
            else if (inner[i] == '>')
                depth--;
            else if (inner[i] == ',' && depth == 0)
                return (inner.Substring(0, i), inner.Substring(i + 1));
        }
        return (inner, inner);
    }
}

// Step 1 of the product pipeline: a small, *checked-in*, normalized API model.
//
//   extract : root headers -> clang AST  ->  model/qtsharp.api.json   (parse-only, deterministic)
//   codegen : qtsharp.api.json           ->  C# bindings + C++ wrapper (pure function)
//
// The point of the model is that codegen no longer reads raw Qt headers itself, so it is
// a pure function of a versioned artifact: the same model always produces byte-identical
// output, and CI can enforce "regenerate -> diff must be empty".
// The class is split in two files: ApiModel.cs reads the AST through CppSharp, ApiModel2.cs
// through libclang/ClangSharp. Everything downstream of "the AST has been read" - the model
// shape, the ordering rules, the serializer - is shared, so the two can be diffed byte for
// byte while the CppSharp side is being retired.
public static partial class ApiModel
{
    public const int FormatVersion = 2;
    public const string QtVersion = "6.11.0";
    public const string Namespace = "QtSharp.Api";

    // Classes the model exposes, each with the header that declares it and the Qt module it
    // belongs to. Extraction parses exactly these headers instead of the whole Qt header
    // set, which is what makes it fast; growing the model means adding a row here.
    //
    // The second flag binds constructors. Value-ish Qt classes (events) are only ever seen
    // through a pointer, so there is no point in exposing a way to build them.
    // One entry of the model's class table. Parsing is scoped to these headers, so this table
    // is the model's single input description.
    class Root
    {
        public string Class;
        public string Header;
        public string Module;
        // Whether constructors are published. Events are only ever received (Qt builds them
        // on the stack), so offering a way to build one would only invite mistakes.
        public bool BindCtors = true;
        // Whether the binding may delete instances. Only true for the value types we box
        // ourselves; Qt-owned objects and events must never be freed by the GC.
        public bool Owned = true;
    }

    static Root R(string cls, string header, string module, bool bindCtors = true, bool owned = true)
        => new Root
        {
            Class = cls, Header = header, Module = module, BindCtors = bindCtors, Owned = owned,
        };

    static readonly Root[] Roots =
    {
        // The QObject cluster is one chunk: a class must never be separated from the
        // ancestors that declare the virtuals it inherits.
        //
        // QObject is the one class that never gets a release path. A bare QObject is not something
        // a program needs to own, and the release path is inherited: a subclass marked owned:false
        // (QMimeData, the graphics items) would otherwise still be disposable through its base,
        // which would undo exactly the guarantee owned:false is for.
        R("QObject",      "qobject.h",      "QtCore", owned: false),
        R("QTimer",       "qtimer.h",       "QtCore"),
        R("QAction",      "qaction.h",      "QtGui"),
        R("QWidget",      "qwidget.h",      "QtWidgets"),
        R("QTime",        "qdatetime.h",    "QtCore"),
        // Geometry value types: the single biggest blocker of widget APIs (rect/geometry/
        // size/move/mapTo/... all take or return one of these). They are boxed like the
        // other value types, so "by value" and "by const reference" look the same.
        R("QPoint",       "qpoint.h",       "QtCore"),
        R("QPointF",      "qpoint.h",       "QtCore"),
        R("QSize",        "qsize.h",        "QtCore"),
        R("QRect",        "qrect.h",        "QtCore"),
        R("QRectF",       "qrect.h",        "QtCore"),
        // Events: bound so that a handler can actually read what happened. QEvent and the
        // point events are bound as a chain, because Qt6 declares the interesting accessors
        // on the bases (QSinglePointEvent::position, QEvent::type).
        R("QEvent",             "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QPointerEvent",      "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QSinglePointEvent",  "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QMouseEvent",        "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QFocusEvent",        "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QContextMenuEvent",  "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QPaintEvent",        "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QResizeEvent",       "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QMoveEvent",         "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QCloseEvent",        "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QShowEvent",         "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QHideEvent",         "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QPaintDevice", "qpaintdevice.h", "QtGui", bindCtors: false),
        R("QPainter",     "qpainter.h",     "QtGui"),
        R("QColor",       "qcolor.h",       "QtGui"),
        R("QPolygon",     "qpolygon.h",     "QtGui"),
        // Layouts and the widgets the LineEdits example is built from: enough to lay out a
        // real dialog, validate input and react to a combo box.
        R("QLayout",      "qlayout.h",      "QtWidgets"),
        R("QGridLayout",  "qgridlayout.h",  "QtWidgets"),
        // QFrame is the base of QLabel and QGroupBox; without it they would not be widgets as
        // far as C# is concerned, and could not be handed to a layout.
        R("QFrame",       "qframe.h",       "QtWidgets"),
        R("QLabel",       "qlabel.h",       "QtWidgets"),
        R("QGroupBox",    "qgroupbox.h",    "QtWidgets"),
        R("QComboBox",    "qcombobox.h",    "QtWidgets"),
        R("QLineEdit",    "qlineedit.h",    "QtWidgets"),
        // QValidator lives in QtGui since Qt6.
        R("QValidator",       "qvalidator.h", "QtGui"),
        R("QIntValidator",    "qvalidator.h", "QtGui"),
        R("QDoubleValidator", "qvalidator.h", "QtGui"),
        // The remaining value types that block the most methods: pens/brushes/regions/fonts/
        // transforms/paths are what every painting call takes, and images are what it draws.
        R("QBrush",       "qbrush.h",       "QtGui"),
        R("QPen",         "qpen.h",         "QtGui"),
        R("QRegion",      "qregion.h",      "QtGui"),
        R("QTransform",   "qtransform.h",   "QtGui"),
        R("QFont",        "qfont.h",        "QtGui"),
        R("QPainterPath", "qpainterpath.h", "QtGui"),
        R("QImage",       "qimage.h",       "QtGui"),
        R("QPixmap",      "qpixmap.h",      "QtGui"),

        // ------------------------------------------------------------------ QtWidgets coverage
        // Everything a QtWidgets application reaches for: the widgets, the dialogs, the item
        // views, the menus, the layouts, and the value types they hand around (icons, cursors,
        // palettes, metrics, undo stacks). The rows were derived from the module's own headers
        // with `clangprobe --index`, then filtered to the classes that are worth binding.
        // Adding a row is the only step needed to widen the model; what each new class still
        // needs shows up as the "blocked by" report at the end of extraction.
        R("QAbstractButton",  "qabstractbutton.h",  "QtWidgets"),
        R("QPushButton",      "qpushbutton.h",      "QtWidgets"),
        R("QToolButton",      "qtoolbutton.h",      "QtWidgets"),
        R("QRadioButton",     "qradiobutton.h",     "QtWidgets"),
        R("QCheckBox",        "qcheckbox.h",        "QtWidgets"),
        R("QCommandLinkButton", "qcommandlinkbutton.h", "QtWidgets"),
        R("QButtonGroup",     "qbuttongroup.h",     "QtWidgets"),
        R("QActionGroup",     "qactiongroup.h",     "QtGui"),
        R("QAbstractSpinBox", "qabstractspinbox.h", "QtWidgets"),
        R("QSpinBox",         "qspinbox.h",         "QtWidgets"),
        R("QDoubleSpinBox",   "qspinbox.h",         "QtWidgets"),
        R("QDateTimeEdit",    "qdatetimeedit.h",    "QtWidgets"),
        R("QDateEdit",        "qdatetimeedit.h",    "QtWidgets"),
        R("QTimeEdit",        "qdatetimeedit.h",    "QtWidgets"),
        R("QKeySequenceEdit", "qkeysequenceedit.h",  "QtWidgets"),
        R("QAbstractSlider",  "qabstractslider.h",  "QtWidgets"),
        R("QSlider",          "qslider.h",          "QtWidgets"),
        R("QScrollBar",       "qscrollbar.h",       "QtWidgets"),
        R("QDial",            "qdial.h",            "QtWidgets"),
        R("QTextEdit",        "qtextedit.h",        "QtWidgets"),
        R("QPlainTextEdit",   "qplaintextedit.h",   "QtWidgets"),
        R("QTextBrowser",     "qtextbrowser.h",     "QtWidgets"),
        R("QAbstractScrollArea", "qabstractscrollarea.h", "QtWidgets"),
        R("QScrollArea",      "qscrollarea.h",      "QtWidgets"),
        // The rich-text model behind QTextEdit/QTextDocument.
        R("QTextDocument",    "qtextdocument.h",    "QtGui"),
        R("QTextCursor",      "qtextcursor.h",      "QtGui"),
        R("QTextCharFormat",  "qtextformat.h",      "QtGui"),
        R("QTextBlockFormat", "qtextformat.h",      "QtGui"),
        R("QTextListFormat",  "qtextformat.h",      "QtGui"),
        R("QTextTableFormat", "qtextformat.h",      "QtGui"),
        R("QTextTable",       "qtexttable.h",       "QtGui"),
        R("QTextFrame",       "qtextobject.h",      "QtGui"),
        R("QTextBlock",       "qtextobject.h",      "QtGui"),
        R("QTextList",        "qtextlist.h",        "QtGui"),
        R("QTextOption",      "qtextoption.h",      "QtGui"),
        R("QTextLayout",      "qtextlayout.h",      "QtGui"),
        R("QTextObject",      "qtextobject.h",      "QtGui"),
        R("QLCDNumber",       "qlcdnumber.h",       "QtWidgets"),
        R("QProgressBar",     "qprogressbar.h",     "QtWidgets"),
        R("QSizeGrip",        "qsizegrip.h",        "QtWidgets"),
        R("QRubberBand",      "qrubberband.h",      "QtWidgets"),
        R("QFocusFrame",      "qfocusframe.h",      "QtWidgets"),
        R("QErrorMessage",    "qerrormessage.h",    "QtWidgets"),
        R("QMainWindow",      "qmainwindow.h",      "QtWidgets"),
        R("QDialog",          "qdialog.h",          "QtWidgets"),
        R("QMessageBox",      "qmessagebox.h",      "QtWidgets"),
        R("QFileDialog",      "qfiledialog.h",      "QtWidgets"),
        R("QInputDialog",     "qinputdialog.h",     "QtWidgets"),
        R("QColorDialog",     "qcolordialog.h",     "QtWidgets"),
        R("QFontDialog",      "qfontdialog.h",      "QtWidgets"),
        R("QProgressDialog",  "qprogressdialog.h",  "QtWidgets"),
        R("QDialogButtonBox", "qdialogbuttonbox.h", "QtWidgets"),
        R("QWizard",          "qwizard.h",          "QtWidgets"),
        R("QMenu",            "qmenu.h",            "QtWidgets"),
        R("QMenuBar",         "qmenubar.h",         "QtWidgets"),
        R("QToolBar",         "qtoolbar.h",         "QtWidgets"),
        R("QStatusBar",       "qstatusbar.h",       "QtWidgets"),
        R("QShortcut",        "qshortcut.h",        "QtGui"),
        R("QWhatsThis",       "qwhatsthis.h",       "QtWidgets"),
        R("QTabBar",          "qtabbar.h",          "QtWidgets"),
        R("QTabWidget",       "qtabwidget.h",       "QtWidgets"),
        R("QStackedWidget",   "qstackedwidget.h",   "QtWidgets"),
        R("QSplitter",        "qsplitter.h",        "QtWidgets"),
        R("QDockWidget",      "qdockwidget.h",      "QtWidgets"),
        R("QToolBox",         "qtoolbox.h",         "QtWidgets"),
        R("QBoxLayout",       "qboxlayout.h",       "QtWidgets"),
        R("QHBoxLayout",      "qboxlayout.h",       "QtWidgets"),
        R("QVBoxLayout",      "qboxlayout.h",       "QtWidgets"),
        R("QFormLayout",      "qformlayout.h",      "QtWidgets"),
        R("QStackedLayout",   "qstackedlayout.h",   "QtWidgets"),
        R("QAbstractItemView", "qabstractitemview.h", "QtWidgets"),
        R("QListView",        "qlistview.h",        "QtWidgets"),
        R("QTableView",       "qtableview.h",       "QtWidgets"),
        R("QTreeView",        "qtreeview.h",        "QtWidgets"),
        R("QColumnView",      "qcolumnview.h",      "QtWidgets"),
        R("QHeaderView",      "qheaderview.h",      "QtWidgets"),
        R("QListWidget",      "qlistwidget.h",      "QtWidgets"),
        R("QTreeWidget",      "qtreewidget.h",      "QtWidgets"),
        R("QTableWidget",     "qtablewidget.h",     "QtWidgets"),
        R("QDataWidgetMapper", "qdatawidgetmapper.h", "QtWidgets"),
        R("QCompleter",       "qcompleter.h",       "QtWidgets"),
        R("QCalendarWidget",  "qcalendarwidget.h",  "QtWidgets"),
        R("QFontComboBox",    "qfontcombobox.h",    "QtWidgets"),
        R("QFileSystemModel", "qfilesystemmodel.h", "QtGui"),
        R("QFileIconProvider", "qfileiconprovider.h", "QtWidgets"),
        R("QSystemTrayIcon",  "qsystemtrayicon.h",  "QtWidgets"),
        R("QUndoView",        "qundoview.h",        "QtWidgets"),
        R("QApplication",     "qapplication.h",     "QtWidgets"),
        R("QClipboard",       "qclipboard.h",       "QtGui"),
        R("QDesktopServices", "qdesktopservices.h", "QtGui"),
        R("QStyleHints",      "qstylehints.h",      "QtGui"),
        R("QIcon",            "qicon.h",            "QtGui"),
        R("QBitmap",          "qbitmap.h",          "QtGui"),
        R("QCursor",          "qcursor.h",          "QtGui"),
        R("QPalette",         "qpalette.h",         "QtGui"),
        R("QFontDatabase",    "qfontdatabase.h",    "QtGui"),
        R("QFontMetrics",     "qfontmetrics.h",     "QtGui"),
        R("QFontMetricsF",    "qfontmetrics.h",     "QtGui"),
        R("QFontInfo",        "qfontinfo.h",        "QtGui"),
        R("QStandardItemModel", "qstandarditemmodel.h", "QtGui"),
        R("QStandardItem",    "qstandarditemmodel.h", "QtGui"),
        R("QUndoStack",       "qundostack.h",       "QtGui"),
        R("QUndoCommand",     "qundostack.h",       "QtGui"),
        R("QSyntaxHighlighter", "qsyntaxhighlighter.h", "QtGui"),
        R("QSizePolicy",      "qsizepolicy.h",      "QtWidgets"),
        R("QItemSelection",   "qitemselectionmodel.h", "QtCore"),
        R("QItemSelectionModel", "qitemselectionmodel.h", "QtCore"),
        R("QPainterPathStroker", "qpainterpath.h",  "QtGui"),
        R("QPolygonF",        "qpolygon.h",         "QtGui"),
        R("QImageReader",     "qimagereader.h",     "QtGui"),
        R("QImageWriter",     "qimagewriter.h",     "QtGui"),
        R("QLinearGradient",  "qbrush.h",           "QtGui"),
        R("QRadialGradient",  "qbrush.h",           "QtGui"),
        R("QConicalGradient", "qbrush.h",           "QtGui"),
        R("QGradient",        "qbrush.h",           "QtGui"),
        // Meta-objects (the Q_OBJECT scaffolding becomes usable: metaObject().className()),
        // the calendar/date engine, the drag-and-drop payload, and the bit array.
        R("QMetaObject",      "qobjectdefs.h",      "QtCore"),
        R("QCalendar",        "qcalendar.h",        "QtCore"),
        // Not ours to delete even though we can build one: QDrag and QClipboard::setMimeData adopt
        // the payload, and neither route leaves a QObject parent behind for the generated
        // destructor to notice (unlike the widget/layout adoption, which reparents). The wrapper
        // therefore never releases it - the one case where that costs a leak rather than a crash.
        R("QMimeData",        "qmimedata.h",        "QtCore", owned: false),
        R("QBitArray",        "qbitarray.h",        "QtCore"),
        R("QTimeZone",        "qtimezone.h",        "QtCore"),
        R("QIODevice",        "qiodevice.h",        "QtCore"),
        R("QDataStream",      "qdatastream.h",      "QtCore"),
        R("QMetaType",        "qmetatype.h",        "QtCore"),
        R("QRegularExpression", "qregularexpression.h", "QtCore"),
        R("QTextStream",      "qtextstream.h",      "QtCore"),
        R("QColorSpace",      "qcolorspace.h",      "QtGui"),
        R("QPicture",       "qpicture.h",       "QtGui"),
        R("QScreen",        "qscreen.h",        "QtGui"),
        R("QVector2D",      "qvectornd.h",      "QtGui"),
        R("QVector3D",      "qvectornd.h",      "QtGui"),
        R("QVector4D",      "qvectornd.h",      "QtGui"),
        R("QQuaternion",    "qquaternion.h",    "QtGui"),
        R("QEventPoint",    "qeventpoint.h",    "QtGui"),
        R("QColorTransform","qcolortransform.h","QtGui"),
        R("QMatrix4x4",     "qmatrix4x4.h",     "QtGui"),
        R("QPointingDevice","qpointingdevice.h","QtGui"),
        // ------------------------------------------------------------------ graphics view
        // The scene/view/item family. Items are owned by the scene they are added to, so they are
        // added with owned: false (the GC must not delete them); the scene events are built by Qt
        // on the stack, so they get no constructors either.
        R("QAbstractGraphicsShapeItem", "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsRectItem",     "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsEllipseItem",  "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsLineItem",     "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsPathItem",     "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsPolygonItem",  "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsSimpleTextItem", "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsTextItem",     "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsItemGroup",    "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsProxyWidget",  "qgraphicsproxywidget.h", "QtWidgets", owned: false),
        R("QGraphicsItemAnimation", "qgraphicsitemanimation.h", "QtWidgets"),
        // A transform or an effect is handed to an item (setTransformations/setGraphicsEffect),
        // which adopts it - again with no QObject parent to hide behind.
        R("QGraphicsTransform",    "qgraphicstransform.h", "QtWidgets", owned: false),
        R("QGraphicsRotation",     "qgraphicstransform.h", "QtWidgets", owned: false),
        R("QGraphicsScale",        "qgraphicstransform.h", "QtWidgets", owned: false),
        R("QGraphicsEffect",       "qgraphicseffect.h", "QtWidgets", owned: false),
        R("QGraphicsBlurEffect",   "qgraphicseffect.h", "QtWidgets", owned: false),
        R("QGraphicsColorizeEffect", "qgraphicseffect.h", "QtWidgets", owned: false),
        R("QGraphicsDropShadowEffect", "qgraphicseffect.h", "QtWidgets", owned: false),
        R("QGraphicsOpacityEffect", "qgraphicseffect.h", "QtWidgets", owned: false),
        R("QGraphicsLayoutItem",   "qgraphicslayoutitem.h", "QtWidgets", owned: false),
        R("QGraphicsLayout",       "qgraphicslayout.h", "QtWidgets", owned: false),
        R("QGraphicsGridLayout",   "qgraphicsgridlayout.h", "QtWidgets", owned: false),
        R("QGraphicsLinearLayout", "qgraphicslinearlayout.h", "QtWidgets", owned: false),
        R("QGraphicsAnchorLayout", "qgraphicsanchorlayout.h", "QtWidgets", owned: false),
        R("QGraphicsAnchor",       "qgraphicsanchorlayout.h", "QtWidgets", owned: false),
        R("QGraphicsSceneEvent",   "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QGraphicsSceneContextMenuEvent", "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QGraphicsSceneDragDropEvent", "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QGraphicsSceneHelpEvent", "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QGraphicsSceneHoverEvent", "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QGraphicsSceneMouseEvent", "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QGraphicsSceneMoveEvent", "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QGraphicsSceneResizeEvent", "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QGraphicsSceneWheelEvent", "qgraphicssceneevent.h", "QtWidgets", bindCtors: false, owned: false),
        R("QMetaMethod",      "qmetaobject.h",      "QtCore"),
        // ------------------------------------------------------------------ item views, styles, values
        // The item-model data types and the item-view item classes, the style option structs that
        // every custom paintEvent receives, and the value types (QModelIndex, QVariant, bytes,
        // dates, keys, geometry) that block the most signatures.
        // Items and layout items are owned by the view/layout they are inserted into, so they are
        // added with owned: false: the GC must never delete them.

        // ------------------------------------------------------------------ the rest of QtWidgets
        // Splash screens, tool tips, widget actions, wizard pages, the undo group, splitter handles,
        // the style option/hint structs, the scroller and the gesture family.
        R("QSplashScreen",             "qsplashscreen.h", "QtWidgets"),
        R("QToolTip",                  "qtooltip.h", "QtWidgets"),
        R("QWidgetAction",             "qwidgetaction.h", "QtWidgets"),
        R("QWizardPage",               "qwizard.h", "QtWidgets"),
        R("QUndoGroup",                "qundogroup.h", "QtWidgets"),
        R("QSplitterHandle",           "qsplitter.h", "QtWidgets"),
        R("QStylePainter",             "qstylepainter.h", "QtWidgets"),
        R("QTableWidgetSelectionRange", "qtablewidget.h", "QtWidgets"),
        R("QTreeWidgetItemIterator",   "qtreewidgetitemiterator.h", "QtWidgets"),
        R("QPlainTextDocumentLayout",  "qplaintextedit.h", "QtWidgets"),
        R("QItemEditorFactory",        "qitemeditorfactory.h", "QtWidgets"),
        R("QItemEditorCreatorBase",    "qitemeditorfactory.h", "QtWidgets"),
        R("QStyleOptionSizeGrip",      "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionTabBarBase",    "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionTabWidgetFrame", "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionToolBox",       "qstyleoption.h", "QtWidgets"),
        R("QStyleHintReturn",          "qstyleoption.h", "QtWidgets"),
        R("QStyleHintReturnMask",      "qstyleoption.h", "QtWidgets"),
        R("QStyleHintReturnVariant",   "qstyleoption.h", "QtWidgets"),
        R("QScroller",                 "qscroller.h", "QtWidgets"),
        R("QScrollerProperties",       "qscrollerproperties.h", "QtWidgets"),
        R("QGesture",                  "qgesture.h", "QtWidgets"),
        R("QGestureEvent",             "qgesture.h", "QtWidgets"),
        R("QGestureRecognizer",        "qgesturerecognizer.h", "QtWidgets"),
        R("QPanGesture",               "qgesture.h", "QtWidgets"),
        R("QPinchGesture",             "qgesture.h", "QtWidgets"),
        R("QSwipeGesture",             "qgesture.h", "QtWidgets"),
        R("QTapGesture",               "qgesture.h", "QtWidgets"),
        R("QTapAndHoldGesture",        "qgesture.h", "QtWidgets"),
        R("QAbstractItemDelegate", "qabstractitemdelegate.h", "QtWidgets"),
        R("QAbstractItemModel",    "qabstractitemmodel.h", "QtCore"),
        R("QAbstractListModel",    "qabstractitemmodel.h", "QtCore"),
        R("QAbstractProxyModel",   "qabstractproxymodel.h", "QtCore"),
        R("QAbstractTableModel",   "qabstractitemmodel.h", "QtCore"),
        R("QByteArray",            "qbytearray.h", "QtCore"),
        R("QChar",                 "qchar.h", "QtCore"),
        R("QCommonStyle",          "qcommonstyle.h", "QtWidgets"),
        R("QDate",                 "qdatetime.h", "QtCore"),
        R("QDateTime",             "qdatetime.h", "QtCore"),
        R("QGraphicsItem",         "qgraphicsitem.h", "QtWidgets", owned: false),
        // A QObject-shaped item is in the same boat as the plain ones, and this is the case the
        // generated destructor cannot see: it asks QObject::parent(), while an item's owner is
        // *parentItem()/scene()* - a QGraphicsItem relationship QObject knows nothing about.
        // Releasing one because its wrapper was collected would take it out from under the scene.
        R("QGraphicsObject",       "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsPixmapItem",   "qgraphicsitem.h", "QtWidgets", owned: false),
        R("QGraphicsScene",        "qgraphicsscene.h", "QtWidgets"),
        R("QGraphicsView",         "qgraphicsview.h", "QtWidgets"),
        R("QGraphicsWidget",       "qgraphicswidget.h", "QtWidgets", owned: false),
        R("QIdentityProxyModel",   "qidentityproxymodel.h", "QtCore"),
        R("QItemDelegate",         "qitemdelegate.h", "QtWidgets"),
        R("QKeySequence",          "qkeysequence.h", "QtGui"),
        R("QLayoutItem",           "qlayoutitem.h", "QtWidgets", owned: false),
        R("QLine",                 "qline.h", "QtCore"),
        R("QLineF",                "qline.h", "QtCore"),
        R("QListWidgetItem",       "qlistwidget.h", "QtWidgets", owned: false),
        R("QLocale",               "qlocale.h", "QtCore"),
        R("QMargins",              "qmargins.h", "QtCore"),
        R("QMarginsF",             "qmargins.h", "QtCore"),
        R("QMdiArea",              "qmdiarea.h", "QtWidgets"),
        R("QMdiSubWindow",         "qmdisubwindow.h", "QtWidgets"),
        R("QModelIndex",           "qabstractitemmodel.h", "QtCore"),
        R("QPersistentModelIndex", "qabstractitemmodel.h", "QtCore"),
        R("QProxyStyle",           "qproxystyle.h", "QtWidgets"),
        R("QSizeF",                "qsize.h", "QtCore"),
        R("QSortFilterProxyModel", "qsortfilterproxymodel.h", "QtCore"),
        R("QSpacerItem",           "qlayoutitem.h", "QtWidgets", owned: false),
        R("QStringListModel",      "qstringlistmodel.h", "QtCore"),
        R("QStyle",                "qstyle.h", "QtWidgets"),
        R("QStyledItemDelegate",   "qstyleditemdelegate.h", "QtWidgets"),
        R("QStyleOption",          "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionButton",    "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionComboBox",  "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionComplex",   "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionDockWidget", "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionFocusRect", "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionFrame",     "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionGraphicsItem", "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionGroupBox",  "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionHeader",    "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionMenuItem",  "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionProgressBar", "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionRubberBand", "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionSlider",    "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionSpinBox",   "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionTab",       "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionTitleBar",  "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionToolBar",   "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionToolButton", "qstyleoption.h", "QtWidgets"),
        R("QStyleOptionViewItem",  "qstyleoption.h", "QtWidgets"),
        R("QTableWidgetItem",      "qtablewidget.h", "QtWidgets", owned: false),
        R("QTreeWidgetItem",       "qtreewidget.h", "QtWidgets", owned: false),
        R("QUrl",                  "qurl.h", "QtCore"),
        R("QVariant",              "qvariant.h", "QtCore"),
        R("QWidgetItem",           "qlayoutitem.h", "QtWidgets", owned: false),

        // ---------------------------------------------------------------- QtCore batch 1
        // The QtCore value classes: QtCore was 42 classes against PySide's ~200, and these are
        // the ones a real application reaches for (files, JSON, ids, random numbers, the
        // command line). Every one of them is a plain value type - none derives from QObject -
        // so the "managed owns it" rule holds unchanged.
        R("QDeadlineTimer",        "qdeadlinetimer.h", "QtCore"),
        R("QDir",                  "qdir.h", "QtCore"),
        R("QDirIterator",          "qdiriterator.h", "QtCore"),
        R("QTemporaryDir",         "qtemporarydir.h", "QtCore"),
        R("QFileInfo",             "qfileinfo.h", "QtCore"),
        // Statics only: both the constructor and the destructor are private.
        R("QStandardPaths",        "qstandardpaths.h", "QtCore", bindCtors: false),
        R("QUuid",                 "quuid.h", "QtCore"),
        R("QRandomGenerator",      "qrandom.h", "QtCore"),
        R("QElapsedTimer",         "qelapsedtimer.h", "QtCore"),
        R("QEasingCurve",          "qeasingcurve.h", "QtCore"),
        R("QVersionNumber",        "qversionnumber.h", "QtCore"),
        // No declared constructor at all.
        R("QSysInfo",              "qsysinfo.h", "QtCore", bindCtors: false),
        R("QJsonValue",            "qjsonvalue.h", "QtCore"),
        R("QJsonObject",           "qjsonobject.h", "QtCore"),
        R("QJsonArray",            "qjsonarray.h", "QtCore"),
        R("QJsonDocument",         "qjsondocument.h", "QtCore"),
        // An aggregate of public fields; nothing to construct or own.
        R("QJsonParseError",       "qjsonparseerror.h", "QtCore", bindCtors: false),
        R("QCommandLineOption",    "qcommandlineoption.h", "QtCore"),
        R("QCommandLineParser",    "qcommandlineparser.h", "QtCore"),
        R("QProcessEnvironment",   "qprocess.h", "QtCore"),
        // QtGui value types that other QtGui classes keep asking for.
        R("QSurfaceFormat",        "qsurfaceformat.h", "QtGui"),
        R("QGlyphRun",             "qglyphrun.h", "QtGui"),

        // ---------------------------------------------------------------- QtCore batch 2
        // The QObject-derived QtCore classes. These only became safe once the generated
        // destructor learned to ask Qt first (`if (parent()) return;`): a QFile/QThread a program
        // creates is ours to delete, but one Qt adopted is not.
        // The enum holder QIODevice derives from: without it QIODeviceBase::OpenMode is a flag
        // type whose scope is unbound, and every open()/read()/write() signature is dropped.
        R("QIODeviceBase",         "qiodevicebase.h", "QtCore", bindCtors: false),
        R("QWindow",               "qwindow.h", "QtGui"),
        R("QFileDevice",           "qfiledevice.h", "QtCore", bindCtors: false),
        R("QFile",                 "qfile.h", "QtCore"),
        R("QSaveFile",             "qsavefile.h", "QtCore"),
        R("QTemporaryFile",        "qtemporaryfile.h", "QtCore"),
        R("QProcess",              "qprocess.h", "QtCore"),
        R("QThread",               "qthread.h", "QtCore"),
        R("QLockFile",             "qlockfile.h", "QtCore"),
        // Static-only in practice: the constructors take `int &argc, char **argv`, which the
        // model cannot express, so the class lands with its statics (instance, arguments,
        // applicationDirPath, ...) and no way to build one - which is right, Qt already did.
        R("QCoreApplication",      "qcoreapplication.h", "QtCore"),
        R("QGuiApplication",       "qguiapplication.h", "QtGui"),
        R("QLibrary",              "qlibrary.h", "QtCore"),
        R("QPluginLoader",         "qpluginloader.h", "QtCore"),
        R("QXmlStreamReader",      "qxmlstream.h", "QtCore"),
        R("QXmlStreamWriter",      "qxmlstream.h", "QtCore"),
        R("QTextDocumentWriter",   "qtextdocumentwriter.h", "QtGui"),
        // The thread primitives. QBasic* are the bases that actually declare lock()/unlock(), so
        // without them the QMutex/QReadWriteLock methods would silently disappear.
        R("QBasicMutex",           "qmutex.h", "QtCore"),
        R("QMutex",                "qmutex.h", "QtCore"),
        R("QBasicReadWriteLock",   "qreadwritelock.h", "QtCore"),
        R("QReadWriteLock",        "qreadwritelock.h", "QtCore"),
        R("QSemaphore",            "qsemaphore.h", "QtCore"),
        R("QWaitCondition",        "qwaitcondition.h", "QtCore"),
        // The thread pool and the task a managed class implements: a managed QRunnable subclass is
        // how C# code hands work to Qt's pool.
        R("QRunnable",             "qrunnable.h", "QtCore"),
        R("QThreadPool",           "qthreadpool.h", "QtCore"),

        // ---------------------------------------------------------- the input events (QtGui)
        // Every event Qt delivers used to be an opaque pointer, so a handler could tell that
        // something happened but not what: keyPressEvent had no key code and wheelEvent no delta.
        // The CppSharp front end could not bind these (a chunk containing QInputEvent never
        // produced the same model twice), which is why they were left opaque; the clang front end
        // has no such limit, so the whole family is a plain Roots entry now.
        // Like every event they are built by Qt - on the stack, inside the dispatch - so they are
        // neither constructible nor owned by us; the GC must never free one.
        R("QInputEvent",                 "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QKeyEvent",                   "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QWheelEvent",                 "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QHoverEvent",                 "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QEnterEvent",                 "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QTabletEvent",                "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QNativeGestureEvent",         "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QTouchEvent",                 "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QScrollEvent",                "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QScrollPrepareEvent",         "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QExposeEvent",                "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QFileOpenEvent",              "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QChildWindowEvent",           "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QWindowStateChangeEvent",     "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QScreenOrientationChangeEvent", "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QPlatformSurfaceEvent",       "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QShortcutEvent",              "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QInputMethodEvent",           "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QInputMethodQueryEvent",      "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QHelpEvent",                  "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QStatusTipEvent",             "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QWhatsThisClickedEvent",      "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QToolBarChangeEvent",         "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QIconDragEvent",              "qevent.h", "QtGui", bindCtors: false, owned: false),
        // The drag-and-drop pair: a drag carries its QMimeData, a drop reports where it landed.
        R("QDragEnterEvent",             "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QDragMoveEvent",              "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QDragLeaveEvent",             "qevent.h", "QtGui", bindCtors: false, owned: false),
        R("QDropEvent",                  "qevent.h", "QtGui", bindCtors: false, owned: false),
        // The input devices, so a handler can tell a mouse from a touch screen.
        R("QInputDevice",                "qinputdevice.h", "QtGui", bindCtors: false, owned: false),
        R("QPointingDeviceUniqueId",     "qpointingdevice.h", "QtGui"),

        // ---------------------------------------------------------- QtCore batch 3
        // The runtime services a real application reaches for next: waiting, byte buffers, the
        // meta-object, matching, hashing, mime types, translation and shared memory.
        R("QEventLoop",            "qeventloop.h", "QtCore"),
        R("QEventLoopLocker",      "qeventloop.h", "QtCore"),
        R("QBasicTimer",           "qbasictimer.h", "QtCore"),
        R("QBuffer",               "qbuffer.h", "QtCore"),
        // The meta-object, now introspectable from C# (QMetaObject::indexOfSignal alone could
        // only answer "does this exist"; QMetaProperty/QMetaEnum say what a property is).
        R("QMetaClassInfo",        "qmetaobject.h", "QtCore"),
        R("QMetaEnum",             "qmetaobject.h", "QtCore"),
        R("QMetaProperty",         "qmetaobject.h", "QtCore"),
        R("QItemSelectionRange",   "qitemselectionmodel.h", "QtCore"),
        R("QRegularExpressionMatch",         "qregularexpression.h", "QtCore"),
        R("QRegularExpressionMatchIterator", "qregularexpression.h", "QtCore"),
        R("QUrlQuery",             "qurlquery.h", "QtCore"),
        R("QCollator",             "qcollator.h", "QtCore"),
        R("QCollatorSortKey",      "qcollator.h", "QtCore"),
        R("QCryptographicHash",    "qcryptographichash.h", "QtCore"),
        R("QMessageAuthenticationCode", "qmessageauthenticationcode.h", "QtCore"),
        R("QTranslator",           "qtranslator.h", "QtCore"),
        // Static-only: the only constructor is private, so the class lands as a table of paths.
        R("QLibraryInfo",          "qlibraryinfo.h", "QtCore", bindCtors: false),
        R("QStorageInfo",          "qstorageinfo.h", "QtCore"),
        R("QMimeDatabase",         "qmimedatabase.h", "QtCore"),
        R("QMimeType",             "qmimetype.h", "QtCore"),
        R("QFileSystemWatcher",    "qfilesystemwatcher.h", "QtCore"),
        R("QSharedMemory",         "qsharedmemory.h", "QtCore"),
        R("QSystemSemaphore",      "qsystemsemaphore.h", "QtCore"),
        // Qt6 keys a shared segment by this value type rather than by a bare string; without it
        // the QSharedMemory/QSystemSemaphore constructors and nativeKey() are all dropped.
        R("QNativeIpcKey",         "qtipccommon.h", "QtCore"),
        // The base declares the version constants, so it has to be a root as well.
        R("QOperatingSystemVersionBase", "qoperatingsystemversion.h", "QtCore", bindCtors: false),
        // ... and the middle of the chain too: QOperatingSystemVersion derives from this
        // unexported class, so without it the version accessors (majorVersion/minorVersion)
        // would be two inheritance steps away and silently disappear.
        R("QOperatingSystemVersionUnexported", "qoperatingsystemversion.h", "QtCore"),
        R("QOperatingSystemVersion",     "qoperatingsystemversion.h", "QtCore"),
        R("QTextBoundaryFinder",   "qtextboundaryfinder.h", "QtCore"),
        R("QSignalMapper",         "qsignalmapper.h", "QtCore"),
        R("QRandomGenerator64",    "qrandom.h", "QtCore"),
        R("QRecursiveMutex",       "qmutex.h", "QtCore"),
        R("QSocketNotifier",       "qsocketnotifier.h", "QtCore"),
        R("QXmlStreamAttribute",            "qxmlstream.h", "QtCore"),
        R("QXmlStreamNamespaceDeclaration", "qxmlstream.h", "QtCore"),
        R("QXmlStreamEntityDeclaration",    "qxmlstream.h", "QtCore"),
        R("QXmlStreamNotationDeclaration",  "qxmlstream.h", "QtCore"),
        R("QXmlStreamEntityResolver",       "qxmlstream.h", "QtCore"),
        R("QCborValue",            "qcborvalue.h", "QtCore"),
        R("QCborArray",            "qcborarray.h", "QtCore"),
        R("QCborMap",              "qcbormap.h", "QtCore"),
        R("QCborStreamReader",     "qcborstreamreader.h", "QtCore"),
        R("QCborStreamWriter",     "qcborstreamwriter.h", "QtCore"),

        // ---------------------------------------------------------- the rich text types
        // QTextFormat is the base of every format the already-bound QTextCharFormat/QTextBlockFormat
        // derive from: without it in the model those classes had no base at all, so a C# caller
        // could not pass a QTextCharFormat where a QTextFormat is wanted.
        R("QTextFormat",           "qtextformat.h", "QtGui"),
        R("QTextLength",           "qtextformat.h", "QtGui"),
        R("QTextFrameFormat",      "qtextformat.h", "QtGui"),
        R("QTextImageFormat",      "qtextformat.h", "QtGui"),
        R("QTextTableCellFormat",  "qtextformat.h", "QtGui"),
        R("QTextDocumentFragment", "qtextdocumentfragment.h", "QtGui"),
        R("QTextTableCell",        "qtexttable.h", "QtGui"),
        R("QTextBlockGroup",       "qtextobject.h", "QtGui"),
        R("QTextFragment",         "qtextobject.h", "QtGui"),
        R("QTextBlockUserData",    "qtextobject.h", "QtGui"),
        // Laying text out by hand: a QTextLine is a line of a QTextLayout.
        R("QTextLine",             "qtextlayout.h", "QtGui"),
        R("QTextInlineObject",     "qtextlayout.h", "QtGui"),
        R("QAbstractTextDocumentLayout", "qabstracttextdocumentlayout.h", "QtGui", bindCtors: false),
        R("QTextObjectInterface",  "qabstracttextdocumentlayout.h", "QtGui"),
        R("QRegularExpressionValidator", "qvalidator.h", "QtGui"),
        R("QStaticText",           "qstatictext.h", "QtGui"),
        R("QRawFont",              "qrawfont.h", "QtGui"),
        R("QPixmapCache",          "qpixmapcache.h", "QtGui"),
        R("QPixelFormat",          "qpixelformat.h", "QtGui"),
        R("QRgba64",               "qrgba64.h", "QtGui"),
        R("QMovie",                "qmovie.h", "QtGui"),
    };

    // Names that only ever occur as a pointer and are deliberately left opaque (IntPtr).
    //
    // This list is the *legacy* extractor's: QInputEvent, QKeyEvent and QWheelEvent are here
    // because adding them to a CppSharp parse made its AST conversion irreproducible (verified by
    // bisection - a chunk containing QInputEvent never produced the same model twice). The clang
    // front end (extract2) does not use this list at all and binds the whole event family as
    // ordinary roots, which is why a managed keyPressEvent can read a key code now.
    static readonly string[] OpaquePointers =
    {
        "QTimerEvent", "QChildEvent", "QEnterEvent", "QInputEvent", "QKeyEvent", "QWheelEvent",
        "QDragEnterEvent", "QDragMoveEvent", "QDragLeaveEvent", "QDropEvent",
        "QHelpEvent", "QActionEvent", "QInputMethodEvent", "QDynamicPropertyChangeEvent",
    };

    // The whole type vocabulary of the model: anything outside it is skipped rather than
    // guessed, and that is what keeps the model bounded and deterministic.
    static readonly HashSet<string> SupportedTypes = BuildVocabulary();

    static HashSet<string> BuildVocabulary()
    {
        // The scalar vocabulary plus the two string conventions Qt uses in signatures: QString
        // and the NUL-terminated "char*" (always const - a writable char buffer is an out
        // parameter). The 64-bit and unsigned typedefs (qsizetype, qint64, uint, QRgb) resolve
        // to these through their canonical types.
        var types = new HashSet<string>
        {
            "void", "bool", "int", "float", "double",
            "long long", "unsigned long long", "unsigned int",
            "short", "unsigned short", "long", "unsigned long",
            "char", "unsigned char", "char16_t",
            // An untyped pointer: a raw handle, kept as IntPtr.
            "void*",
            // A const byte buffer with its length passed separately.
            "uchar*",
            // The Qt6 byte view: a length-carrying byte buffer, so returns are fine here.
            "QByteArrayView",
            "QString", "char*", "int*",
            // A pointer to any other scalar: an input array (QPolygon's int*) or the out
            // parameter Qt writes a value back through (QColor::getRgb(int *r, ...)).
            "float*", "double*", "bool*", "short*", "unsigned short*",
            "unsigned int*", "long*", "unsigned long*", "long long*", "unsigned long long*",
            // The string views Qt declares next to QString; they all surface as a managed
            // string, and folding the overloads is what ApiTypes.Managed is for.
            "QStringView", "QAnyStringView", "QLatin1StringView",
            // The one container: QList<QString>, marshalled as a managed string array.
            "QStringList",
            // A std::chrono duration (QTimer::setInterval, QThread::sleep, QDeadlineTimer):
            // managed code sees a TimeSpan, the C ABI carries nanoseconds.
            "TimeSpan",
        };
        foreach (var root in ActiveRoots)
        {
            types.Add(root.Class);
            types.Add(root.Class + "*");
        }
        foreach (var opaque in OpaquePointers)
            types.Add(opaque + "*");
        return types;
    }

    // Roots is the model's input description. QTSHARP_ROOTS=<comma separated class names>
    // narrows it, which is how the parser's stability limit was bisected (CppSharp's AST
    // conversion is not deterministic once the parse gets large - see RunExtract).
    static Root[] ActiveRoots
    {
        get
        {
            var filter = Environment.GetEnvironmentVariable("QTSHARP_ROOTS");
            if (string.IsNullOrEmpty(filter))
                return Roots;
            var wanted = new HashSet<string>(filter.Split(','), StringComparer.Ordinal);
            return Roots.Where(r => wanted.Contains(r.Class)).ToArray();
        }
    }

    static IEnumerable<string> RootClasses => ActiveRoots.Select(r => r.Class);
    static bool BindsCtors(string className) => ActiveRoots.First(r => r.Class == className).BindCtors;
    static bool PublishesOwnership(string className) => ActiveRoots.First(r => r.Class == className).Owned;

    // The override points a managed subclass may install. Everything here lowers to a plain
    // pointer or a supported scalar, so adding a name is all it takes to make an event
    // handler reachable from native code.
    static readonly HashSet<string> VirtualAllow = new()
    {
        // QObject
        "eventFilter", "event", "customEvent", "timerEvent", "childEvent",
        // QWidget
        "paintEvent", "resizeEvent", "moveEvent", "closeEvent", "showEvent", "hideEvent",
        "mousePressEvent", "mouseReleaseEvent", "mouseMoveEvent", "mouseDoubleClickEvent",
        "wheelEvent", "keyPressEvent", "keyReleaseEvent", "focusInEvent", "focusOutEvent",
        "enterEvent", "leaveEvent", "changeEvent", "contextMenuEvent",
        "dragEnterEvent", "dragMoveEvent", "dragLeaveEvent", "dropEvent",
        // The tablet pen, next to the mouse handlers above.
        "tabletEvent",
        // The layout side of a custom widget: without these a managed widget cannot say how big
        // it wants to be (the Calculator example's CalcButton overrides sizeHint).
        "sizeHint", "minimumSizeHint", "heightForWidth", "hasHeightForWidth",
    };

    // The parse-only side of the pipeline only needs the headers of the roots it is given:
    // no generator, no type maps, no code generation, none of the legacy post-processing.
    // Parsing is the whole cost, so scoping it to the roots is what keeps extraction fast.
    class ModelLibrary : ILibrary
    {
        readonly Root[] group;

        public ModelLibrary(Root[] group) => this.group = group;

        public void Setup(Driver driver)
        {
            driver.ParserOptions.LanguageVersion = LanguageVersion.CPP17;

            var options = driver.Options;
            options.OutputDir = Path.Combine(Path.GetTempPath(), "qtsharp-api-model");

            const string inc = QtLibrary.QtIncludeRoot;
            foreach (var moduleDir in group.Select(r => r.Module).Distinct())
            {
                var module = options.AddModule("QtSharp.Api." + moduleDir);
                module.OutputNamespace = Namespace;
                module.SharedLibraryName = "QtSharp";
                module.IncludeDirs.Add(inc);
                module.IncludeDirs.Add(Path.Combine(inc, moduleDir));
                module.Defines.Add("QT_NO_DEBUG");
                module.Defines.Add("QT_CORE_LIB");
                module.Defines.Add("QT_GUI_LIB");
                module.Defines.Add("QT_WIDGETS_LIB");
                foreach (var header in group.Where(r => r.Module == moduleDir).Select(r => r.Header).Distinct())
                    module.Headers.Add(header);
            }
        }

        public void SetupPasses(Driver driver) { }
        public void Preprocess(Driver driver, ASTContext ctx) { }
        public void Postprocess(Driver driver, ASTContext ctx) { }
    }

    // One parsed chunk of the model: class bodies keyed by class name, and the enum table.
    // Splitting the request into chunks is what keeps extraction reproducible - see
    // RunExtract.
    class ModelPart
    {
        public readonly Dictionary<string, string> Classes = new(StringComparer.Ordinal);
        public readonly Dictionary<string, EnumEntry> Enums = new(StringComparer.Ordinal);

        public void Merge(ModelPart other)
        {
            foreach (var kv in other.Classes)
                Classes[kv.Key] = kv.Value;
            foreach (var kv in other.Enums)
                Enums[kv.Key] = kv.Value;
        }

        // A canonical rendering, used both as the final model and to compare two parses.
        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"formatVersion\": {FormatVersion},\n");
            sb.Append($"  \"qtVersion\": \"{QtVersion}\",\n");
            sb.Append($"  \"namespace\": \"{Namespace}\",\n");
            sb.Append("  \"enums\": [\n");
            var enumList = Enums.Values
                .Where(e => e.Items.Count > 0 && !string.IsNullOrEmpty(e.Name))
                .OrderBy(e => e.Qualified, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < enumList.Count; i++)
                WriteEnum(sb, enumList[i], i + 1 < enumList.Count);
            sb.Append("  ],\n");
            sb.Append("  \"classes\": [\n");
            var names = Classes.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
            for (int i = 0; i < names.Count; i++)
            {
                sb.Append(Classes[names[i]]);
                sb.Append(i + 1 < names.Count ? ",\n" : "\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }

    // Diagnostic: print exactly what configuration CppSharp hands to clang. A replacement
    // front end has to reproduce this, so the list is worth being able to ask for
    // (`qtgen dumpopts`) instead of reverse engineering it from the CppSharp sources.
    public static int DumpParseOptions()
    {
        var options = new DriverOptions { Quiet = true };
        using var driver = new Driver(options);
        new ModelLibrary(ActiveRoots.ToArray()).Setup(driver);
        driver.Setup();

        var po = driver.ParserOptions;
        // ParseCode() is what normally folds the modules into the parser options.
        po.BuildForSourceFile(driver.Options.Modules);

        Console.WriteLine($"[opts] ClangVersion       = {po.ClangVersion}");
        Console.WriteLine($"[opts] TargetTriple       = '{po.TargetTriple}'");
        Console.WriteLine($"[opts] LanguageVersion    = {po.LanguageVersion}");
        Console.WriteLine($"[opts] MicrosoftMode      = {po.MicrosoftMode}");
        Console.WriteLine($"[opts] ToolSetToUse       = {po.ToolSetToUse}");
        Console.WriteLine($"[opts] NoBuiltinIncludes  = {po.NoBuiltinIncludes}");
        Console.WriteLine($"[opts] NoStandardIncludes = {po.NoStandardIncludes}");
        Console.WriteLine($"[opts] BuiltinsDir        = {po.BuiltinsDir}");
        Console.WriteLine($"[opts] SkipFunctionBodies = {po.SkipFunctionBodies}");

        Console.WriteLine($"[opts] Arguments ({po.ArgumentsCount}):");
        for (uint i = 0; i < po.ArgumentsCount; i++)
            Console.WriteLine($"[opts]   {po.GetArguments(i)}");
        Console.WriteLine($"[opts] IncludeDirs ({po.IncludeDirsCount}):");
        for (uint i = 0; i < po.IncludeDirsCount; i++)
            Console.WriteLine($"[opts]   {po.GetIncludeDirs(i)}");
        Console.WriteLine($"[opts] SystemIncludeDirs ({po.SystemIncludeDirsCount}):");
        for (uint i = 0; i < po.SystemIncludeDirsCount; i++)
            Console.WriteLine($"[opts]   {po.GetSystemIncludeDirs(i)}");
        Console.WriteLine($"[opts] Defines ({po.DefinesCount}):");
        for (uint i = 0; i < po.DefinesCount; i++)
            Console.WriteLine($"[opts]   {po.GetDefines(i)}");
        Console.WriteLine($"[opts] Undefines ({po.UndefinesCount}):");
        for (uint i = 0; i < po.UndefinesCount; i++)
            Console.WriteLine($"[opts]   {po.GetUndefines(i)}");
        return 0;
    }

    // ------------------------------------------------------------ parse-only entry
    //
    // CppSharp's AST conversion is not reproducible once the parse gets large: some
    // parameters come back with a type that belongs to a completely unrelated declaration,
    // and which ones changes from run to run (it is not GC timing; suspending the GC does
    // not help). The corruption scales with the amount of code in one parse, so the model is
    // extracted in chunks: a chunk is accepted only when two parses of it agree, and a chunk
    // that will not reproduce is halved until it does. Chunks are independent because the
    // type vocabulary is global, only the parsed headers differ.
    // Chunks are one header's roots. That is the natural granularity: the classes in a header
    // are parsed together anyway, and the virtual chain is merged from the model rather than
    // from the AST (see ApiCodeGen), so no chunk has to contain a class and its ancestors.
    // A header whose classes cannot be parsed reproducibly has to be dropped from the table:
    // an unreproducible model is worse than a missing class.
    public static int RunExtract(string modelPath)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // CppSharp reports a bad header row as "Files not found" listing every header of the
        // chunk, so the culprit is checked up front instead.
        foreach (var root in ActiveRoots)
        {
            var header = Path.Combine(QtLibrary.QtIncludeRoot, root.Module, root.Header);
            if (!File.Exists(header))
            {
                Console.WriteLine($"[model] FAILED: {root.Class} is declared in {root.Module}/{root.Header}, which does not exist");
                return 1;
            }
        }

        var part = new ModelPart();

        foreach (var chunk in ActiveRoots.GroupBy(r => (r.Module, r.Header)))
            if (!ExtractChunk(chunk.ToArray(), part))
                return 1;

        var text = part.Serialize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(modelPath)));
        File.WriteAllText(modelPath, text);
        Console.WriteLine($"[model] wrote {modelPath} ({part.Classes.Count} class(es), {part.Enums.Count} enum(s)) in {clock.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    // A chunk is accepted only when two parses of it agree byte for byte.
    static bool ExtractChunk(Root[] group, ModelPart into)
    {
        if (group.Length == 0)
            return true;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var first = ParseChunk(group);
            var second = first == null ? null : ParseChunk(group);
            if (first != null && second != null && first.Serialize() == second.Serialize())
            {
                into.Merge(first);
                Console.WriteLine($"[model]   {group[0].Module}/{group[0].Header}: {group.Length} class(es) verified reproducible");
                return true;
            }
        }

        Console.WriteLine($"[model] FAILED: {group[0].Module}/{group[0].Header} does not parse reproducibly.");
        Console.WriteLine($"[model]   Offending classes: {string.Join(",", group.Select(r => r.Class))}");
        Console.WriteLine("[model]   Drop them from the Roots table, or split the header by hand.");
        return false;
    }

    // A chunk's parse can also throw inside CppSharp itself (DeclConverter hits an assertion on
    // some template alias; whether it does depends on how much is being converted, so it is
    // order-dependent). That is treated like any other unreproducible chunk: split and retry.
    static ModelPart ParseChunk(Root[] group)
    {
        try
        {
            var options = new DriverOptions { Quiet = true };
            using var driver = new Driver(options);

            new ModelLibrary(group).Setup(driver);
            driver.Setup();

            if (!driver.ParseCode())
                return null;

            var part = new ModelPart();
            Extract(driver.Context.ASTContext, part, group.Select(r => r.Class).ToHashSet(StringComparer.Ordinal));
            return part;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[model]   the parser threw on [{string.Join(",", group.Select(r => r.Class))}]: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // ---------------------------------------------------------------- extraction
    static void Extract(ASTContext ctx, ModelPart part, HashSet<string> chunkNames)
    {
        var all = new List<Class>();
        foreach (var tu in ctx.TranslationUnits)
            Collect(tu, all);

        var byName = new Dictionary<string, Class>();
        Skipped.Clear();
        Counted.Clear();
        foreach (var raw in all)
        {
            // A chunk also sees forward declarations of another chunk's classes (qwindowdefs.h
            // forward-declares QWidget, qpainter.h forward-declares QColor). Those are type
            // names for the vocabulary, not model entries - emitting them would overwrite the
            // real class with an empty one.
            if (!chunkNames.Contains(raw.OriginalName))
                continue;
            // Keep the fleshed-out declaration when a class is seen more than once.
            var c = (raw.CompleteDeclaration as Class) ?? raw;
            if (Environment.GetEnvironmentVariable("QTSHARP_MODEL_DEBUG") == "1")
                Console.WriteLine($"[model-candidates] {raw.OriginalName} file={raw.TranslationUnit?.FilePath} score={Score(raw)} complete={(raw.CompleteDeclaration != null)} origScore={Score(c)} sameRef={ReferenceEquals(raw, c)}");
            if (!byName.TryGetValue(c.OriginalName, out var existing) || Score(c) > Score(existing))
                byName[c.OriginalName] = c;
        }

        foreach (var missing in chunkNames.Where(r => !byName.ContainsKey(r)))
            Console.WriteLine($"[model] WARNING: root class {missing} was not found in the parsed headers");

        var classes = byName.Values.OrderBy(c => c.OriginalName, StringComparer.Ordinal).ToList();

        // Signals are read from the declarations in the root headers. Several roots can share
        // one header (QValidator/QIntValidator/QDoubleValidator all live in qvalidator.h), so
        // the header list has to be deduplicated or every signal is counted once per root.
        var rootSignals = ActiveRoots
            .Select(r => (r.Module, r.Header))
            .Distinct()
            .SelectMany(h => Program.ScanSignals(h.Module, h.Header))
            .Where(s => byName.ContainsKey(s.ClassName))
            .ToList();

        var signals = rootSignals
            .Where(s => s.Params.Where(p => !p.Private)
                .All(p => Program.MapSignalType(p.CppType, out _, out _, out _, out _, out _)))
            .ToList();
        // Every signal name (supported or not) so callable methods do not include signals.
        var allSignalNames = new HashSet<string>(rootSignals
            .Select(s => s.ClassName + "::" + s.SignalName));

        var enums = part.Enums;

        foreach (var c in classes)
        {
            if (Environment.GetEnvironmentVariable("QTSHARP_MODEL_DEBUG") == "1")
                DumpDebug(c);

            var baseClass = BaseInModel(c, byName);
            var boundBases = BoundBases(c);
            var body = new StringBuilder();

            var callable = new List<Method>();
            callable.AddRange(c.Methods);
            foreach (var p in c.Properties)
            {
                // Properties are explicitly ignored by the legacy pipeline, but their
                // accessors are exactly the methods a binding wants to expose.
                if (p.GetMethod != null) callable.Add(p.GetMethod);
                if (p.SetMethod != null) callable.Add(p.SetMethod);
            }

            // An abstract class cannot be instantiated: its C++ pure virtuals are not
            // implemented by the generated subclass, so offering constructors would only
            // produce a wrapper that fails at link time (or worse, at run time). Abstract
            // classes still work as types and as bases.
            var ctors = BindsCtors(c.OriginalName) && !c.IsAbstract
                ? callable.Where(m => m.IsConstructor && !m.IsCopyConstructor)
                    .Where(m => m.Access == AccessSpecifier.Public)
                    // CppSharp only marks copy constructors after its own passes, so they
                    // have to be recognised by shape here (QColor(const QColor&)).
                    .Where(m => VisibleParams(m).Count() != 1
                                || Normalize(VisibleParams(m).First().Type) != c.OriginalName)
                    .Where(Accept)
                    .GroupBy(m => CtorDedupeKey(m))
                    .Select(g => g.First())
                    .OrderBy(m => m.Parameters.Count)
                    .ThenBy(m => SignatureKey(m), StringComparer.Ordinal)
                    .ToList()
                : new List<Method>();

            var methods = callable
                .Where(m => !m.IsDestructor && !m.IsOperator && !m.IsConstructor)
                .Where(m => !m.IsVirtual)
                // Qt's own "qt_" members are macro scaffolding (qt_check_for_QGADGET_macro),
                // not API, and they have no exported symbol.
                .Where(m => !m.OriginalName.StartsWith("qt_", StringComparison.Ordinal))
                .Where(m => m.Access == AccessSpecifier.Public)
                .Where(m => !allSignalNames.Contains(c.OriginalName + "::" + m.OriginalName))
                // Deliberately not gated on IsGenerated: the legacy pipeline prunes methods
                // for its own reasons, and we decide by our own type whitelist instead.
                .Where(Accept)
                .GroupBy(m => DedupeKey(m))
                .Select(g => g.First())
                .OrderBy(m => m.OriginalName, StringComparer.Ordinal)
                .ThenBy(m => m.Parameters.Count)
                .ThenBy(m => SignatureKey(m), StringComparer.Ordinal)
                .ToList();

            // Only the virtuals this class declares. The dispatch table a native subclass has
            // to install is the *chain* of these, and the chain is a relation between model
            // classes, so it is merged by the code generator rather than while walking the
            // AST. Keeping it out of the parser is what lets extraction be chunked.
            var virtuals = OwnVirtualsOf(c);

            // A signal still has to be unique by shape: the managed surface exposes it as a
            // property named after the signal, so two of the same name cannot coexist.
            var classSignals = signals
                .Where(s => s.ClassName == c.OriginalName)
                .GroupBy(s => s.SignalName + "(" + string.Join(",", s.Params.Where(p => !p.Private).Select(p => p.CppType)) + ")")
                .Select(g => g.First())
                .OrderBy(s => s.SignalName, StringComparer.Ordinal)
                .ToList();

            foreach (var fn in ctors.Concat(methods).Concat(virtuals))
                CollectEnums(ReturnTypeOf(fn), VisibleParams(fn), enums);

            body.Append("    {\n");
            body.Append($"      \"name\": \"{c.OriginalName}\",\n");
            var root = ActiveRoots.First(r => r.Class == c.OriginalName);
            body.Append($"      \"header\": \"{root.Module}/{root.Header}\",\n");
            body.Append($"      \"bases\": [{string.Join(", ", boundBases.Select(b => Json(b.OriginalName)))}],\n");
            body.Append($"      \"isAbstract\": {(c.IsAbstract ? "true" : "false")},\n");
            body.Append($"      \"owned\": {(PublishesOwnership(c.OriginalName) && !DerivesFromQObject(c, byName) ? "true" : "false")},\n");
            WriteConstructors(body, ctors);
            WriteFunctions(body, "methods", methods);
            WriteVirtuals(body, virtuals);
            WriteSignals(body, classSignals);
            body.Append("    }\n");
            part.Classes[c.OriginalName] = body.ToString();
        }

        ReportCoverage();
        Console.WriteLine($"[model]   parsed {classes.Count} class(es), {enums.Count} enum(s)");
    }

    // What is *not* in the model and why. Growing the model is a matter of widening the type
    // vocabulary, so the interesting number is not "how many classes" but "which type blocks
    // the most methods".
    static readonly Dictionary<string, int> Skipped = new(StringComparer.Ordinal);

    static void ReportCoverage()
    {
        foreach (var kv in Skipped.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(20))
            Console.WriteLine($"[model]   blocked by {kv.Key} x{kv.Value}");
    }

    static void NoteSkip(string blocker)
    {
        if (blocker == null)
            return;
        Skipped.TryGetValue(blocker, out int n);
        Skipped[blocker] = n + 1;
    }

    static readonly HashSet<Method> Counted = new();

    // The same method can reach the selection twice (once from the class, once from the
    // property that wraps it), so only the first rejection is counted.
    static bool Accept(Method m)
    {
        var blocker = Blocker(ReturnTypeOf(m), VisibleParams(m));
        if (blocker != null && Counted.Add(m))
            NoteSkip(blocker);
        return blocker == null;
    }

    // The first type in the signature that the vocabulary cannot express.
    static string Blocker(AstType returnType, IEnumerable<Parameter> parameters)
    {
        if (!Supported(returnType))
            return Describe(returnType);
        foreach (var p in parameters)
            if (!Supported(p.Type))
                return Describe(p.Type);
        return null;
    }

    static string Describe(AstType type)
    {
        switch (type)
        {
            case null:
                return "void";
            case PointerType p:
                return Describe(p.QualifiedPointee.Type)
                       + (p.Modifier == PointerType.TypeModifier.Pointer ? "*" : "&");
            case BuiltinType b:
                return b.Type.ToString();
            case TemplateSpecializationType ts:
                return (ts.Template?.TemplatedDecl?.OriginalName ?? "template") + "<...>";
            case TypedefType td:
                return td.Declaration?.OriginalName ?? "typedef";
            case TagType tag:
                return tag.Declaration?.OriginalName ?? "tag";
            default:
                return type.GetType().Name;
        }
    }

    static void Collect(DeclarationContext dc, List<Class> classes)
    {
        foreach (var c in dc.Classes)
        {
            classes.Add(c);
            Collect(c, classes);
        }
        foreach (var ns in dc.Namespaces)
            Collect(ns, classes);
    }

    static int Score(Class c) => c.Methods.Count + c.Bases.Count + c.Fields.Count + c.Properties.Count;

    static List<Method> OwnVirtualsOf(Class c)
        => c.Methods
            .Where(m => m.IsVirtual && !m.IsDestructor && !m.IsOperator && !m.IsConstructor)
            .Where(m => VirtualAllow.Contains(m.OriginalName))
            .Where(Accept)
            .GroupBy(m => DedupeKey(m))
            .Select(g => g.First())
            .OrderBy(m => m.OriginalName, StringComparer.Ordinal)
            .ThenBy(m => m.Parameters.Count)
            .ToList();

    static void WriteVirtuals(StringBuilder sb, List<Method> virtuals)
    {
        sb.Append("      \"virtuals\": [\n");
        for (int i = 0; i < virtuals.Count; i++)
        {
            var v = virtuals[i];
            sb.Append("        { \"name\": ").Append(Json(v.OriginalName));
            sb.Append(", \"returnType\": ").Append(Json(Normalize(ReturnTypeOf(v))));
            sb.Append(", \"parameters\": [");
            sb.Append(string.Join(", ", VisibleParams(v).Select(ParamJson)));
            sb.Append("] }");
            sb.Append(i + 1 < virtuals.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    static void DumpDebug(Class c)
    {
        Console.WriteLine($"[model-debug] {c.OriginalName} file={c.TranslationUnit?.FilePath} methods={c.Methods.Count} score={Score(c)}");
        Console.WriteLine($"[model-debug] {c.OriginalName} methods:");
        foreach (var m in c.Methods)
            Console.WriteLine($"    {m.OriginalName}({string.Join(",", m.Parameters.Select(p => p.Kind + ":" + Normalize(p.Type) + (p.DefaultArgument != null ? "=" + p.DefaultArgument.String : "")))}) ret={Normalize(ReturnTypeOf(m))} ctor={m.IsConstructor} virt={m.IsVirtual} acc={m.Access} op={m.IsOperator} dtor={m.IsDestructor} static={m.IsStatic} gen={m.GenerationKind}");
        foreach (var b in c.Bases)
            Console.WriteLine($"    base: {b.Class?.OriginalName ?? "?"} isClass={b.IsClass}");
        foreach (var p in c.Properties)
            Console.WriteLine($"    prop: {p.Name} get={p.GetMethod?.OriginalName ?? "null"} set={p.SetMethod?.OriginalName ?? "null"}");
    }

    static void WriteConstructors(StringBuilder sb, List<Method> ctors)
    {
        sb.Append("      \"constructors\": [\n");
        for (int i = 0; i < ctors.Count; i++)
        {
            sb.Append("        { \"parameters\": [");
            sb.Append(string.Join(", ", VisibleParams(ctors[i]).Select(ParamJson)));
            sb.Append("] }");
            sb.Append(i + 1 < ctors.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    static void WriteFunctions(StringBuilder sb, string key, List<Method> methods)
    {
        sb.Append($"      \"{key}\": [\n");
        for (int i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            sb.Append("        { \"name\": ").Append(Json(m.OriginalName));
            if (m.IsStatic)
                sb.Append(", \"static\": true");
            sb.Append(", \"returnType\": ").Append(Json(Normalize(ReturnTypeOf(m))));
            sb.Append(", \"parameters\": [");
            sb.Append(string.Join(", ", VisibleParams(m).Select(ParamJson)));
            sb.Append("] }");
            sb.Append(i + 1 < methods.Count ? ",\n" : "\n");
        }
        sb.Append("      ],\n");
    }

    static void WriteSignals(StringBuilder sb, List<Program.SignalInfo> classSignals)
    {
        sb.Append("      \"signals\": [\n");
        for (int i = 0; i < classSignals.Count; i++)
        {
            var s = classSignals[i];
            sb.Append("        { \"name\": ").Append(Json(s.SignalName)).Append(", \"parameters\": [");
            sb.Append(string.Join(", ", s.Params.Where(p => !p.Private).Select(p =>
                "{ \"name\": " + Json(CleanName(p.Name)) + ", \"type\": " + Json(Program.MapSignalType(p.CppType, out var cs, out _, out _, out _, out _) ? SignalModelType(cs) : CleanName(p.CppType)) + " }")));
            sb.Append("] }");
            sb.Append(i + 1 < classSignals.Count ? ",\n" : "\n");
        }
        sb.Append("      ]\n");
    }

    static void WriteEnum(StringBuilder sb, EnumEntry entry, bool comma)
    {
        sb.Append("    { \"name\": ").Append(Json(entry.Name));
        sb.Append(", \"qualified\": ").Append(Json(entry.Qualified));
        sb.Append(", \"flags\": ").Append(entry.Flags ? "true" : "false");
        sb.Append(", \"items\": [");
        sb.Append(string.Join(", ", entry.Items.Select(item =>
            "{ \"name\": " + Json(item.Name) + ", \"value\": " + item.Value + " }")));
        sb.Append("] }");
        sb.Append(comma ? ",\n" : "\n");
    }

    static string ParamJson(Parameter p)
    {
        var sb = new StringBuilder();
        sb.Append("{ \"name\": ").Append(Json(CleanName(p.Name.Length > 0 ? p.Name : "arg" + p.Index)));
        sb.Append(", \"type\": ").Append(Json(Normalize(p.Type)));
        if (p.DefaultArgument != null)
        {
            sb.Append(", \"optional\": true");
            var literal = DefaultLiteral(p);
            if (literal != null)
                sb.Append(", \"default\": ").Append(Json(literal));
        }
        sb.Append(" }");
        return sb.ToString();
    }

    // A default argument is only passed on to C# when it is a plain literal; anything else
    // becomes a type default. Enumerators matter here: `default` is the zero member, which is
    // not the same as the declared default (Qt::BrushStyle's zero is NoBrush, while
    // QBrush::QBrush declares SolidPattern), so a named enumerator is carried over as the
    // matching C# enum member.
    static string DefaultLiteral(Parameter p)
    {
        var text = p.DefaultArgument?.String?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        var type = Normalize(p.Type);
        if (text is "nullptr" or "NULL" or "0")
            return type != null && (type.EndsWith("*") || type == "QString" || RootClasses.Contains(type)) ? "null" : "0";
        if (text is "true" or "false")
            return text;
        if (type != null && type.Contains("::") && text.Contains("::") && !text.Contains("("))
        {
            var item = text.Substring(text.LastIndexOf("::", StringComparison.Ordinal) + 2);
            if (item.Length > 0 && item.All(char.IsLetterOrDigit))
                return ApiTypes.Managed(type, RootClasses.ToArray()) + "." + item;
        }
        var isHex = text.StartsWith("0x") || text.StartsWith("0X");
        var digits = isHex ? text.Substring(2) : text;
        try
        {
            return Convert.ToInt64(digits, isHex ? 16 : 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    // Value returns are represented in the AST as a hidden "indirect return" parameter;
    // the real type lives in OriginalReturnType.
    static AstType ReturnTypeOf(Method m) => m.OriginalReturnType.Type ?? m.ReturnType.Type;

    // The parameters we can actually marshal: the *longest prefix* of the declaration.
    //
    // Dropping an unmarshalable parameter is only safe if it is defaulted, because C++ then
    // guarantees that everything after it is defaulted too, so the C++ default fills the tail
    // in. The prefix rule matters: QPixmap::load(QString, const char *format = nullptr,
    // Qt::ImageConversionFlags = ...) must not keep the flags parameter, or the flag value
    // would be passed in the format slot. A required parameter we cannot marshal rejects the
    // whole declaration instead (it is kept here so the signature check fails).
    static IEnumerable<Parameter> VisibleParams(Method m)
        => Prefix(m);

    static List<Parameter> Prefix(Method m)
    {
        var result = new List<Parameter>();
        foreach (var p in m.Parameters)
        {
            if (p.Kind == ParameterKind.IndirectReturnType)
                continue;
            if (Supported(p.Type))
            {
                result.Add(p);
                continue;
            }
            if (p.DefaultArgument == null)
                result.Add(p);      // not droppable: poison the signature so the method is rejected
            break;
        }
        return result;
    }

    // Overloads are keyed by the *managed* signature, because that is the constraint that
    // matters: two C++ overloads that surface as the same C# method cannot both be
    // generated. Qt regularly declares a view overload next to the owning type
    // (QObject::setObjectName(QString) / (QAnyStringView)); both become `SetObjectName(string)`,
    // so only the canonical one is kept. The first declaration wins - Qt lists the owning
    // type first - and the model diff gate makes any change to that choice visible.
    static string DedupeKey(Method m)
        => m.OriginalName + "(" + string.Join(",", VisibleParams(m).Select(p => ApiTypes.Managed(Normalize(p.Type) ?? "?", RootClasses.ToArray()))) + ")";

    // Constructors all surface as the same C# name, and CppSharp is not consistent about the
    // OriginalName it gives them. Two overloads that only differed by a parameter we dropped
    // (an unmarshalable default) therefore have to be folded by shape alone.
    static string CtorDedupeKey(Method m)
        => "(" + string.Join(",", VisibleParams(m).Select(p => ApiTypes.Managed(Normalize(p.Type) ?? "?", RootClasses.ToArray()))) + ")";

    static string SignatureKey(Method m)
        => m.OriginalName + "(" + string.Join(",", VisibleParams(m).Select(p => Normalize(p.Type) ?? "?")) + ")";

    static bool Signature(AstType returnType, IEnumerable<Parameter> parameters)
    {
        if (!Supported(returnType))
            return false;
        foreach (var p in parameters)
            if (!Supported(p.Type))
                return false;
        return true;
    }

    // The class vocabulary is a closed set, but enums are open: they are plain integers with
    // no further reachability, so any enum that shows up in an accepted signature is
    // included (and only those). Its scope has to be somewhere the managed surface can put
    // it, though - the global namespace, the Qt namespace, or one of the bound classes.
    static bool Supported(AstType type)
    {
        if (type == null)
            return false;
        var flags = QFlagsName(type);
        if (flags != null)
            return ScopeIsPublishable(ScopeOfQualified(flags));
        var normalized = Normalize(type);
        if (normalized == null)
            return false;
        if (SupportedTypes.Contains(normalized))
            return true;
        var e = EnumOf(type);
        if (e == null)
            return false;
        return ScopeIsPublishable(EnumScope(e));
    }

    static Enumeration EnumOf(AstType type)
    {
        var d = type?.Desugar();
        return d is TagType tag && tag.Declaration is Enumeration e
               && !string.IsNullOrEmpty(e.OriginalName) && !e.IsAnonymous
            ? e
            : null;
    }

    static string EnumScope(Enumeration e) => e.Namespace switch
    {
        Class c => c.OriginalName,
        Namespace ns => ns.OriginalName,
        _ => string.Empty,
    };

    // -------------------------------------------------------------- QFlags
    // Qt declares "flag" types as QFlags<Enum> behind a typedef (Q_DECLARE_FLAGS), and that
    // typedef name is the one to publish: the managed surface sees a [Flags] enum.
    static string QFlagsName(AstType type)
    {
        if (!IsQFlagsSpecialization(type?.Desugar()))
            return null;
        // Only a named typedef can be published; a bare QFlags<E> has nowhere to go.
        return type is TypedefType td ? QualifiedDeclName(td.Declaration) : null;
    }

    static bool IsQFlagsSpecialization(AstType desugared)
        => desugared is TemplateSpecializationType ts
           && ts.Template?.TemplatedDecl?.OriginalName == "QFlags";

    static Enumeration UnderlyingFlagsEnum(AstType type)
    {
        if (type?.Desugar() is not TemplateSpecializationType ts)
            return null;
        foreach (var arg in ts.Arguments)
        {
            if (arg.Kind != TemplateArgument.ArgumentKind.Type || arg.Type.Type == null)
                continue;
            if (arg.Type.Type.Desugar() is TagType tag && tag.Declaration is Enumeration e)
                return e;
        }
        return null;
    }

    static string QualifiedDeclName(Declaration d)
    {
        if (d == null || string.IsNullOrEmpty(d.OriginalName))
            return null;
        return d.Namespace switch
        {
            Class c => c.OriginalName + "::" + d.OriginalName,
            Namespace ns when !string.IsNullOrEmpty(ns.OriginalName) => ns.OriginalName + "::" + d.OriginalName,
            _ => d.OriginalName,
        };
    }

    // Where an enum / flags type can live on the managed side.
    static string ScopeOfQualified(string qualified)
    {
        int i = qualified.LastIndexOf("::", StringComparison.Ordinal);
        return i < 0 ? string.Empty : qualified.Substring(0, i);
    }

    static bool ScopeIsPublishable(string scope)
        => scope.Length == 0 || scope == "Qt" || RootClasses.Contains(scope);

    // -------------------------------------------------------------- type normalization
    // Reference parameters and reference returns are modelled as the pointee value: the
    // binding boxes the object and passes/returns it by pointer, so the C++ distinction
    // between "by value" and "by const reference" does not change the managed surface.
    public static string Normalize(AstType type)
    {
        if (type == null)
            return "void";
        var flags = QFlagsName(type);
        if (flags != null)
            return flags;
        var d = type.Desugar();
        if (d is PointerType pointer)
        {
            if (pointer.Modifier != PointerType.TypeModifier.Pointer)
                return Normalize(pointer.QualifiedPointee.Type);
            return NormalizePointer(pointer);
        }
        if (d is BuiltinType builtin)
            return builtin.Type switch
            {
                PrimitiveType.Void => "void",
                PrimitiveType.Bool => "bool",
                PrimitiveType.Int => "int",
                PrimitiveType.Float => "float",
                PrimitiveType.Double => "double",
                _ => null,
            };
        return DeclName(d);
    }

    static string NormalizePointer(PointerType pointer)
    {
        var pointee = pointer.QualifiedPointee;
        var inner = pointee.Type.Desugar();
        if (inner is BuiltinType b && b.Type == PrimitiveType.Int)
            // Only const input arrays; a non-const pointer means an out parameter and the
            // managed side could not see the write-back, so it must not be bound.
            return pointee.Qualifiers.IsConst ? "int*" : null;
        var name = DeclName(inner);
        return name == null ? null : name + "*";
    }

    static string DeclName(AstType d) => d switch
    {
        TagType tag when tag.Declaration is Enumeration en => EnumTypeName(en),
        TagType tag => tag.Declaration?.OriginalName,
        TypedefType td => td.Declaration?.OriginalName,
        _ => null,
    };

    static string EnumTypeName(Enumeration e)
    {
        switch (e.Namespace)
        {
            case Class c:
                return c.OriginalName + "::" + e.OriginalName;
            case Namespace ns when !string.IsNullOrEmpty(ns.OriginalName):
                return ns.OriginalName + "::" + e.OriginalName;
            default:
                return e.OriginalName;
        }
    }

    // One entry of the model's enum table. A QFlags typedef is published under its own name
    // (Qt::WindowFlags) but carries the items of the enum it masks (Qt::WindowType).
    class EnumEntry
    {
        public string Name;
        public string Qualified;
        // The items are kept as plain values rather than as a declaration so both front ends
        // (CppSharp's AST and libclang) can produce the same entry.
        public List<(string Name, long Value)> Items = new();
        public bool Flags;

        public static List<(string Name, long Value)> ItemsOf(Enumeration e)
            => e.Items.Select(i => (i.Name, unchecked((long)i.Value))).ToList();
    }

    static void CollectEnums(AstType returnType, IEnumerable<Parameter> parameters, Dictionary<string, EnumEntry> enums)
    {
        Visit(returnType);
        foreach (var p in parameters)
            Visit(p.Type);

        void Visit(AstType type)
        {
            var flags = QFlagsName(type);
            if (flags != null)
            {
                var underlying = UnderlyingFlagsEnum(type);
                if (underlying != null)
                    enums[flags] = new EnumEntry
                    {
                        Name = flags.Substring(flags.LastIndexOf("::", StringComparison.Ordinal) + 2),
                        Qualified = flags,
                        Items = EnumEntry.ItemsOf(underlying),
                        Flags = true,
                    };
                return;
            }
            var e = EnumOf(type);
            if (e != null)
                enums[EnumTypeName(e)] = new EnumEntry
                {
                    Name = e.OriginalName,
                    Qualified = EnumTypeName(e),
                    Items = EnumEntry.ItemsOf(e),
                    Flags = false,
                };
        }
    }

    // ------------------------------------------------------------------ class relations
    // C# has single inheritance, so only the first bound base becomes the base class; the
    // others (QWidget is also a QPaintDevice) are exposed as explicit views, because their
    // subobject lives at a different address than the primary one.
    //
    // Membership is decided by name against the whole vocabulary, not against what this chunk
    // parsed: a base may live in another chunk, and the relation still has to be emitted.
    static List<Class> BoundBases(Class c)
    {
        var result = new List<Class>();
        foreach (var b in c.Bases)
        {
            if (!b.IsClass || b.Class == null)
                continue;
            // Template bases (QPolygon : QList<QPoint>) are not modelled.
            if (b.Class is ClassTemplateSpecialization)
                continue;
            if (RootClasses.Contains(b.Class.OriginalName))
                result.Add(b.Class);
        }
        return result;
    }

    // The primary base, when its declaration was parsed in this chunk (needed to walk the
    // virtual chain, which lives in the AST).
    static Class BaseInModel(Class c, Dictionary<string, Class> byName)
    {
        var bases = BoundBases(c);
        return bases.Count > 0 && byName.TryGetValue(bases[0].OriginalName, out var parsed)
            ? parsed
            : null;
    }

    static bool DerivesFromQObject(Class c, Dictionary<string, Class> byName)
    {
        if (c.OriginalName == "QObject")
            return true;
        var seen = new HashSet<string>();
        for (var cur = c; cur != null && seen.Add(cur.OriginalName);)
        {
            var bases = BoundBases(cur);
            if (bases.Count == 0)
                return false;
            if (bases[0].OriginalName == "QObject")
                return true;
            if (!byName.TryGetValue(bases[0].OriginalName, out var next))
                return false;       // the chain crosses a chunk boundary
            cur = next;
        }
        return false;
    }

    static string Json(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // CppSharp escapes C# keywords in names ("event" -> "@event"); the model stores plain
    // C++ names and the code generator escapes again for C# only.
    static string CleanName(string s) => string.IsNullOrEmpty(s) ? s : (s[0] == '@' ? s.Substring(1) : s);

    static string SignalModelType(string csharpType) => csharpType switch
    {
        "string" => "QString",
        "string[]" => "QStringList",
        _ => csharpType,
    };
}
