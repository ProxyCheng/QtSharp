using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

public static partial class ApiCodeGen
{
    static readonly NativeEmitter Native = new();

    sealed class NativeEmitter : Emitter
    {
        public void WriteNativeDeclarations(StringBuilder sb, JsonElement cls)
        {
            string name = Name(cls);
            var ctors = PublishedCtors(cls);
            var methods = Methods(cls).ToList();
            var virtuals = ChainOf(Name(cls));

            var virtualOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var slot in virtuals.Where(s => s.DeclaredHere))
            {
                var v = slot.Virtual;
                string vname = Name(v);
                virtualOverloads.TryGetValue(vname, out int vseen);
                virtualOverloads[vname] = vseen + 1;
                string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
                // A private virtual has nothing to chain to *and* nothing to call, so no forwarder is
                // declared for it (the suffix counter is still advanced above, to stay aligned with the
                // other three emitters). A pure one has no base implementation either, but the C++
                // object still implements the virtual, so the stub calls it through the vtable.
                // A private or final virtual has no forwarder at all, and neither has any virtual of
                // a class that gets no generated subclass (there is no access class to reach the
                // vtable through - see WriteNativeClass). A final one is published as a plain method
                // through the ordinary forwarder instead.
                if (Has(v, ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final) || !Subclassable(cls))
                    continue;
                var ps = Params(v);
                string sig = string.Join(", ", new[] { "IntPtr self" }.Concat(ps.SelectMany(NativeParamsFor)));
                string ret = ReturnType(v);
                // The vtable call: what a wrapper of a *C++* object uses, so the object's own override
                // runs instead of the declaring class's implementation.
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_{vname}{vSuffix}_vt\", CallingConvention = CallingConvention.Cdecl)]");
                if (ret == "bool")
                    sb.AppendLine($"        [return: MarshalAs(UnmanagedType.I1)]");
                sb.AppendLine($"        internal static extern {NativeRetType(ret)} {name}_{vname}{vSuffix}_vt({sig});");
                // The declaring class's implementation: what a managed subclass chains to when it does
                // not override a non-pure virtual.
                if (!HasNoBase(v))
                {
                    sb.AppendLine();
                    sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_{vname}__base{vSuffix}\", CallingConvention = CallingConvention.Cdecl)]");
                    if (ret == "bool")
                        sb.AppendLine($"        [return: MarshalAs(UnmanagedType.I1)]");
                    sb.AppendLine($"        internal static extern {NativeRetType(ret)} {name}_{vname}__base{vSuffix}({sig});");
                }
            }

            for (int i = 0; i < ctors.Count; i++)
            {
                var ps = Params(ctors[i]).ToList();
                // A pinned buffer crosses as the address of the pin, not as the managed array the
                // marshaller would only pin for the call.
                bool pinned = TryPinnedBuffer(ps, out int dataIndex, out _, out _);
                var nativeParams = new List<string> { "IntPtr userData", "IntPtr dispatch" };
                for (int k = 0; k < ps.Count; k++)
                {
                    if (pinned && k == dataIndex)
                        nativeParams.Add("IntPtr " + SafeName(PName(ps[k])));
                    else
                        nativeParams.AddRange(NativeParamsFor(ps[k]));
                }
                string sig = string.Join(", ", nativeParams);
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_ctor{CtorSuffix(i)}\", CallingConvention = CallingConvention.Cdecl)]");
                sb.AppendLine($"        internal static extern IntPtr Ctor_{name}{CtorSuffix(i)}({sig});");
            }

            if (SubclassCtor(cls))
            {
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_ctor\", CallingConvention = CallingConvention.Cdecl)]");
                sb.AppendLine($"        internal static extern IntPtr Ctor_{name}(IntPtr userData, IntPtr dispatch);");
            }

            foreach (var field in FieldList(cls))
            {
                string fieldName = Name(field);
                string fieldType = field.GetProperty("type").GetString();
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_get_{fieldName}\", CallingConvention = CallingConvention.Cdecl)]");
                sb.AppendLine($"        internal static extern {ElemAbiCsRet(fieldType)} {name}_get_{fieldName}(IntPtr self);");
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_set_{fieldName}\", CallingConvention = CallingConvention.Cdecl)]");
                sb.AppendLine($"        internal static extern void {name}_set_{fieldName}(IntPtr self, {ElemAbiCsParam(fieldType)} value);");
            }

            if (HasClass(cls, ApiModel.ClassFlags.Owned) && (ctors.Count > 0 || SubclassCtor(cls)))
            {
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_dtor\", CallingConvention = CallingConvention.Cdecl)]");
                sb.AppendLine($"        internal static extern void Destroy_{name}(IntPtr self);");
            }

            foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
            {
                string baseName = SimpleName(b);
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}__as_{baseName}\", CallingConvention = CallingConvention.Cdecl)]");
                sb.AppendLine($"        internal static extern IntPtr {name}__as_{baseName}(IntPtr self);");
            }

            var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in methods)
            {
                string mname = Name(m);
                overloadCounts.TryGetValue(mname, out int seen);
                overloadCounts[mname] = seen + 1;
                string entrySuffix = seen == 0 ? "" : (seen + 1).ToString();

                var ps = Params(m);
                var nativeParams = new List<string>();
                if (!IsStatic(m))
                    nativeParams.Add("IntPtr self");
                nativeParams.AddRange(ps.SelectMany(NativeParamsFor));
                string sig = string.Join(", ", nativeParams);
                string ret = ReturnType(m);
                sb.AppendLine();
                sb.AppendLine($"        [SuppressUnmanagedCodeSecurity, DllImport(Library, EntryPoint = \"qs_api_{name}_{mname}{entrySuffix}\", CallingConvention = CallingConvention.Cdecl)]");
                if (ret == "bool")
                    sb.AppendLine($"        [return: MarshalAs(UnmanagedType.I1)]");
                sb.AppendLine($"        internal static extern {NativeRetType(ret)} {name}_{mname}{entrySuffix}({sig});");
            }
        }

        public void WriteNativeClass(StringBuilder sb, JsonElement cls)
        {
            string name = Name(cls);
            var ctors = PublishedCtors(cls);
            var methods = Methods(cls).ToList();
            var virtuals = ChainOf(Name(cls));

            var declaredVirtuals = virtuals.Where(s => s.DeclaredHere).ToList();

            // The native subclass exists to install the vtable hooks of a managed subclass. A class
            // C++ would not let us derive from (private destructor: QClipboard, QStyleHints) simply
            // gets no subclass - its methods are still reachable through the free forwarders below.
            bool subclassable = Subclassable(cls);
            bool subclassCtor = SubclassCtor(cls);
            if (subclassable)
            {
            sb.AppendLine($"class QsApi_{name} : public {CppClass(name)}");
            sb.AppendLine("{");
            sb.AppendLine("public:");
            // One constructor per model constructor; the dispatch pair comes first so that the
            // generated forwarders below stay a mechanical translation. The parameters use the
            // C ABI types and are converted where the base constructor is called.
            for (int i = 0; i < ctors.Count; i++)
            {
                var ps = Params(ctors[i]);
                // The native subclass constructor keeps the real C++ parameter types, because
                // two constructors that differ only by enum (QColor::Spec vs Qt::GlobalColor)
                // would otherwise collapse into one signature through the flat C ABI.
                string sig = string.Join(", ", ps.Select(p => $"{CppCtorParamTypeFor(p)} {NativeName(PName(p))}"));
                string args = string.Join(", ", ps.Select(CppCtorArgFor));
                sb.AppendLine($"    QsApi_{name}(void* userData, QsApiDispatch dispatch{(ps.Count > 0 ? ", " + sig : "")})");
                sb.AppendLine($"        : {CppClass(name)}({args}), m_userData(userData), m_dispatch(dispatch) {{}}");
            }
            if (subclassCtor)
            {
                // The abstract class has no constructor of its own in the model; this is the one a
                // managed subclass uses, and it is concrete because every pure virtual this class
                // inherits is an override point we install.
                sb.AppendLine($"    QsApi_{name}(void* userData, QsApiDispatch dispatch)");
                sb.AppendLine($"        : {CppClass(name)}(), m_userData(userData), m_dispatch(dispatch) {{}}");
            }
            sb.AppendLine();
            // Qt declares most override points protected, and a protected base member is only
            // reachable through an object of the derived type - hence these members. Only the
            // slots this class declares need one: an inherited slot chains through the forwarder
            // that the class declaring it already generated.
            //
            // NOTE: the `self` parameter is typed as the generated subclass even though the object
            // may be an instance of a *further* generated subclass (a managed QLineEdit subclass
            // chains through the forwarder QObject generated). That is only sound because every
            // chain in this model puts the declaring base at offset 0 of the complete object, so
            // the cast is the identity; a fully conforming version would need the forwarder to be
            // a friend of a helper derived from the declaring class.
            var declaredOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < declaredVirtuals.Count; i++)
            {
                var v = declaredVirtuals[i].Virtual;
                string vname = Name(v);
                declaredOverloads.TryGetValue(vname, out int vseen);
                declaredOverloads[vname] = vseen + 1;
                string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
                if (Has(v, ApiModel.VirtualFlags.Pure | ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final))
                    continue;
                var ps = Params(v);
                // The helper takes the flat C ABI parameters and converts them itself. A conversion
                // spells the C++ type it aims at, and Qt declares some of those *protected* nested
                // types (QAbstractItemView::CursorAction), which only a member of the class - this
                // helper - may name. Doing the cast here is what keeps the exported forwarder (a free
                // function, which has no such access) down to the ABI types.
                string sig = string.Join(", ", ps.SelectMany(CppParamsFor));
                string args = string.Join(", ", ps.SelectMany(CppArgsFor));
                string ret = ReturnType(v);
                sb.AppendLine($"    static {VirtualReturnCpp(v)} CallBase_{vname}{vSuffix}(QsApi_{name}* self{(ps.Count > 0 ? ", " + sig : "")})");
                sb.AppendLine("    {");
                foreach (string line in VirtualCallBody(
                             v, $"self->{CppClass(name)}::{vname}({args})", r => $"return {r};"))
                    sb.AppendLine("        " + line);
                sb.AppendLine("    }");
            }
            // The protected members the model publishes (createIndex, beginInsertRows, ...): C++ lets
            // only a member call them, so the forwarder goes through one of these. The overload
            // suffix is counted exactly like the forwarder loop below counts it.
            var protectedOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in methods)
            {
                string pname = Name(m);
                protectedOverloads.TryGetValue(pname, out int pseen);
                protectedOverloads[pname] = pseen + 1;
                if (!IsProtected(m) || IsStatic(m))
                    continue;
                string pSuffix = pseen == 0 ? "" : (pseen + 1).ToString();
                var pps = Params(m);
                // The helper takes the flat C ABI parameters and does the same conversion the free
                // forwarders do, so no C++ reference type has to be spelled here (QDataStream arrives
                // as void* and is dereferenced inside - spelling it as a parameter would mean knowing
                // whether the base declared it const, which the model does not record).
                string psig = string.Join(", ", pps.SelectMany(CppParamsFor));
                string pargs = string.Join(", ", pps.SelectMany(CppArgsFor));
                string pret = ReturnType(m);
                sb.AppendLine($"    static {pret} Call{Pascal(pname)}{pSuffix}(QsApi_{name}* self{(pps.Count > 0 ? ", " + psig : "")})");
                sb.AppendLine("    {");
                sb.AppendLine($"        {(pret == "void" ? "" : "return ")}self->{pname}({pargs});");
                sb.AppendLine("    }");
            }
            sb.AppendLine();
            sb.AppendLine("protected:");
            for (int i = 0; i < virtuals.Count; i++)
            {
                // A blocked slot is not installed: Qt's own (private) implementation is the only
                // correct one, and overriding it is what broke QStringListModel's indices. The slot id
                // stays the position in the chain, so the numbering still matches the dispatch switch.
                if (virtuals[i].Blocked)
                    continue;
                var v = virtuals[i].Virtual;
                var ps = Params(v);
                string sig = string.Join(", ", ps.Select(p => $"{CppVirtualParamType(p)} {NativeName(PName(p))}"));
                string ret = ReturnType(v);
                // The override must repeat Qt's declaration exactly; the model type is the normalized
                // managed one (a const pointer or a typedef differs).
                string retCpp = VirtualReturnCpp(v);
                sb.AppendLine($"    {retCpp} {Name(v)}({sig}){ConstSuffix(v)} override");
                sb.AppendLine("    {");
                // A virtual may have a parameter named `result` (QWidget::nativeEvent's qintptr *result),
                // so the local the dispatch fills is spelled with a reserved prefix.
                if (ret != "void")
                    sb.AppendLine($"        {retCpp} qs_api_result = {{}};");
                var argv = new List<string> { ret == "void" ? "nullptr" : "&qs_api_result" };
                argv.AddRange(ps.Select(p => DispatchArgStore(PType(p), NativeName(PName(p)))));
                sb.AppendLine($"        void* argv[] = {{ {string.Join(", ", argv)} }};");
                sb.AppendLine($"        m_dispatch(m_userData, {i + 1}, argv);");
                if (ret != "void")
                    sb.AppendLine("        return qs_api_result;");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
            sb.AppendLine("private:");
            sb.AppendLine("    void* m_userData;");
            sb.AppendLine("    QsApiDispatch m_dispatch;");
            sb.AppendLine("};");
            sb.AppendLine();
            }

            // An abstract class with no constructor of its own still needs one entry point: a managed
            // subclass has to be able to build the native subclass that installs the vtable hooks.
            if (subclassCtor)
            {
                sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{name}_ctor(void* userData, QsApiDispatch dispatch)");
                sb.AppendLine("{");
                sb.AppendLine($"    return new QsApi_{name}(userData, dispatch);");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            for (int i = 0; i < ctors.Count && subclassable; i++)
            {
                var ps = Params(ctors[i]);
                string sig = string.Join(", ", new[] { "void* userData", "QsApiDispatch dispatch" }
                    .Concat(ps.SelectMany(CppParamsFor)));
                string args = string.Join(", ", new[] { "userData", "dispatch" }
                    .Concat(ps.SelectMany(CppArgsFor)));
                sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{name}_ctor{CtorSuffix(i)}({sig})");
                sb.AppendLine("{");
                sb.AppendLine($"    return new QsApi_{name}({args});");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            if (subclassable && HasClass(cls, ApiModel.ClassFlags.Owned) && (ctors.Count > 0 || subclassCtor))
            {
                sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{name}_dtor(void* self)");
                sb.AppendLine("{");
                if (DerivesFromQObject(Ref(cls)))
                {
                    // A QObject's owner can move to Qt at runtime (setParent, or a layout adopting the
                    // widget), and deleting something Qt owns is a double free - the crash the legacy
                    // pipeline hit with QCommandLinkButton. Qt is asked first; an object it does not
                    // own is still released, so nothing leaks.
                    sb.AppendLine($"    {CppClass(name)}* object = static_cast<{CppClass(name)}*>(self);");
                    sb.AppendLine("    if (object->parent())");
                    sb.AppendLine("        return;");
                    sb.AppendLine("    delete object;");
                }
                else
                {
                    sb.AppendLine($"    delete static_cast<{CppClass(name)}*>(self);");
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }

            foreach (var b in cls.GetProperty("bases").EnumerateArray().Select(x => x.GetString()).Skip(1))
            {
                string baseEntry = SimpleName(b);
                sb.AppendLine($"extern \"C\" QS_API_EXPORT void* qs_api_{name}__as_{baseEntry}(void* self)");
                sb.AppendLine("{");
                sb.AppendLine($"    return static_cast<{b}*>(static_cast<{CppClass(name)}*>(self));");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            // A data member is inline storage, so its accessors are generated rather than exported.
            foreach (var field in FieldList(cls))
            {
                string fieldName = Name(field);
                string fieldType = field.GetProperty("type").GetString();
                string access = $"static_cast<{CppClass(name)}*>(self)->{fieldName}";
                // A member can be declared `const QWidget *widget`, so a pointer needs its const taken
                // off before it fits the void* slot.
                string getter = IsFlags(fieldType)
                    ? $"static_cast<int>(({access}).toInt())"
                    : IsPointerElem(fieldType) || IsArray(fieldType)
                        ? $"const_cast<void*>(static_cast<const void*>({access}))"
                        : ElemFromCpp(fieldType, access);
                string setter = IsFlags(fieldType)
                    ? $"{fieldType}::fromInt(value)"
                    : ElemToCpp(fieldType, "value");
                sb.AppendLine($"extern \"C\" QS_API_EXPORT {ElemAbiCppRet(fieldType)} qs_api_{name}_get_{fieldName}(void* self)");
                sb.AppendLine("{");
                sb.AppendLine($"    return {getter};");
                sb.AppendLine("}");
                sb.AppendLine($"extern \"C\" QS_API_EXPORT void qs_api_{name}_set_{fieldName}(void* self, {ElemAbiCppParam(fieldType)} value)");
                sb.AppendLine("{");
                sb.AppendLine($"    static_cast<{CppClass(name)}*>(self)->{fieldName} = {setter};");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in methods)
            {
                string mname = Name(m);
                overloadCounts.TryGetValue(mname, out int seen);
                overloadCounts[mname] = seen + 1;
                string entrySuffix = seen == 0 ? "" : (seen + 1).ToString();

                var ps = Params(m);
                var nativeParams = new List<string>();
                if (!IsStatic(m))
                    nativeParams.Add("void* self");
                nativeParams.AddRange(ps.SelectMany(CppParamsFor));
                string sig = string.Join(", ", nativeParams);
                string ret = ReturnType(m);
                string args = string.Join(", ", ps.SelectMany(CppArgsFor));
                // The protected helper takes the flat C ABI parameters and converts them itself, so it
                // is handed the raw values rather than the already-converted arguments.
                string rawArgs = string.Join(", ", ps.SelectMany(AbiArgs));
                string call;
                if (IsStatic(m))
                    call = $"{CppClass(name)}::{mname}({args})";
                else if (IsProtected(m))
                    call = $"QsApi_{name}::Call{Pascal(mname)}{entrySuffix}(static_cast<QsApi_{name}*>(self)"
                         + (rawArgs.Length > 0 ? ", " + rawArgs : "") + ")";
                else
                    call = $"static_cast<{CppClass(name)}*>(self)->{mname}({args})";
                sb.AppendLine($"extern \"C\" QS_API_EXPORT {NativeRetCpp(ret)} qs_api_{name}_{mname}{entrySuffix}({sig})");
                sb.AppendLine("{");
                foreach (string line in NativeBody(m, call))
                    sb.AppendLine("    " + line);
                sb.AppendLine("}");
                sb.AppendLine();
            }

            // Explicitly qualified call to the base implementation of each declared virtual, so a
            // managed override can chain to it (and so the default override does not disable Qt's
            // own handling). The qualification also keeps this from dispatching back into the
            // override, which would recurse forever.
            // The C ABI forwarder a pure virtual's C# stub calls when the object is *not* one of ours.
            // It needs no dispatch hooks - it is an ordinary virtual call on the real object - but Qt
            // declares some of those virtuals protected (QAbstractButton::paintEvent,
            // QGraphicsLayoutItem::sizeHint), and neither a free function nor a plain cast may call
            // one. A derived class with no overrides of its own can: the call below is virtual, so it
            // lands on whatever the object really implements, and the cast is the same identity cast
            // the chaining forwarders already rely on.
            bool anyForwardable = declaredVirtuals.Any(s => !Has(s.Virtual, ApiModel.VirtualFlags.NoBase));
            if (anyForwardable && subclassable)
            {
                sb.AppendLine($"class QsApiAccess_{name} : public {CppClass(name)}");
                sb.AppendLine("{");
                sb.AppendLine("public:");
                var accessOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var slot in declaredVirtuals)
                {
                    var v = slot.Virtual;
                    string vname = Name(v);
                    accessOverloads.TryGetValue(vname, out int aseen);
                    accessOverloads[vname] = aseen + 1;
                    string aSuffix = aseen == 0 ? "" : (aseen + 1).ToString();
                    if (Has(v, ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final))
                        continue;
                    var ps = Params(v);
                    // The helper takes and returns the same flat C ABI types the export does, so the
                    // conversions (a boxed value arrives as a pointer, a QString as char16_t*) happen
                    // in exactly one place.
                    string sig = string.Join(", ", ps.SelectMany(CppParamsFor));
                    string args = string.Join(", ", ps.SelectMany(CppArgsFor));
                    string ret = ReturnType(v);
                    string call = $"static_cast<QsApiAccess_{name}*>(self)->{vname}({args})";
                    sb.AppendLine($"    static {NativeRetCpp(ret)} Call_{vname}{aSuffix}({CppClass(name)}* self{(sig.Length > 0 ? ", " + sig : "")})");
                    sb.AppendLine("    {");
                    foreach (string line in VirtualCallBody(v, call, r => NativeRetExpr(ret, r)))
                        sb.AppendLine("        " + line);
                    sb.AppendLine("    }");
                }
                sb.AppendLine("};");
                sb.AppendLine();
            }

            // The suffix counter runs over every declared slot, exactly like the DllImport emitter, so
            // the two sides agree on the name even when a class declares several overloads.
            var pureOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var slot in declaredVirtuals)
            {
                var v = slot.Virtual;
                string vname = Name(v);
                pureOverloads.TryGetValue(vname, out int pseen);
                pureOverloads[vname] = pseen + 1;
                string pSuffix = pseen == 0 ? "" : (pseen + 1).ToString();
                if (Has(v, ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final) || !subclassable)
                    continue;

                var pureParams = Params(v);
                string pureSig = string.Join(", ", new[] { "void* self" }.Concat(pureParams.SelectMany(CppParamsFor)));
                string pureArgs = string.Join(", ", new[] { $"static_cast<{CppClass(name)}*>(self)" }
                    .Concat(pureParams.SelectMany(AbiArgs)));
                string pureRet = ReturnType(v);
                // The access helper already returns the C ABI value, so this only forwards it.
                string forwarded = $"QsApiAccess_{name}::Call_{vname}{pSuffix}({pureArgs})";
                sb.AppendLine($"extern \"C\" QS_API_EXPORT {NativeRetCpp(pureRet)} qs_api_{name}_{vname}{pSuffix}_vt({pureSig})");
                sb.AppendLine("{");
                sb.AppendLine("    " + (pureRet == "void" ? forwarded + ";" : "return " + forwarded + ";"));
                sb.AppendLine("}");
                sb.AppendLine();
            }

            var virtualOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < declaredVirtuals.Count && subclassable; i++)
            {
                var v = declaredVirtuals[i].Virtual;
                string vname = Name(v);
                virtualOverloads.TryGetValue(vname, out int vseen);
                virtualOverloads[vname] = vseen + 1;
                string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
                if (Has(v, ApiModel.VirtualFlags.Pure | ApiModel.VirtualFlags.NoBase | ApiModel.VirtualFlags.Final))
                    continue;

                var ps = Params(v);
                string sig = string.Join(", ", new[] { "void* self" }
                    .Concat(ps.SelectMany(CppParamsFor)));
                string ret = ReturnType(v);
                // The conversions happen in CallBase_ - a member of the generated subclass, which may
                // name the protected nested types some of those conversions target. This forwarder
                // only carries the ABI values across.
                string baseArgs = string.Join(", ", new[] { $"static_cast<QsApi_{name}*>(self)" }
                    .Concat(ps.SelectMany(AbiArgs)));
                sb.AppendLine($"extern \"C\" QS_API_EXPORT {NativeRetCpp(ret)} qs_api_{name}_{vname}__base{vSuffix}({sig})");
                sb.AppendLine("{");
                sb.AppendLine("    " + NativeRetExpr(ret, $"QsApi_{name}::CallBase_{vname}{vSuffix}({baseArgs})"));
                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        // What the generated wrapper takes for one parameter. An out parameter Qt fills through a
        // non-const pointer stays that pointer; a non-const reference (`int &pos`) has to become a
        // pointer the C ABI can carry, and the call passes its pointee.

    }
}
