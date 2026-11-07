using System;
using System.Collections.Generic;

namespace QtSharp.Generator
{
    /// <summary>
    /// The module dependency graph and the small amount of module metadata the code generator
    /// needs: managed namespace, whether it is a base module, its C++ namespaces, its Qt include
    /// module. It is data, like the extractor's registry; adding a module is a row here.
    /// </summary>
    public static class ModuleCatalog
    {
        private sealed record Entry(
            string Namespace, bool IsCore, string[] CppNamespaces, string[] DependsOn,
            string NativeLibrary, string QtModule);

        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
        {
            ["qtcore"] = new("QtSharp.QtCore", true, new[] { "Qt" }, Array.Empty<string>(),
                "QtSharp.Api", "QtCore"),
            ["qtgui"] = new("QtSharp.QtGui", true, Array.Empty<string>(), new[] { "qtcore" },
                "QtSharp.Api", "QtGui"),
            ["qtwidgets"] = new("QtSharp.QtWidgets", true, Array.Empty<string>(),
                new[] { "qtgui", "qtcore" }, "QtSharp.Api", "QtWidgets"),
            ["qtnetwork"] = new("QtSharp.QtNetwork", true, Array.Empty<string>(),
                new[] { "qtcore" }, "QtSharp.Api", "QtNetwork"),
            ["qtsvg"] = new("QtSharp.QtSvg", true, Array.Empty<string>(),
                new[] { "qtgui", "qtcore" }, "QtSharp.Api", "QtSvg"),
            ["qtsvgwidgets"] = new("QtSharp.QtSvgWidgets", true, Array.Empty<string>(),
                new[] { "qtsvg", "qtwidgets", "qtgui", "qtcore" }, "QtSharp.Api", "QtSvgWidgets"),
            ["qtprintsupport"] = new("QtSharp.QtPrintSupport", true, Array.Empty<string>(),
                new[] { "qtwidgets", "qtgui", "qtcore" }, "QtSharp.Api", "QtPrintSupport"),
            ["qtxml"] = new("QtSharp.QtXml", true, Array.Empty<string>(),
                new[] { "qtcore" }, "QtSharp.Api", "QtXml"),
            ["qtopengl"] = new("QtSharp.QtOpenGL", true, Array.Empty<string>(),
                new[] { "qtgui", "qtcore" }, "QtSharp.Api", "QtOpenGL"),
            ["qtopenglwidgets"] = new("QtSharp.QtOpenGLWidgets", true, Array.Empty<string>(),
                new[] { "qtopengl", "qtwidgets", "qtgui", "qtcore" }, "QtSharp.Api", "QtOpenGLWidgets"),
            ["charts"] = new("QtSharp.Charts", false, Array.Empty<string>(),
                new[] { "qtcore", "qtgui", "qtwidgets" }, "QtSharp.Charts.Native", "QtCharts"),
            ["qtmultimedia"] = new("QtSharp.QtMultimedia", false, Array.Empty<string>(),
                new[] { "qtcore", "qtgui", "qtwidgets", "qtnetwork" },
                "QtSharp.QtMultimedia.Native", "QtMultimedia"),
            ["qtpositioning"] = new("QtSharp.QtPositioning", false, Array.Empty<string>(),
                new[] { "qtcore" }, "QtSharp.QtPositioning.Native", "QtPositioning"),
            ["qt3dcore"] = new("QtSharp.Qt3DCore", false, new[] { "Qt3DCore" },
                new[] { "qtcore", "qtgui" }, "QtSharp.Qt3DCore.Native", "Qt3DCore"),
            ["qt3drender"] = new("QtSharp.Qt3DRender", false, new[] { "Qt3DRender" },
                new[] { "qtcore", "qtgui", "qtopengl", "qt3dcore" },
                "QtSharp.Qt3DRender.Native", "Qt3DRender"),
            ["qt3dinput"] = new("QtSharp.Qt3DInput", false, new[] { "Qt3DInput" },
                new[] { "qtcore", "qtgui", "qt3dcore" }, "QtSharp.Qt3DInput.Native", "Qt3DInput"),
            ["qt3dlogic"] = new("QtSharp.Qt3DLogic", false, new[] { "Qt3DLogic" },
                new[] { "qtcore", "qt3dcore" }, "QtSharp.Qt3DLogic.Native", "Qt3DLogic"),
            ["qt3danimation"] = new("QtSharp.Qt3DAnimation", false, new[] { "Qt3DAnimation" },
                new[] { "qtcore", "qtgui", "qt3dcore", "qt3drender" },
                "QtSharp.Qt3DAnimation.Native", "Qt3DAnimation"),
            ["qt3dextras"] = new("QtSharp.Qt3DExtras", false, new[] { "Qt3DExtras" },
                new[] { "qtcore", "qtgui", "qtwidgets", "qtopengl", "qt3dcore", "qt3drender",
                        "qt3dinput", "qt3dlogic", "qt3danimation" },
                "QtSharp.Qt3DExtras.Native", "Qt3DExtras"),
        };

        public static ApiModel.ExtensionModule Describe(string module)
        {
            if (module != null && Entries.TryGetValue(module, out var entry))
            {
                return new ApiModel.ExtensionModule
                {
                    Name = module,
                    Namespace = entry.Namespace,
                    IsCore = entry.IsCore,
                    NativeLibrary = entry.NativeLibrary,
                    QtModule = entry.QtModule,
                    CppNamespaces = entry.CppNamespaces,
                    DependsOn = entry.DependsOn,
                };
            }
            return null;
        }

        public static string[] For(string module)
            => module != null && Entries.TryGetValue(module, out var entry)
                ? entry.DependsOn
                : Array.Empty<string>();

        /// <summary>
        /// The module itself followed by every module it builds on, transitively. The stack order
        /// matches the extractor's registry: dependencies are pushed in declaration order, so the
        /// last one is visited first.
        /// </summary>
        public static IReadOnlyList<string> Closure(string module)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            if (module != null)
                stack.Push(module);
            while (stack.Count > 0)
            {
                string current = stack.Pop();
                if (!seen.Add(current))
                    continue;
                result.Add(current);
                if (Entries.TryGetValue(current, out var entry))
                    foreach (string dependency in entry.DependsOn)
                        stack.Push(dependency);
            }
            return result;
        }
    }
}
