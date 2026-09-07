using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        var module = options.AddModule("QtSharp");
        module.OutputNamespace = "QtSharp";

        string inc = @"D:\QtSharp\Qt\6.11.0\msvc2022_64\include";
        module.IncludeDirs.Add(inc);
        module.IncludeDirs.Add(Path.Combine(inc, "QtCore"));
        module.IncludeDirs.Add(Path.Combine(inc, "QtGui"));
        module.IncludeDirs.Add(Path.Combine(inc, "QtWidgets"));

        module.Defines.Add("QT_NO_DEBUG");
        module.Defines.Add("QT_CORE_LIB");
        module.Defines.Add("QT_GUI_LIB");
        module.Defines.Add("QT_WIDGETS_LIB");

        module.Headers.Add("qapplication.h");
        module.Headers.Add("qwidget.h");
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
    }

    static readonly string[] RootNames = { "QApplication", "QWidget" };

    static readonly string[] MethodWhitelist = {
        "exec", "show", "setWindowTitle", "resize", "quit", "hide", "close"
    };

    static readonly string[] ForceIgnoreNames = {
        "QPartialOrdering", "QStrongOrdering", "QWeakOrdering",
        "QTotallyOrderedWrapper",
        "PartialOrdering", "WeakOrdering", "StrongOrdering"
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

            foreach (var m in c.Methods)
                if (m.IsConstructor || MethodWhitelist.Contains(m.OriginalName))
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

    static bool IsMyModule(TranslationUnit tu) => tu.Module?.LibraryName == "QtSharp";

    static void CollectRoots(Namespace ns, Queue<Class> queue, HashSet<Declaration> reachable)
    {
        foreach (var c in ns.Classes)
            if (RootNames.Contains(c.OriginalName))
                AddReachable(c, reachable, queue);
        foreach (var sub in ns.Namespaces)
            CollectRoots(sub, queue, reachable);
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
                    if (!m.IsConstructor && !m.IsDestructor && !MethodWhitelist.Contains(m.OriginalName))
                        m.ExplicitlyIgnore();
                foreach (var p in c.Properties)
                    p.ExplicitlyIgnore();
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
    static int Main(string[] args)
    {
        try
        {
            bool ok = ConsoleDriver.Run(new QtLibrary());
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
