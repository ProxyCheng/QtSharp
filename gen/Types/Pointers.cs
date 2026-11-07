public static partial class ApiCodeGen
{
    [TypeHandler(@"^[\w:]+$", 80)]
    sealed class StructHandler : TypeHandler
    {
        public override bool CanHandle(string t) => ApiTypes.IsStruct(t);
        public override bool IsStruct(string t) => true;
        public override bool IsBoxed(string t) => true;
        public override bool IsValueType(string t) => true;
        public override string ManagedType(string t) => CsTypeName(t);
        public override string ManagedElement(string t) => CsTypeName(t);
        public override string NativeParamType(string t) => CsTypeName(t);
        public override string NativeRetType(string t) => CsTypeName(t);
        public override string CppAbiType(string t) => StructPod(t);
        public override string CppAbiRetType(string t) => StructPod(t);
        public override string CppCtorParamType(string t) => $"const {CppTypeName(t)}&";
        public override string CppVirtualParamType(string t) => $"const {CppTypeName(t)}&";
        public override string DispatchRead(string t, int index)
            => $"ApiRuntime.ReadStruct<{CsTypeName(t)}>({ArgvSlot(index)})";
        public override string SignalRead(string t, int index)
            => $"ApiRuntime.ReadStruct<{CsTypeName(t)}>({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return {StructToPod(t, call)};";
        public override string ElementAbiCppParam(string e) => StructPod(e);
        public override string ElementAbiCppRet(string e) => StructPod(e);
        public override string ElementAbiCsParam(string e) => ManagedType(e);
        public override string ElementAbiCsRet(string e) => ManagedType(e);
        public override string ElementToCpp(string e, string expr) => StructFromPod(e, expr);
        public override string ElementFromCpp(string e, string expr) => StructToPod(e, expr);
        public override string ElementRead(string e, string expr) => expr;
        public override string ElementArg(string e, string expr) => expr;
    }

    // A pointer to a runtime struct is an array in parameter position (T[]) and an address in
    // return position (the view Qt hands back).
    [TypeHandler(@"^(.+)\*$", 50)]
    sealed class StructArrayHandler : TypeHandler
    {
        public override bool CanHandle(string t)
            => ApiTypes.IsStruct(t.Substring(0, t.Length - 1));
        public override bool IsStructPointer(string t) => true;
        public override string ManagedType(string t)
            => ApiCodeGen.ManagedElement(t.Substring(0, t.Length - 1)) + "[]";
        public override string NativeParamType(string t) => ManagedType(t);
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => StructArrayAbiType(t);
        public override string CppCtorParamType(string t) => StructArrayAbiType(t);
        public override string DefaultValue(string t) => "default";
        public override string NativeRetExpr(string t, string call)
            => $"return static_cast<void*>(const_cast<{RefValueType(t)}*>({call}));";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"reinterpret_cast<{RefValueType(t)}*>({name})";
    }

    [TypeHandler(@"^(.+)\*$", 60)]
    sealed class ScalarArrayHandler : TypeHandler
    {
        public override bool CanHandle(string t)
            => IsScalarName(t.Substring(0, t.Length - 1));
        public override bool IsScalarArray(string t) => true;
        public override bool IsByteBuffer(string t) => t == "uchar*";
        public override string ManagedType(string t)
            => IsByteBuffer(t)
                ? "byte[]"
                : ApiCodeGen.ManagedElement(t.Substring(0, t.Length - 1)) + "[]";
        public override string NativeParamType(string t) => ManagedType(t);
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t)
            => IsByteBuffer(t) ? "unsigned char*" : "const " + t;
        public override string CppCtorParamType(string t) => CppAbiType(t);
        public override string DefaultValue(string t) => "null";
    }

    [TypeHandler(@"^(.+)\*$", 70)]
    sealed class ClassPointerHandler : TypeHandler
    {
        public override bool CanHandle(string t)
            => ClassNames?.Contains(t.Substring(0, t.Length - 1)) == true;
        public override bool IsClassPointer(string t) => true;
        public override string ManagedType(string t) => CsTypeName(t.Substring(0, t.Length - 1));
        public override string ManagedElement(string t) => CsTypeName(t.Substring(0, t.Length - 1));
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => CppTypeName(t);
        public override string ArgToNative(string t, string name) => $"{name}?.Handle ?? IntPtr.Zero";
        public override string RetToManaged(string t, string call)
            => $"{CsTypeName(t.Substring(0, t.Length - 1))}.FromHandle({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
            => ClassPtrRead(t.Substring(0, t.Length - 1), ArgvSlot(index));
        public override string SignalRead(string t, int index)
            => ClassPtrRead(t.Substring(0, t.Length - 1), $"Marshal.ReadIntPtr({ArgvSlot(index)})");
        public override string NativeRetExpr(string t, string call)
            => $"return static_cast<void*>(const_cast<{CppTypeName(t.Substring(0, t.Length - 1))}*>({call}));";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"static_cast<{CppTypeName(t.Substring(0, t.Length - 1))}*>({name})";
        public override string CppVirtualParamType(string t)
            => CppTypeName(t.Substring(0, t.Length - 1)) + "*";
    }

    [TypeHandler(@"^(.+)\*$", 100)]
    sealed class OpaquePointerHandler : TypeHandler
    {
        public override bool IsOpaquePointer(string t) => true;
        public override string ManagedType(string t) => "IntPtr";
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => CppTypeName(t);
        public override string SignalRead(string t, int index)
            => $"(IntPtr)Marshal.ReadIntPtr({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return static_cast<void*>(const_cast<{CppTypeName(t.Substring(0, t.Length - 1))}*>({call}));";
        public override string CppArg(string t, string name, string defaultCpp)
            => $"static_cast<{CppTypeName(t.Substring(0, t.Length - 1))}*>({name})";
        public override string CppVirtualParamType(string t)
            => CppTypeName(t.Substring(0, t.Length - 1)) + "*";
    }

    [TypeHandler(@"^[\w:]+$", 100)]
    sealed class BoxedClassHandler : TypeHandler
    {
        public override bool CanHandle(string t) => ClassNames?.Contains(t) == true;
        public override bool IsBoxed(string t) => true;
        public override string ManagedType(string t) => CsTypeName(t);
        public override string ManagedElement(string t) => CsTypeName(t);
        public override string NativeParamType(string t) => "IntPtr";
        public override string NativeRetType(string t) => "IntPtr";
        public override string CppAbiType(string t) => "void*";
        public override string CppCtorParamType(string t) => $"const {CppTypeName(t)}&";
        public override string CppVirtualParamType(string t) => $"const {CppTypeName(t)}&";
        public override string ArgToNative(string t, string name) => $"{name}?.Handle ?? IntPtr.Zero";
        public override string RetToManaged(string t, string call) => IsOwned(t)
            ? $"{CsTypeName(t)}.FromOwnedHandle({call})"
            : $"{CsTypeName(t)}.FromTransientHandle({call})";
        public override string DefaultValue(string t) => "null";
        public override string DispatchRead(string t, int index)
        {
            string slot = ArgvSlot(index);
            if (IsNonCopyable(t))
                return $"{CsTypeName(t)}.FromTransientHandle({slot})";
            if (VirtualBoxedParams().Contains(t))
                return $"{CsTypeName(t)}.FromOwnedHandle(ApiNative.Clone_{ListStem(t)}({slot}))";
            return $"{CsTypeName(t)}.FromHandle({slot})";
        }
        public override string SignalRead(string t, int index)
            => $"{CsTypeName(t)}.FromTransientHandle({ArgvSlot(index)})";
        public override string NativeRetExpr(string t, string call)
            => $"return new {CppTypeName(t)}({call});";
        public override string CppArg(string t, string name, string defaultCpp)
            => BoxedCppArg(t, name, defaultCpp);
    }
}
