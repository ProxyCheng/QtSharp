namespace QtSharp.Generator
{
public sealed partial class TypeSystem
{
    [TypeHandler(@"^void$", 0)]
    sealed class VoidHandler : TypeHandler
    {
        public override bool IsVoid(string t) => true;
        public override string ManagedType(string t) => "void";
        public override string NativeRetType(string t) => "void";
        public override string CppAbiRetType(string t) => "void";
        public override string NativeRetExpr(string t, string call) => call + ";";
    }

    [TypeHandler(@"^(?:bool|int|float|double|long long|unsigned long long|unsigned int|short|unsigned short|long|unsigned long|char|uchar|unsigned char|char16_t)$", 20)]
    sealed class ScalarHandler : TypeHandler
    {
        public override bool IsScalar(string t) => true;
        public override bool IsValueType(string t) => true;
        public override string NativeParamType(string t) => CSharpScalar(t) ?? t;
        public override string NativeRetType(string t) => CSharpScalar(t) ?? t;
        public override string CppAbiRetType(string t) => CppAbiScalar(t);

        public override string DispatchRead(string t, int index)
        {
            string slot = ArgvSlot(index);
            return t switch
            {
                "bool" => $"Marshal.ReadByte({slot}) != 0",
                "int" => $"Marshal.ReadInt32({slot})",
                "double" => $"BitConverter.Int64BitsToDouble(Marshal.ReadInt64({slot}))",
                "long long" => $"Marshal.ReadInt64({slot})",
                "unsigned long long" => $"(ulong)Marshal.ReadInt64({slot})",
                "unsigned int" or "unsigned long" => $"(uint)Marshal.ReadInt32({slot})",
                "short" => $"Marshal.ReadInt16({slot})",
                "unsigned short" or "char16_t" => $"({CSharpScalar(t)})Marshal.ReadInt16({slot})",
                "long" => $"Marshal.ReadInt32({slot})",
                "unsigned char" => $"Marshal.ReadByte({slot})",
                "char" => $"(sbyte)Marshal.ReadByte({slot})",
                _ => slot,
            };
        }

        public override string SignalRead(string t, int index)
        {
            string slot = ArgvSlot(index);
            return t switch
            {
                "bool" => $"Marshal.ReadByte({slot}) != 0",
                "int" => $"Marshal.ReadInt32({slot})",
                "double" => $"BitConverter.Int64BitsToDouble(Marshal.ReadInt64({slot}))",
                "float" => $"BitConverter.Int32BitsToSingle(Marshal.ReadInt32({slot}))",
                "long long" => $"Marshal.ReadInt64({slot})",
                "unsigned long long" => $"(ulong)Marshal.ReadInt64({slot})",
                "unsigned int" or "unsigned long" => $"(uint)Marshal.ReadInt32({slot})",
                "short" => $"Marshal.ReadInt16({slot})",
                "unsigned short" or "char16_t" => $"({CSharpScalar(t)})Marshal.ReadInt16({slot})",
                "long" => $"Marshal.ReadInt32({slot})",
                "unsigned char" => $"Marshal.ReadByte({slot})",
                "char" => $"(sbyte)Marshal.ReadByte({slot})",
                _ => slot,
            };
        }

        public override string NativeRetExpr(string t, string call)
            => t is "long" or "unsigned long"
                ? $"return static_cast<{CppAbiScalar(t)}>({call});"
                : $"return {call};";

        public override string CppArg(string t, string name, string defaultCpp)
            => t is "long" or "unsigned long" ? $"static_cast<{t}>({name})" : name;

        public override string ElementAbiCppParam(string e) => CppAbiScalar(e);
        public override string ElementAbiCppRet(string e) => CppAbiScalar(e);
        public override string ElementAbiCsParam(string e) => ManagedType(e);
        public override string ElementAbiCsRet(string e) => ManagedType(e);
        public override string ElementToCpp(string e, string expr) => expr;
        public override string ElementFromCpp(string e, string expr) => expr;
        public override string ElementRead(string e, string expr) => expr;
        public override string ElementArg(string e, string expr) => expr;
    }

    [TypeHandler(@"^[\w:]+$", 90)]
    sealed class EnumHandler : TypeHandler
    {
        public override bool CanHandle(string t) => EnumNames?.Contains(t) == true;
        public override bool IsEnum(string t) => true;
        public override bool IsFlags(string t) => FlagsEnums?.Contains(t) == true;
        public override bool IsValueType(string t) => true;
        public override string ManagedType(string t) => CsEnum(t);
        public override string ManagedElement(string t) => CsEnum(t);
        public override string NativeParamType(string t) => "int";
        public override string NativeRetType(string t) => "int";
        public override string CppAbiType(string t) => "int";
        public override string CppAbiRetType(string t) => "int";
        public override string CppCtorParamType(string t) => CppTypeName(t);
        public override string ArgToNative(string t, string name) => $"(int){name}";
        public override string RetToManaged(string t, string call) => $"({CsEnum(t)}){call}";
        public override string DispatchRead(string t, int index)
            => $"({CsEnum(t)})Marshal.ReadInt32({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"({CsEnum(t)})Marshal.ReadInt32({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => IsFlags(t)
                ? $"return static_cast<int>({call}.toInt());"
                : $"return static_cast<int>({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => IsFlags(t)
                ? $"{CppTypeName(t)}::fromInt({name})"
                : $"static_cast<{CppTypeName(t)}>({name})";
        public override string ElementAbiCppParam(string e) => "int";
        public override string ElementAbiCppRet(string e) => "int";
        public override string ElementAbiCsParam(string e) => "int";
        public override string ElementAbiCsRet(string e) => "int";
        public override string ElementToCpp(string e, string expr) => $"static_cast<{CppTypeName(e)}>({expr})";
        public override string ElementFromCpp(string e, string expr) => $"static_cast<int>({expr})";
        public override string ElementRead(string e, string expr) => $"({CsEnum(e)}){expr}";
        public override string ElementArg(string e, string expr) => $"(int){expr}";
    }

    [TypeHandler(@"^TimeSpan$", 10)]
    sealed class TimeSpanHandler : TypeHandler
    {
        public override bool IsTimeSpan(string t) => true;
        public override bool IsValueType(string t) => true;
        public override string NativeParamType(string t) => "long";
        public override string NativeRetType(string t) => "long";
        public override string CppAbiType(string t) => "long long";
        public override string CppAbiRetType(string t) => "long long";
        public override string ArgToNative(string t, string name) => $"(long)({name}.Ticks * 100)";
        public override string RetToManaged(string t, string call) => $"TimeSpan.FromTicks({call} / 100)";
        public override string DispatchRead(string t, int index)
            => $"TimeSpan.FromTicks(Marshal.ReadInt64({ArgvSlot(index)}) / 100)";
        public override string SignalRead(string t, int index)
            => $"TimeSpan.FromTicks(Marshal.ReadInt64({ArgvSlot(index)}) / 100)";
        public override string NativeRetExpr(string t, string call)
            => $"return static_cast<long long>(std::chrono::duration_cast<std::chrono::nanoseconds>({call}).count());";
    }
}
}
