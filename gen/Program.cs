using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Where Qt's headers live. The CppSharp driver that used to sit here - and the
// `extract` / `dumpopts` commands that drove it - were removed once the libclang front end
// (see ClangExtractor.cs) had reproduced the model byte for byte.
static class QtLibrary
{
    /// <summary>
    /// The Qt kit the model is extracted from: <c>&lt;repo&gt;\Qt\6.12.0\msvc2022_64\include</c> by
    /// default, so a clone laid out like this repository just works wherever it lives. A different
    /// installation (or version) is pointed at with QTSHARP_QT_INCLUDE - and ApiModel.QtVersion has
    /// to match the kit, since the version is what the model records.
    /// </summary>
    internal static string QtIncludeRoot
        => Environment.GetEnvironmentVariable("QTSHARP_QT_INCLUDE")
           ?? Path.Combine(RepoRoot, "Qt", ApiModel.QtVersion, HostPlatform.DefaultKitName, "include");

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

    // One declaration moc registers in the meta-object: a method inside a Q_SLOTS/slots block, one
    // prefixed with the singular Q_SLOT, or one prefixed Q_INVOKABLE. The last is Qt's "method"
    // (QMetaMethod::Method) rather than a slot (QMetaMethod::Slot), which is why the flag is kept.
    public class SlotInfo
    {
        public string ClassName;
        public string Name;
        public int Arity;
        public bool Invokable;
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
        var text = UnwrapQtVersionMacros(File.ReadAllText(path));
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
            // The type may contain spaces of its own (QList<QSpriteSheetItem *>) and a detached
            // star may follow it (QAbstractItemModel *sourceModel), so the name is what comes
            // after the complete type, not the second whitespace-delimited token.
            // Q_PROPERTY( bool hasTopEndcap ...) - the leading space after the parenthesis is
            // legal (Qt3D writes several that way), so skip it before reading the type.
            int pos = 0;
            while (pos < body.Length && char.IsWhiteSpace(body[pos]))
                pos++;
            int angle = 0;
            while (pos < body.Length)
            {
                char c = body[pos];
                if (c == '<') angle++;
                else if (c == '>') angle--;
                else if (char.IsWhiteSpace(c) && angle == 0) break;
                pos++;
            }
            while (pos < body.Length && (char.IsWhiteSpace(body[pos]) || body[pos] == '*' || body[pos] == '&'))
                pos++;
            int nameStart = pos;
            while (pos < body.Length && !char.IsWhiteSpace(body[pos]))
                pos++;
            if (nameStart >= pos)
                continue;
            var info = new PropertyInfo
            {
                ClassName = className,
                Name = body.Substring(nameStart, pos - nameStart).TrimStart('*', '&'),
            };
            // Everything after the name is the attribute list (READ/WRITE/NOTIFY/...).
            var attributes = body.Substring(pos)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < attributes.Length; i++)
            {
                switch (attributes[i])
                {
                    case "READ": info.Getter = attributes[i + 1]; break;
                    case "WRITE": info.Setter = attributes[i + 1]; break;
                    case "NOTIFY": info.Notify = attributes[i + 1]; break;
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

    /// <summary>
    /// Qt 6.12 spells a declaration that differs between Qt 6 and Qt 7 as <c>QT6_ONLY(...)</c> /
    /// <c>QT7_ONLY(...)</c>. Both scanners below read the raw header text, so the wrapper has to be
    /// unwrapped before a declaration is recognizable: QT6_ONLY keeps its contents (this binding is
    /// built for Qt 6), QT7_ONLY is dropped. Preprocessor directives are copied untouched - that is
    /// where the macros themselves are defined (qtversionchecks.h).
    /// </summary>
    static string UnwrapQtVersionMacros(string text)
    {
        var pattern = new System.Text.RegularExpressions.Regex(@"(?<![A-Za-z0-9_])(QT[67]_ONLY)\s*\(");
        var sb = new System.Text.StringBuilder(text.Length);
        int copied = 0;
        foreach (System.Text.RegularExpressions.Match m in pattern.Matches(text))
        {
            // A match inside an already unwrapped block was handled by the recursive call below.
            if (m.Index < copied || StartsPreprocessorLine(text, m.Index))
                continue;
            int open = text.IndexOf('(', m.Index);
            int depth = 0, end = -1;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
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
            sb.Append(text, copied, m.Index - copied);
            if (m.Groups[1].Value == "QT6_ONLY")
                sb.Append(UnwrapQtVersionMacros(text.Substring(open + 1, end - open - 1)));
            copied = end + 1;
        }
        sb.Append(text, copied, text.Length - copied);
        return sb.ToString();
    }

    static bool StartsPreprocessorLine(string text, int index)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        for (int i = lineStart; i < index; i++)
            if (!char.IsWhiteSpace(text[i]))
                return text[i] == '#';
        return false;
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
        var text = UnwrapQtVersionMacros(File.ReadAllText(path));
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
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

    /// <summary>
    /// Strips the annotations Qt puts *before* the return type of a signal, as in
    /// <code>
    ///     Q_REVISION(6, 2) void attachedToChartChanged(bool attachedToChart);
    /// </code>
    /// Without this the signal does not look like a declaration at all, and the AST side then picks
    /// it up as an ordinary method: it can be called, but it can neither be connected nor emitted.
    /// Only all-caps macros are stripped, so a declaration that happens to start with something
    /// call-like is left alone.
    /// </summary>
    static string StripLeadingAnnotations(string line)
    {
        var text = line.TrimStart();
        while (true)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^[A-Za-z_][A-Za-z0-9_]*\s*\(");
            if (!m.Success)
                return text;
            var name = m.Value.TrimEnd('(', ' ', '\t');
            if (name != name.ToUpperInvariant())
                return text;
            int depth = 0, i = m.Length - 1;
            for (; i < text.Length; i++)
            {
                if (text[i] == '(')
                    depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        i++;
                        break;
                    }
                }
            }
            if (depth != 0)
                return text;
            text = text.Substring(i).TrimStart();
        }
    }

