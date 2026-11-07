using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QtSharp.Generator
{
    public sealed partial class BindingModel
    {
        /// <summary>
        /// Writes the enums in one scope: the namespace level, the static Qt class, or a class
        /// body. A nested enum that hides an inherited member says <c>new</c>.
        /// </summary>
        internal void WriteEnums(StringBuilder sb, string scope, int indent, Members inherited = null)
        {
            var scoped = Enums.Where(e => ScopeOf(e) == scope).ToList();
            if (scoped.Count == 0)
                return;
            string pad = new(' ', indent * 4);
            if (scope != "Qt")
            {
                foreach (var e in scoped)
                    WriteEnum(sb, e, indent, inherited != null && NewName(inherited, Name(e)) != "");
                return;
            }
            sb.AppendLine($"{pad}public static partial class Qt");
            sb.AppendLine($"{pad}{{");
            foreach (var e in scoped)
                WriteEnum(sb, e, indent + 1);
            sb.AppendLine($"{pad}}}");
            sb.AppendLine();
        }

        internal void WriteEnum(StringBuilder sb, JsonElement e, int indent, bool hide = false, string name = null)
        {
            string pad = new(' ', indent * 4);
            var items = e.GetProperty("items").EnumerateArray().ToList();
            var values = items.Select(i => i.GetProperty("value").GetInt64()).ToList();
            string underlying = values.Any(v => v < int.MinValue || v > int.MaxValue) ? "long" : "int";
            if (IsFlags(e.GetProperty("qualified").GetString()))
                sb.AppendLine($"{pad}[Flags]");
            sb.AppendLine($"{pad}public {(hide ? "new " : "")}enum {name ?? Name(e)} : {underlying}");
            sb.AppendLine($"{pad}{{");
            foreach (var item in items)
                sb.AppendLine($"{pad}    {SafeName(Name(item))} = {item.GetProperty("value").GetInt64()},");
            sb.AppendLine($"{pad}}}");
            sb.AppendLine();
        }
    }
}
