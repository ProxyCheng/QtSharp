using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QtSharp.Generator
{
    public sealed class ContainerGlueEmitter : BindingEmitter
    {
        public ContainerGlueEmitter(GeneratorContext context) : base(context)
        {
        }

        // The C# side of the container conversion: New/Free around a call, Read for a return. A list
        // surfaces as an array and a set as a HashSet; everything else - the element codec, the
        // transient/owned split, the write-back path - is the same for both.
        public void WriteSequenceHelper(CodeWriter w, string type)
        {
            string elem = B.SequenceElem(type);
            bool isSet = B.ContainerOf(type).IsSet;
            string helper = B.SequenceHelper(type);
            string prefix = B.SequencePrefix(type);
            string stem = B.ListStem(elem);
            string managed = B.ManagedElement(elem);
            string collection = isSet ? $"HashSet<{managed}>" : $"List<{managed}>";
            // The element is the loop variable of the generated New(); ElemToAbi supplies the leaf
            // conversion or the temporary a QVariant / nested container element needs.
            var (prelude, argConv, cleanup) = B.ElemToAbi(elem, "item", "__item");
            string add = isSet ? "insert" : "append";
            string insert = $"ApiNative.{prefix}_{stem}_{add}(container, {argConv});";

            w.Blank();
            w.Open($"internal static class {helper}");
            w.Open($"public static IntPtr New(IEnumerable<{managed}> items)");
            w.Line($"var container = ApiNative.{prefix}_{stem}_new();");
            w.Line("if (items != null)");
            w.Line("    foreach (var item in items)");
            if (prelude.Count == 0 && cleanup.Count == 0)
            {
                w.Line("        " + insert);
            }
            else
            {
                w.Line("    {");
                w.Indent().Indent();
                foreach (var line in prelude)
                    w.Line(line);
                if (cleanup.Count == 0)
                {
                    w.Line(insert);
                }
                else
                {
                    w.Open("try");
                    w.Line(insert);
                    w.Close();
                    w.Open("finally");
                    foreach (var line in cleanup)
                        w.Line(line);
                    w.Close();
                }
                w.Dedent().Dedent();
                w.Line("    }");
            }
            w.Line("return container;");
            w.Close();
            w.Blank();
            w.Line($"public static void Free(IntPtr container) => ApiNative.{prefix}_{stem}_free(container);");
            w.Blank();
            // Read copies the container out and releases it (it was allocated for us); ReadTransient
            // is for the container a virtual override is handed, which belongs to Qt.
            w.Block(CSharpTemplates.ReadWrapper(
                collection, "container", $"ApiNative.{prefix}_{stem}_free(container)"));
            w.Blank();
            w.Open($"internal static {collection} ReadTransient(IntPtr container)");
            w.Line("if (container == IntPtr.Zero)");
            w.Line("    return null;");
            w.Line($"int count = ApiNative.{prefix}_{stem}_count(container);");
            w.Line(isSet ? $"var result = new HashSet<{managed}>(count);" : $"var result = new List<{managed}>(count);");
            w.Line("for (int i = 0; i < count; i++)");
            w.Line($"    result.Add({ReadElement(type, "container")});");
            w.Line("return result;");
            w.Close();
            w.Blank();
            // Write is the virtual-dispatch direction: the managed override produced the collection and
            // it has to land in the caller's slot.
            w.Block(CSharpTemplates.WriteWrapper(
                collection, "container", "items", "New(items)",
                $"ApiNative.Assign_{prefix}_{stem}(destination, container)"));
            w.Close();
        }

        // The pair helper: a ValueTuple is built from two element-codec arguments, read back from
        // the two accessors, and assigned into a virtual's caller slot.
        public void WritePairHelper(CodeWriter w, string t)
        {
            var (first, second) = B.PairElems(t);
            string helper = B.PairHelper(t);
            string stem = B.PairStem(t);
            string managedFirst = B.ManagedElement(first);
            string managedSecond = B.ManagedElement(second);
            string tuple = $"ValueTuple<{managedFirst}, {managedSecond}>";
            var (firstPrelude, firstArg, firstCleanup) = B.ElemToAbi(first, "first", "__first");
            var (secondPrelude, secondArg, secondCleanup) = B.ElemToAbi(second, "second", "__second");
            var prelude = firstPrelude.Concat(secondPrelude).ToList();
            var cleanup = secondCleanup.Concat(firstCleanup).ToList();

            w.Blank();
            w.Open($"internal static class {helper}");
            w.Open($"public static IntPtr New({tuple} value)");
            w.Line("var (first, second) = value;");
            foreach (var line in prelude)
                w.Line(line);
            string newCall = $"ApiNative.Pair_{stem}_new({firstArg}, {secondArg});";
            if (cleanup.Count == 0)
            {
                w.Line($"return {newCall}");
            }
            else
            {
                w.Open("try");
                w.Line($"return {newCall}");
                w.Close();
                w.Open("finally");
                foreach (var line in cleanup)
                    w.Line(line);
                w.Close();
            }
            w.Close();
            w.Blank();
            w.Line($"public static void Free(IntPtr pair) => ApiNative.Pair_{stem}_free(pair);");
            w.Blank();
            w.Block(CSharpTemplates.ReadWrapper(
                tuple, "pair", $"ApiNative.Pair_{stem}_free(pair)"));
            w.Blank();
            w.Open($"internal static {tuple} ReadTransient(IntPtr pair)");
            w.Line("if (pair == IntPtr.Zero)");
            w.Line("    return default;");
            w.Line($"return new {tuple}(");
            w.Line($"    {B.ElemRead(first, $"ApiNative.Pair_{stem}_first(pair)")},");
            w.Line($"    {B.ElemRead(second, $"ApiNative.Pair_{stem}_second(pair)")});");
            w.Close();
            w.Blank();
            w.Block(CSharpTemplates.WriteWrapper(
                tuple, "pair", "value", "New(value)",
                $"ApiNative.Assign_Pair_{stem}(destination, pair)"));
            w.Close();
        }

        // The optional helper: New/Free around a call, Read for a return. A value type arrives as
        // Nullable<T>; a string/wrapper uses null as the disengaged state.
        public void WriteOptionalHelper(CodeWriter w, string t)
        {
            string inner = B.OptionalElem(t);
            string helper = B.OptionalHelper(t);
            string stem = B.OptionalStem(t);
            string managedInner = B.ManagedElement(inner);
            bool valueType = B.IsValueTypeManaged(inner);
            string managed = valueType ? managedInner + "?" : managedInner;
            string argExpr = valueType ? "(value ?? default)" : "value";
            string hasValue = valueType ? "value.HasValue" : "value != null";
            var (prelude, arg, cleanup) = B.ElemToAbi(inner, argExpr, "__value");
            string newCall = $"ApiNative.Optional_{stem}_new({arg}, {hasValue});";

            w.Blank();
            w.Open($"internal static class {helper}");
            w.Open($"public static IntPtr New({managed} value)");
            foreach (var line in prelude)
                w.Line(line);
            if (cleanup.Count == 0)
            {
                w.Line($"return {newCall}");
            }
            else
            {
                w.Open("try");
                w.Line($"return {newCall}");
                w.Close();
                w.Open("finally");
                foreach (var line in cleanup)
                    w.Line(line);
                w.Close();
            }
            w.Close();
            w.Blank();
            w.Line($"public static void Free(IntPtr optional) => ApiNative.Optional_{stem}_free(optional);");
            w.Blank();
            w.Block(CSharpTemplates.ReadWrapper(
                managed, "optional", $"ApiNative.Optional_{stem}_free(optional)"));
            w.Blank();
            w.Open($"internal static {managed} ReadTransient(IntPtr optional)");
            w.Line($"if (optional == IntPtr.Zero || !ApiNative.Optional_{stem}_has(optional))");
            w.Line("    return default;");
            w.Line($"return {B.ElemRead(inner, $"ApiNative.Optional_{stem}_value(optional)")};");
            w.Close();
            w.Close();
        }

        // The C# side of the container conversion: New/Free around a call, Read for a return.
        public void WriteAssocHelper(CodeWriter w, string t)
        {
            var (key, value) = B.AssocElems(t);
            string helper = B.AssocHelper(t);
            string prefix = B.AssocPrefix(t);
            string stem = B.AssocStem(t);
            string managedKey = B.ManagedElement(key);
            string managedValue = B.ManagedElement(value);
            var (keyPrelude, keyArg, keyCleanup) = B.ElemToAbi(key, "pair.Key", "__key");
            var (valuePrelude, valueArg, valueCleanup) = B.ElemToAbi(value, "pair.Value", "__value");
            var prelude = keyPrelude.Concat(valuePrelude).ToList();
            var cleanup = valueCleanup.Concat(keyCleanup).ToList();
            string insert = $"ApiNative.{prefix}_{stem}_insert(map, {keyArg}, {valueArg});";

            w.Blank();
            w.Open($"internal static class {helper}");
            w.Open($"public static IntPtr New(IEnumerable<KeyValuePair<{managedKey}, {managedValue}>> items)");
            w.Line($"var map = ApiNative.{prefix}_{stem}_new();");
            w.Line("if (items != null)");
            w.Line("    foreach (var pair in items)");
            if (prelude.Count == 0 && cleanup.Count == 0)
            {
                w.Line("        " + insert);
            }
            else
            {
                w.Line("    {");
                w.Indent().Indent();
                foreach (var line in prelude)
                    w.Line(line);
                if (cleanup.Count == 0)
                {
                    w.Line(insert);
                }
                else
                {
                    w.Open("try");
                    w.Line(insert);
                    w.Close();
                    w.Open("finally");
                    foreach (var line in cleanup)
                        w.Line(line);
                    w.Close();
                }
                w.Dedent().Dedent();
                w.Line("    }");
            }
            w.Line("return map;");
            w.Close();
            w.Blank();
            w.Line($"public static void Free(IntPtr map) => ApiNative.{prefix}_{stem}_free(map);");
            w.Blank();
            w.Block(CSharpTemplates.ReadWrapper(
                $"Dictionary<{managedKey}, {managedValue}>", "map",
                $"ApiNative.{prefix}_{stem}_free(map)"));
            w.Blank();
            w.Open($"internal static Dictionary<{managedKey}, {managedValue}> ReadTransient(IntPtr map)");
            w.Line("if (map == IntPtr.Zero)");
            w.Line("    return null;");
            w.Line($"int count = ApiNative.{prefix}_{stem}_count(map);");
            w.Line($"var result = new Dictionary<{managedKey}, {managedValue}>(count);");
            w.Line("for (int i = 0; i < count; i++)");
            w.Line("{");
            w.Indent();
            w.Line($"var itemKey = {B.ElemRead(key, $"ApiNative.{prefix}_{stem}_key(map, i)")};");
            w.Line($"var itemValue = {B.ElemRead(value, $"ApiNative.{prefix}_{stem}_value(map, i)")};");
            w.Line("result[itemKey] = itemValue;");
            w.Dedent();
            w.Line("}");
            w.Line("return result;");
            w.Close();
            w.Blank();
            w.Block(CSharpTemplates.WriteWrapper(
                $"Dictionary<{managedKey}, {managedValue}>", "map", "items", "New(items)",
                $"ApiNative.Assign_{prefix}_{stem}(destination, map)"));
            w.Close();
        }

        // The DllImports the generated container helper class calls.
        public void WriteAssocNativeDeclarations(CodeWriter w, string t)
        {
            var (key, value) = B.AssocElems(t);
            string prefix = B.AssocPrefix(t);
            string stem = B.AssocStem(t);
            string abiKey = B.ElemAbiCsParam(key);
            string abiValue = B.ElemAbiCsParam(value);
            string retKey = B.ElemAbiCsRet(key);
            string retValue = B.ElemAbiCsRet(value);
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{B.AssocTag(t)}_{stem}_new"));
            w.Line($"internal static extern IntPtr {prefix}_{stem}_new();");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{B.AssocTag(t)}_{stem}_insert"));
            w.Line($"internal static extern void {prefix}_{stem}_insert(IntPtr map, {abiKey} key, {abiValue} value);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{B.AssocTag(t)}_{stem}_count"));
            w.Line($"internal static extern int {prefix}_{stem}_count(IntPtr map);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{B.AssocTag(t)}_{stem}_key"));
            w.Line($"internal static extern {retKey} {prefix}_{stem}_key(IntPtr map, int index);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{B.AssocTag(t)}_{stem}_value"));
            w.Line($"internal static extern {retValue} {prefix}_{stem}_value(IntPtr map, int index);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{B.AssocTag(t)}_{stem}_free"));
            w.Line($"internal static extern void {prefix}_{stem}_free(IntPtr map);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"assign_{B.AssocTag(t)}_{stem}"));
            w.Line($"internal static extern void Assign_{prefix}_{stem}(IntPtr destination, IntPtr source);");
        }

        // The C++ side. Neither container has random access, so the pair at an index is reached by
        // walking the iterator; the containers Qt hands us here are small.
        public void WriteAssocNativeCpp(StringBuilder sb, string t)
        {
            var (key, value) = B.AssocElems(t);
            string tag = B.AssocTag(t);
            string stem = B.AssocStem(t);
            string container = $"{B.AssocClass(t)}<{B.CppElementType(key)},{B.CppElementType(value)}>";
            string abiKey = B.ElemAbiCppParam(key);
            string abiValue = B.ElemAbiCppParam(value);
            string retKey = B.ElemAbiCppRet(key);
            string retValue = B.ElemAbiCppRet(value);

            sb.AppendLine();
            sb.AppendLine($"// {container}: pair-by-pair conversion for the managed side.");
            NativeTemplates.AppendExport(sb, "void*", $"qs_api_{tag}_{stem}_new", "",
                new[] { $"return new {container}();" });
            NativeTemplates.AppendExport(sb, "void", $"qs_api_{tag}_{stem}_insert",
                $"void* map, {abiKey} key, {abiValue} value",
                new[] { $"static_cast<{container}*>(map)->insert({B.ElemToCpp(key, "key")}, {B.ElemToCpp(value, "value")});" });
            NativeTemplates.AppendExport(sb, "int", $"qs_api_{tag}_{stem}_count", "const void* map",
                new[] { $"return static_cast<int>(static_cast<const {container}*>(map)->size());" });
            NativeTemplates.AppendExport(sb, retKey, $"qs_api_{tag}_{stem}_key", "const void* map, int index", new[]
            {
                $"auto it = static_cast<const {container}*>(map)->constBegin();",
                "for (int i = 0; i < index; ++i)",
                "    ++it;",
                $"return {B.ElemFromCpp(key, "it.key()")};",
            });
            NativeTemplates.AppendExport(sb, retValue, $"qs_api_{tag}_{stem}_value", "const void* map, int index", new[]
            {
                $"auto it = static_cast<const {container}*>(map)->constBegin();",
                "for (int i = 0; i < index; ++i)",
                "    ++it;",
                $"return {B.ElemFromCpp(value, "it.value()")};",
            });
            NativeTemplates.AppendExport(sb, "void", $"qs_api_{tag}_{stem}_free", "void* map",
                new[] { $"delete static_cast<{container}*>(map);" });
            sb.AppendLine($"// A virtual override that returns a {container}: copy it into the caller's slot.");
            NativeTemplates.AppendExport(sb, "void", $"qs_api_assign_{tag}_{stem}",
                "void* destination, void* source", new[]
                {
                    "if (source)",
                    $"    *static_cast<{container}*>(destination) = *static_cast<const {container}*>(source);",
                });
        }

        // The DllImports the generated helper class calls.
        public void WriteSequenceNativeDeclarations(CodeWriter w, string type)
        {
            string elem = B.SequenceElem(type);
            bool isSet = B.ContainerOf(type).IsSet;
            string prefix = B.SequencePrefix(type);
            string tag = B.SequenceTag(type);
            string stem = B.ListStem(elem);
            // The operation that adds an element: a list appends, a set inserts.
            string add = isSet ? "insert" : "append";
            // The same per-element ABI the map/hash helpers use: a scalar as itself, a string as its
            // UTF-16 pointer, a handle as the raw pointer it wraps, a struct as its POD.
            string abi = B.ElemAbiCsParam(elem);
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{tag}_{stem}_new"));
            w.Line($"internal static extern IntPtr {prefix}_{stem}_new();");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{tag}_{stem}_{add}"));
            w.Line($"internal static extern void {prefix}_{stem}_{add}(IntPtr container, {abi} item);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{tag}_{stem}_count"));
            w.Line($"internal static extern int {prefix}_{stem}_count(IntPtr container);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{tag}_{stem}_at"));
            w.Line($"internal static extern {abi} {prefix}_{stem}_at(IntPtr container, int index);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"{tag}_{stem}_free"));
            w.Line($"internal static extern void {prefix}_{stem}_free(IntPtr container);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"assign_{tag}_{stem}"));
            w.Line($"internal static extern void Assign_{prefix}_{stem}(IntPtr destination, IntPtr source);");
        }

        // The DllImports the generated pair helper calls.
        public void WritePairNativeDeclarations(CodeWriter w, string t)
        {
            var (first, second) = B.PairElems(t);
            string stem = B.PairStem(t);
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"pair_{stem}_new"));
            w.Line($"internal static extern IntPtr Pair_{stem}_new({B.ElemAbiCsParam(first)} first, {B.ElemAbiCsParam(second)} second);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"pair_{stem}_first"));
            w.Line($"internal static extern {B.ElemAbiCsRet(first)} Pair_{stem}_first(IntPtr pair);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"pair_{stem}_second"));
            w.Line($"internal static extern {B.ElemAbiCsRet(second)} Pair_{stem}_second(IntPtr pair);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"pair_{stem}_free"));
            w.Line($"internal static extern void Pair_{stem}_free(IntPtr pair);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"assign_pair_{stem}"));
            w.Line($"internal static extern void Assign_Pair_{stem}(IntPtr destination, IntPtr source);");
        }

        // The C++ side of the pair conversion: both halves go through the element codec.
        public void WritePairNativeCpp(StringBuilder sb, string t)
        {
            var (first, second) = B.PairElems(t);
            string stem = B.PairStem(t);
            string pair = B.CppTypeName(t);
            string abiFirst = B.ElemAbiCppParam(first);
            string abiSecond = B.ElemAbiCppParam(second);
            string retFirst = B.ElemAbiCppRet(first);
            string retSecond = B.ElemAbiCppRet(second);

            sb.AppendLine();
            sb.AppendLine($"// {pair}: two-field conversion for the managed side.");
            NativeTemplates.AppendExport(sb, "void*", $"qs_api_pair_{stem}_new",
                $"{abiFirst} first, {abiSecond} second",
                new[] { $"return new {pair}({B.ElemToCpp(first, "first")}, {B.ElemToCpp(second, "second")});" });
            NativeTemplates.AppendExport(sb, retFirst, $"qs_api_pair_{stem}_first", "const void* pair",
                new[] { $"return {B.ElemFromCpp(first, $"static_cast<const {pair}*>(pair)->first")};" });
            NativeTemplates.AppendExport(sb, retSecond, $"qs_api_pair_{stem}_second", "const void* pair",
                new[] { $"return {B.ElemFromCpp(second, $"static_cast<const {pair}*>(pair)->second")};" });
            NativeTemplates.AppendExport(sb, "void", $"qs_api_pair_{stem}_free", "void* pair",
                new[] { $"delete static_cast<{pair}*>(pair);" });
            sb.AppendLine($"// A virtual override that returns a {pair}: copy it into the caller's slot.");
            NativeTemplates.AppendExport(sb, "void", $"qs_api_assign_pair_{stem}",
                "void* destination, void* source", new[]
                {
                    "if (source)",
                    $"    *static_cast<{pair}*>(destination) = *static_cast<const {pair}*>(source);",
                });
        }

        // The DllImports the generated optional helper calls.
        public void WriteOptionalNativeDeclarations(CodeWriter w, string t)
        {
            string inner = B.OptionalElem(t);
            string stem = B.OptionalStem(t);
            string abi = B.ElemAbiCsParam(inner);
            string ret = B.ElemAbiCsRet(inner);
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"optional_{stem}_new"));
            w.Line($"internal static extern IntPtr Optional_{stem}_new({abi} value, [MarshalAs(UnmanagedType.I1)] bool hasValue);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"optional_{stem}_has"));
            w.Line($"[return: MarshalAs(UnmanagedType.I1)]");
            w.Line($"internal static extern bool Optional_{stem}_has(IntPtr optional);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"optional_{stem}_value"));
            w.Line($"internal static extern {ret} Optional_{stem}_value(IntPtr optional);");
            w.Blank();
            w.Line(NativeTemplates.PInvokeAttribute($"optional_{stem}_free"));
            w.Line($"internal static extern void Optional_{stem}_free(IntPtr optional);");
        }

        // The C++ side of std::optional: a heap optional built from the element codec.
        public void WriteOptionalNativeCpp(StringBuilder sb, string t)
        {
            string inner = B.OptionalElem(t);
            string stem = B.OptionalStem(t);
            string optional = B.CppTypeName(t);
            string abiParam = B.ElemAbiCppParam(inner);
            string abiRet = B.ElemAbiCppRet(inner);
            sb.AppendLine();
            sb.AppendLine($"// {optional}: a value that may not be set.");
            NativeTemplates.AppendExport(sb, "void*", $"qs_api_optional_{stem}_new",
                $"{abiParam} value, bool hasValue",
                new[] { $"return hasValue ? new {optional}({B.ElemToCpp(inner, "value")}) : new {optional}();" });
            NativeTemplates.AppendExport(sb, "bool", $"qs_api_optional_{stem}_has", "const void* optional",
                new[] { $"return static_cast<const {optional}*>(optional)->has_value();" });
            NativeTemplates.AppendExport(sb, abiRet, $"qs_api_optional_{stem}_value", "const void* optional",
                new[] { $"return {B.ElemFromCpp(inner, $"static_cast<const {optional}*>(optional)->value()")};" });
            NativeTemplates.AppendExport(sb, "void", $"qs_api_optional_{stem}_free", "void* optional",
                new[] { $"delete static_cast<{optional}*>(optional);" });
        }

        // The C++ side of the container conversion: one add/count/at per element type, with the
        // element crossing the C ABI as itself, as a handle, or as a pointer to a copy.
        public void WriteSequenceNativeCpp(StringBuilder sb, string type)
        {
            string elem = B.SequenceElem(type);
            bool isSet = B.ContainerOf(type).IsSet;
            string tag = B.SequenceTag(type);
            string stem = B.ListStem(elem);
            string container = $"{B.SequenceClass(type)}<{B.CppElementType(elem)}>";
            string add = isSet ? "insert" : "append";
            string abi = B.ElemAbiCppParam(elem);
            string addConv = B.ElemToCpp(elem, "item");

            sb.AppendLine();
            sb.AppendLine($"// {container}: element-by-element conversion for the managed side.");
            NativeTemplates.AppendExport(sb, "void*", $"qs_api_{tag}_{stem}_new", "",
                new[] { $"return new {container}();" });
            NativeTemplates.AppendExport(sb, "void", $"qs_api_{tag}_{stem}_{add}",
                $"void* container, {abi} item",
                new[] { $"static_cast<{container}*>(container)->{add}({addConv});" });
            NativeTemplates.AppendExport(sb, "int", $"qs_api_{tag}_{stem}_count", "const void* container",
                new[] { $"return static_cast<int>(static_cast<const {container}*>(container)->size());" });

            var atBody = new List<string>();
            string atValue;
            if (isSet)
            {
                // A set has no index, so the element is reached by walking its iterator.
                atBody.Add($"auto it = static_cast<const {container}*>(container)->constBegin();");
                atBody.Add("for (int i = 0; i < index; ++i)");
                atBody.Add("    ++it;");
                atValue = "*it";
            }
            else
            {
                atValue = $"static_cast<const {container}*>(container)->at(index)";
            }
            atBody.Add($"return {B.ElemFromCpp(elem, atValue)};");
            NativeTemplates.AppendExport(sb, abi, $"qs_api_{tag}_{stem}_at",
                "const void* container, int index", atBody);

            NativeTemplates.AppendExport(sb, "void", $"qs_api_{tag}_{stem}_free", "void* container",
                new[] { $"delete static_cast<{container}*>(container);" });
            sb.AppendLine($"// A virtual override that returns a {container}: copy it into the caller's slot.");
            NativeTemplates.AppendExport(sb, "void", $"qs_api_assign_{tag}_{stem}",
                "void* destination, void* source", new[]
                {
                    "if (source)",
                    $"    *static_cast<{container}*>(destination) = *static_cast<const {container}*>(source);",
                });
        }

        public string ToCSharpElement(string elem) => B.ManagedElement(elem);

        // One element read out of a sequence container. It goes through the same ElemRead codec the
        // map/hash helpers use, so every element kind the vocabulary admits has a managed spelling.
        public string ReadElement(string type, string container)
        {
            string elem = B.SequenceElem(type);
            string prefix = B.SequencePrefix(type);
            string stem = B.ListStem(elem);
            return B.ElemRead(elem, $"ApiNative.{prefix}_{stem}_at({container}, i)");
        }
    
    }
}
