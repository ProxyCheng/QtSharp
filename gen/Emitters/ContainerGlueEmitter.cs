using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static partial class ApiCodeGen
{
    static readonly ContainerGlueEmitter ContainerGlue = new();

    sealed class ContainerGlueEmitter : Emitter
    {
        // The C# side of the container conversion: New/Free around a call, Read for a return. A list
        // surfaces as an array and a set as a HashSet; everything else - the element codec, the
        // transient/owned split, the write-back path - is the same for both.
        public void WriteSequenceHelper(CodeWriter w, string type)
        {
            string elem = SequenceElem(type);
            bool isSet = ContainerOf(type).IsSet;
            string helper = SequenceHelper(type);
            string prefix = SequencePrefix(type);
            string stem = ListStem(elem);
            string managed = ManagedElement(elem);
            string collection = isSet ? $"HashSet<{managed}>" : $"List<{managed}>";
            // The element is the loop variable of the generated New(); ElemToAbi supplies the leaf
            // conversion or the temporary a QVariant / nested container element needs.
            var (prelude, argConv, cleanup) = ElemToAbi(elem, "item", "__item");
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
            w.Open($"public static {collection} Read(IntPtr container)");
            w.Line("var result = ReadTransient(container);");
            w.Line("if (container != IntPtr.Zero)");
            w.Line($"    ApiNative.{prefix}_{stem}_free(container);");
            w.Line("return result;");
            w.Close();
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
            w.Open($"internal static void Write(IntPtr destination, {collection} items)");
            w.Line("var container = New(items);");
            w.Open("try");
            w.Line($"ApiNative.Assign_{prefix}_{stem}(destination, container);");
            w.Close();
            w.Open("finally");
            w.Line("Free(container);");
            w.Close();
            w.Close();
            w.Close();
        }

        // The pair helper: a ValueTuple is built from two element-codec arguments, read back from
        // the two accessors, and assigned into a virtual's caller slot.
        public void WritePairHelper(CodeWriter w, string t)
        {
            var (first, second) = PairElems(t);
            string helper = PairHelper(t);
            string stem = PairStem(t);
            string managedFirst = ManagedElement(first);
            string managedSecond = ManagedElement(second);
            string tuple = $"ValueTuple<{managedFirst}, {managedSecond}>";
            var (firstPrelude, firstArg, firstCleanup) = ElemToAbi(first, "first", "__first");
            var (secondPrelude, secondArg, secondCleanup) = ElemToAbi(second, "second", "__second");
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
            w.Open($"public static {tuple} Read(IntPtr pair)");
            w.Line("var result = ReadTransient(pair);");
            w.Line("if (pair != IntPtr.Zero)");
            w.Line($"    ApiNative.Pair_{stem}_free(pair);");
            w.Line("return result;");
            w.Close();
            w.Blank();
            w.Open($"internal static {tuple} ReadTransient(IntPtr pair)");
            w.Line("if (pair == IntPtr.Zero)");
            w.Line("    return default;");
            w.Line($"return new {tuple}(");
            w.Line($"    {ElemRead(first, $"ApiNative.Pair_{stem}_first(pair)")},");
            w.Line($"    {ElemRead(second, $"ApiNative.Pair_{stem}_second(pair)")});");
            w.Close();
            w.Blank();
            w.Open($"internal static void Write(IntPtr destination, {tuple} value)");
            w.Line("var pair = New(value);");
            w.Open("try");
            w.Line($"ApiNative.Assign_Pair_{stem}(destination, pair);");
            w.Close();
            w.Open("finally");
            w.Line("Free(pair);");
            w.Close();
            w.Close();
            w.Close();
        }

        // The optional helper: New/Free around a call, Read for a return. A value type arrives as
        // Nullable<T>; a string/wrapper uses null as the disengaged state.
        public void WriteOptionalHelper(CodeWriter w, string t)
        {
            string inner = OptionalElem(t);
            string helper = OptionalHelper(t);
            string stem = OptionalStem(t);
            string managedInner = ManagedElement(inner);
            bool valueType = IsValueTypeManaged(inner);
            string managed = valueType ? managedInner + "?" : managedInner;
            string argExpr = valueType ? "(value ?? default)" : "value";
            string hasValue = valueType ? "value.HasValue" : "value != null";
            var (prelude, arg, cleanup) = ElemToAbi(inner, argExpr, "__value");
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
            w.Open($"public static {managed} Read(IntPtr optional)");
            w.Line("var result = ReadTransient(optional);");
            w.Line("if (optional != IntPtr.Zero)");
            w.Line($"    ApiNative.Optional_{stem}_free(optional);");
            w.Line("return result;");
            w.Close();
            w.Blank();
            w.Open($"internal static {managed} ReadTransient(IntPtr optional)");
            w.Line($"if (optional == IntPtr.Zero || !ApiNative.Optional_{stem}_has(optional))");
            w.Line("    return default;");
            w.Line($"return {ElemRead(inner, $"ApiNative.Optional_{stem}_value(optional)")};");
            w.Close();
            w.Close();
        }

        // The C# side of the container conversion: New/Free around a call, Read for a return.
        public void WriteAssocHelper(CodeWriter w, string t)
        {
            var (key, value) = AssocElems(t);
            string helper = AssocHelper(t);
            string prefix = AssocPrefix(t);
            string stem = AssocStem(t);
            string managedKey = ManagedElement(key);
            string managedValue = ManagedElement(value);
            var (keyPrelude, keyArg, keyCleanup) = ElemToAbi(key, "pair.Key", "__key");
            var (valuePrelude, valueArg, valueCleanup) = ElemToAbi(value, "pair.Value", "__value");
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
            w.Open($"public static Dictionary<{managedKey}, {managedValue}> Read(IntPtr map)");
            w.Line("var result = ReadTransient(map);");
            w.Line("if (map != IntPtr.Zero)");
            w.Line($"    ApiNative.{prefix}_{stem}_free(map);");
            w.Line("return result;");
            w.Close();
            w.Blank();
            w.Open($"internal static Dictionary<{managedKey}, {managedValue}> ReadTransient(IntPtr map)");
            w.Line("if (map == IntPtr.Zero)");
            w.Line("    return null;");
            w.Line($"int count = ApiNative.{prefix}_{stem}_count(map);");
            w.Line($"var result = new Dictionary<{managedKey}, {managedValue}>(count);");
            w.Line("for (int i = 0; i < count; i++)");
            w.Line("{");
            w.Indent();
            w.Line($"var itemKey = {ElemRead(key, $"ApiNative.{prefix}_{stem}_key(map, i)")};");
            w.Line($"var itemValue = {ElemRead(value, $"ApiNative.{prefix}_{stem}_value(map, i)")};");
            w.Line("result[itemKey] = itemValue;");
            w.Dedent();
            w.Line("}");
            w.Line("return result;");
            w.Close();
            w.Blank();
            w.Open($"internal static void Write(IntPtr destination, Dictionary<{managedKey}, {managedValue}> items)");
            w.Line("var map = New(items);");
            w.Open("try");
            w.Line($"ApiNative.Assign_{prefix}_{stem}(destination, map);");
            w.Close();
            w.Open("finally");
            w.Line("Free(map);");
            w.Close();
            w.Close();
            w.Close();
        }

        // The DllImports the generated container helper class calls.
        public void WriteAssocNativeDeclarations(CodeWriter w, string t)
        {
            var (key, value) = AssocElems(t);
            string prefix = AssocPrefix(t);
            string stem = AssocStem(t);
            string abiKey = ElemAbiCsParam(key);
            string abiValue = ElemAbiCsParam(value);
            string retKey = ElemAbiCsRet(key);
            string retValue = ElemAbiCsRet(value);
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_new\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern IntPtr {prefix}_{stem}_new();");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_insert\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void {prefix}_{stem}_insert(IntPtr map, {abiKey} key, {abiValue} value);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_count\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern int {prefix}_{stem}_count(IntPtr map);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_key\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern {retKey} {prefix}_{stem}_key(IntPtr map, int index);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_value\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern {retValue} {prefix}_{stem}_value(IntPtr map, int index);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{AssocTag(t)}_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void {prefix}_{stem}_free(IntPtr map);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_assign_{AssocTag(t)}_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void Assign_{prefix}_{stem}(IntPtr destination, IntPtr source);");
        }

        // The C++ side. Neither container has random access, so the pair at an index is reached by
        // walking the iterator; the containers Qt hands us here are small.
        public void WriteAssocNativeCpp(StringBuilder sb, string t)
        {
            var (key, value) = AssocElems(t);
            string tag = AssocTag(t);
            string stem = AssocStem(t);
            string container = $"{AssocClass(t)}<{key},{value}>";
            string abiKey = ElemAbiCppParam(key);
            string abiValue = ElemAbiCppParam(value);
            string retKey = ElemAbiCppRet(key);
            string retValue = ElemAbiCppRet(value);

            sb.AppendLine();
            sb.AppendLine($"// {container}: pair-by-pair conversion for the managed side.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{tag}_{stem}_new()");
            sb.AppendLine("{");
            sb.AppendLine($"    return new {container}();");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{tag}_{stem}_insert(void* map, {abiKey} key, {abiValue} value)");
            sb.AppendLine("{");
            sb.AppendLine($"    static_cast<{container}*>(map)->insert({ElemToCpp(key, "key")}, {ElemToCpp(value, "value")});");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT int qs_api_{tag}_{stem}_count(const void* map)");
            sb.AppendLine("{");
            sb.AppendLine($"    return static_cast<int>(static_cast<const {container}*>(map)->size());");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {retKey} qs_api_{tag}_{stem}_key(const void* map, int index)");
            sb.AppendLine("{");
            sb.AppendLine($"    auto it = static_cast<const {container}*>(map)->constBegin();");
            sb.AppendLine("    for (int i = 0; i < index; ++i)");
            sb.AppendLine("        ++it;");
            sb.AppendLine($"    return {ElemFromCpp(key, "it.key()")};");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {retValue} qs_api_{tag}_{stem}_value(const void* map, int index)");
            sb.AppendLine("{");
            sb.AppendLine($"    auto it = static_cast<const {container}*>(map)->constBegin();");
            sb.AppendLine("    for (int i = 0; i < index; ++i)");
            sb.AppendLine("        ++it;");
            sb.AppendLine($"    return {ElemFromCpp(value, "it.value()")};");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{tag}_{stem}_free(void* map)");
            sb.AppendLine("{");
            sb.AppendLine($"    delete static_cast<{container}*>(map);");
            sb.AppendLine("}");
            sb.AppendLine($"// A virtual override that returns a {container}: copy it into the caller's slot.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_assign_{tag}_{stem}(void* destination, void* source)");
            sb.AppendLine("{");
            sb.AppendLine("    if (source)");
            sb.AppendLine($"        *static_cast<{container}*>(destination) = *static_cast<const {container}*>(source);");
            sb.AppendLine("}");
        }

        // The DllImports the generated helper class calls.
        public void WriteSequenceNativeDeclarations(CodeWriter w, string type)
        {
            string elem = SequenceElem(type);
            bool isSet = ContainerOf(type).IsSet;
            string prefix = SequencePrefix(type);
            string tag = SequenceTag(type);
            string stem = ListStem(elem);
            // The operation that adds an element: a list appends, a set inserts.
            string add = isSet ? "insert" : "append";
            // The same per-element ABI the map/hash helpers use: a scalar as itself, a string as its
            // UTF-16 pointer, a handle as the raw pointer it wraps, a struct as its POD.
            string abi = ElemAbiCsParam(elem);
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{tag}_{stem}_new\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern IntPtr {prefix}_{stem}_new();");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{tag}_{stem}_{add}\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void {prefix}_{stem}_{add}(IntPtr container, {abi} item);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{tag}_{stem}_count\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern int {prefix}_{stem}_count(IntPtr container);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{tag}_{stem}_at\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern {abi} {prefix}_{stem}_at(IntPtr container, int index);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{tag}_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void {prefix}_{stem}_free(IntPtr container);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_assign_{tag}_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void Assign_{prefix}_{stem}(IntPtr destination, IntPtr source);");
        }

        // The DllImports the generated pair helper calls.
        public void WritePairNativeDeclarations(CodeWriter w, string t)
        {
            var (first, second) = PairElems(t);
            string stem = PairStem(t);
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_pair_{stem}_new\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern IntPtr Pair_{stem}_new({ElemAbiCsParam(first)} first, {ElemAbiCsParam(second)} second);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_pair_{stem}_first\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern {ElemAbiCsRet(first)} Pair_{stem}_first(IntPtr pair);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_pair_{stem}_second\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern {ElemAbiCsRet(second)} Pair_{stem}_second(IntPtr pair);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_pair_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void Pair_{stem}_free(IntPtr pair);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_assign_pair_{stem}\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void Assign_Pair_{stem}(IntPtr destination, IntPtr source);");
        }

        // The C++ side of the pair conversion: both halves go through the element codec.
        public void WritePairNativeCpp(StringBuilder sb, string t)
        {
            var (first, second) = PairElems(t);
            string stem = PairStem(t);
            string pair = CppTypeName(t);
            string abiFirst = ElemAbiCppParam(first);
            string abiSecond = ElemAbiCppParam(second);
            string retFirst = ElemAbiCppRet(first);
            string retSecond = ElemAbiCppRet(second);

            sb.AppendLine();
            sb.AppendLine($"// {pair}: two-field conversion for the managed side.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_pair_{stem}_new({abiFirst} first, {abiSecond} second)");
            sb.AppendLine("{");
            sb.AppendLine($"    return new {pair}({ElemToCpp(first, "first")}, {ElemToCpp(second, "second")});");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {retFirst} qs_api_pair_{stem}_first(const void* pair)");
            sb.AppendLine("{");
            sb.AppendLine($"    return {ElemFromCpp(first, $"static_cast<const {pair}*>(pair)->first")};");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {retSecond} qs_api_pair_{stem}_second(const void* pair)");
            sb.AppendLine("{");
            sb.AppendLine($"    return {ElemFromCpp(second, $"static_cast<const {pair}*>(pair)->second")};");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_pair_{stem}_free(void* pair)");
            sb.AppendLine("{");
            sb.AppendLine($"    delete static_cast<{pair}*>(pair);");
            sb.AppendLine("}");
            sb.AppendLine($"// A virtual override that returns a {pair}: copy it into the caller's slot.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_assign_pair_{stem}(void* destination, void* source)");
            sb.AppendLine("{");
            sb.AppendLine("    if (source)");
            sb.AppendLine($"        *static_cast<{pair}*>(destination) = *static_cast<const {pair}*>(source);");
            sb.AppendLine("}");
        }

        // The DllImports the generated optional helper calls.
        public void WriteOptionalNativeDeclarations(CodeWriter w, string t)
        {
            string inner = OptionalElem(t);
            string stem = OptionalStem(t);
            string abi = ElemAbiCsParam(inner);
            string ret = ElemAbiCsRet(inner);
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_optional_{stem}_new\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern IntPtr Optional_{stem}_new({abi} value, [MarshalAs(UnmanagedType.I1)] bool hasValue);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_optional_{stem}_has\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"[return: MarshalAs(UnmanagedType.I1)]");
            w.Line($"internal static extern bool Optional_{stem}_has(IntPtr optional);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_optional_{stem}_value\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern {ret} Optional_{stem}_value(IntPtr optional);");
            w.Blank();
            w.Line($"[SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_optional_{stem}_free\", CallingConvention = CallingConvention.Cdecl)]");
            w.Line($"internal static extern void Optional_{stem}_free(IntPtr optional);");
        }

        // The C++ side of std::optional: a heap optional built from the element codec.
        public void WriteOptionalNativeCpp(StringBuilder sb, string t)
        {
            string inner = OptionalElem(t);
            string stem = OptionalStem(t);
            string optional = CppTypeName(t);
            string abiParam = ElemAbiCppParam(inner);
            string abiRet = ElemAbiCppRet(inner);
            sb.AppendLine();
            sb.AppendLine($"// {optional}: a value that may not be set.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_optional_{stem}_new({abiParam} value, bool hasValue)");
            sb.AppendLine("{");
            sb.AppendLine($"    return hasValue ? new {optional}({ElemToCpp(inner, "value")}) : new {optional}();");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT bool qs_api_optional_{stem}_has(const void* optional)");
            sb.AppendLine("{");
            sb.AppendLine($"    return static_cast<const {optional}*>(optional)->has_value();");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {abiRet} qs_api_optional_{stem}_value(const void* optional)");
            sb.AppendLine("{");
            sb.AppendLine($"    return {ElemFromCpp(inner, $"static_cast<const {optional}*>(optional)->value()")};");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_optional_{stem}_free(void* optional)");
            sb.AppendLine("{");
            sb.AppendLine($"    delete static_cast<{optional}*>(optional);");
            sb.AppendLine("}");
        }

        // The C++ side of the container conversion: one add/count/at per element type, with the
        // element crossing the C ABI as itself, as a handle, or as a pointer to a copy.
        public void WriteSequenceNativeCpp(StringBuilder sb, string type)
        {
            string elem = SequenceElem(type);
            bool isSet = ContainerOf(type).IsSet;
            string tag = SequenceTag(type);
            string stem = ListStem(elem);
            string container = $"{SequenceClass(type)}<{elem}>";
            string add = isSet ? "insert" : "append";
            // The same per-element ABI and conversion the map/hash helpers use.
            string abi = ElemAbiCppParam(elem);
            string addConv = ElemToCpp(elem, "item");

            sb.AppendLine();
            sb.AppendLine($"// {container}: element-by-element conversion for the managed side.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{tag}_{stem}_new()");
            sb.AppendLine("{");
            sb.AppendLine($"    return new {container}();");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{tag}_{stem}_{add}(void* container, {abi} item)");
            sb.AppendLine("{");
            sb.AppendLine($"    static_cast<{container}*>(container)->{add}({addConv});");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT int qs_api_{tag}_{stem}_count(const void* container)");
            sb.AppendLine("{");
            sb.AppendLine($"    return static_cast<int>(static_cast<const {container}*>(container)->size());");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT {abi} qs_api_{tag}_{stem}_at(const void* container, int index)");
            sb.AppendLine("{");
            string atValue;
            if (isSet)
            {
                // A set has no index, so the element is reached by walking its iterator.
                sb.AppendLine($"    auto it = static_cast<const {container}*>(container)->constBegin();");
                sb.AppendLine("    for (int i = 0; i < index; ++i)");
                sb.AppendLine("        ++it;");
                atValue = "*it";
            }
            else
            {
                atValue = $"static_cast<const {container}*>(container)->at(index)";
            }
            sb.AppendLine($"    return {ElemFromCpp(elem, atValue)};");
            sb.AppendLine("}");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{tag}_{stem}_free(void* container)");
            sb.AppendLine("{");
            sb.AppendLine($"    delete static_cast<{container}*>(container);");
            sb.AppendLine("}");
            sb.AppendLine($"// A virtual override that returns a {container}: copy it into the caller's slot.");
            sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_assign_{tag}_{stem}(void* destination, void* source)");
            sb.AppendLine("{");
            sb.AppendLine("    if (source)");
            sb.AppendLine($"        *static_cast<{container}*>(destination) = *static_cast<const {container}*>(source);");
            sb.AppendLine("}");
        }

        public string ToCSharpElement(string elem) => ManagedElement(elem);

        // One element read out of a sequence container. It goes through the same ElemRead codec the
        // map/hash helpers use, so every element kind the vocabulary admits has a managed spelling.
        public string ReadElement(string type, string container)
        {
            string elem = SequenceElem(type);
            string prefix = SequencePrefix(type);
            string stem = ListStem(elem);
            return ElemRead(elem, $"ApiNative.{prefix}_{stem}_at({container}, i)");
        }
    }
}