    static SignalInfo TryParseSignal(string line, string className)
    {
        line = StripLeadingAnnotations(line);
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

    /// <summary>
    /// The declarations moc registers as slots or Q_INVOKABLE methods, scanned out of the header
    /// text like the signals: <c>Q_SLOTS</c>/<c>slots</c> expands to an annotation clang's AST does
    /// not distinguish, and <c>Q_SLOT</c>/<c>Q_INVOKABLE</c> are per-declaration markers that leave
    /// no cursor behind either. What is recorded is the class, the method name, its parameter count
    /// and whether Qt published it as an invokable (QMetaMethod::Method) or a slot
    /// (QMetaMethod::Slot); the AST supplies the parameter types, matched by name and arity.
    /// </summary>
    internal static List<SlotInfo> ScanSlots(string dir, string header)
    {
        var result = new List<SlotInfo>();
        var path = Path.Combine(QtLibrary.QtIncludeRoot, dir, header);
        if (File.Exists(path))
            ScanSlotsInFile(path, dir, header, result);
        return result;
    }

    // The exact access-specifier spellings Qt uses: "Q_SLOTS:" or "slots:" alone, and whichever of
    // the three access specifiers precedes them (public/protected/private). Every one of these is
    // in the bound headers; the closed set keeps a stray member name from opening a block.
    static bool IsSlotBlockStart(string line)
        => System.Text.RegularExpressions.Regex.IsMatch(line,
            @"^(?:(?:public|protected|private)\s+)?(?:Q_SLOTS|slots)\s*:$");

    // A slot block ends at the next access specifier, at a signals block, or at the end of the
    // class body - the same boundaries the signal scanner uses.
    static bool IsSlotBlockEnd(string line)
        => line == "};"
           || System.Text.RegularExpressions.Regex.IsMatch(line,
               @"^(?:public|protected|private)\s*:$")
           || System.Text.RegularExpressions.Regex.IsMatch(line,
               @"^(?:(?:public|protected|private)\s+)?(?:Q_SIGNALS|signals)\s*:$");

    static void ScanSlotsInFile(string path, string dir, string header, List<SlotInfo> result)
    {
        var text = UnwrapQtVersionMacros(File.ReadAllText(path));
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string currentClass = null;
        bool inSlots = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var cm = System.Text.RegularExpressions.Regex.Match(line, @"^class\s+(?:[A-Za-z_][A-Za-z0-9_]*_EXPORT\s+)?([A-Za-z_]\w*)");
            if (cm.Success)
                currentClass = cm.Groups[1].Value;

            if (IsSlotBlockStart(line))
            {
                inSlots = true;
                continue;
            }
            if (inSlots && IsSlotBlockEnd(line))
            {
                inSlots = false;
                continue;
            }
            if (currentClass == null || line.Length == 0 || line[0] == '#' ||
                line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("/*", StringComparison.Ordinal))
                continue;

            // Q_SLOT/Q_INVOKABLE can precede the return type, possibly after another annotation
            // such as Q_REVISION(2, 9). Strip them all; remember whether one of the two was there.
            var declaration = StripSlotAnnotations(line, out bool annotated, out bool invokable);
            if (!inSlots && !annotated)
                continue;

            // A declaration may wrap onto the next line (qsceneloader.h's component()), so keep
            // appending while the parameter list is still open.
            while (ParenBalance(declaration) > 0 && i + 1 < lines.Length)
            {
                i++;
                declaration += " " + lines[i].Trim();
            }

            if (!TryParseSlotDeclaration(declaration, out var name, out int arity))
                continue;
            result.Add(new SlotInfo
            {
                ClassName = currentClass,
                Name = name,
                // A declaration Q_INVOKABLE marks stays an invokable even inside a slots block:
                // that is the flag Qt itself uses (QMetaMethod::Method, not ::Slot).
                Invokable = invokable,
                Arity = arity,
                HeaderFile = header,
                ModuleDir = dir,
            });
        }
    }

