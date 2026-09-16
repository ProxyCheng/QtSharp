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

class QtLibrary : ILibrary
{
    public void Setup(Driver driver)
    {
        driver.ParserOptions.LanguageVersion = LanguageVersion.CPP17;

        var options = driver.Options;
        options.GeneratorKind = GeneratorKind.CSharp;
        options.OutputDir = @"D:\QtSharp\out\qt_min";
        options.GetClassGenerationOptions = _ => new ClassGenerationOptions { GenerateNativeToManaged = true };
        options.GenerateDefaultValuesForArguments = true;

        string inc = @"D:\QtSharp\Qt\6.11.0\msvc2022_64\include";

        void ConfigureModule(Module m, string moduleDir)
        {
            m.IncludeDirs.Add(inc);
            m.IncludeDirs.Add(Path.Combine(inc, moduleDir));
            m.SharedLibraryName = "QtSharp";
            m.Defines.Add("QT_NO_DEBUG");
            m.Defines.Add("QT_CORE_LIB");
            m.Defines.Add("QT_GUI_LIB");
            m.Defines.Add("QT_WIDGETS_LIB");
        }

        var core = options.AddModule("QtSharp.Core");
        core.OutputNamespace = "QtSharp.QtCore";
        ConfigureModule(core, "QtCore");
        core.Headers.Add("qobject.h");
        core.Headers.Add("qobjectdefs.h");
        core.Headers.Add("qstring.h");
        core.Headers.Add("qchar.h");
        core.Headers.Add("qbytearray.h");
        core.Headers.Add("qlist.h");
        core.Headers.Add("qvector.h");
        core.Headers.Add("qmap.h");
        core.Headers.Add("qhash.h");
        core.Headers.Add("qrect.h");
        core.Headers.Add("qsize.h");
        core.Headers.Add("qpoint.h");
        core.Headers.Add("qmargins.h");
        core.Headers.Add("qline.h");
        core.Headers.Add("qscopedpointer.h");
        core.Headers.Add("qflags.h");
        core.Headers.Add("qnamespace.h");
        core.Headers.Add("qvariant.h");
        core.Headers.Add("qdatetime.h");
        core.Headers.Add("qdir.h");
        core.Headers.Add("qfile.h");
        core.Headers.Add("qfileinfo.h");
        core.Headers.Add("qurl.h");
        core.Headers.Add("qcoreapplication.h");
        core.Headers.Add("qeventloop.h");
        core.Headers.Add("qmimedata.h");
        core.Headers.Add("qtimer.h");
        core.Headers.Add("qregularexpression.h");
        core.Headers.Add("qstringlist.h");
        core.Headers.Add("qstringview.h");
        core.Headers.Add("qbytearrayview.h");
        core.Headers.Add("qshareddata.h");
        core.Headers.Add("qmetatype.h");
        core.Headers.Add("qdatastream.h");
        core.Headers.Add("qtextstream.h");
        core.Headers.Add("qsettings.h");
        core.Headers.Add("qabstractitemmodel.h");
        core.Headers.Add("qitemselectionmodel.h");
        core.Headers.Add("qsortfilterproxymodel.h");
        core.Headers.Add("qstringlistmodel.h");
        core.Headers.Add("qabstractanimation.h");
        core.Headers.Add("qpropertyanimation.h");
        core.Headers.Add("qeasingcurve.h");

        var gui = options.AddModule("QtSharp.Gui");
        gui.OutputNamespace = "QtSharp.QtGui";
        ConfigureModule(gui, "QtGui");
        gui.Dependencies.Add(core);
        gui.Headers.Add("qpaintdevice.h");
        gui.Headers.Add("qevent.h");
        gui.Headers.Add("qwindow.h");
        gui.Headers.Add("qcolor.h");
        gui.Headers.Add("qbrush.h");
        gui.Headers.Add("qpainter.h");
        gui.Headers.Add("qpen.h");
        gui.Headers.Add("qkeysequence.h");
        gui.Headers.Add("qcursor.h");
        gui.Headers.Add("qfont.h");
        gui.Headers.Add("qicon.h");
        gui.Headers.Add("qpalette.h");
        gui.Headers.Add("qpixmap.h");
        gui.Headers.Add("qimage.h");
        gui.Headers.Add("qregion.h");
        gui.Headers.Add("qpolygon.h");
        gui.Headers.Add("qtextcursor.h");
        gui.Headers.Add("qtextformat.h");
        gui.Headers.Add("qtransform.h");
        gui.Headers.Add("qsurfaceformat.h");
        gui.Headers.Add("qscreen.h");
        gui.Headers.Add("qbackingstore.h");
        gui.Headers.Add("qvalidator.h");
        gui.Headers.Add("qstandarditemmodel.h");
        gui.Headers.Add("qbitmap.h");
        gui.Headers.Add("qguiapplication.h");
        gui.Headers.Add("qfontdatabase.h");
        gui.Headers.Add("qabstracttextdocumentlayout.h");
        gui.Headers.Add("qtextdocument.h");
        gui.Headers.Add("qtextobject.h");

        var widgets = options.AddModule("QtSharp.Widgets");
        widgets.OutputNamespace = "QtSharp.QtWidgets";
        ConfigureModule(widgets, "QtWidgets");
        widgets.Dependencies.Add(gui);
        widgets.Headers.Add("qapplication.h");
        widgets.Headers.Add("qwidget.h");
        widgets.Headers.Add("qmainwindow.h");
        widgets.Headers.Add("qdialog.h");
        widgets.Headers.Add("qframe.h");
        widgets.Headers.Add("qgroupbox.h");
        widgets.Headers.Add("qlabel.h");
        widgets.Headers.Add("qlcdnumber.h");
        widgets.Headers.Add("qlineedit.h");
        widgets.Headers.Add("qtextedit.h");
        widgets.Headers.Add("qplaintextedit.h");
        widgets.Headers.Add("qtextbrowser.h");
        widgets.Headers.Add("qabstractbutton.h");
        widgets.Headers.Add("qpushbutton.h");
        widgets.Headers.Add("qtoolbutton.h");
        widgets.Headers.Add("qradiobutton.h");
        widgets.Headers.Add("qcheckbox.h");
        widgets.Headers.Add("qcommandlinkbutton.h");
        widgets.Headers.Add("qbuttongroup.h");
        widgets.Headers.Add("qabstractspinbox.h");
        widgets.Headers.Add("qspinbox.h");
        widgets.Headers.Add("qabstractslider.h");
        widgets.Headers.Add("qslider.h");
        widgets.Headers.Add("qscrollbar.h");
        widgets.Headers.Add("qdial.h");
        widgets.Headers.Add("qcombobox.h");
        widgets.Headers.Add("qfontcombobox.h");
        widgets.Headers.Add("qdatetimeedit.h");
        widgets.Headers.Add("qkeysequenceedit.h");
        widgets.Headers.Add("qcalendarwidget.h");
        widgets.Headers.Add("qabstractscrollarea.h");
        widgets.Headers.Add("qscrollarea.h");
        widgets.Headers.Add("qtabbar.h");
        widgets.Headers.Add("qtabwidget.h");
        widgets.Headers.Add("qstackedwidget.h");
        widgets.Headers.Add("qsplitter.h");
        widgets.Headers.Add("qdockwidget.h");
        widgets.Headers.Add("qtoolbox.h");
        widgets.Headers.Add("qaction.h");
        widgets.Headers.Add("qmenu.h");
        widgets.Headers.Add("qmenubar.h");
        widgets.Headers.Add("qtoolbar.h");
        widgets.Headers.Add("qstatusbar.h");
        widgets.Headers.Add("qprogressbar.h");
        widgets.Headers.Add("qsizegrip.h");
        widgets.Headers.Add("qrubberband.h");
        widgets.Headers.Add("qmessagebox.h");
        widgets.Headers.Add("qfiledialog.h");
        widgets.Headers.Add("qinputdialog.h");
        widgets.Headers.Add("qcolordialog.h");
        widgets.Headers.Add("qfontdialog.h");
        widgets.Headers.Add("qprogressdialog.h");
        widgets.Headers.Add("qdialogbuttonbox.h");
        widgets.Headers.Add("qabstractitemview.h");
        widgets.Headers.Add("qabstractitemdelegate.h");
        widgets.Headers.Add("qstyleditemdelegate.h");
        widgets.Headers.Add("qstyleoption.h");
        widgets.Headers.Add("qlistview.h");
        widgets.Headers.Add("qtableview.h");
        widgets.Headers.Add("qtreeview.h");
        widgets.Headers.Add("qheaderview.h");
        widgets.Headers.Add("qlistwidget.h");
        widgets.Headers.Add("qtreewidget.h");
        widgets.Headers.Add("qtablewidget.h");
        widgets.Headers.Add("qlayoutitem.h");
        widgets.Headers.Add("qlayout.h");
        widgets.Headers.Add("qboxlayout.h");
        widgets.Headers.Add("qgridlayout.h");
        widgets.Headers.Add("qformlayout.h");
        widgets.Headers.Add("qstackedlayout.h");
        widgets.Headers.Add("qsystemtrayicon.h");
        widgets.Headers.Add("qshortcut.h");
        widgets.Headers.Add("qcompleter.h");
        widgets.Headers.Add("qfilesystemmodel.h");
        widgets.Headers.Add("qdatawidgetmapper.h");
        widgets.Headers.Add("qitemdelegate.h");
        widgets.Headers.Add("qwizard.h");
        widgets.Headers.Add("qerrormessage.h");
        widgets.Headers.Add("qfocusframe.h");
        widgets.Headers.Add("qscroller.h");
        widgets.Headers.Add("qundostack.h");
        widgets.Headers.Add("qundoview.h");
        widgets.Headers.Add("qstyle.h");
        widgets.Headers.Add("qproxystyle.h");
        widgets.Headers.Add("qstylepainter.h");
        widgets.Headers.Add("qstylefactory.h");
    }

