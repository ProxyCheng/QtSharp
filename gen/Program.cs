using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Where Qt's headers live. The CppSharp driver that used to sit here - and the
// `extract` / `dumpopts` commands that drove it - were removed once the libclang front end
// (see ApiModel2.cs) had reproduced the model byte for byte.
static class QtLibrary
{
    /// <summary>
    /// The Qt kit the model is extracted from: <c>&lt;repo&gt;\Qt\6.11.0\msvc2022_64\include</c> by
    /// default, so a clone laid out like this repository just works wherever it lives. A different
    /// installation (or version) is pointed at with QTSHARP_QT_INCLUDE - and ApiModel.QtVersion has
    /// to match the kit, since the version is what the model records.
    /// </summary>
    internal static string QtIncludeRoot
        => Environment.GetEnvironmentVariable("QTSHARP_QT_INCLUDE")
           ?? Path.Combine(RepoRoot, "Qt", ApiModel.QtVersion, "msvc2022_64", "include");

    /// <summary>
    /// The repository root, found by walking up from the generator's own folder to the solution
    /// file. The generator runs from gen\bin\Release\net10.0\win-x64, so the source tree (and the
    /// Qt kit and clang headers inside it) is a handful of levels up - and a published or copied
    /// generator still finds the tree it was built from.
    /// </summary>
    internal static string RepoRoot
    {
        get
        {
            if (repoRoot == null)
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "QtSharp.slnx")))
                    dir = dir.Parent;
                repoRoot = dir?.FullName;
            }
            return repoRoot;
        }
    }

    static string repoRoot;
}

class Program
{
    public class SignalParamInfo
    {
        public string CppType;
        public string RawType;
        public string Name;
        // QPrivateSignal marker: part of the real C++ signature (needed to disambiguate the
        // member pointer) but carries no data and is not exposed to managed code.
        public bool Private;
        // The model type of the parameter (QWidget*, QModelIndex, QString, int), which the
        // meta-object signature is spelled from too. Set by the clang extractor, which reads the
        // signal out of the AST; the legacy extractor leaves it null and WriteSignals falls back
        // to the old scalar-only mapping.
        public string ModelType;
    }

    public class SignalInfo
    {
        public string ClassName;
        public string SignalName;
        public string HeaderFile;
        public string ModuleDir;
        public List<SignalParamInfo> Params = new();
    }

    // One Q_PROPERTY declaration: the Qt name plus the accessors it is sugar over.
    public class PropertyInfo
    {
        public string ClassName;
        public string Name;
        public string Getter;
        public string Setter;
        public string Notify;
        public string HeaderFile;
        public string ModuleDir;
    }

    // Q_PROPERTY(Type name READ getter WRITE setter NOTIFY signal ...) is scanned out of the
    // header text, like the signals: the declaration can span several lines and Qt's own metadata
    // is not visible to the model at this point. The scan is a pure function of the header.
    internal static List<PropertyInfo> ScanProperties(string dir, string header)
    {
        var result = new List<PropertyInfo>();
        var path = Path.Combine(QtLibrary.QtIncludeRoot, dir, header);
        if (!File.Exists(path))
            return result;
        var text = File.ReadAllText(path);
        const string macro = "Q_PROPERTY(";
        int search = 0;
        while (true)
        {
            int at = text.IndexOf(macro, search, StringComparison.Ordinal);
            if (at < 0)
                break;
            search = at + 1;

            // Balance the parentheses: a declaration may carry nested parentheses (default values)
            // and run over several lines.
            int depth = 0, end = -1;
            for (int i = at + macro.Length - 1; i < text.Length; i++)
            {
                if (text[i] == '(')
                    depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }
            if (end < 0)
                continue;

            var className = EnclosingClass(text, at);
            if (className == null)
                continue;
            var body = text.Substring(at + macro.Length, end - at - macro.Length);
            var tokens = body.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;
            // The type may be spelled with a detached star (QAbstractItemModel *sourceModel), and
            // then the pointer sign is glued to the name.
            var info = new PropertyInfo
            {
                ClassName = className,
                Name = tokens[1].TrimStart('*', '&'),
            };
            for (int i = 2; i + 1 < tokens.Length; i++)
            {
                switch (tokens[i])
                {
                    case "READ": info.Getter = tokens[i + 1]; break;
                    case "WRITE": info.Setter = tokens[i + 1]; break;
                    case "NOTIFY": info.Notify = tokens[i + 1]; break;
                }
            }
            info.HeaderFile = header;
            info.ModuleDir = dir;
            result.Add(info);
        }
        return result;
    }

    // The class a Q_PROPERTY belongs to: the nearest class declaration before it. Qt always
    // declares a class's properties inside its body.
    static string EnclosingClass(string text, int position)
    {
        string found = null;
        var regex = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:class|struct)\s+(?:[A-Za-z_][A-Za-z0-9_]*_EXPORT\s+)?([A-Za-z_]\w*)",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (System.Text.RegularExpressions.Match m in regex.Matches(text))
        {
            if (m.Index >= position)
                break;
            found = m.Groups[1].Value;
        }
        return found;
    }

    internal static List<SignalInfo> ScanSignals(string dir, string header)
    {
        var result = new List<SignalInfo>();
        var path = Path.Combine(QtLibrary.QtIncludeRoot, dir, header);
        if (File.Exists(path))
            ScanSignalsInFile(path, dir, header, result);
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

    static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "codegen")
            {
                ApiCodeGen.Run(args[1], args[2]);
                return 0;
            }

            // The model, read out of libclang (ClangSharp). This is the only front end.
            if (args.Length > 0 && args[0] == "extract2")
                return ApiModel.RunExtract2(args[1]);

            Console.WriteLine("usage: qtgen extract2 <model.json> | codegen <model.json> <outDir>");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[EXCEPTION] " + ex);
            return 1;
        }
    }
}