    /// <summary>
    /// Strips the all-caps macros a declaration may start with (<c>Q_REVISION(2, 9) Q_INVOKABLE</c>,
    /// <c>Q_SLOT</c>) and reports whether <c>Q_SLOT</c> or <c>Q_INVOKABLE</c> was among them. The
    /// all-caps test is the same guard StripLeadingAnnotations uses, so a declaration that starts
    /// with a type (QRect, QVariant) is left alone.
    /// </summary>
    static string StripSlotAnnotations(string line, out bool slotAnnotation, out bool invokable)
    {
        slotAnnotation = false;
        invokable = false;
        var text = line.TrimStart();
        while (true)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^([A-Za-z_][A-Za-z0-9_]*)\s*");
            if (!m.Success)
                return text;
            var name = m.Groups[1].Value;
            if (name != name.ToUpperInvariant())
                return text;
            int end = m.Length;
            // An annotation may take arguments (Q_REVISION(2, 9)); balance them like the scanner
            // above balances a parameter list.
            if (end < text.Length && text[end] == '(')
            {
                int depth = 0, i = end;
                for (; i < text.Length; i++)
                {
                    if (text[i] == '(')
                        depth++;
                    else if (text[i] == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            i++;
                            break;
                        }
                    }
                }
                if (depth != 0)
                    return text;
                end = i;
            }
            if (name == "Q_SLOT" || name == "Q_INVOKABLE")
                slotAnnotation = true;
            if (name == "Q_INVOKABLE")
                invokable = true;
            text = text.Substring(end).TrimStart();
        }
    }

    // The open parenthesis count of a declaration; a wrapped declaration has not closed its
    // parameter list yet.
    static int ParenBalance(string text)
    {
        int depth = 0;
        foreach (char c in text)
        {
            if (c == '(')
                depth++;
            else if (c == ')' && depth > 0)
                depth--;
        }
        return depth;
    }

    // The method name and parameter count of a declaration. The name is the identifier before the
    // first top-level '(', and the count splits the list on top-level commas only, so a default
    // argument (QRect(QPoint(0, 0), QSize(-1, -1))) and a template (QMap<QString, int>) each
    // contribute one parameter.
    static bool TryParseSlotDeclaration(string line, out string name, out int arity)
    {
        name = null;
        arity = 0;
        int angle = 0, open = -1;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '<')
                angle++;
            else if (c == '>')
            {
                if (angle > 0)
                    angle--;
            }
            else if (c == '(' && angle == 0)
            {
                open = i;
                break;
            }
            else if (c == ';' || c == '{' || c == '}')
                return false;
        }
        if (open < 0)
            return false;
        int end = open - 1;
        while (end >= 0 && char.IsWhiteSpace(line[end]))
            end--;
        int start = end;
        while (start >= 0 && (char.IsLetterOrDigit(line[start]) || line[start] == '_'))
            start--;
        if (start == end)
            return false;
        name = line.Substring(start + 1, end - start);

        int depth = 0, close = -1;
        for (int i = open; i < line.Length; i++)
        {
            if (line[i] == '(')
                depth++;
            else if (line[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
        }
        if (close < 0)
            return false;
        var body = line.Substring(open + 1, close - open - 1).Trim();
        if (body.Length == 0)
            return true;
        arity = 1;
        depth = 0;
        angle = 0;
        foreach (char c in body)
        {
            if (c == '(' || c == '[' || c == '{')
                depth++;
            else if (c == ')' || c == ']' || c == '}')
            {
                if (depth > 0)
                    depth--;
            }
            else if (c == '<')
                angle++;
            else if (c == '>')
            {
                if (angle > 0)
                    angle--;
            }
            else if (c == ',' && depth == 0 && angle == 0)
                arity++;
        }
        return true;
    }

        const string Usage =
            "usage: qtgen extract2 <model.json> [--module <name>]\n" +
            "       qtgen codegen  <model.json> <outDir> [--module <name>]\n" +
            "       qtgen split-core <model.json> <outDir>   (one base model per Qt module)\n" +
            "       qtgen module   <name>            (descriptor, for the build scripts)\n" +
            "\n" +
            "  --module <name>  bind one module from the registry (gen/ApiModel.Modules.cs): its\n" +
            "                   classes only, in its own namespace/assembly and against its native\n" +
            "                   library, with the modules it depends on available for the types it\n" +
            "                   derives from. Without --module, codegen emits the combined base model's\n" +
            "                   native glue (--native-only) or its whole C# tree.";

        static int Main(string[] args)
        {
            try
            {
                // --module may appear anywhere after the command.
                string moduleName = null;
                bool nativeOnly = false;
                var positional = new List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--native-only")
                    {
                        // The combined base model only produces the shared native glue; the ten
                        // base assemblies come from the split models.
                        nativeOnly = true;
                        continue;
                    }
                    if (args[i] == "--module")
                    {
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine("--module needs a module name\n\n" + Usage);
                            return 1;
                        }
                        moduleName = args[++i];
                    }
                    else
                    {
                        positional.Add(args[i]);
                    }
                }
                var module = moduleName == null ? null : ApiModel.FindExtensionModule(moduleName);

                if (positional.Count >= 3 && positional[0] == "codegen")
                {
                    ApiCodeGen.Run(positional[1], positional[2], module, nativeOnly);
                    return 0;
                }

                // The module descriptor as plain text, so a build script does not have to repeat
                // what the generator already knows (tools/build-native-module.ps1 reads this).
                if (positional.Count >= 2 && positional[0] == "module")
                {
                    var m = ApiModel.FindExtensionModule(positional[1]);
                    Console.WriteLine("name=" + m.Name);
                    Console.WriteLine("namespace=" + m.Namespace);
                    Console.WriteLine("nativeLibrary=" + m.NativeLibrary);
                    Console.WriteLine("qtModules=" + string.Join(",", m.QtModules));
                    Console.WriteLine("qtDefines=" + string.Join(",", m.QtDefines));
                    Console.WriteLine("qtLibs=" + string.Join(",", m.QtLibs));
                    Console.WriteLine("dependsOn=" + string.Join(",", m.DependsOn));
                    Console.WriteLine("classes=" + m.Roots.Length);
                    return 0;
                }

                // The model, read out of libclang (ClangSharp). This is the only front end.
                if (positional.Count >= 2 && positional[0] == "extract2")
                    return ApiModel.RunExtract2(positional[1], module);

                // Split the combined base model into one model per Qt module.
                if (positional.Count >= 3 && positional[0] == "split-core")
                    return ApiModel.RunSplitCore(positional[1], positional[2]);

                Console.WriteLine(Usage);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[EXCEPTION] " + ex);
                return 1;
            }
        }
}
