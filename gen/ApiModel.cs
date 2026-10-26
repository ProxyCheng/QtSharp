using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// The single place that defines how a model type is surfaced in managed code. The model
// needs it to fold overloads that would collapse to one C# signature, and the code
// generator uses it to emit that signature.
public static class ApiTypes
{
    /// <summary>
    /// The Qt types the binding provides as C# *structs* rather than generating a class for them: the
    /// name, the scalar fields in declaration order, what they are, and - for the ones whose memory
    /// layout is not the order of their accessors - the two C++ expressions that convert between the
    /// POD and Qt's own type (@pod is the POD value, @qt the Qt one; null means the default spelling,
    /// a constructor call and a field-wise read). The code generator skips their classes and crosses
    /// the boundary with those bytes; the C# side is hand-written in runtime/QtStructs.cs and the POD
    /// in native/QtSharpApiRuntime.h.
    ///
    /// Qt spells these `class`, but they are trivially copyable: a QPointF is two qreals, every member
    /// is arithmetic, and it is a POD in the C ABI. That is the whole admission test - a copy of one
    /// owns nothing. QColor, QPen, QString and the rest of Qt's implicitly shared classes cross by
    /// value too, but they carry a pointer with a reference count, so a bitwise copy would
    /// double-free; those keep their handle.
    ///
    /// C++ passes them by value, and PyQt and PySide both model them as value types (SIP inlines
    /// them in the Python object, shiboken's typesystem marks them one). A class with a handle costs
    /// a native allocation, a registration and a finalizer per value, which is what makes
    /// `series.replace(points)` leak a wrapper per sample.
    /// </summary>
    public static readonly (string Name, string[] Fields, string Scalar, string FromPod, string ToPod)[] RuntimeStructs =
    {
        ("QPointF", new[] { "x", "y" }, "double", null, null),
        ("QPoint", new[] { "x", "y" }, "int", null, null),
        ("QSize", new[] { "width", "height" }, "int", null, null),
        ("QSizeF", new[] { "width", "height" }, "double", null, null),
        ("QMargins", new[] { "left", "top", "right", "bottom" }, "int", null, null),
        ("QMarginsF", new[] { "left", "top", "right", "bottom" }, "double", null, null),
        ("QLine", new[] { "x1", "y1", "x2", "y2" }, "int", null, null),
        ("QLineF", new[] { "x1", "y1", "x2", "y2" }, "double", null, null),
        ("QRectF", new[] { "x", "y", "width", "height" }, "double", null, null),
        // QRect keeps its two corners (x1,y1) and (x2,y2), while its accessors spell x,y,width,height
        // and right()/bottom() are inclusive - so the POD has to follow the corners, and the two
        // conversions have to go through them rather than through the accessors.
        ("QRect", new[] { "x1", "y1", "x2", "y2" }, "int",
            "QRect(QPoint(@pod.x1, @pod.y1), QPoint(@pod.x2, @pod.y2))",
            "QsApiRect{ @qt.left(), @qt.top(), @qt.bottomRight().x(), @qt.bottomRight().y() }"),
        // The rest of the trivially copyable ones: integers and floats, nothing that owns anything.
        // QDate and QTime are one integer each and their accessors are not field-shaped
        // (toJulianDay/msecsSinceStartOfDay), so both conversions are spelled out; the vector family
        // and QMatrix4x4 are floats with a matching constructor and matching accessors, which is the
        // default spelling.
        ("QDate", new[] { "jd" }, "long long",
            "QDate::fromJulianDay(@pod.jd)", "QsApiDate{ @qt.toJulianDay() }"),
        ("QTime", new[] { "mds" }, "int",
            "QTime::fromMSecsSinceStartOfDay(@pod.mds)", "QsApiTime{ @qt.msecsSinceStartOfDay() }"),
        ("QVector2D", new[] { "x", "y" }, "float", null, null),
        ("QVector3D", new[] { "x", "y", "z" }, "float", null, null),
        ("QVector4D", new[] { "x", "y", "z", "w" }, "float", null, null),
        ("QQuaternion", new[] { "scalar", "x", "y", "z" }, "float", null, null),
        // QMatrix4x4 stays a handle: Qt 6.11 no longer has the m11()..m44() accessors the POD
        // spelling needs (data() is column-major), and a bulk path through matrices is not a leak
        // risk the way a point list is.
    };

    public static (string Name, string[] Fields, string Scalar, string FromPod, string ToPod)? RuntimeStructOf(string name)
    {
        if (name == null)
            return null;
        foreach (var candidate in RuntimeStructs)
            if (candidate.Name == name)
                return candidate;
        return null;
    }

