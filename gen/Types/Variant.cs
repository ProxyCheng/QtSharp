public static partial class ApiCodeGen
{
    // QVariant is erased to object: the value is unwrapped to its BCL shape and boxed back on the
    // way in. The wrapper builds or leases the native variant around the call.
    [TypeHandler(@"^QVariant$", 10)]
    sealed class VariantHandler : TypeHandler
    {
        public override bool IsVariant(string t) => true;
        public override bool IsBoxed(string t) => true;
        public override string ManagedType(string t) => "object";
        public override string ManagedElement(string t) => "object";
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => "const QVariant&";
        public override string CppVirtualParamType(string t) => "const QVariant&";
        public override string ArgToNative(string t, string name) => $"{name}?.Handle ?? IntPtr.Zero";
        public override string RetToManaged(string t, string call)
            => $"{VariantMarshalling}.ToManaged({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => $"{VariantMarshalling}.ToManaged(ApiNative.Clone_{ListStem(t)}({ArgvSlot(index)}))";
        public override string SignalRead(string t, int index)
            => $"{VariantMarshalling}.ToManagedTransient({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return new QVariant({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => BoxedCppArg(t, name, defaultCpp);
        public override string ElementRead(string e, string expr)
            => $"{VariantMarshalling}.ToManaged({expr})";
        public override (System.Collections.Generic.List<string> Prelude, string Arg, System.Collections.Generic.List<string> Cleanup)
            ElementToAbi(string e, string expr, string prefix)
            => (new System.Collections.Generic.List<string> { $"using var {prefix}Variant = {VariantMarshalling}.Value({expr});" },
                $"{prefix}Variant.Handle",
                new System.Collections.Generic.List<string>());
    }
}