    public void SetupPasses(Driver driver) { }

    public void Preprocess(Driver driver, ASTContext ctx)
    {
        LimitScope(ctx);
    }

    public static string ExtractModelPath;

    public void Postprocess(Driver driver, ASTContext ctx)
    {
        if (ExtractModelPath != null)
            ApiModel.Extract(ctx, ExtractModelPath);
        ClearInvalidInitializers(ctx);
        ReportCoverage(ctx);
        DedupeAll(ctx);
        GenerateDef(ctx);
    }

    static void DedupeAll(ASTContext ctx)
    {
        foreach (var tu in ctx.TranslationUnits)
            if (IsMyModule(tu))
                DedupeAllIn(tu);
    }

    static void DedupeAllIn(DeclarationContext ctx)
    {
        foreach (var c in ctx.Classes)
            if (c.IsGenerated)
                DedupeOverloads(c);
        foreach (var c in ctx.Classes)
            DedupeAllIn(c);
        foreach (var sub in ctx.Namespaces)
            DedupeAllIn(sub);
    }

    static readonly string[] RootNames = {
        "QApplication", "QWidget", "QEvent", "QPaintEvent", "QMouseEvent", "QKeyEvent", "QResizeEvent", "QMoveEvent", "QCloseEvent", "QFocusEvent", "QWheelEvent", "QShowEvent", "QHideEvent", "QTimerEvent", "QChildEvent", "QEnterEvent",
        "QContextMenuEvent", "QInputEvent", "QHoverEvent", "QDragEnterEvent", "QDragMoveEvent", "QDragLeaveEvent", "QDropEvent", "QHelpEvent", "QActionEvent", "QMimeData",
        "QString", "QStringList",
        "QPainter", "QTimer", "QTime",
        "QMainWindow", "QDialog", "QFrame", "QGroupBox", "QLabel", "QLCDNumber",
        "QLineEdit", "QTextEdit", "QPlainTextEdit", "QTextBrowser",
        "QAbstractButton", "QPushButton", "QToolButton", "QRadioButton", "QCheckBox", "QCommandLinkButton", "QButtonGroup",
        "QAbstractSpinBox", "QSpinBox", "QDoubleSpinBox",
        "QAbstractSlider", "QSlider", "QScrollBar", "QDial",
        "QComboBox", "QFontComboBox", "QDateTimeEdit", "QDateEdit", "QTimeEdit", "QKeySequenceEdit", "QCalendarWidget",
        "QAbstractScrollArea", "QScrollArea",
        "QTabBar", "QTabWidget", "QStackedWidget", "QSplitter", "QSplitterHandle", "QDockWidget", "QToolBox",
        "QLayout", "QLayoutItem", "QBoxLayout", "QVBoxLayout", "QHBoxLayout", "QGridLayout", "QFormLayout", "QStackedLayout", "QSpacerItem",
        "QAction", "QActionGroup", "QMenu", "QMenuBar", "QToolBar", "QStatusBar",
        "QProgressBar", "QSizeGrip", "QRubberBand",
        "QMessageBox", "QFileDialog", "QInputDialog", "QColorDialog", "QFontDialog", "QProgressDialog", "QDialogButtonBox",
        "QAbstractItemView", "QListView", "QTableView", "QTreeView", "QHeaderView",
        "QListWidget", "QListWidgetItem", "QTreeWidget", "QTreeWidgetItem", "QTableWidget", "QTableWidgetItem",
        "QAbstractItemModel", "QAbstractTableModel", "QStandardItemModel", "QStyledItemDelegate", "QAbstractItemDelegate", "QStyleOptionViewItem", "QStyleOption",
        "QSettings",
        "QSystemTrayIcon", "QShortcut",
        "QIntValidator", "QDoubleValidator", "QRegularExpressionValidator", "QRegularExpression",
        "QCommonStyle", "QProxyStyle", "QStylePainter", "QStyleFactory",
        "QAbstractAnimation", "QPropertyAnimation", "QEasingCurve", "QSortFilterProxyModel", "QStringListModel",
        "QSyntaxHighlighter", "QImageReader", "QImageWriter", "QPainterPath", "QStandardItem",
        "QCompleter", "QFileSystemModel", "QDataWidgetMapper",
        "QItemDelegate", "QWizard", "QWizardPage", "QErrorMessage", "QFocusFrame", "QScroller",
        "QUndoStack", "QUndoCommand", "QUndoView"
    };

