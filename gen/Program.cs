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
        options.GetClassGenerationOptions = _ => new ClassGenerationOptions { GenerateNativeToManaged = false };
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
    }

    public void SetupPasses(Driver driver) { }

    public void Preprocess(Driver driver, ASTContext ctx)
    {
        LimitScope(ctx);
    }

    public void Postprocess(Driver driver, ASTContext ctx)
    {
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
        "QString", "QStringList",
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
        "QAbstractItemModel", "QStandardItemModel", "QStyledItemDelegate"
    };

    static readonly string[] MethodWhitelist = {
        "exec", "show", "setWindowTitle", "resize", "quit", "hide", "close"
    };

    static readonly string[] ForceIgnoreNames = {
        "QPartialOrdering", "QStrongOrdering", "QWeakOrdering",
        "QTotallyOrderedWrapper",
        "PartialOrdering", "WeakOrdering", "StrongOrdering",
        "QMetaObject", "QHashPrivate", "QHash", "QMultiHash", "QVariant",
        "QHeaderView", "QWidgetItemData", "QStackedLayout"
    };

    static readonly string[] ForceIgnoreMethodNames = {
        "QChar::fromUcs4",
        "QTableWidget::insertColumn",
        "QDir::rmpath"
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
                    if (rt is TemplateSpecializationType ts && ts.Template?.TemplatedDecl?.OriginalName == "QFlags")
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

    static int Main(string[] args)
    {
        try
        {
            bool ok = ConsoleDriver.Run(new QtLibrary());
            if (ok)
            {
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
