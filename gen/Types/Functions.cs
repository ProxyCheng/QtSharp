public static partial class ApiCodeGen
{
    // A concrete C function type is the delegate Qt declared; QFunctionPointer has no signature,
    // so it stays the carrier struct and the caller opts into a delegate with As<TDelegate>.
    [TypeHandler(@"^.+\(\*\)\(.*\)$", 30)]
    sealed class CallbackHandler : TypeHandler
    {
        public override bool IsCallback(string t) => true;
        public override string ManagedType(string t) => CallbackDelegateType(t);
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string RetToManaged(string t, string call)
            => $"{CallbackThunkName(t)}.Wrap({call})";
        public override string DefaultValue(string t) => "null";
        public override string NativeRetExpr(string t, string call)
            => $"return reinterpret_cast<void*>({call});";
    }

    [TypeHandler(@"^QFunctionPointer$", 10)]
    sealed class FunctionPointerHandler : TypeHandler
    {
        public override bool IsFunctionPointer(string t) => true;
        public override string ManagedType(string t) => CoreTypeName("QFunctionPointer");
        public override string ManagedElement(string t) => "QFunctionPointer";
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string ArgToNative(string t, string name) => $"{name}.Address";
        public override string RetToManaged(string t, string call)
            => $"new {CoreTypeName("QFunctionPointer")}({call})";
        public override string NativeRetExpr(string t, string call)
            => $"return reinterpret_cast<void*>({call});";
    }
}