    static readonly string[] MethodWhitelist = {
        "exec", "show", "setWindowTitle", "resize", "quit", "hide", "close"
    };

    static readonly string[] ForceIgnoreNames = {
        "QPartialOrdering", "QStrongOrdering", "QWeakOrdering",
        "QTotallyOrderedWrapper",
        "PartialOrdering", "WeakOrdering", "StrongOrdering",
        "QMetaObject", "QHashPrivate", "QHash", "QMultiHash",
        "QWidgetItemData", "QStackedLayout", "QFileDevice", "PrivateShared"
    };

    static readonly string[] ForceIgnoreMethodNames = {
        "QChar::fromUcs4",
        "QTableWidget::insertColumn",
        "QDir::rmpath",
        "QLayoutItemInternal::tr"
    };

    // Methods returning QFlags are dropped from the bindings (QFlags maps to int), but a
    // few are required by the examples (e.g. an editable item model needs flags()).
    static readonly HashSet<string> QFlagsReturnWhitelist = new()
    {
        "QAbstractItemModel::flags"
    };

    static void LimitScope(ASTContext ctx)
    {
        var reachable = new HashSet<Declaration>();
        var queue = new Queue<Class>();

        foreach (var tu in ctx.TranslationUnits)
            if (IsMyModule(tu))
                CollectRoots(tu, queue, reachable);

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var b in c.Bases)
                if (b.IsClass && b.Class != null && !reachable.Contains(b.Class))
                    AddReachable(b.Class, reachable, queue);

            if (ForceIgnoreNames.Contains(c.OriginalName))
                continue;

            foreach (var m in c.Methods)
                if (m.Access != AccessSpecifier.Private)
                {
                    VisitType(m.ReturnType.Type, reachable, queue);
                    foreach (var p in m.Parameters)
                        VisitType(p.Type, reachable, queue);
                }

