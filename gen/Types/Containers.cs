using System.Collections.Generic;

namespace QtSharp.Generator
{
public sealed partial class TypeSystem
{
    [TypeHandler(@"^QStringList$", 10)]
    sealed class StringListHandler : TypeHandler
    {
        public override bool IsStringList(string t) => true;
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => "const QStringList&";
        public override string ArgToNative(string t, string name) => $"{name}Handle";
        public override string RetToManaged(string t, string call)
            => $"ApiRuntime.ToManagedStringList({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => $"ApiRuntime.ToManagedStringListTransient({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"ApiRuntime.ToManagedStringListTransient({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return new QStringList({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"*static_cast<QStringList*>({name})";
    }

    [TypeHandler(@"^QByteArrayView$", 10)]
    sealed class ByteArrayViewHandler : TypeHandler
    {
        public override bool IsByteArrayView(string t) => true;
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => "const QByteArray&";
        public override string ArgToNative(string t, string name) => $"{name}Handle";
        public override string RetToManaged(string t, string call) => $"ApiRuntime.ToManagedBytes({call})";
        public override string DefaultValue(string t) => "null";
        public override string NativeRetExpr(string t, string call)
            => $"return new QByteArray({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"*static_cast<QByteArray*>({name})";
    }

    // A nested container/pair element is a helper-allocated handle in both directions.
    abstract class GeneratedContainerHandlerBase : TypeHandler
    {
        public override string ElementToCpp(string e, string expr)
            => $"*static_cast<{CppTypeName(e)}*>({expr})";
        public override string ElementFromCpp(string e, string expr)
            => $"new {CppTypeName(e)}({expr})";
        public override string ElementRead(string e, string expr)
            => $"{Container(e).Helper}.Read({expr})";
        public override (List<string> Prelude, string Arg, List<string> Cleanup)
            ElementToAbi(string e, string expr, string prefix)
        {
            string helper = Container(e).Helper;
            return (new List<string> { $"var {prefix}Handle = {helper}.New({expr});" },
                $"{prefix}Handle",
                new List<string> { $"{helper}.Free({prefix}Handle);" });
        }
    }

    abstract class SequenceHandlerBase : GeneratedContainerHandlerBase
    {
        public override bool IsSequence(string t) => true;
        public override string ManagedType(string t) => Container(t).IsSet
            ? $"HashSet<{S.ManagedElement(Container(t).Element)}>"
            : $"List<{S.ManagedElement(Container(t).Element)}>";
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => $"const {CppTypeName(t)}&";
        public override string ArgToNative(string t, string name) => $"{name}Handle";
        public override string RetToManaged(string t, string call) => $"{Container(t).Helper}.Read({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => $"{Container(t).Helper}.ReadTransient({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"{Container(t).Helper}.ReadTransient({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
        {
            string elem = Container(t).Element;
            if (IsClassPtr(elem))
            {
                string add = Container(t).IsSet ? "insert" : "append";
                return "{\n"
                     + $"        auto qs_source = {call};\n"
                     + $"        auto* qs_container = new {CppTypeName(t)}();\n"
                     + "        for (auto qs_item : qs_source)\n"
                     + $"            qs_container->{add}(const_cast<{elem}>(qs_item));\n"
                     + "        return qs_container;\n"
                     + "    }";
            }
            return $"return new {CppTypeName(t)}({call});";
        }
        public override string CppArg(string t, string name, string defaultCpp)
            => $"*static_cast<{CppTypeName(t)}*>({name})";
    }

    [TypeHandler(@"^QList<.+>$", 40)]
    sealed class ListHandler : SequenceHandlerBase
    {
        public override ContainerInfo Container(string t)
        {
            string elem = TemplateInner(t);
            string stem = ListStem(elem);
            return new ContainerInfo("list", "QList", "List", "list", stem,
                "QsApiList_" + stem, elem, null, null, false);
        }
    }

    [TypeHandler(@"^QSet<.+>$", 40)]
    sealed class SetHandler : SequenceHandlerBase
    {
        public override ContainerInfo Container(string t)
        {
            string elem = TemplateInner(t);
            string stem = ListStem(elem);
            return new ContainerInfo("set", "QSet", "Set", "set", stem,
                "QsApiSet_" + stem, elem, null, null, true);
        }
    }

    abstract class AssocHandlerBase : GeneratedContainerHandlerBase
    {
        public override bool IsAssoc(string t) => true;
        public override string ManagedType(string t)
        {
            var (key, value) = AssocElems(t);
            return $"Dictionary<{S.ManagedElement(key)}, {S.ManagedElement(value)}>";
        }
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => $"const {CppTypeName(t)}&";
        public override string ArgToNative(string t, string name) => $"{name}Handle";
        public override string RetToManaged(string t, string call) => $"{Container(t).Helper}.Read({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => $"{Container(t).Helper}.ReadTransient({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"{Container(t).Helper}.ReadTransient({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return new {CppTypeName(t)}({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"*static_cast<{CppTypeName(t)}*>({name})";
    }

    [TypeHandler(@"^QMap<.+>$", 40)]
    sealed class MapHandler : AssocHandlerBase
    {
        public override ContainerInfo Container(string t)
        {
            var (key, value) = ApiTypes.SplitPair(TemplateInner(t));
            string stem = MapStem(key, value);
            return new ContainerInfo("map", "QMap", "Map", "map", stem,
                "QsApiMap_" + stem, null, key, value, false);
        }
    }

    [TypeHandler(@"^QHash<.+>$", 40)]
    sealed class HashHandler : AssocHandlerBase
    {
        public override ContainerInfo Container(string t)
        {
            var (key, value) = ApiTypes.SplitPair(TemplateInner(t));
            string stem = MapStem(key, value);
            return new ContainerInfo("hash", "QHash", "Hash", "hash", stem,
                "QsApiHash_" + stem, null, key, value, false);
        }
    }

    [TypeHandler(@"^QPair<.+>$", 40)]
    sealed class PairHandler : GeneratedContainerHandlerBase
    {
        public override bool IsPair(string t) => true;
        public override bool IsValueType(string t) => true;
        public override ContainerInfo Container(string t)
        {
            var (first, second) = ApiTypes.SplitPair(TemplateInner(t));
            string stem = ListStem(first) + "_" + ListStem(second);
            return new ContainerInfo("pair", "QPair", "Pair", "pair", stem,
                "QsApiPair_" + stem, first, null, second, false);
        }
        public override string ManagedType(string t)
        {
            var (first, second) = PairElems(t);
            return $"ValueTuple<{S.ManagedElement(first)}, {S.ManagedElement(second)}>";
        }
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => $"const {CppTypeName(t)}&";
        public override string ArgToNative(string t, string name) => $"{name}Handle";
        public override string RetToManaged(string t, string call) => $"{Container(t).Helper}.Read({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => $"{Container(t).Helper}.ReadTransient({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"{Container(t).Helper}.ReadTransient({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return new {CppTypeName(t)}({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"*static_cast<{CppTypeName(t)}*>({name})";
    }

    [TypeHandler(@"^std::optional<.+>$", 40)]
    sealed class OptionalHandler : GeneratedContainerHandlerBase
    {
        public override bool IsOptional(string t) => true;
        public override ContainerInfo Container(string t)
        {
            string inner = TemplateInner(t);
            string stem = ListStem(inner);
            return new ContainerInfo("optional", "std::optional", "Optional", "optional", stem,
                "QsApiOptional_" + stem, inner, null, null, false);
        }
        public override string ManagedType(string t)
        {
            string inner = OptionalElem(t);
            string managed = S.ManagedElement(inner);
            return IsValueTypeManaged(inner) ? managed + "?" : managed;
        }
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => $"const {CppTypeName(t)}&";
        public override string ArgToNative(string t, string name) => $"{name}Handle";
        public override string RetToManaged(string t, string call) => $"{Container(t).Helper}.Read({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => $"{Container(t).Helper}.ReadTransient({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"{Container(t).Helper}.ReadTransient({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return new {CppTypeName(t)}({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"*static_cast<{CppTypeName(t)}*>({name})";
    }

    // The mainline's container-shape facade. Only handlers that override Container participate;
    // a non-container returns null.
    internal string SequenceElem(string t) => ContainerOf(t).Element;
    internal string SequenceClass(string t) => ContainerOf(t).Class;
    internal string SequencePrefix(string t) => ContainerOf(t).Prefix;
    internal string SequenceTag(string t) => ContainerOf(t).Tag;
    internal string SequenceStem(string t) => ContainerOf(t).Stem;
    internal string SequenceHelper(string t) => ContainerOf(t).Helper;
    internal string AssocClass(string t) => ContainerOf(t).Class;
    internal string AssocPrefix(string t) => ContainerOf(t).Prefix;
    internal string AssocTag(string t) => ContainerOf(t).Tag;
    internal string AssocStem(string t) => ContainerOf(t).Stem;
    internal string AssocHelper(string t) => ContainerOf(t).Helper;
    internal (string Key, string Value) AssocElems(string t)
        => (ContainerOf(t).Key, ContainerOf(t).Value);
    internal (string First, string Second) PairElems(string t)
        => (ContainerOf(t).Element, ContainerOf(t).Value);
    internal string PairStem(string t) => ContainerOf(t).Stem;
    internal string PairHelper(string t) => ContainerOf(t).Helper;
    internal string OptionalElem(string t) => ContainerOf(t).Element;
    internal string OptionalStem(string t) => ContainerOf(t).Stem;
    internal string OptionalHelper(string t) => ContainerOf(t).Helper;
    internal string NativeContainerHelper(string t) => ContainerOf(t).Helper;
    internal string GeneratedHelper(string t) => ContainerOf(t).Helper;

    // Every container spelling in a signature, in stable order. Nested containers recurse so
    // QList<QMap<...>> emits both helpers.
    internal IEnumerable<string> ContainerTypesInCore(string t)
    {
        if (t == null)
            yield break;
        var container = ContainerOf(t);
        if (container == null)
            yield break;
        yield return t;
        if (container.Family is "list" or "set")
        {
            foreach (var inner in ContainerTypesInCore(container.Element))
                yield return inner;
        }
        else if (container.Family is "map" or "hash")
        {
            foreach (var inner in ContainerTypesInCore(container.Key))
                yield return inner;
            foreach (var inner in ContainerTypesInCore(container.Value))
                yield return inner;
        }
        else if (container.Family == "pair")
        {
            foreach (var inner in ContainerTypesInCore(container.Element))
                yield return inner;
            foreach (var inner in ContainerTypesInCore(container.Value))
                yield return inner;
        }
    }
}
}