    public static bool IsStruct(string name) => RuntimeStructOf(name) != null;

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
            // A pointer to a runtime struct is an array of it too: Qt's drawPoints-style entry points
            // take `const QPointF *points, int pointCount`, and a pinned managed array of the struct
            // is the very memory Qt reads - and writes, for the ones that fill a buffer in
            // (QRawFont::advancesForGlyphIndexes).
            if (IsStruct(pointee))
                return Managed(pointee, classes) + "[]";
            // A bound class in a named C++ namespace (Qt3DCore::QEntity) is emitted under its
            // simple name: the extension's whole binding is one C# namespace.
            return classes != null && classes.Contains(pointee) ? SimpleName(pointee) : "IntPtr";
        }
        if (modelType.Contains("::"))
        {
            // A bound class keeps only its last segment; a nested enum (QEntity::Aspect) and a
            // core namespace enum (Qt::Alignment) keep the C# nesting their generator gives them.
            if (classes != null && classes.Contains(modelType))
                return SimpleName(modelType);
            return modelType.Replace("::", ".");
        }
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

    /// <summary>The last segment of a (possibly namespace-qualified) C++ name.</summary>
    public static string SimpleName(string modelType)
    {
        int i = modelType.LastIndexOf("::", StringComparison.Ordinal);
        return i < 0 ? modelType : modelType.Substring(i + 2);
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
//   extract2 : root headers -> clang AST ->  model/qtsharp.api.json   (parse-only, deterministic)
//   codegen  : qtsharp.api.json          ->  C# bindings + C++ wrapper (pure function)
//
// The point of the model is that codegen no longer reads raw Qt headers itself, so it is
// a pure function of a versioned artifact: the same model always produces byte-identical
// output, and CI can enforce "regenerate -> diff must be empty".
// The class is split in two files: ApiModel.cs holds the model shape, the roots table, the
// vocabulary, the header scanners and the serializer; ClangExtractor.cs reads the AST through
// libclang/ClangSharp (the `extract2` command). Everything downstream of "the AST has been
// read" is shared.
public static partial class ApiModel
{
    public const int FormatVersion = 2;
    public const string QtVersion = "6.12.0";

    /// <summary>
    /// The managed namespace the binding being produced lives in: QtSharp.Api for the core binding,
    /// and a module's own for an extension (QtSharp.Charts), because an extension is a separate
    /// assembly and a separate package.
    /// </summary>
    public static string Namespace => CurrentModule?.Namespace ?? CoreNamespace;

    public const string CoreNamespace = "QtSharp.Api";

    /// <summary>
    /// The extension module being extracted or generated, or null for the core binding. It is set
    /// once at the start of a run; everything else about the model is shaped identically for both.
    /// </summary>
    internal static ExtensionModule CurrentModule;

    // Classes the model exposes, each with the header that declares it and the Qt module it
    // belongs to. Extraction parses exactly these headers instead of the whole Qt header
    // set, which is what makes it fast; growing the model means adding a row here.
    //
    // The second flag binds constructors. Value-ish Qt classes (events) are only ever seen
    // through a pointer, so there is no point in exposing a way to build them.
    // One entry of the model's class table. Parsing is scoped to these headers, so this table
    // is the model's single input description.
    public class Root
    {
        // The C++ name as the extractor has to look it up. It is unqualified for the core
        // binding (QObject) and namespace-qualified for a module that lives in one
        // (Qt3DCore::QEntity), because Qt3D's QBuffer/QTransform/QAction share their simple
        // names with QtCore/QtGui/QtWidgets classes the core binding already publishes.
        public string Class;
        public string Header;
        public string Module;
        // Whether constructors are published. Events are only ever received (Qt builds them
        // on the stack), so offering a way to build one would only invite mistakes.
        public bool BindCtors = true;
        // Whether the binding may delete instances. Only true for the value types we box
        // ourselves; Qt-owned objects and events must never be freed by the GC.
        public bool Owned = true;

        /// <summary>The class name the model and the managed binding use (the last C++ segment).</summary>
        public string ManagedClass
        {
            get
            {
                int i = Class.LastIndexOf("::", StringComparison.Ordinal);
                return i < 0 ? Class : Class.Substring(i + 2);
            }
        }
    }

    static Root R(string cls, string header, string module, bool bindCtors = true, bool owned = true)
        => new Root
        {
            Class = cls, Header = header, Module = module, BindCtors = bindCtors, Owned = owned,
        };

    /// <summary>
    /// A module bound as its own package against the core binding - QtSharp.Charts and
    /// QtSharp.Qt3D today, and whatever follows them - mirroring how PyQt6-Charts and
    /// PySide6-Addons are split away from PyQt6 and PySide6.
    ///
    /// The Qt module is an addon outside qtbase/qtsvg. Keeping it out of QtSharp.Api is also what
    /// stops every core consumer from inheriting an addon's license (Qt Charts, for one, is
    /// GPLv3-or-commercial rather than LGPL). The classes here may derive from core classes - the
    /// generator knows both models, so `QChart : QGraphicsWidget` works even though
    /// QGraphicsWidget is only bound in the core assembly.
    ///
    /// Adding a module is a row here, an addon row in tools/build-qt-kit.ps1 (when the kit does
    /// not already carry it), and a package project under nuget/ - QtSharp.Charts and
    /// QtSharp.Qt3D are the two worked examples.
    /// </summary>
    public sealed class ExtensionModule
    {
        /// <summary>The value passed to --module.</summary>
        public string Name;
        /// <summary>The managed namespace - and so the assembly name - of the module's binding.</summary>
        public string Namespace;
        /// <summary>The assembly this module's code is compiled into (normally <see cref="Namespace"/>).</summary>
        public string Assembly;
        /// <summary>The NuGet package this assembly is shipped in (QtSharp.Api for a base module).</summary>
        public string Package;
        /// <summary>Whether this is a base-binding module rather than an addon.</summary>
        public bool IsCore;
        /// <summary>The native library the module's generated P/Invokes call.</summary>
        public string NativeLibrary;
        /// <summary>
        /// The Qt module(s): <c>include\&lt;QtModule&gt;</c> is where the headers live. One entry
        /// for a module with a single include directory (QtCharts); Qt3D is a family of six
        /// modules that share one binding, so its headers span Qt3DCore, Qt3DRender, ...
        /// </summary>
        public string[] QtModules;
        /// <summary>The define(s) the headers expect, e.g. <c>QT_CHARTS_LIB</c>.</summary>
        public string[] QtDefines;
        /// <summary>
        /// The C++ namespaces whose declarations belong to this module. QtCore owns the <c>Qt</c>
        /// namespace (Qt::Alignment); a Qt3D module owns the namespace with its module name.
        /// </summary>
        public string[] CppNamespaces = Array.Empty<string>();
        /// <summary>The Qt libraries the module's native glue links.</summary>
        public string[] QtLibs;
        /// <summary>
        /// Headers of this module that no root includes but whose namespace-level declarations
        /// are part of its public API (Qt3DRender's qrenderapi.h declares API, which only
        /// dependent modules name in their signatures).
        /// </summary>
        public string[] ExtraHeaders = Array.Empty<string>();
        /// <summary>
        /// Other extension modules this one builds on (Qt3DRender builds on Qt3DCore). Their
        /// classes are part of the vocabulary and may be base classes, but they are emitted and
        /// packaged by their own module, so this binding references their assembly instead.
        /// </summary>
        public string[] DependsOn = Array.Empty<string>();
        /// <summary>The classes to bind. Their bases may be classes of the core binding.</summary>
        public Root[] Roots;
    }

    /// <summary>The extension modules this generator can bind.</summary>
    static ExtensionModule[] extensionModules;
    internal static ExtensionModule[] ExtensionModules => extensionModules ??= BuildExtensionModules();

    // Qt Charts: an addon package of its own, because Qt Charts is not part of the LGPL base
    // runtime - a core consumer must not inherit its license by referencing QtSharp.Api.
    static ExtensionModule ChartsModule()
    {
        return new ExtensionModule
        {
            Name = "charts",
            Namespace = "QtSharp.Charts",
            // The managed assembly is QtSharp.Charts (the namespace, and what a consumer references);
            // the native glue needs a different file name or the two overwrite each other in the
            // output directory. The base binding has the same split: QtSharp.QtCore.dll beside QtSharp.Api.dll.
            NativeLibrary = "QtSharp.Charts.Native",
            Assembly = "QtSharp.Charts",
            Package = "QtSharp.Charts",
            QtModules = new[] { "QtCharts" },
            QtDefines = new[] { "QT_CHARTS_LIB" },
            QtLibs = new[] { "Qt6Charts" },
            DependsOn = new[] { "qtcore", "qtgui", "qtwidgets" },
            CppNamespaces = Array.Empty<string>(),
            Roots = new[]
            {
                // The widget pair the module exists for, and the polar variant.
                R("QChart",                        "qchart.h",                       "QtCharts"),
                R("QChartView",                    "qchartview.h",                   "QtCharts"),
                R("QPolarChart",                   "qpolarchart.h",                  "QtCharts"),
                // Series: the value families, the bar families and the pie family.
                R("QAbstractSeries",               "qabstractseries.h",              "QtCharts", bindCtors: false),
                R("QXYSeries",                     "qxyseries.h",                    "QtCharts", bindCtors: false),
                R("QLineSeries",                   "qlineseries.h",                  "QtCharts"),
                R("QSplineSeries",                 "qsplineseries.h",                "QtCharts"),
                R("QScatterSeries",                "qscatterseries.h",               "QtCharts"),
                R("QAreaSeries",                   "qareaseries.h",                  "QtCharts"),
                R("QAbstractBarSeries",            "qabstractbarseries.h",           "QtCharts", bindCtors: false),
                R("QBarSeries",                    "qbarseries.h",                   "QtCharts"),
                R("QStackedBarSeries",             "qstackedbarseries.h",            "QtCharts"),
                R("QPercentBarSeries",             "qpercentbarseries.h",            "QtCharts"),
                R("QHorizontalBarSeries",          "qhorizontalbarseries.h",         "QtCharts"),
                R("QHorizontalStackedBarSeries",   "qhorizontalstackedbarseries.h",  "QtCharts"),
                R("QHorizontalPercentBarSeries",   "qhorizontalpercentbarseries.h",  "QtCharts"),
                R("QBarSet",                       "qbarset.h",                      "QtCharts"),
                // The box-and-whisker and candlestick families: a series of their own set type, the
                // way the bar series pair with QBarSet.
                R("QBoxPlotSeries",                "qboxplotseries.h",               "QtCharts"),
                R("QBoxSet",                       "qboxset.h",                      "QtCharts"),
                R("QCandlestickSeries",            "qcandlestickseries.h",           "QtCharts"),
                R("QCandlestickSet",               "qcandlestickset.h",              "QtCharts"),
                R("QPieSeries",                    "qpieseries.h",                   "QtCharts"),
                R("QPieSlice",                     "qpieslice.h",                    "QtCharts"),
                // The model mappers: they drive a series from a QAbstractItemModel, which a managed
                // caller supplies (the examples write their own table model in C#). The two bases
                // have to be published or the setters would vanish with them.
                R("QBarModelMapper",               "qbarmodelmapper.h",              "QtCharts"),
                R("QVBarModelMapper",              "qvbarmodelmapper.h",             "QtCharts"),
                R("QHBarModelMapper",              "qhbarmodelmapper.h",             "QtCharts"),
                R("QXYModelMapper",                "qxymodelmapper.h",               "QtCharts"),
                R("QVXYModelMapper",               "qvxymodelmapper.h",              "QtCharts"),
                R("QHXYModelMapper",               "qhxymodelmapper.h",              "QtCharts"),
                // Axes.
                R("QAbstractAxis",                 "qabstractaxis.h",                "QtCharts", bindCtors: false),
                R("QValueAxis",                    "qvalueaxis.h",                   "QtCharts"),
                R("QCategoryAxis",                 "qcategoryaxis.h",                "QtCharts"),
                R("QBarCategoryAxis",              "qbarcategoryaxis.h",             "QtCharts"),
                R("QDateTimeAxis",                 "qdatetimeaxis.h",                "QtCharts"),
                R("QLogValueAxis",                 "qlogvalueaxis.h",                "QtCharts"),
                // The legend and its markers. Nothing here is QObject-owned except through the chart,
                // so the release rule stays with the core's: parented objects are not ours to delete.
                R("QLegend",                       "qlegend.h",                      "QtCharts", bindCtors: false, owned: false),
                R("QLegendMarker",                 "qlegendmarker.h",                "QtCharts", bindCtors: false, owned: false),
            },
        };
    }

    static ExtensionModule[] BuildExtensionModules() => new[]
    {
        ChartsModule(),
        // Qt3D is one binding per Qt module (six separate packages), not one package for the
        // family. Each module declares the ones it builds on; see ApiModel.Modules.cs.
        Qt3DCoreModule(),
        Qt3DRenderModule(),
        Qt3DInputModule(),
        Qt3DLogicModule(),
        Qt3DAnimationModule(),
        Qt3DExtrasModule(),
        QtWebSocketsModule(),
        // The base binding is the same kind of module family: one assembly per Qt module,
        // grouped into the QtSharp.Api package. Their roots come from the core Roots table.
        QtCoreModule(),
        QtGuiModule(),
        QtWidgetsModule(),
        QtNetworkModule(),
        QtSvgModule(),
        QtSvgWidgetsModule(),
        QtPrintSupportModule(),
        QtXmlModule(),
        QtOpenGLModule(),
        QtOpenGLWidgetsModule(),
        QtSqlModule(),
    };

    internal static ExtensionModule FindExtensionModule(string name)
    {
        var module = ExtensionModules.FirstOrDefault(m => m.Name == name);
        if (module == null)
        {
            throw new InvalidOperationException(
                $"Unknown extension module '{name}'. Known modules: {string.Join(", ", ExtensionModules.Select(m => m.Name))}");
        }
        return module;
    }

    /// <summary>
    /// The module itself followed by every extension module it builds on, transitively. The
    /// extractor needs their roots to accept a base/type that belongs to another package; the
    /// code generator needs their models for the same reason.
    /// </summary>
    internal static IEnumerable<ExtensionModule> ModuleClosure(ExtensionModule module)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<ExtensionModule>();
        stack.Push(module);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current.Name))
                continue;
            yield return current;
            foreach (string dependency in current.DependsOn)
            {
                var resolved = ExtensionModules.FirstOrDefault(m => m.Name == dependency);
                if (resolved != null)
                    stack.Push(resolved);
            }
        }
    }

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
        //
        // QTimerEvent and QChildEvent live in QtCore/qcoreevent.h rather than QtGui/qevent.h,
        // but they belong to the same family: a timerEvent()/childEvent() handler could already
        // be reached, it just could not read the timer id or the child.
        R("QTimerEvent",        "qcoreevent.h", "QtCore", bindCtors: false, owned: false),
        R("QChildEvent",        "qcoreevent.h", "QtCore", bindCtors: false, owned: false),
        // QEvent is QtCore's (QtCore/qcoreevent.h); QtGui/qevent.h only includes that header
        // before declaring the GUI events. The root has to name the defining header, or the
        // class is split into the wrong module.
        R("QEvent",             "qcoreevent.h", "QtCore", bindCtors: false, owned: false),
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
        R("QResource",             "qresource.h", "QtCore"),
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
        // A QObject-derived value-ish class the document viewer needs for its settings; its keys
        // are QAnyStringView, which the vocabulary already carries as string.
        R("QSettings",             "qsettings.h", "QtCore"),
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
        // QtNetwork: the local IPC pair a single-instance app uses to hand a second launch over to
        // the running one ("wake the dashboard", "open this object"). The module is a separate
        // library, so the native build links Qt6Network and the runtime folder carries
        // Qt6Network.dll (see build_api.bat / README.md).
        R("QLocalServer",          "qlocalserver.h", "QtNetwork"),
        R("QLocalSocket",          "qlocalsocket.h", "QtNetwork"),
        // The rest of the QtNetwork surface a desktop app uses: addressing, the TCP/UDP transport
        // and the HTTP client. Types they reference but that are not bound (QSslError, cookies)
        // only cost the methods that name them - the extractor drops those and keeps the rest.
        R("QHostAddress",          "qhostaddress.h", "QtNetwork"),
        R("QHostInfo",             "qhostinfo.h", "QtNetwork"),
        R("QNetworkInterface",     "qnetworkinterface.h", "QtNetwork"),
        R("QNetworkAddressEntry",  "qnetworkinterface.h", "QtNetwork"),
        R("QAbstractSocket",       "qabstractsocket.h", "QtNetwork"),
        R("QTcpSocket",            "qtcpsocket.h", "QtNetwork"),
        R("QTcpServer",            "qtcpserver.h", "QtNetwork"),
        R("QUdpSocket",            "qudpsocket.h", "QtNetwork"),
        R("QNetworkDatagram",      "qnetworkdatagram.h", "QtNetwork"),
        R("QNetworkProxy",         "qnetworkproxy.h", "QtNetwork"),
        R("QNetworkRequest",       "qnetworkrequest.h", "QtNetwork"),
        R("QNetworkReply",         "qnetworkreply.h", "QtNetwork"),
        R("QNetworkAccessManager", "qnetworkaccessmanager.h", "QtNetwork"),
        // QtSvg / QtSvgWidgets: vector graphics - an SVG the tray or dashboard draws at any size,
        // and SVG as a paint device (QPainter output to a file). Each is its own module + DLL.
        R("QSvgRenderer",          "qsvgrenderer.h", "QtSvg"),
        R("QSvgGenerator",         "qsvggenerator.h", "QtSvg"),
        R("QSvgWidget",            "qsvgwidget.h", "QtSvgWidgets"),
        // QtPrintSupport: printing, print preview and (through QPrinter) PDF output. QPrinter's
        // base is the QtGui QPagedPaintDevice, and the dialogs' base is QAbstractPrintDialog, so
        // those two have to be roots first or the chain stops.
        R("QPagedPaintDevice",     "qpagedpaintdevice.h", "QtGui", bindCtors: false),
        R("QAbstractPrintDialog",  "qabstractprintdialog.h", "QtPrintSupport", bindCtors: false),
        R("QPrinter",              "qprinter.h", "QtPrintSupport"),
        R("QPrintDialog",          "qprintdialog.h", "QtPrintSupport"),
        R("QPageSetupDialog",      "qpagesetupdialog.h", "QtPrintSupport"),
        R("QPrinterInfo",          "qprinterinfo.h", "QtPrintSupport"),
        R("QPrintPreviewDialog",   "qprintpreviewdialog.h", "QtPrintSupport"),
        R("QPrintPreviewWidget",   "qprintpreviewwidget.h", "QtPrintSupport"),
        // QtXml: the DOM (QXmlStreamReader/Writer are already bound). Implicitly-shared value
        // classes that point into the document they came from.
        R("QDomNode",              "qdom.h", "QtXml"),
        R("QDomDocument",          "qdom.h", "QtXml"),
        R("QDomDocumentType",      "qdom.h", "QtXml"),
        R("QDomDocumentFragment",  "qdom.h", "QtXml"),
        R("QDomElement",           "qdom.h", "QtXml"),
        R("QDomAttr",              "qdom.h", "QtXml"),
        R("QDomCharacterData",     "qdom.h", "QtXml"),
        R("QDomText",              "qdom.h", "QtXml"),
        R("QDomComment",           "qdom.h", "QtXml"),
        R("QDomCDATASection",      "qdom.h", "QtXml"),
        R("QDomProcessingInstruction", "qdom.h", "QtXml"),
        R("QDomImplementation",    "qdom.h", "QtXml"),
        R("QDomNodeList",          "qdom.h", "QtXml"),
        R("QDomNamedNodeMap",      "qdom.h", "QtXml"),
        // QtOpenGLWidgets / QtOpenGL: rendering with OpenGL, including from C# (QOpenGLWidget's
        // initializeGL/resizeGL/paintGL are overridable). QOpenGLContext is the QtGui half.
        R("QPaintEngine",          "qpaintengine.h", "QtGui", bindCtors: false),
        R("QOpenGLContext",        "qopenglcontext.h", "QtGui"),
        R("QOpenGLWidget",         "qopenglwidget.h", "QtOpenGLWidgets"),
        R("QOpenGLShaderProgram",  "qopenglshaderprogram.h", "QtOpenGL"),
        R("QOpenGLBuffer",         "qopenglbuffer.h", "QtOpenGL"),
        R("QOpenGLVertexArrayObject", "qopenglvertexarrayobject.h", "QtOpenGL"),
        R("QOpenGLFramebufferObject", "qopenglframebufferobject.h", "QtOpenGL"),
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

        // ---------------------------------------------------------- QtSql
        // QtSql is part of qtbase, so it needs no addon download; the driver plugins it loads
        // (sqldrivers/qsqlite.dll) are part of the runtime payload instead. QSqlRelationalDelegate
        // is deliberately absent: it derives from QStyledItemDelegate, and binding it would make
        // the QtSql assembly depend on QtWidgets for one convenience class.
        R("QSqlDatabase",  "qsqldatabase.h",  "QtSql"),
        R("QSqlDriver",    "qsqldriver.h",    "QtSql", bindCtors: false),
        R("QSqlError",     "qsqlerror.h",     "QtSql"),
        R("QSqlField",     "qsqlfield.h",     "QtSql"),
        R("QSqlRecord",    "qsqlrecord.h",    "QtSql"),
        R("QSqlIndex",     "qsqlindex.h",     "QtSql"),
        R("QSqlQuery",     "qsqlquery.h",     "QtSql"),
        R("QSqlResult",    "qsqlresult.h",    "QtSql", bindCtors: false),
        R("QSqlQueryModel", "qsqlquerymodel.h", "QtSql"),
        R("QSqlTableModel", "qsqltablemodel.h", "QtSql"),
        R("QSqlRelationalTableModel", "qsqlrelationaltablemodel.h", "QtSql"),
        R("QSqlRelation",  "qsqlrelationaltablemodel.h", "QtSql"),
    };

    // Names that only ever occur as a pointer and are deliberately left opaque (IntPtr).
    //
    // The extractor does not consult this list to decide what to bind; the roots do that (the whole
    // input-event family is bound as ordinary roots, which is why a managed keyPressEvent can read a
    // key code). What keeps this list alive is BuildVocabulary: a spelling that appears here is
    // still a supported type, so a pointer to one of these can sit in an otherwise-bound signature
    // (QEnterEvent*, QKeyEvent*, ...) instead of disqualifying the declaration.
    //
    // QTimerEvent and QChildEvent used to be here as well; they are roots now, so a timerEvent()/
    // childEvent() handler can actually read the event (the root loop in BuildVocabulary covers the
    // pointer spelling too).
    static readonly string[] OpaquePointers =
    {
        "QEnterEvent", "QInputEvent", "QKeyEvent", "QWheelEvent",
        "QDragEnterEvent", "QDragMoveEvent", "QDragLeaveEvent", "QDropEvent",
        "QHelpEvent", "QActionEvent", "QInputMethodEvent", "QDynamicPropertyChangeEvent",
    };

    // The whole type vocabulary of the model: anything outside it is skipped rather than
    // guessed, and that is what keeps the model bounded and deterministic.
    // Built on first use rather than in a field initializer. The vocabulary depends on which module
    // is being bound, and assigning to a static field runs the type's initializers *first* - so a
    // field initializer here would always see CurrentModule as null and hand a module run the core
    // vocabulary, which shows up as every one of the module's own classes being "unsupported".
    static HashSet<string> supportedTypes;
    static HashSet<string> SupportedTypes => supportedTypes ??= BuildVocabulary();

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
            // Both spellings: a signature read out of the AST is qualified for Qt3D
            // (Qt3DCore::QEntity*), while the model's class table and the enum scopes use the
            // simple name.
            types.Add(root.Class);
            types.Add(root.Class + "*");
            types.Add(root.ManagedClass);
            types.Add(root.ManagedClass + "*");
        }
        // An extension module's vocabulary has to include the core's classes as well: a
        // QChart::setBackgroundBrush takes a QBrush and QAbstractSeries::hovered carries a QPointF,
        // and both are bound - as boxed value types, in the core assembly. Without them every such
        // signature would be dropped as unsupported.
        if (CurrentModule != null)
        {
            foreach (var root in Roots)
            {
                types.Add(root.Class);
                types.Add(root.Class + "*");
                types.Add(root.ManagedClass);
                types.Add(root.ManagedClass + "*");
            }
            // ... and the classes of the extension modules this one builds on: a Qt3DRender
            // signature takes Qt3DCore::QEntity, which is bound, just in another package.
            foreach (var dependency in ModuleClosure(CurrentModule))
            {
                foreach (var root in dependency.Roots)
                {
                    types.Add(root.Class);
                    types.Add(root.Class + "*");
                    types.Add(root.ManagedClass);
                    types.Add(root.ManagedClass + "*");
                }
            }
        }
        foreach (var opaque in OpaquePointers)
            types.Add(opaque + "*");
        return types;
    }

    // Roots is the model's input description. QTSHARP_ROOTS=<comma separated class names>
    // narrows it, which is how the parser was debugged while the front end was being replaced.
        static Root[] ActiveRoots
        {
            get
            {
                // An extension module binds its own classes; everything else about the run - the
                // vocabulary, the base resolution, the virtual chains - is shared with the core.
                var roots = CurrentModule?.Roots ?? Roots;
                var filter = Environment.GetEnvironmentVariable("QTSHARP_ROOTS");
                if (string.IsNullOrEmpty(filter))
                    return roots;
                var wanted = new HashSet<string>(filter.Split(','), StringComparer.Ordinal);
                return roots.Where(r => wanted.Contains(r.Class) || wanted.Contains(r.ManagedClass)).ToArray();
            }
        }

        // Which classes count as *bound* while reading types. For the core that is its own roots.
        // For an extension it is the module's classes *and* the core's: QGraphicsWidget is not one
        // of the module's roots, but a QChart derives from it, and the generated code has to treat
        // its pointers as the core binding's types rather than as unknown ones.
        static IEnumerable<string> RootClasses => CurrentModule == null
            ? ActiveRoots.Select(r => r.Class)
            // The qualified name is what a signature read out of the AST spells
            // (Qt3DCore::QEntity*); the simple one is what the model's class table and the
            // class-scoped enum names use (QEntity::Aspect).
            : Roots.Select(r => r.Class)
                .Concat(ModuleClosure(CurrentModule)
                    .SelectMany(m => m.Roots)
                    .SelectMany(r => new[] { r.Class, r.ManagedClass }))
                .Distinct(StringComparer.Ordinal);

        // Only ever asked about a class that is being extracted, so the module's own table is the
        // one to look in.
        static bool BindsCtors(string className)
            => ActiveRoots.First(r => r.ManagedClass == className).BindCtors;

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
        // QOpenGLWidget: a widget that renders through OpenGL, subclassed from C#.
        "initializeGL", "resizeGL", "paintGL",
    };

    // The parse-only side of the pipeline only needs the headers of the roots it is given:
    // no generator, no type maps, no code generation, none of the legacy post-processing.
    // Parsing is the whole cost, so scoping it to the roots is what keeps extraction fast.
    class ModelPart
    {
        public readonly Dictionary<string, string> Classes = new(StringComparer.Ordinal);
        public readonly Dictionary<string, EnumEntry> Enums = new(StringComparer.Ordinal);
        // Namespace-level (free) functions: the QtCore/QtGui/QtWidgets module functions PyQt
        // publishes next to the classes (qCompress, qVersion, qRegisterResourceData, qDrawShadePanel).
        // Each entry is a pre-rendered JSON object with the same shape as a method.
        public readonly List<string> Functions = new();

        public void Merge(ModelPart other)
        {
            foreach (var kv in other.Classes)
                Classes[kv.Key] = kv.Value;
            foreach (var kv in other.Enums)
                Enums[kv.Key] = kv.Value;
            Functions.AddRange(other.Functions);
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
            sb.Append("  \"functions\": [\n");
            for (int i = 0; i < Functions.Count; i++)
            {
                sb.Append("    ").Append(Functions[i]);
                sb.Append(i + 1 < Functions.Count ? ",\n" : "\n");
            }
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

    static void WriteSignals(StringBuilder sb, List<Program.SignalInfo> classSignals)
    {
        sb.Append("      \"signals\": [\n");
        for (int i = 0; i < classSignals.Count; i++)
        {
            var s = classSignals[i];
            sb.Append("        { \"name\": ").Append(Json(s.SignalName)).Append(", \"parameters\": [");
            sb.Append(string.Join(", ", s.Params.Where(p => !p.Private).Select(SignalParamJson)));
            sb.Append("] }");
            sb.Append(i + 1 < classSignals.Count ? ",\n" : "\n");
        }
        sb.Append("      ]\n");
    }

    // One signal parameter, as the model spells it. The clang front end reads it out of the AST,
    // and that same spelling is what Qt stores in its meta-object (QString, QWidget*,
    // Qt::Orientation) - which is why a signal needs no separate "cpp" field.
    static string SignalParamJson(Program.SignalParamInfo p)
        => "{ \"name\": " + Json(CleanName(p.Name)) + ", \"type\": " + Json(p.ModelType) + " }";

    static void WriteEnum(StringBuilder sb, EnumEntry entry, bool comma)
    {
        sb.Append("    { \"name\": ").Append(Json(entry.Name));
        sb.Append(", \"qualified\": ").Append(Json(entry.Qualified));
        // The owning extension module. A dependency enum is carried for type resolution but is
        // emitted by the dependency's own package.
        if (!string.IsNullOrEmpty(entry.Module))
            sb.Append(", \"module\": ").Append(Json(entry.Module));
        // The C++ spelling when the published name dropped a module's namespace
        // (Qt3DCore::ChangeFlag -> ChangeFlag), so the native glue can still name the type.
        if (!string.IsNullOrEmpty(entry.CppQualified) &&
            !string.Equals(entry.CppQualified, entry.Qualified, StringComparison.Ordinal))
            sb.Append(", \"cppQualified\": ").Append(Json(entry.CppQualified));
        sb.Append(", \"flags\": ").Append(entry.Flags ? "true" : "false");
        sb.Append(", \"items\": [");
        sb.Append(string.Join(", ", entry.Items.Select(item =>
            "{ \"name\": " + Json(item.Name) + ", \"value\": " + item.Value + " }")));
        sb.Append("] }");
        sb.Append(comma ? ",\n" : "\n");
    }

    class EnumEntry
    {
        public string Name;
        public string Qualified;
        // The fully qualified C++ name when Qualified dropped a module namespace.
        public string CppQualified;
        // The extension module that declares the enum, or null for a core enum. An extension
        // model carries its dependencies' enums for type resolution, but only its own are
        // emitted and packaged.
        public string Module;
        // The items are kept as plain values rather than as a declaration.
        public List<(string Name, long Value)> Items = new();
        public bool Flags;

    }

    // Where an enum / flags type can live on the managed side: a global enum, a Qt-namespace one,
    // or one nested in a class the model publishes. The extractor asks this before it puts an
    // enum into the model, because an enum whose scope is not reachable could not be named in C#.
    static string ScopeOfQualified(string qualified)
    {
        int i = qualified.LastIndexOf("::", StringComparison.Ordinal);
        return i < 0 ? string.Empty : qualified.Substring(0, i);
    }

    static bool ScopeIsPublishable(string scope)
        => scope.Length == 0 || scope == "Qt" || RootClasses.Contains(scope)
           // A module that lives in a C++ namespace (Qt3D) declares enums at that namespace's
           // scope (Qt3DCore::ChangeFlag). They are published as namespace-level C# enums.
           || (CurrentModule != null
               && ModuleClosure(CurrentModule).Any(m => m.QtModules.Contains(scope)));

    static string Json(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // The model stores plain C++ names; the code generator escapes again for C# only. (A
    // front end that escaped keywords in names, "event" -> "@event", is undone here.)
    static string CleanName(string s) => string.IsNullOrEmpty(s) ? s : (s[0] == '@' ? s.Substring(1) : s);

}