            foreach (var f in c.Fields)
                VisitType(f.Type, reachable, queue);
        }

        foreach (var tu in ctx.TranslationUnits)
            if (IsMyModule(tu))
                PruneMembers(tu, reachable);

        int ignored = 0;
        foreach (var tu in ctx.TranslationUnits)
            if (IsMyModule(tu))
                IgnoreClasses(tu, reachable, ref ignored);

        Console.WriteLine($"[scope] reachable={reachable.Count} ignored={ignored}");
    }

    static bool IsMyModule(TranslationUnit tu) => tu.Module?.LibraryName?.StartsWith("QtSharp") == true;

    static void CollectRoots(Namespace ns, Queue<Class> queue, HashSet<Declaration> reachable)
    {
        foreach (var c in ns.Classes)
            if (RootNames.Contains(c.OriginalName))
                AddReachable(c, reachable, queue);
        if (ns.OriginalName == "Qt")
            foreach (var e in ns.Enums)
                AddReachable(e, reachable, queue);
        foreach (var sub in ns.Namespaces)
            CollectRoots(sub, queue, reachable);
    }

    static HashSet<string> LoadQtExports()
    {
        var set = new HashSet<string>();
        foreach (var f in new[] { "exports_core.txt", "exports_gui.txt", "exports_widgets.txt" })
        {
            var path = Path.Combine(@"D:\QtSharp\Qt\6.11.0\runtime", f);
            if (!File.Exists(path))
                continue;
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 && parts[3].StartsWith("?"))
                    set.Add(parts[3]);
            }
        }
        return set;
    }

    static void GenerateDef(ASTContext ctx)
    {
        var exports = LoadQtExports();
        var wrapped = CppSharp.Passes.SymbolsCodeGenerator.WrappedSymbols;
        var sb = new StringBuilder();
        sb.AppendLine("LIBRARY QtSharp");
        sb.AppendLine("EXPORTS");
        var seen = new HashSet<string>();
        int skipped = 0;
        foreach (var tu in ctx.TranslationUnits)
            if (IsMyModule(tu))
                GenerateDefIn(tu, sb, seen, exports, wrapped, ref skipped);
        Directory.CreateDirectory(@"D:\QtSharp\native");
        File.WriteAllText(@"D:\QtSharp\native\QtSharp.def", sb.ToString());
        Console.WriteLine($"[def] wrote {seen.Count - skipped} exports (wrapped={wrapped.Count} skipped={skipped})");
    }

    static void GenerateDefIn(DeclarationContext dc, StringBuilder sb, HashSet<string> seen,
        HashSet<string> exports, Dictionary<string, string> wrapped, ref int skipped)
    {
        foreach (var c in dc.Classes)
        {
            foreach (var m in c.Methods)
                AddMethodDef(m, sb, seen, exports, wrapped, ref skipped);
            foreach (var p in c.Properties)
            {
                AddMethodDef(p.GetMethod, sb, seen, exports, wrapped, ref skipped);
                AddMethodDef(p.SetMethod, sb, seen, exports, wrapped, ref skipped);
            }
            GenerateDefIn(c, sb, seen, exports, wrapped, ref skipped);
        }
        foreach (var f in dc.Functions)
            if (f.IsGenerated && !string.IsNullOrEmpty(f.Mangled) && seen.Add(f.Mangled))
            {
                if (exports.Contains(f.Mangled))
                    sb.AppendLine($"  {f.Mangled}");
                else
                    skipped++;
            }
        foreach (var ns in dc.Namespaces)
            GenerateDefIn(ns, sb, seen, exports, wrapped, ref skipped);
    }

    static void AddMethodDef(Method m, StringBuilder sb, HashSet<string> seen,
        HashSet<string> exports, Dictionary<string, string> wrapped, ref int skipped)
    {
        if (m == null || string.IsNullOrEmpty(m.Mangled) || !seen.Add(m.Mangled))
            return;
        if (!m.IsGenerated && !(m.AssociatedDeclaration is Property { IsGenerated: true }))
            return;
        if (wrapped.TryGetValue(m.Mangled, out var wrapper))
            sb.AppendLine($"  {m.Mangled} = {wrapper}");
        else if (exports.Contains(m.Mangled))
            sb.AppendLine($"  {m.Mangled}");
        else
            skipped++;
    }

    static void ReportCoverage(ASTContext ctx)
    {
        int classes = 0, methods = 0, ctors = 0, enums = 0, typedefs = 0;
        var skipped = new Dictionary<string, int>();

        void Walk(DeclarationContext dc)
        {
            foreach (var c in dc.Classes)
            {
                if (!c.IsGenerated) { skipped["class:" + c.OriginalName] = skipped.GetValueOrDefault("class:" + c.OriginalName) + 1; }
                else
                {
                    classes++;
                    foreach (var m in c.Methods)
                    {
                        if (!m.IsGenerated) { skipped["method:" + m.QualifiedOriginalName] = skipped.GetValueOrDefault("method:" + m.QualifiedOriginalName) + 1; }
                        else if (m.IsConstructor) ctors++;
                        else methods++;
                    }
                }
                Walk(c);
            }
            foreach (var e in dc.Enums) if (e.IsGenerated) enums++;
            foreach (var t in dc.Typedefs) if (t.IsGenerated) typedefs++;
            foreach (var ns in dc.Namespaces) Walk(ns);
        }

        foreach (var tu in ctx.TranslationUnits)
            if (IsMyModule(tu))
                Walk(tu);

        Console.WriteLine($"[coverage] classes={classes} enums={enums} typedefs={typedefs} ctors={ctors} methods={methods}");
        foreach (var kv in skipped.OrderByDescending(k => k.Value).Take(15))
            Console.WriteLine($"    skipped {kv.Key} x{kv.Value}");
    }

    static void AddReachable(Declaration d, HashSet<Declaration> reachable, Queue<Class> queue)
    {
        if (d == null)
            return;
        if (reachable.Add(d) && d is Class cls)
            queue.Enqueue(cls);
        for (var p = d.Namespace; p != null && !(p is TranslationUnit); p = p.Namespace)
            reachable.Add(p);
    }

    static void VisitType(CppSharp.AST.Type t, HashSet<Declaration> reachable, Queue<Class> queue)
    {
        if (t == null)
            return;
        if (t is TypedefType td && td.Declaration != null)
            AddReachable(td.Declaration, reachable, queue);
        var d = t.Desugar();
        if (d == null)
            return;
        if (d.TryGetDeclaration(out Declaration decl))
            AddReachable(decl, reachable, queue);
        if (d is TemplateSpecializationType ts)
            foreach (var arg in ts.Arguments)
                if (arg.Kind == TemplateArgument.ArgumentKind.Type && arg.Type.Type != null)
                    VisitType(arg.Type.Type, reachable, queue);
    }

    static void PruneMembers(DeclarationContext ctx, HashSet<Declaration> reachable)
    {
        foreach (var c in ctx.Classes)
        {
            if (reachable.Contains(c) && !c.IsDependent)
            {
                foreach (var m in c.Methods)
                {
                    if (ForceIgnoreMethodNames.Contains(m.QualifiedOriginalName))
                    {
                        m.ExplicitlyIgnore();
                        continue;
                    }
                    var rt = m.ReturnType.Type?.Desugar();
                    if (rt is TemplateSpecializationType ts &&
                        ts.Template?.TemplatedDecl?.OriginalName == "QFlags" &&
                        !QFlagsReturnWhitelist.Contains(m.QualifiedOriginalName))
                        m.ExplicitlyIgnore();
                }
                DedupeOverloads(c);
                foreach (var p in c.Properties)
                    p.ExplicitlyIgnore();
                foreach (var f in c.Fields)
                    if (f.IsBitField)
                        f.ExplicitlyIgnore();
            }
            PruneMembers(c, reachable);
        }
        foreach (var sub in ctx.Namespaces)
            PruneMembers(sub, reachable);
    }

    static void IgnoreClasses(DeclarationContext ctx, HashSet<Declaration> reachable, ref int ignored)
    {
        foreach (var c in ctx.Classes)
        {
            bool topLevel = !(c.Namespace is Class);
            if (ForceIgnoreNames.Contains(c.OriginalName) || (topLevel && !reachable.Contains(c)))
            {
                c.ExplicitlyIgnore();
                ignored++;
            }
            if (c.IsDependent)
                foreach (var spec in c.Specializations)
                    if (ForceIgnoreNames.Contains(c.OriginalName))
                    {
                        spec.ExplicitlyIgnore();
                        ignored++;
                    }
            IgnoreClasses(c, reachable, ref ignored);
        }
        foreach (var f in ctx.Functions)
            if (!reachable.Contains(f)) { f.ExplicitlyIgnore(); ignored++; }
        foreach (var e in ctx.Enums)
            if (!reachable.Contains(e)) { e.ExplicitlyIgnore(); ignored++; }
        foreach (var t in ctx.Typedefs)
            if (!reachable.Contains(t)) { t.ExplicitlyIgnore(); ignored++; }
        foreach (var v in ctx.Variables)
            if (!reachable.Contains(v)) { v.ExplicitlyIgnore(); ignored++; }
        foreach (var ev in ctx.Events)
            if (!reachable.Contains(ev)) { ev.ExplicitlyIgnore(); ignored++; }
        foreach (var sub in ctx.Namespaces)
        {
            if (ForceIgnoreNames.Contains(sub.OriginalName) || !reachable.Contains(sub))
                sub.ExplicitlyIgnore();
            else
                IgnoreClasses(sub, reachable, ref ignored);
        }
    }

    static void DedupeOverloads(Class c)
    {
        var seen = new HashSet<string>();
        foreach (var m in c.Methods)
        {
            if (m.IsConstructor || m.IsDestructor || m.IsOperator)
                continue;
            var key = MappedSigKey(m);
            if (!seen.Add(key))
                m.ExplicitlyIgnore();
        }
    }

    static string MappedSigKey(Method m)
    {
        var sb = new StringBuilder(m.IsStatic ? "static " : "instance ");
        sb.Append(m.Name);
        sb.Append('(');
        bool first = true;
        for (int i = 0; i < m.Parameters.Count; i++)
        {
            if (m.Parameters[i].GenerationKind == CppSharp.AST.GenerationKind.None)
                continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append(MappedTypeKey(m.Parameters[i].Type));
        }
        sb.Append(')');
        return sb.ToString();
    }

    static string MappedTypeKey(CppSharp.AST.Type t)
    {
        if (t == null) return "?";
        var d = t.Desugar();
        if (d is TemplateSpecializationType ts && ts.Template?.TemplatedDecl?.OriginalName == "QFlags")
            return "int";
        return d.ToString();
    }

    static void ClearInvalidInitializers(ASTContext ctx)
    {
        foreach (var tu in ctx.TranslationUnits)
            ClearInvalidInitializersIn(tu);
    }

    static void ClearInvalidInitializersIn(DeclarationContext ctx)
    {
        foreach (var v in ctx.Variables)
            if (v.Initializer != null && IsInvalidCpp(v.Initializer.String))
                v.Initializer = null;
        foreach (var c in ctx.Classes)
            ClearInvalidInitializersIn(c);
        foreach (var sub in ctx.Namespaces)
            ClearInvalidInitializersIn(sub);
    }

    static bool IsInvalidCpp(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        s = s.Trim();
        if (s.Length > 1 && s[0] == '-' && s.Substring(1).All(char.IsDigit))
            return true;
        return s.Contains("::") || s.Contains("noexcept") || s.Contains("decltype")
            || s.Contains("declval") || s.Contains("std::") || s.Contains("->")
            || s.Contains("static_cast") || s.Contains("reinterpret_cast")
            || s.Contains("const_cast") || s.Contains("sizeof(")
            || s.Contains("<") || s.Contains(">") || s.Contains("&&")
            || s.Contains("||") || s.Contains("==") || s.Contains("!=");
    }
}

