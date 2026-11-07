using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QtSharp.Generator
{
    public sealed partial class BindingModel
    {
        // The container-shape facade. The handlers own the parsing; the model exposes the pieces
        // the emitters name so a container is one row of data, not a chain of IsXxx tests.
        internal string SequenceElem(string t) => Types.SequenceElem(t);
        internal string SequenceClass(string t) => Types.SequenceClass(t);
        internal string SequencePrefix(string t) => Types.SequencePrefix(t);
        internal string SequenceTag(string t) => Types.SequenceTag(t);
        internal string SequenceStem(string t) => Types.SequenceStem(t);
        internal string SequenceHelper(string t) => Types.SequenceHelper(t);
        internal string AssocClass(string t) => Types.AssocClass(t);
        internal string AssocPrefix(string t) => Types.AssocPrefix(t);
        internal string AssocTag(string t) => Types.AssocTag(t);
        internal string AssocStem(string t) => Types.AssocStem(t);
        internal string AssocHelper(string t) => Types.AssocHelper(t);
        internal (string Key, string Value) AssocElems(string t) => Types.AssocElems(t);
        internal (string First, string Second) PairElems(string t) => Types.PairElems(t);
        internal string PairStem(string t) => Types.PairStem(t);
        internal string PairHelper(string t) => Types.PairHelper(t);
        internal string OptionalStem(string t) => Types.OptionalStem(t);
        internal string OptionalHelper(string t) => Types.OptionalHelper(t);
        internal string NativeContainerHelper(string t) => Types.NativeContainerHelper(t);
        internal string GeneratedHelper(string t) => Types.GeneratedHelper(t);
        internal string NativeRetCpp(string t) => Types.NativeRetCpp(t);

        private List<(JsonElement Function, string Entry)> freeFunctionEntries;

        internal List<(JsonElement Function, string Entry)> FreeFunctionEntries()
        {
            if (freeFunctionEntries != null)
                return freeFunctionEntries;
            var entries = new List<(JsonElement, string)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fn in Functions)
            {
                string entry = FreeFunctionEntry(fn);
                if (!seen.Add(entry))
                    throw new InvalidOperationException(
                        $"two free functions share the entry {entry}; a signature is not unique: {Name(fn)}");
                entries.Add((fn, entry));
            }
            return freeFunctionEntries = entries;
        }

        internal string FreeFunctionEntry(JsonElement fn)
        {
            var signature = new StringBuilder();
            signature.Append(fn.GetProperty("returnType").GetString()).Append(' ').Append(Name(fn)).Append('(');
            foreach (var p in fn.GetProperty("parameters").EnumerateArray())
            {
                signature.Append(p.GetProperty("type").GetString());
                if (p.TryGetProperty("cpp", out var cpp) && cpp.ValueKind == JsonValueKind.String)
                    signature.Append('|').Append(cpp.GetString());
                if (p.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String)
                    signature.Append('|').Append(mode.GetString());
                signature.Append(';');
            }
            signature.Append(')');
            if (fn.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array)
                signature.Append('|').Append(string.Join(",", flags.EnumerateArray().Select(f => f.GetString())));
            return "fn_" + Name(fn) + "_" + StableHash(signature.ToString());
        }

        internal string NamespaceClassName
            => CurrentModule == null || CurrentModule.Name == "qtcore"
                ? "Qt"
                : CurrentModule.QtModule;
    }
}
