public static partial class ApiCodeGen
{
    [TypeHandler(@"^(?:QString|QStringView|QAnyStringView)$", 10)]
    sealed class StringHandler : TypeHandler
    {
        public override bool IsString(string t) => true;
        public override string ManagedType(string t) => "string";
        public override string NativeParamType(string t) => "[MarshalAs(UnmanagedType.LPWStr)] string";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "const char16_t*";
        public override string CppCtorParamType(string t) => "QString";
        public override string RetToManaged(string t, string call) => $"ApiRuntime.ToManaged({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => $"ApiRuntime.ToManagedTransient({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"ApiRuntime.ToManagedTransient({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => t == "QString"
                ? $"return new QString({call});"
                : $"return new QString({call}.toString());";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"{name} ? QString::fromUtf16({name}) : QString()";
        public override string CppVirtualParamType(string t) => "QString";
        public override string ElementAbiCppParam(string e) => "const char16_t*";
        public override string ElementAbiCppRet(string e) => "void*";
        public override string ElementAbiCsParam(string e) => "[MarshalAs(UnmanagedType.LPWStr)] string";
        public override string ElementAbiCsRet(string e) => "IntPtr";
        public override string ElementToCpp(string e, string expr)
            => $"({expr} ? QString::fromUtf16({expr}) : QString())";
        public override string ElementFromCpp(string e, string expr) => $"new QString({expr})";
        public override string ElementRead(string e, string expr) => $"ApiRuntime.ToManaged({expr})";
        public override string ElementArg(string e, string expr) => expr;
    }

    [TypeHandler(@"^char\*$", 10)]
    sealed class CStringHandler : TypeHandler
    {
        public override bool IsCString(string t) => true;
        public override string ManagedType(string t) => "string";
        public override string NativeParamType(string t) => "[MarshalAs(UnmanagedType.LPUTF8Str)] string";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "const char*";
        public override string CppCtorParamType(string t) => "const char*";
        public override string RetToManaged(string t, string call) => $"ApiRuntime.ToManaged({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => $"ApiRuntime.FromUtf8({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"ApiRuntime.FromUtf8(Marshal.ReadIntPtr({ArgvSlot(index)}))";
        public override string NativeRetExpr(string t, string call)
            => $"{{ const char* text = ({call}); return new QString(text ? QString::fromUtf8(text) : QString()); }}";
        public override string CppVirtualParamType(string t) => "const char*";
    }

    [TypeHandler(@"^QChar$", 10)]
    sealed class CharHandler : TypeHandler
    {
        public override bool IsChar(string t) => true;
        public override bool IsValueType(string t) => true;
        public override string ManagedType(string t) => "char";
        public override string NativeParamType(string t) => "ushort";
        public override string NativeRetType(string t) => "ushort";
        public override string CppAbiType(string t) => "char16_t";
        public override string CppAbiRetType(string t) => "char16_t";
        public override string CppCtorParamType(string t) => "QChar";
        public override string ArgToNative(string t, string name) => $"(ushort){name}";
        public override string RetToManaged(string t, string call) => $"(char){call}";
        public override string DispatchRead(string t, int index)
            => $"(char)Marshal.ReadInt16({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"(char)Marshal.ReadInt16({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call) => $"return {call}.unicode();";
        public override string CppArg(string t, string name, string defaultCpp) => $"QChar({name})";
        public override string CppVirtualParamType(string t) => "QChar";
        public override string ElementAbiCppParam(string e) => "char16_t";
        public override string ElementAbiCppRet(string e) => "char16_t";
        public override string ElementAbiCsParam(string e) => "ushort";
        public override string ElementAbiCsRet(string e) => "ushort";
        public override string ElementToCpp(string e, string expr) => $"QChar({expr})";
        public override string ElementFromCpp(string e, string expr) => $"{expr}.unicode()";
        public override string ElementRead(string e, string expr) => $"(char){expr}";
        public override string ElementArg(string e, string expr) => $"(ushort){expr}";
    }

    [TypeHandler(@"^QChar\*$", 10)]
    sealed class CharPtrHandler : TypeHandler
    {
        public override bool IsCharPtr(string t) => true;
        public override string ManagedType(string t) => "string";
        public override string NativeParamType(string t) => "[MarshalAs(UnmanagedType.LPWStr)] string";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "const char16_t*";
        public override string CppCtorParamType(string t) => "const QChar*";
        public override string DefaultValue(string t) => "null";
        public override string SignalRead(string t, int index)
            => $"(IntPtr)Marshal.ReadIntPtr({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return static_cast<void*>(const_cast<QChar*>({call}));";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"QString::fromUtf16({name}).constData()";
        public override string CppVirtualParamType(string t) => "const QChar*";
    }

    [TypeHandler(@"^QUuid$", 10)]
    sealed class UuidHandler : TypeHandler
    {
        public override bool IsUuid(string t) => true;
        public override bool IsValueType(string t) => true;
        public override string ManagedType(string t) => "Guid";
        public override string NativeParamType(string t) => "Guid";
        public override string NativeRetType(string t) => "Guid";
        public override string CppAbiType(string t) => "QsApiGuid";
        public override string CppAbiRetType(string t) => "QsApiGuid";
        public override string DefaultValue(string t) => "default";
        public override string DispatchRead(string t, int index)
            => $"ApiRuntime.ReadStruct<Guid>({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"ApiRuntime.ReadStruct<Guid>({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return QsApiQuuidToGuid({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"QsApiGuidToQuuid({name})";
        public override string CppVirtualParamType(string t) => "const QUuid&";
        public override string ElementAbiCppParam(string e) => "QsApiGuid";
        public override string ElementAbiCppRet(string e) => "QsApiGuid";
        public override string ElementAbiCsParam(string e) => "Guid";
        public override string ElementAbiCsRet(string e) => "Guid";
        public override string ElementToCpp(string e, string expr) => $"QsApiGuidToQuuid({expr})";
        public override string ElementFromCpp(string e, string expr) => $"QsApiQuuidToGuid({expr})";
        public override string ElementRead(string e, string expr) => expr;
        public override string ElementArg(string e, string expr) => expr;
    }
}