class Program
{
    static void FixDtorHooks(string path)
    {
        var text = File.ReadAllText(path);
        var regex = new System.Text.RegularExpressions.Regex(
            @"var __target = (?<cls>[\w:.]+?)\.__GetInstance\(__instance\);\r?\n(?<indent>\s*)__target\.Dispose\(disposing: true, callNativeDtor: true\);");
        int count = regex.Matches(text).Count;
        text = regex.Replace(text, m =>
            $"if (!{m.Groups["cls"].Value}.__TryGetNativeToManagedMapping(__instance, out var __mapped))\n{m.Groups["indent"].Value}    return;\n{m.Groups["indent"].Value}var __target = ({m.Groups["cls"].Value}) __mapped;\n{m.Groups["indent"].Value}__target.Dispose(disposing: true, callNativeDtor: true);");

        File.WriteAllText(path, text);
        Console.WriteLine($"[fix] dtor={count}");
    }

    static void FixDuplicateFields(string path)
    {
        var text = File.ReadAllText(path);
        var structRegex = new System.Text.RegularExpressions.Regex(
            @"(?<open>partial struct __Internal\s*\{)(?<body>.*?)(?<close>\n\s*\})", System.Text.RegularExpressions.RegexOptions.Singleline);
        var fieldRegex = new System.Text.RegularExpressions.Regex(
            @"internal\s+(?!static)[\w:.<>]+\s+(?<name>\w+)\s*;");
        int total = 0;
        text = structRegex.Replace(text, m =>
        {
            var seen = new HashSet<string>();
            var body = fieldRegex.Replace(m.Groups["body"].Value, mm =>
            {
                var name = mm.Groups["name"].Value;
                if (seen.Add(name))
                    return mm.Value;
                total++;
                return mm.Value.Replace(" " + name + ";", " " + name + "_dup;");
            });
            return m.Groups["open"].Value + body + m.Groups["close"].Value;
        });
        File.WriteAllText(path, text);
        Console.WriteLine($"[fix] dupFields={total}");
    }

