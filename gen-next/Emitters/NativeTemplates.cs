using System.Collections.Generic;
using System.Text;

namespace QtSharp.Generator
{
    /// <summary>
    /// The fixed shapes of the native glue: the runtime includes, the P/Invoke attribute, the POD
    /// mirrors and the C export wrapper. They are templates, not decisions - the caller supplies
    /// the type names and the body lines, and every line goes through <see cref="AppendLines"/> so
    /// the output keeps the platform newline the rest of the generator writes.
    /// </summary>
    public static class NativeTemplates
    {
        /// <summary>The includes every generated translation unit needs, after its Qt headers.</summary>
        public const string FixedIncludes = """
            #include "QtSharpApiRuntime.h"
            #include <utility>
            #include <optional>
            #include <qhashfunctions.h>
            #include "QtSharpApiExport.h"
            """;

        /// <summary>The default value a boxed parameter falls back to when no default was modelled.</summary>
        public const string DefaultValue =
            "template <class T> T& QsApiDefaultValue() { static T value{}; return value; }";

        public static string PInvokeAttribute(string entryPoint) => $$"""
            [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = "qs_api_{{entryPoint}}", CallingConvention = CallingConvention.Cdecl)]
            """;

        public static string PodTypedef(string fields, string pod)
            => $"typedef struct {{ {fields} }} {pod};";

        public static string PodStaticAssert(string pod, string qtType, string name)
            => $"static_assert(sizeof({pod}) == sizeof({qtType}), \"{name} POD layout\");";

        /// <summary>
        /// One exported C function. The body lines keep their relative indentation; the template
        /// adds the four spaces of the function body.
        /// </summary>
        public static void AppendExport(
            StringBuilder sb, string returnType, string name, string signature, IEnumerable<string> body)
        {
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {returnType} {name}({signature})");
            sb.AppendLine("{");
            foreach (string line in body)
                sb.AppendLine("    " + line);
            sb.AppendLine("}");
        }

        /// <summary>
        /// Appends a template's lines with the platform newline. Raw string literals contain LF;
        /// generated files use <see cref="StringBuilder.AppendLine()"/> everywhere else, so mixing
        /// them directly would make the output platform-dependent.
        /// </summary>
        public static void AppendLines(StringBuilder sb, string text)
        {
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i != text.Length && text[i] != '\n')
                    continue;
                int length = i - start;
                if (length > 0 && text[start + length - 1] == '\r')
                    length--;
                sb.AppendLine(text.Substring(start, length));
                start = i + 1;
            }
        }
    }
}