    static void DedupeStructs(string[] paths)
    {
        var seen = new HashSet<string>();
        int total = 0;
        var regex = new System.Text.RegularExpressions.Regex(
            @"(?<block>[ \t]*namespace\s+[A-Za-z_]\w*\s*\{\s*\[StructLayout[^\]]*\]\s*public unsafe partial struct\s+\w+\s*\{\s*internal\s+[^;]+;\s*\}\s*\})",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (var path in paths)
        {
            var text = File.ReadAllText(path);
            text = regex.Replace(text, m =>
            {
                if (!seen.Add(m.Groups["block"].Value))
                {
                    total++;
                    return "";
                }
                return m.Value;
            });
            File.WriteAllText(path, text);
        }
        Console.WriteLine($"[fix] dedupeStructs={total}");
    }

    public class SignalParamInfo
    {
        public string CppType;
        public string RawType;
        public string Name;
        // QPrivateSignal marker: part of the real C++ signature (needed to disambiguate the
        // member pointer) but carries no data and is not exposed to managed code.
        public bool Private;
    }

    public class SignalInfo
    {
        public string ClassName;
        public string SignalName;
        public string HeaderFile;
        public string ModuleDir;
        public List<SignalParamInfo> Params = new();
    }

    static readonly (string dir, string[] headers)[] SignalModules =
    {
        ("QtCore", new[] { "qobject.h", "qcoreapplication.h", "qeventloop.h", "qfile.h", "qfileinfo.h", "qsettings.h", "qabstractitemmodel.h", "qitemselectionmodel.h", "qsortfilterproxymodel.h", "qstringlistmodel.h", "qprocess.h", "qtimer.h", "qabstractanimation.h", "qpropertyanimation.h" }),
        ("QtGui", new[] { "qevent.h", "qwindow.h", "qscreen.h", "qguiapplication.h", "qvalidator.h", "qstandarditemmodel.h", "qaction.h", "qsyntaxhighlighter.h" }),
        ("QtWidgets", new[] { "qwidget.h", "qmainwindow.h", "qdialog.h", "qframe.h", "qgroupbox.h", "qlabel.h", "qlcdnumber.h", "qlineedit.h", "qtextedit.h", "qplaintextedit.h", "qtextbrowser.h", "qabstractbutton.h", "qpushbutton.h", "qtoolbutton.h", "qradiobutton.h", "qcheckbox.h", "qcommandlinkbutton.h", "qbuttongroup.h", "qabstractspinbox.h", "qspinbox.h", "qabstractslider.h", "qslider.h", "qscrollbar.h", "qdial.h", "qcombobox.h", "qfontcombobox.h", "qdatetimeedit.h", "qkeysequenceedit.h", "qcalendarwidget.h", "qabstractscrollarea.h", "qscrollarea.h", "qtabbar.h", "qtabwidget.h", "qstackedwidget.h", "qsplitter.h", "qdockwidget.h", "qtoolbox.h", "qaction.h", "qmenu.h", "qmenubar.h", "qtoolbar.h", "qstatusbar.h", "qprogressbar.h", "qsizegrip.h", "qrubberband.h", "qmessagebox.h", "qfiledialog.h", "qinputdialog.h", "qcolordialog.h", "qfontdialog.h", "qprogressdialog.h", "qdialogbuttonbox.h", "qabstractitemview.h", "qlistview.h", "qtableview.h", "qtreeview.h", "qheaderview.h", "qlistwidget.h", "qtreewidget.h", "qtablewidget.h", "qsystemtrayicon.h", "qcompleter.h", "qfilesystemmodel.h", "qwizard.h", "qundostack.h" }),
    };

    internal static List<SignalInfo> ScanSignals()
    {
        var result = new List<SignalInfo>();
        var inc = @"D:\QtSharp\Qt\6.11.0\msvc2022_64\include";
        foreach (var (dir, headers) in SignalModules)
        {
            foreach (var header in headers)
            {
                var path = Path.Combine(inc, dir, header);
                if (!File.Exists(path))
                    continue;
                ScanSignalsInFile(path, dir, header, result);
            }
        }
        return result;
    }

    static void ScanSignalsInFile(string path, string dir, string header, List<SignalInfo> result)
    {
        var lines = File.ReadAllLines(path);
        string currentClass = null;
        bool inSignals = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var cm = System.Text.RegularExpressions.Regex.Match(line, @"^class\s+(?:[A-Za-z_][A-Za-z0-9_]*_EXPORT\s+)?([A-Za-z_]\w*)");
            if (cm.Success)
                currentClass = cm.Groups[1].Value;

            if (line == "Q_SIGNALS:" || line == "signals:" || line.StartsWith("Q_SIGNALS:"))
            {
                inSignals = true;
                continue;
            }

            if (inSignals)
            {
                if (line.EndsWith("Q_SLOTS:") || line == "public:" || line == "private:" ||
                    line == "protected:" || line == "};")
                {
                    inSignals = false;
                    continue;
                }

                var sig = TryParseSignal(line, currentClass);
                if (sig != null)
                {
                    sig.HeaderFile = header;
                    sig.ModuleDir = dir;
                    result.Add(sig);
                }
            }
        }
    }

    static SignalInfo TryParseSignal(string line, string className)
    {
        if (className == null || !line.StartsWith("void "))
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^void\s+(\w+)\s*\(([^)]*)\)");
        if (!m.Success)
            return null;
        var info = new SignalInfo { ClassName = className, SignalName = m.Groups[1].Value };
        var paramStr = m.Groups[2].Value.Trim();
        if (paramStr.Length > 0)
        {
            foreach (var p in paramStr.Split(','))
            {
                var pt = p.Trim();
                if (pt.Length == 0)
                    continue;
                var eq = pt.IndexOf('=');
                if (eq >= 0)
                    pt = pt.Substring(0, eq).Trim();
                int sp = pt.LastIndexOf(' ');
                string type, decl;
                if (sp < 0)
                {
                    // Unnamed parameter: the whole token is the type.
                    type = pt.Trim();
                    decl = string.Empty;
                }
                else
                {
                    type = pt.Substring(0, sp).Trim();
                    decl = pt.Substring(sp + 1).Trim();
                }
                string suffix = string.Empty;
                if (decl.StartsWith("&"))
                {
                    suffix = "&";
                    decl = decl.Substring(1).Trim();
                }
                else if (decl.StartsWith("*"))
                {
                    suffix = "*";
                    decl = decl.Substring(1).Trim();
                }
                // Qt declares "private" signals as taking a QPrivateSignal marker parameter;
                // it carries no data and must not appear in the generated signature. It still
                // has to be part of the member pointer type, fully qualified because parameter
                // types of a pointer-to-member are looked up in the enclosing scope.
                bool isPrivate = type.Contains("QPrivateSignal");
                var name = decl;
                if (name.Length == 0)
                    name = "arg" + info.Params.Count;
                info.Params.Add(new SignalParamInfo
                {
                    CppType = type,
                    RawType = isPrivate ? className + "::QPrivateSignal" : type + suffix,
                    Name = name,
                    Private = isPrivate
                });
            }
        }
        return info;
    }

    internal static bool MapSignalType(string cppType, out string csharpType, out string lambdaType, out string cbType, out string toCb, out string fromCb)
    {
        var t = cppType.Replace("const", "").Replace("&", "").Replace(" ", "").Trim();
        switch (t)
        {
            case "bool":
                csharpType = "bool"; lambdaType = "bool"; cbType = "int"; toCb = "{name} ? 1 : 0"; fromCb = "{p} != 0"; return true;
            case "int":
                csharpType = "int"; lambdaType = "int"; cbType = "int"; toCb = "{name}"; fromCb = "{p}"; return true;
            case "short":
                csharpType = "short"; lambdaType = "short"; cbType = "short"; toCb = "{name}"; fromCb = "{p}"; return true;
            case "ushort":
                csharpType = "ushort"; lambdaType = "ushort"; cbType = "unsigned short"; toCb = "{name}"; fromCb = "{p}"; return true;
            case "uint":
                csharpType = "uint"; lambdaType = "uint"; cbType = "unsigned int"; toCb = "{name}"; fromCb = "{p}"; return true;
            case "ulong":
                csharpType = "ulong"; lambdaType = "ulong"; cbType = "unsigned long long"; toCb = "{name}"; fromCb = "{p}"; return true;
            case "float":
                csharpType = "float"; lambdaType = "float"; cbType = "float"; toCb = "{name}"; fromCb = "{p}"; return true;
            case "double":
            case "qreal":
                csharpType = "double"; lambdaType = "double"; cbType = "double"; toCb = "{name}"; fromCb = "{p}"; return true;
            case "qint64":
            case "qlonglong":
            case "longlong":
                csharpType = "long"; lambdaType = "qint64"; cbType = "long long"; toCb = "{name}"; fromCb = "{p}"; return true;
            case "QString":
                csharpType = "string"; lambdaType = "QString"; cbType = "const ushort*"; toCb = "{name}.utf16()"; fromCb = "Marshal.PtrToStringUni({p})"; return true;
            case "QStringList":
                csharpType = "string[]"; lambdaType = "QStringList"; cbType = "void*"; toCb = "&{name}"; fromCb = "QtSharp.QStringListMarshaller.ToManaged({p})"; return true;
            default:
                csharpType = lambdaType = cbType = toCb = fromCb = null;
                return false;
        }
    }

    static void GenerateSignals()
    {
        var signals = ScanSignals();

        // collect actually-generated classes (skip force-ignored / pruned ones like QHeaderView)
        var generatedClasses = new HashSet<string>();
        foreach (var f in new[] { "QtSharp.Core.cs", "QtSharp.Gui.cs", "QtSharp.Widgets.cs" })
        {
            var path = @"D:\QtSharp\out\qt_min\" + f;
            if (!File.Exists(path))
                continue;
            foreach (var line in File.ReadLines(path))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"class\s+(\w+)\s*:");
                if (m.Success)
                    generatedClasses.Add(m.Groups[1].Value);
            }
        }

        var supported = new List<SignalInfo>();
        foreach (var s in signals)
        {
            if (!generatedClasses.Contains(s.ClassName))
                continue;
            bool ok = true;
            foreach (var p in s.Params)
                if (!p.Private && !MapSignalType(p.CppType, out _, out _, out _, out _, out _))
                    ok = false;
            if (ok)
                supported.Add(s);
        }
        Console.WriteLine($"[signals] scanned {signals.Count}, supported {supported.Count}");

        var overloaded = new HashSet<string>(signals
            .GroupBy(s => s.ClassName + "::" + s.SignalName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key));

        GenerateSignalsGlue(supported, overloaded);
        GenerateSignalsCSharp(supported);
    }

    static void GenerateSignalsGlue(List<SignalInfo> signals, HashSet<string> overloaded)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated signal glue.");
        sb.AppendLine("#include <qobject.h>");
        sb.AppendLine("#include <qstring.h>");
        sb.AppendLine("#include <qstringlist.h>");
        var includes = new HashSet<string>();
        foreach (var s in signals)
            includes.Add("#include <" + s.HeaderFile + ">");
        foreach (var inc in includes.OrderBy(x => x))
            sb.AppendLine(inc);

        foreach (var s in signals.OrderBy(x => x.ClassName).ThenBy(x => x.SignalName))
        {
            var signalKey = s.ClassName + "::" + s.SignalName;
            bool isOverloaded = overloaded.Contains(signalKey);
            // QPrivateSignal marker parameters cannot be named outside the class, so an
            // overloaded private signal cannot be disambiguated; skip it.
            if (isOverloaded && s.Params.Any(p => p.Private))
                continue;
            var lambdaParams = new List<string>();
            var cbArgs = new List<string>();
            var cbTypes = new List<string>();
            for (int i = 0; i < s.Params.Count; i++)
            {
                var p = s.Params[i];
                if (p.Private)
                    continue;
                MapSignalType(p.CppType, out _, out var lambdaType, out var cbType, out var toCb, out _);
                lambdaParams.Add(lambdaType + " " + p.Name);
                cbArgs.Add(toCb.Replace("{name}", p.Name));
                cbTypes.Add(cbType);
            }
            var fn = $"qs_{s.ClassName}_connect_{s.SignalName}";
            sb.Append($"extern \"C\" __declspec(dllexport) void {fn}(void* obj, void (*cb)(void* ud");
            for (int i = 0; i < cbTypes.Count; i++)
                sb.Append(", " + cbTypes[i] + " a" + i);
            sb.Append("), void* ud) {\n");
            var signalPointer = isOverloaded
                ? $"static_cast<void ({s.ClassName}::*)({string.Join(", ", s.Params.Select(p => p.RawType))})>(&{s.ClassName}::{s.SignalName})"
                : $"&{s.ClassName}::{s.SignalName}";
            sb.Append($"    QObject::connect(({s.ClassName}*)obj, {signalPointer}, [cb, ud](");
            sb.Append(string.Join(", ", lambdaParams));
            sb.Append(") {\n");
            sb.Append("        cb(ud");
            for (int i = 0; i < cbArgs.Count; i++)
                sb.Append(", " + cbArgs[i]);
            sb.Append(");\n");
            sb.Append("    });\n");
            sb.Append("}\n\n");
        }

        Directory.CreateDirectory(@"D:\QtSharp\native");
        File.WriteAllText(@"D:\QtSharp\native\QtSharp_signals.cpp", sb.ToString());
        Console.WriteLine($"[signals] wrote native/QtSharp_signals.cpp");
    }

    static void GenerateSignalsCSharp(List<SignalInfo> signals)
    {
        var byClass = signals.GroupBy(s => s.ClassName).OrderBy(g => g.Key).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated signal bindings (PyQt-style .XxxSignal.Connect(...)).");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using QtSharp.QtCore;");
        sb.AppendLine("using QtSharp.QtGui;");
        sb.AppendLine("using QtSharp.QtWidgets;");
        sb.AppendLine();

        // SignalHost static class: delegates + P/Invoke + Connect methods
        sb.AppendLine("public static class SignalHost");
        sb.AppendLine("{");
        sb.AppendLine("    static readonly List<GCHandle> __handles = new();");
        sb.AppendLine("    static readonly List<Delegate> __delegates = new();");
        sb.AppendLine();

        foreach (var g in byClass)
        {
            foreach (var s in g.OrderBy(x => x.SignalName))
            {
                var csTypes = new List<string>();
                var cbTypes = new List<string>();
                var fromCbs = new List<string>();
                for (int i = 0; i < s.Params.Count; i++)
                {
                    var p = s.Params[i];
                    if (p.Private)
                        continue;
                    MapSignalType(p.CppType, out var cs, out _, out var cb, out _, out var fromCb);
                    csTypes.Add(cs);
                    cbTypes.Add((cb == "const ushort*" || cb == "void*") ? "IntPtr" : cb);
                    fromCbs.Add(fromCb.Replace("{p}", "p" + i));
                }
                var csSig = csTypes.Count == 0 ? "" : string.Join(", ", csTypes);
                var delegateParams = string.Join(", ", cbTypes.Select((t, i) => t + " p" + i));
                var actionType = csSig.Length > 0 ? "Action<" + csSig + ">" : "Action";

                sb.AppendLine($"    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]");
                sb.AppendLine($"    delegate void {s.ClassName}_{s.SignalName}__cb(IntPtr ud{(delegateParams.Length > 0 ? ", " + delegateParams : "")});");
                sb.AppendLine();
                sb.AppendLine($"    [DllImport(\"QtSharp\", EntryPoint = \"qs_{s.ClassName}_connect_{s.SignalName}\", CallingConvention = CallingConvention.Cdecl)]");
                sb.AppendLine($"    static extern void {s.ClassName}_{s.SignalName}__native(IntPtr obj, {s.ClassName}_{s.SignalName}__cb cb, IntPtr ud);");
                sb.AppendLine();
                sb.AppendLine($"    public static void Connect_{s.SignalName}({s.ClassName} obj, {actionType} handler)");
                sb.AppendLine("    {");
                sb.AppendLine("        var handle = GCHandle.Alloc(handler);");
                sb.AppendLine($"        var cb = new {s.ClassName}_{s.SignalName}__cb((ud{string.Concat(cbTypes.Select((t, i) => ", p" + i))}) =>");
                sb.AppendLine("        {");
                sb.AppendLine($"            var target = ({actionType})GCHandle.FromIntPtr(ud).Target;");
                sb.Append("            target(");
                sb.Append(string.Join(", ", fromCbs));
                sb.AppendLine(");");
                sb.AppendLine("        });");
                sb.AppendLine("        __handles.Add(handle);");
                sb.AppendLine("        __delegates.Add(cb);");
                sb.AppendLine($"        {s.ClassName}_{s.SignalName}__native(obj.__Instance, cb, GCHandle.ToIntPtr(handle));");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // partial class signal properties
        foreach (var g in byClass)
        {
            foreach (var s in g.OrderBy(x => x.SignalName))
            {
                var csTypes = new List<string>();
                foreach (var p in s.Params)
                {
                    if (p.Private)
                        continue;
                    MapSignalType(p.CppType, out var cs, out _, out _, out _, out _);
                    csTypes.Add(cs);
                }
                var csSig = csTypes.Count == 0 ? "" : string.Join(", ", csTypes);
                var actionType = csSig.Length > 0 ? "Action<" + csSig + ">" : "Action";
                var ns = ModuleNamespace(s.ModuleDir);
                sb.AppendLine($"namespace {ns} {{ public partial class {s.ClassName} {{");
                sb.AppendLine($"    public global::QSignal<{actionType}> {Capitalize(s.SignalName)}Signal => new global::QSignal<{actionType}>(h => global::SignalHost.Connect_{s.SignalName}(this, h));");
                sb.AppendLine("} }");
            }
        }

        File.WriteAllText(@"D:\QtSharp\testbind\GeneratedSignals.cs", sb.ToString());
        Console.WriteLine($"[signals] wrote testbind/GeneratedSignals.cs");
    }

    static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    static string ModuleNamespace(string dir) => dir switch
    {
        "QtCore" => "QtSharp.QtCore",
        "QtGui" => "QtSharp.QtGui",
        _ => "QtSharp.QtWidgets",
    };

    static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "codegen")
            {
                ApiCodeGen.Run(args[1], args[2]);
                return 0;
            }

            if (args.Length > 0 && args[0] == "extract")
                QtLibrary.ExtractModelPath = args[1];

            bool ok = ConsoleDriver.Run(new QtLibrary());
            if (ok)
            {
                GenerateSignals();
                var files = new[] { "QtSharp.Core.cs", "QtSharp.Gui.cs", "QtSharp.Widgets.cs" };
                foreach (var f in files)
                {
                    var path = @"D:\QtSharp\out\qt_min\" + f;
                    if (File.Exists(path))
                    {
                        FixDtorHooks(path);
                        FixDuplicateFields(path);
                    }
                }
                DedupeStructs(files.Select(f => @"D:\QtSharp\out\qt_min\" + f).ToArray());
            }
            Console.WriteLine(ok ? "[OK] Qt scope-limited probe completed." : "[FAIL] Qt probe returned false.");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[EXCEPTION] " + ex);
            return 1;
        }
    }
}
