using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

using Members = QtSharp.Generator.BindingModel.Members;
namespace QtSharp.Generator
{
    public sealed class CSharpClassEmitter : BindingEmitter
    {
        public CSharpClassEmitter(GeneratorContext context) : base(context)
        {
        }

        public void WriteClass(StringBuilder sb, JsonElement cls)
        {
            string name = B.Name(cls);
            bool boxed = B.HasClass(cls, ApiModel.ClassFlags.Owned);
            var bases = cls.GetProperty("bases").EnumerateArray().Select(b => b.GetString()).ToList();
            // The model records a Qt3D base by its qualified C++ name; C# names the class by its
            // simple one, and the inherited-member analysis resolves either.
            string baseRef = bases.Count > 0 ? bases[0] : null;
            string baseName = baseRef != null ? B.CsTypeName(baseRef) : null;
            var extraBases = bases.Skip(1).ToList();
            var ctors = B.PublishedCtors(cls);
            var methods = B.Methods(cls).ToList();
            var virtuals = B.ChainOf(B.Name(cls));
            var signals = cls.GetProperty("signals").EnumerateArray().ToList();
            // The members the base chain already publishes: a member of this class that collides has
            // to say `new`, or the compiler warns that it hides the inherited one (CS0108).
            var inherited = baseRef != null ? B.InheritedMembers(baseRef) : new Members();

            // A class we allocate ourselves needs a release path; Qt-owned objects must not get
            // one, or the GC would free an object the parent still owns (ownership PoC rule 1).
            bool owned = boxed && (ctors.Count > 0 || B.SubclassCtor(cls));
            var declared = new List<string>();
            if (baseName != null)
                declared.Add(baseName);
            if (owned)
                declared.Add("IDisposable");
            // Only the root class implements them; every descendant inherits the interface and the
            // invalidation flag with it. IQtHandle is what lets a C#-declared signal/property carry a
            // bound class without the runtime knowing the type at compile time.
            if (baseName == null)
            {
                declared.Add("IQtObjectView");
                // QObject is the one root with a meta-object, so it is what the runtime's
                // [QtSignal]/[QtProperty] support can attach to. The other roots (QGraphicsItem,
                // QModelIndex) are plain wrappers and only carry a handle.
                declared.Add(name == "QObject" ? "IQtObject" : "IQtHandle");
            }
            // A value type Qt itself can convert into a QVariant carries the marker the runtime's
            // QVariant dispatch looks for. It is an interface, not a constructor: C# keeps the
            // conversion in QVariant(object), where QMetaType decides how to copy the value.
            if (B.ConvertsToVariant(cls))
                declared.Add("IQtVariantValue");
            string ifaces = declared.Count > 0 ? " : " + string.Join(", ", declared) : "";

            sb.AppendLine($"    [GeneratedCode(\"qtgen\", \"{BindingModel.GeneratedByVersion}\")]");
            string partial = B.handWrittenParts.Contains(name) ? "partial " : "";
            // QVariant is the bridge, not API: every signature says object, the type table and the
            // marshaller live in this assembly, and nothing outside it should name the class.
            string visibility = name == "QVariant" ? "internal" : "public";
            sb.AppendLine($"    {visibility} {partial}class {name}{ifaces}");
            sb.AppendLine("    {");
            if (B.ConvertsToVariant(cls))
            {
                // The C++ type name is what QMetaType::type() resolves; the managed name can differ
                // (Qt3D's namespaced classes), so the model's cppName is the one to publish.
                sb.AppendLine($"        string IQtVariantValue.QtTypeName => {B.Quoted(B.CppClass(name))};");
                sb.AppendLine();
            }
            if (baseName == null)
            {
                // The handle is behind a property so that a wrapper whose native object Qt destroyed
                // (or that was disposed) fails loudly instead of calling into freed memory - the same
                // contract PySide has, where a deleted C++ object raises RuntimeError.
                // internal, not private: a derived class's Dispose works on them (a private field is
                // inherited but not reachable).
                // protected internal, not internal: a class generated into a *separate* extension
                // assembly (QtSharp.Charts) derives from this one and needs both fields. Widening is
                // enough - the meaning and the behaviour are unchanged.
                sb.AppendLine("        protected internal IntPtr __handle;");
                sb.AppendLine("        protected internal bool __invalidated;");
                sb.AppendLine();
                sb.AppendLine("        public IntPtr Handle");
                sb.AppendLine("        {");
                sb.AppendLine("            get");
                sb.AppendLine("            {");
                sb.AppendLine("                if (__invalidated)");
                sb.AppendLine("                    throw new ObjectDisposedException(GetType().Name,");
                sb.AppendLine("                        \"The Qt object behind this wrapper has been destroyed, or the wrapper was disposed.\");");
                sb.AppendLine("                return __handle;");
                sb.AppendLine("            }");
                sb.AppendLine("            protected set => __handle = value;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        void IQtObjectView.MarkDestroyed() => __invalidated = true;");
                // The identity cache asks this before handing a cached wrapper back: one Qt already
                // invalidated no longer stands for anything, even while it is still reachable.
                sb.AppendLine("        bool IQtObjectView.IsInvalidated => __invalidated;");
                sb.AppendLine();
                // Whether the Qt object still exists. Qt destroys what it owns (a parent, a layout, a
                // scene, a view) at any moment, and a disposed wrapper is spent, so this is the
                // sip.isValid() of the binding - and the question `== null` answers below.
                sb.AppendLine("        /// <summary>Whether the Qt object behind this wrapper still exists.</summary>");
                sb.AppendLine("        public bool IsAlive => __handle != IntPtr.Zero && !__invalidated;");
                sb.AppendLine();
                // A destroyed object reads as null, the way a destroyed UnityEngine.Object does, so
                // `if (w != null) w.Use();` is the guard that keeps it from throwing. Only the operator
                // is liveness-aware: `w?.Use()`, `w ?? fallback` and `w is null` test the reference
                // itself, exactly as they do in Unity.
                sb.AppendLine("        /// <summary>True when both sides are null or destroyed; reference equality otherwise.</summary>");
                sb.AppendLine($"        public static bool operator ==({name} left, {name} right)");
                sb.AppendLine("        {");
                sb.AppendLine("            bool leftGone = left is null || left.__invalidated;");
                sb.AppendLine("            bool rightGone = right is null || right.__invalidated;");
                sb.AppendLine("            return leftGone || rightGone ? leftGone && rightGone : ReferenceEquals(left, right);");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine($"        public static bool operator !=({name} left, {name} right) => !(left == right);");
                sb.AppendLine();
                // Whether this wrapper is the one that has to release the native object. Only a
                // constructor we published, a boxed copy or an owned method return Qt handed over
                // (FromOwnedHandle) sets it: a wrapper that merely adopts an object
                // (FromHandle/FromTransientHandle - the objects a view or a scene owns) must never
                // delete it at finalization, or the GC frees what Qt still points at.
                //
                // An owned return can also be parented (a conditional return such as a window
                // container with a parent): the model then leaves it borrowed, and even a wrapper
                // that does own has the destructor's parent() guard in front of the delete.
                //
                // Always declared: a root class's derived classes may live in *another* module's
                // assembly (QEvent is QtCore's, QGestureEvent is QtWidgets'), and they assign the
                // ownership flags the root's constructors/Dispose define. The file disables CS0649 for
                // a root whose own subtree never builds anything.
                {
                    sb.AppendLine("        protected internal bool __ownsNative;");
                    sb.AppendLine();
                }
                // Whether the native object is one of the generated subclasses that install the vtable
                // hooks - i.e. whether this wrapper was *made* rather than adopted. Only a constructor
                // sets it. A pure virtual's C# stub answers `default` for a managed subclass (nothing
                // to chain to), but calls the real C++ virtual for an adopted object: Qt's own models
                // (QTableWidget's, QComboBox's, ...) are private C++ classes that are not part of this
                // model, so without this their index()/data()/setData()/rowCount() would answer
                // null/0/false and the model would be silently unreachable.
                // Same reasoning as __ownsNative: a derived class in another module sets it.
                sb.AppendLine("        protected internal bool __managedSubclass;");
                sb.AppendLine();
            }

            // wrapping constructor (never allocates: it adopts an object someone else owns)
            sb.AppendLine($"        protected {name}(IntPtr handle){(baseName != null ? " : base(handle)" : "")}");
            sb.AppendLine("        {");
            if (baseName == null)
            {
                sb.AppendLine("            Handle = handle;");
                // Qt owns the object and will destroy it: the wrapper has to learn about that.
                if (B.DerivesFromQObject(B.Ref(cls)))
                    sb.AppendLine("            ApiRuntime.Watch(handle);");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            int ctorIndex = 0;
            foreach (var ctor in ctors)
            {
                var ps = B.Params(ctor).ToList();
                string sig = string.Join(", ", ps.Select(B.ParamSig));
                // A constructor that hands Qt a buffer together with its cleanup function pins the
                // array and composes the cleanup (see TryPinnedBuffer).
                var pinned = B.TryPinnedBuffer(ps, out int dataIndex, out int cleanupIndex, out string cleanupName)
                    ? (dataIndex, cleanupIndex, cleanupName)
                    : ((int, int, string)?)null;
                string dispatch = virtuals.Count > 0
                    ? "(IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch"
                    : "IntPtr.Zero";
                // Only the constructor that takes the dispatch function pointer needs `unsafe`; the
                // type itself does not carry the modifier.
                string unsafeModifier = dispatch == "IntPtr.Zero" ? "" : "unsafe ";
                // The native subclass is concrete, but the abstract Qt class itself is not: its
                // constructors exist for a managed subclass, so they are protected in C#.
                bool abstractClass = B.HasClass(cls, ApiModel.ClassFlags.Abstract);
                string access = abstractClass ? "protected" : "public";
                sb.AppendLine($"        {access} {unsafeModifier}{name}({sig}){(baseName != null ? " : base(IntPtr.Zero)" : "")}");
                sb.AppendLine("        {");
                // A class with no virtuals at all needs no context: the native side reads this pointer
                // for one thing only - dispatching a virtual into the managed object - and this one has
                // none, so it would never be read. Passing one anyway costs the object its life: a
                // GCHandle is a strong root, so the wrapper stays reachable, the finalizer that would
                // dispose it never runs, and the native object behind it is never destroyed either.
                // (A class C# *can* derive from, and whose object is derived, still needs it - that is
                // what the subclass constructor below is for.)
                if (dispatch != "IntPtr.Zero")
                    sb.AppendLine("            var __context = GCHandle.Alloc(this);");
                var marshal = B.MarshalParams(ps, null, pinned);
                foreach (string line in marshal.Prelude)
                    sb.AppendLine("            " + line);
                string context = dispatch == "IntPtr.Zero" ? "IntPtr.Zero" : "GCHandle.ToIntPtr(__context)";
                string args = string.Join(", ", marshal.Args);
                string create = $"Handle = ApiNative.Ctor_{name}{B.CtorSuffix(ctorIndex++)}({context}, {dispatch}{(args.Length > 0 ? ", " + args : "")});";
                // This wrapper made the object, so it is the one that may release it. A QObject can
                // still be destroyed by Qt later (it gets reparented), so it is watched as well.
                string register = "            ApiRuntime.Register(Handle, this);\n            __ownsNative = true;\n            __managedSubclass = true;";
                if (B.DerivesFromQObject(B.Ref(cls)))
                {
                    register += "\n            ApiRuntime.Watch(Handle);";
                    // The signals and properties the *C#* class declared with [QtSignal]/[QtProperty]:
                    // the object's meta-object gets them before any connection can be made.
                    register += "\n            ApiRuntime.AttachMetaObject(this);";
                }
                B.EmitGuarded(sb, "            ", create + "\n" + register, marshal.Cleanup,
                    pinned != null ? "__buffer.Dispose();" : null);
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Q_PROPERTY declarations, as C# properties. These are pure sugar: the body calls the
            // accessor methods that are generated anyway, so no native glue is duplicated and the
            // accessors keep working under their own names. A property whose name a virtual also wants
            // keeps it; the virtual is emitted under its C++ spelling (see MemberName).
            foreach (var property in B.PropertyList(cls))
            {
                string propName = B.Pascal(B.Name(property));
                if (B.hardReservedByClass[name].Contains(propName) || B.fieldNamesByClass[name].Contains(propName))
                    continue;
                string propType = property.GetProperty("type").GetString();
                string getter = property.GetProperty("getter").GetString();
                string setter = property.TryGetProperty("setter", out var s) ? s.GetString() : null;
                // The Qt name and type are what a reader cannot see from the C# signature (the name is
                // camelCase, the type is the C++ spelling); the accessors are right below.
                string qtName = System.Security.SecurityElement.Escape(B.Name(property));
                string qtPropType = System.Security.SecurityElement.Escape(propType);
                string access = setter != null ? "read/write" : "read-only";
                sb.AppendLine($"        /// <summary>The <c>{qtName}</c> property (Q_PROPERTY, <c>{qtPropType}</c>, {access}).</summary>");
                sb.AppendLine($"        [QtNativeProperty({B.CsString(B.Name(property))}, {B.CsString(propType)}, {(setter != null ? "true" : "false")})]");
                sb.AppendLine($"        public {B.NewName(inherited, propName)}{B.ManagedType(propType)} {propName}");
                sb.AppendLine("        {");
                sb.AppendLine($"            get => {CSharpMemberName(getter)}();");
                if (setter != null)
                    sb.AppendLine($"            set => {CSharpMemberName(setter)}(value);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Public data members, as properties. Qt's option/style structs are plain aggregates
            // (QStyleOption::rect, QJsonParseError::error) and a managed delegate cannot paint
            // without them.
            foreach (var field in B.FieldList(cls))
            {
                string fieldName = B.Name(field);
                string fieldType = field.GetProperty("type").GetString();
                string managed = B.ManagedType(fieldType);
                // A property is always PascalCase; MemberName's "keep the C++ spelling when the
                // Pascal one is taken" rule is for methods, and a data member named `checked` would
                // otherwise be published under a C# keyword.
                string prop = B.SafeName(B.Pascal(fieldName));
                string read = B.ElemRead(fieldType, $"ApiNative.{name}_get_{fieldName}(Handle)");
                string write = $"ApiNative.{name}_set_{fieldName}(Handle, {B.ElemArg(fieldType, "value")})";
                if (B.hardReservedByClass[name].Contains(prop))
                {
                    // A field whose name is taken by a nested type (QStyleOptionHeader::selectedPosition
                    // next to the SelectedPosition enum) stays reachable through accessors instead.
                    sb.AppendLine($"        /// <summary>Data member {name}::{fieldName}.</summary>");
                    sb.AppendLine($"        public {B.NewMethod(inherited, "Get" + prop, "")}{managed} Get{prop}() => {read};");
                    if (B.IsQVariantType(fieldType))
                    {
                        // The managed value is object; the setter needs a variant to hand over, and it
                        // is disposed as soon as the native copy is made.
                        sb.AppendLine($"        public {B.NewMethod(inherited, "Set" + prop, managed)}void Set{prop}({managed} value)");
                        sb.AppendLine("        {");
                        sb.AppendLine($"            using (var __variant = {BindingModel.VariantMarshalling}.Value(value))");
                        sb.AppendLine($"                ApiNative.{name}_set_{fieldName}(Handle, __variant.Handle);");
                        sb.AppendLine("        }");
                    }
                    else
                    {
                        sb.AppendLine($"        public {B.NewMethod(inherited, "Set" + prop, managed)}void Set{prop}({managed} value) => {write};");
                    }
                    sb.AppendLine();
                    continue;
                }
                sb.AppendLine($"        /// <summary>Data member {name}::{fieldName}.</summary>");
                sb.AppendLine($"        public {B.NewName(inherited, prop)}{managed} {prop}");
                sb.AppendLine("        {");
                sb.AppendLine($"            get => {read};");
                if (B.IsQVariantType(fieldType))
                {
                    sb.AppendLine("            set");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                using (var __variant = {BindingModel.VariantMarshalling}.Value(value))");
                    sb.AppendLine($"                    ApiNative.{name}_set_{fieldName}(Handle, __variant.Handle);");
                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine($"            set => {write};");
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            if (B.SubclassCtor(cls))
            {
                // The abstract class has no constructor of its own in the model, so this one exists
                // purely so a managed subclass can build the native subclass that installs the vtable
                // hooks. It is protected on purpose: an abstract Qt class is not something to
                // instantiate directly (that would leave its pure virtuals unimplemented).
                string dispatch = virtuals.Count > 0
                    ? "(IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)&Dispatch"
                    : "IntPtr.Zero";
                string unsafeModifier = dispatch == "IntPtr.Zero" ? "" : "unsafe ";
                sb.AppendLine($"        /// <summary>Builds the native subclass a managed subclass of this abstract class dispatches through.</summary>");
                sb.AppendLine($"        protected {unsafeModifier}{name}(){(baseName != null ? " : base(IntPtr.Zero)" : "")}");
                sb.AppendLine("        {");
                sb.AppendLine("            var __context = GCHandle.Alloc(this);");
                sb.AppendLine($"            Handle = ApiNative.Ctor_{name}(GCHandle.ToIntPtr(__context), {dispatch});");
                sb.AppendLine("            ApiRuntime.Register(Handle, this);");
                sb.AppendLine("            __ownsNative = true;");
                sb.AppendLine("            __managedSubclass = true;");
                sb.AppendLine("            ApiRuntime.AttachMetaObject(this);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // The factories the actual-type registry calls. They live inside the class because the
            // wrapping constructor is protected and __ownsNative is a protected member: nothing outside
            // the class may set the ownership flag. All three are emitted for every QObject-derived
            // class - not only the ones that dispose themselves - because the object's actual class can
            // be any class below this one in the hierarchy, and that is the class whose flag matters.
            bool qobjectClass = B.DerivesFromQObject(B.Ref(cls));
            if (qobjectClass)
            {
                sb.AppendLine($"        internal static QtSharp.QtCore.QObject __WrapBorrowed_{name}(IntPtr handle)");
                sb.AppendLine($"            => ApiRuntime.Wrap(handle, static h => new {name}(h));");
                sb.AppendLine();
                sb.AppendLine($"        internal static QtSharp.QtCore.QObject __WrapTransient_{name}(IntPtr handle)");
                sb.AppendLine($"            => new {name}(handle);");
                sb.AppendLine();
                sb.AppendLine($"        internal static QtSharp.QtCore.QObject __WrapOwned_{name}(IntPtr handle)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var wrapper = new {name}(handle);");
                sb.AppendLine("            wrapper.__ownsNative = true;");
                sb.AppendLine("            return wrapper;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // A QObject returned as a base type (parent(), window(), a QList<QObject*> element) is
            // created as the object's actual bound class: the native meta-object is read once, when the
            // wrapper is made. That is what makes `is`/`as` work on a QObject*-typed value. The declared
            // class is still the fallback when the object is an internal Qt class (QComboBox's view) or
            // its module is not loaded.
            sb.AppendLine($"        public {B.NewMethod(inherited, "FromHandle", "IntPtr")}static {name} FromHandle(IntPtr handle)");
            sb.AppendLine(qobjectClass
                ? $"            => ({name})QtSharp.QtCore.QObjectTypeRegistry.Wrap(handle, typeof({name}), __WrapBorrowed_{name});"
                : $"            => handle == IntPtr.Zero ? null : ApiRuntime.Wrap(handle, h => new {name}(h));");
            // Arguments handed to a virtual come from an object with a very short life (an event
            // lives on the caller's stack), so they must not enter the identity cache: the next
            // event would reuse the same address and get the previous wrapper back. A QObject is not
            // such an object, so it may return the wrapper the program already holds.
            sb.AppendLine();
                sb.AppendLine($"        public {B.NewMethod(inherited, "FromTransientHandle", "IntPtr")}static {name} FromTransientHandle(IntPtr handle)");
            sb.AppendLine(qobjectClass
                ? $"            => ({name})QtSharp.QtCore.QObjectTypeRegistry.WrapTransient(handle, typeof({name}), __WrapTransient_{name});"
                : $"            => handle == IntPtr.Zero ? null : new {name}(handle);");
            if (owned)
            {
                sb.AppendLine();
                // Qt handed over a copy it allocated for us, so this wrapper owns it. The identity cache
                // is deliberately not consulted: whoever takes ownership must be the only wrapper that
                // can release the object, so an existing borrowed wrapper is never adopted here.
                sb.AppendLine($"        public {B.NewMethod(inherited, "FromOwnedHandle", "IntPtr")}static {name} FromOwnedHandle(IntPtr handle)");
                if (qobjectClass)
                {
                    sb.AppendLine($"            => ({name})QtSharp.QtCore.QObjectTypeRegistry.WrapOwned(handle, typeof({name}), __WrapOwned_{name});");
                }
                else
                {
                    sb.AppendLine("        {");
                    sb.AppendLine("            if (handle == IntPtr.Zero)");
                    sb.AppendLine("                return null;");
                    sb.AppendLine($"            var wrapper = new {name}(handle);");
                    sb.AppendLine("            wrapper.__ownsNative = true;");
                    sb.AppendLine("            return wrapper;");
                    sb.AppendLine("        }");
                }
                sb.AppendLine();
                sb.AppendLine($"        public {B.NewMethod(inherited, "Dispose", "")}void Dispose()");
                sb.AppendLine("        {");
                // The backing field, not the property: a wrapper whose object Qt already destroyed is
                // invalid, and the finalizer still has to run through here without throwing.
                sb.AppendLine("            if (__handle == IntPtr.Zero)");
                sb.AppendLine("                return;");
                // Nothing is released once the process is shutting down: deleting an object then
                // fires Qt's destroyed signal, and native code may not enter managed code any more.
                sb.AppendLine("            if (__ownsNative && !__invalidated && !ApiRuntime.ShuttingDown)");
                sb.AppendLine("            {");
                sb.AppendLine($"                ApiNative.Destroy_{name}(__handle);");
                sb.AppendLine("                __ownsNative = false;");
                sb.AppendLine("            }");
                // A disposed wrapper must leave the identity cache: the native address can be reused,
                // and then the cache would hand a dead wrapper to the next object that lands there.
                sb.AppendLine("            ApiRuntime.Forget(__handle, this);");
                sb.AppendLine("            __handle = IntPtr.Zero;");
                // Using a disposed wrapper fails loudly instead of calling native code with a null
                // handle (the same contract as an object Qt deleted).
                sb.AppendLine("            __invalidated = true;");
                sb.AppendLine("            GC.SuppressFinalize(this);");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine($"        ~{name}() => Dispose();");
            }
            sb.AppendLine();

            // A member cannot share a name with a nested type, so something has to give when Qt
            // declares both (QColor::Spec enum and QColor::spec()); see MemberName.
            string CSharpMemberName(string member) => B.MemberName(name, member);

            // Secondary bases live at a different address inside the object, so the raw handle
            // (which is the complete-object address) cannot simply be reinterpreted.
            foreach (var b in extraBases)
            {
                string baseCs = B.CsTypeName(b);
                string baseSimple = B.SimpleName(b);
                sb.AppendLine($"        /// <summary>View of this object as its {baseSimple} base (a separate subobject).</summary>");
                sb.AppendLine($"        public {B.NewMethod(inherited, "As" + baseSimple, "")}{baseCs} As{baseSimple}() => {baseCs}.FromHandle(ApiNative.{name}__as_{baseSimple}(Handle));");
                sb.AppendLine();
            }

            // methods
            var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in methods)
            {
                string mname = B.Name(m);
                overloadCounts.TryGetValue(mname, out int seen);
                overloadCounts[mname] = seen + 1;
                string entrySuffix = seen == 0 ? "" : (seen + 1).ToString();

                var ps = B.Params(m);
                string sig = string.Join(", ", ps.Select(B.ParamSig));
                string ret = B.ReturnType(m);
                string staticMod = B.IsStatic(m) ? "static " : "";
                // Qt's toString() *is* object.ToString(): spelled without `override` it hides the
                // base member, and then string interpolation and Console.WriteLine print the type
                // name instead of the value (the C# call itself is unaffected, which makes it a
                // thoroughly confusing bug to chase).
                string overrideMod = !B.IsStatic(m) && mname == "toString" && ps.Count == 0 && ret == "QString"
                    ? "override "
                    : "";
                // A method that shadows an inherited member of the same signature (or an inherited
                // property/field/type) needs `new`; an override must not say both.
                string methodNew = overrideMod == "" ? B.NewMethod(inherited, CSharpMemberName(mname), ps) : "";
                // A method Qt itself registered in the meta-object: a Q_SLOTS/Q_SLOT slot or a
                // Q_INVOKABLE method. The signature is the normalized Qt spelling of the model types
                // the DllImport is generated from (setText(QString), grab(QRect)), so
                // QMetaObject::indexOfMethod resolves the very entry moc created. The marker is
                // read-side only: the member already exists, and [QtSlot] is the declaration side a
                // C# subclass uses for a method of its own.
                if (B.HasMethod(m, ApiModel.MethodFlags.Slot) || B.HasMethod(m, ApiModel.MethodFlags.Invokable))
                {
                    string qtSig = mname + "(" + string.Join(",", ps.Select(p => B.CppTypeName(B.PType(p)))) + ")";
                    bool invokable = B.HasMethod(m, ApiModel.MethodFlags.Invokable);
                    sb.AppendLine($"        [QtNativeSlot({B.CsString(qtSig)}{(invokable ? ", invokable: true" : "")})]");
                }
                sb.AppendLine($"        public {overrideMod}{methodNew}{staticMod}{B.ManagedRetType(ret)} {CSharpMemberName(mname)}({sig})");
                sb.AppendLine("        {");
                var marshal = B.MarshalParams(ps, B.IsStatic(m) ? null : "Handle");
                foreach (string line in marshal.Prelude)
                    sb.AppendLine("            " + line);
                string call = $"ApiNative.{name}_{mname}{entrySuffix}({string.Join(", ", marshal.Args)})";
                // A call that hands an item over to Qt records the association, so the item's wrapper
                // dies with its owner.
                var transfer = B.OwnershipTransferOf(mname, ps, ret);
                // An owned return is wrapped with FromOwnedHandle, so Dispose deletes it. Conditional
                // ownership (ownedWhenNull) is the model's explicit predicate: the returned object is
                // only ours when that pointer parameter was null - CreateWindowContainer with a parent
                // hands back a child Qt owns, and claiming it would double-delete when the parent dies.
                // The predicate is read before the call, and an absent predicate means "always ours".
                string ownedParam = B.OwnedWhenNull(m);
                string retExpr = B.RetToManaged(ret, call);
                string ownedPrelude = null;
                if (B.ReturnsOwned(m))
                {
                    B.OwnedRetToManaged(ret, call);      // validates the return has a release path
                    if (ownedParam == null)
                    {
                        retExpr = B.OwnedRetToManaged(ret, call);
                    }
                    else
                    {
                        JsonElement predicate = default;
                        bool found = false;
                        foreach (var p in ps)
                            if (B.PName(p) == ownedParam) { predicate = p; found = true; break; }
                        if (!found || !B.IsClassPtr(B.PType(predicate)))
                            throw new InvalidOperationException(
                                $"{name}.{mname}: ownedWhenNull names '{ownedParam}', which is not a " +
                                "class-pointer parameter (the only nullable spelling the model has).");
                        ownedPrelude = $"bool __ownsReturn = {B.SafeName(ownedParam)} == null;\n"
                                     + $"var __ownedHandle = {call};";
                        retExpr = $"__ownedHandle == IntPtr.Zero ? null : (__ownsReturn ? "
                                + $"{B.OwnedRetToManaged(ret, "__ownedHandle")} : {B.RetToManaged(ret, "__ownedHandle")})";
                    }
                }
                // A postlude has to run between the call and the return, so the return value is kept
                // in a local when there is one.
                string body;
                if (marshal.Postlude.Count == 0 && transfer.IsItem)
                    body = $"var __item = {retExpr};\n"
                         + "ApiRuntime.AssociateOwner(Handle, __item?.Handle ?? IntPtr.Zero);\n"
                         + "return __item;";
                else if (marshal.Postlude.Count == 0 && transfer.IsArgument)
                    body = $"{call};\nApiRuntime.AssociateOwner(Handle, {transfer.Argument});";
                else if (marshal.Postlude.Count == 0)
                    body = ret == "void" ? $"{call};" : $"return {retExpr};";
                else
                {
                    string writeBack = string.Join("\n", marshal.Postlude);
                    body = ret == "void"
                        ? $"{call};\n{writeBack}"
                        : $"var __result = {retExpr};\n{writeBack}\nreturn __result;";
                }
                if (ownedPrelude != null)
                    body = ownedPrelude + "\n" + body;
                B.EmitGuarded(sb, "            ", body, marshal.Cleanup);
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // virtuals a managed subclass may override. Only the ones this class declares become
            // C# virtuals; inherited slots are dispatched but already declared by their base. The
            // default implementation chains to the C++ base implementation: the native subclass
            // overrides the virtual for every instance we create, so a managed override that
            // ignores the base would silently disable Qt's own handling (QWidget::event
            // dispatches everything).
            var virtualOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var slot in virtuals.Where(s => s.DeclaredHere))
            {
                var v = slot.Virtual;
                string vname = B.Name(v);
                virtualOverloads.TryGetValue(vname, out int vseen);
                virtualOverloads[vname] = vseen + 1;
                string vSuffix = vseen == 0 ? "" : (vseen + 1).ToString();
                // A blocked slot gets no C# virtual at all (the suffix counter is advanced above so
                // that it stays aligned with the other three emitters).
                if (slot.Blocked)
                    continue;

                var ps = B.Params(v);
                string sig = string.Join(", ", ps.Select(B.ParamSig));
                string ret = B.ReturnType(v);
                // A base class already published this exact signature, so C# has to *override* it: a
                // plain `virtual` would only hide the base member, and a call through a base-typed
                // reference - which is how Qt and most callers see the object - would run the base
                // implementation instead (for QAbstractItemModel's pure virtuals, "no data at all").
                bool overridesBase = baseRef != null && B.ChainOf(baseRef).Any(s =>
                    B.VirtualKey(s.Virtual) == B.VirtualKey(v) && B.ManagedType(B.ReturnType(s.Virtual)) == B.ManagedType(ret));
                // A `virtual` that is not an override can still shadow an inherited member (`override`
                // never warns); mark it `new` in that case.
                // An override has to use the C# name the base's declaration received. QStyle keeps
                // pixelMetric() (its own PixelMetric enum reserves the property name); a subclass
                // inheriting that enum would otherwise emit PixelMetric() and override nothing. The
                // name is the base-most declarer's, all the way up the chain.
                string virtualCsName = overridesBase ? B.VirtualCsName(name, v) : CSharpMemberName(vname);
                string virtualNew = overridesBase ? "" : B.NewMethod(inherited, CSharpMemberName(vname), ps);
                sb.AppendLine($"        /// <summary>Virtual from {name}; override in a managed subclass.</summary>");
                sb.AppendLine($"        public {virtualNew}{(overridesBase ? "override" : "virtual")} {B.ManagedRetType(ret)} {virtualCsName}({sig})");
                sb.AppendLine("        {");
                // A wrapper of a *C++* object has to reach the object's own override, not the
                // declaring class's implementation: `view.model()` for a QTableWidget hands back a
                // private C++ model class that is not part of this model, so its C# type is exactly
                // this one and this stub is what runs - and `self->QAbstractItemModel::setData(...)`
                // would answer the base's "return false" instead of the table's own store, while
                // `index`/`data`/`rowCount` (pure there) would answer default and leave the model
                // silently unreachable. For an object we made, the native hooks are what call this
                // method, so it must NOT call the virtual back (that would recurse): a pure virtual
                // then answers default - which is what makes an abstract class subclassable - and a
                // non-pure one chains to the base implementation as before. `__managedSubclass` is
                // what tells the two apart (only the constructors that install the hooks set it).
                if (B.Subclassable(cls))
                {
                    var forward = B.MarshalParams(ps, "Handle");
                    sb.AppendLine("            if (!__managedSubclass)");
                    sb.AppendLine("            {");
                    foreach (string line in forward.Prelude)
                        sb.AppendLine("                " + line);
                    string virtualCall = $"ApiNative.{name}_{vname}{vSuffix}_vt({string.Join(", ", forward.Args)})";
                    // An in-out string comes back through an out slot, so the postlude that reads it
                    // has to run before the wrapper returns (QValidator::fixup rewrites its input).
                    string virtualBody;
                    if (forward.Postlude.Count == 0)
                    {
                        virtualBody = ret == "void"
                            ? $"{virtualCall}; return;"
                            : $"return {B.RetToManaged(ret, virtualCall)};";
                    }
                    else
                    {
                        var lines = new List<string>();
                        lines.Add(ret == "void"
                            ? $"{virtualCall};"
                            : $"var __result = {B.RetToManaged(ret, virtualCall)};");
                        lines.AddRange(forward.Postlude);
                        lines.Add(ret == "void" ? "return;" : "return __result;");
                        virtualBody = string.Join("\n", lines);
                    }
                    B.EmitGuarded(sb, "                ", virtualBody, forward.Cleanup);
                    sb.AppendLine("            }");
                    sb.AppendLine("            else");
                    sb.AppendLine("            {");
                }
                // The two halves are inside their own blocks, so the marshalling temporaries a
                // container parameter needs cannot collide with each other.
                string inner = B.Subclassable(cls) ? "                " : "            ";
                if (B.HasNoBase(v) || !B.Subclassable(cls))
                {
                    // Nothing to chain to: Qt declared it pure or private, or the class has no
                    // generated subclass to reach the vtable through. A managed subclass that does not
                    // override this simply does nothing.
                    sb.AppendLine(inner + "// no base implementation to chain to");
                    if (ret != "void")
                        sb.AppendLine(inner + "return default;");
                }
                else
                {
                    // The base call needs the same marshalling a normal call does: a container
                    // parameter has to become a native container first.
                    var baseMarshal = B.MarshalParams(ps, "Handle");
                    foreach (string line in baseMarshal.Prelude)
                        sb.AppendLine(inner + line);
                    string baseCall = $"ApiNative.{name}_{vname}__base{vSuffix}({string.Join(", ", baseMarshal.Args)})";
                    string body;
                    if (baseMarshal.Postlude.Count == 0)
                    {
                        body = ret == "void" ? $"{baseCall};" : $"return {B.RetToManaged(ret, baseCall)};";
                    }
                    else
                    {
                        var lines = new List<string>();
                        lines.Add(ret == "void"
                            ? $"{baseCall};"
                            : $"var __result = {B.RetToManaged(ret, baseCall)};");
                        lines.AddRange(baseMarshal.Postlude);
                        if (ret != "void")
                            lines.Add("return __result;");
                        body = string.Join("\n", lines);
                    }
                    B.EmitGuarded(sb, inner, body, baseMarshal.Cleanup);
                }
                if (B.Subclassable(cls))
                    sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // signals
            var signalOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var s in signals)
            {
                string sname = B.Name(s);
                var ps = B.Params(s);
                var csTypes = ps.Select(p => B.ParamValueManagedType(p)).ToList();
                // The arity classes stop somewhere, and a Qt signal with more than a handful of
                // parameters is not worth growing them for.
                if (csTypes.Count > BindingModel.MaxSignalArity)
                    continue;
                // C# has no overloaded properties, so a signal Qt declares twice (QCompleter has both
                // activated(QString) and activated(QModelIndex)) gets the same 2,3,... suffix the
                // method overloads use.
                signalOverloads.TryGetValue(sname, out int sseen);
                signalOverloads[sname] = sseen + 1;
                string sSuffix = sseen == 0 ? "" : (sseen + 1).ToString();
                string signalType = csTypes.Count == 0
                    ? "QSignal"
                    : "QSignal<" + string.Join(", ", csTypes) + ">";
                string mocSig = sname + "(" + string.Join(",", ps.Select(B.SignalMocType)) + ")";
                string mocSigDoc = System.Security.SecurityElement.Escape(mocSig);
                // Connect: the runtime hands the raw argv over and each parameter is read with the
                // same per-type rules the virtual dispatch uses (a bound class pointer is a pointer
                // *slot*, a boxed value is the value itself, a container is Qt's and copied out).
                // The native forwarder already skipped the return slot (moc's argv[0]), so the
                // signal's first argument is argv[0] here - unlike the virtual-dispatch convention,
                // where the generated wrapper builds the whole array.
                //
                // The handler doubles as the connection key, and the closure forwards the context and
                // connection type: `signal.Connect(h)` uses the defaults, while the QtCore extension
                // overloads (QSignalConnections) pass a context QObject and Qt::ConnectionType through
                // to the same native connection.
                string handler = csTypes.Count == 0
                    ? "h()"
                    : "h(" + string.Join(", ", ps.Select((p, i) => B.SignalRead(B.PType(p), i))) + ")";
                sb.AppendLine($"        /// <summary>The <c>{mocSigDoc}</c> signal as a QSignal (`Connect`/`Emit`).</summary>");
                sb.AppendLine($"        [QtNativeSignal({B.CsString(mocSig)})]");
                sb.AppendLine($"        public {B.NewName(inherited, B.Pascal(sname) + sSuffix + "Signal")}{signalType} {B.Pascal(sname)}{sSuffix}Signal =>");
                sb.AppendLine($"            new {signalType}((h, context, type) => ApiRuntime.Connect(Handle, \"{mocSig}\", (argv, _) => {handler}, h, context?.Handle ?? IntPtr.Zero, type),");
                foreach (string line in B.EmitLambda(ps, mocSig))
                    sb.AppendLine("                " + line);
                sb.AppendLine("                );");
                sb.AppendLine();

                // The same signal as a C# *event*, so the Qt spelling has its C# equivalent:
                //     button.Clicked += handler;    button.Clicked -= handler;
                // This is what a C# user reaches for, and the remove accessor really disconnects -
                // the runtime remembers the handler as the key of the connection, so a long-lived
                // object does not leak. The XxxSignal property stays: Emit lives there, and a shape
                // that never has to remember the key is often what a test wants.
                //
                // A signal whose Pascal name a property, field or nested type already took keeps the
                // QSignal only, the same way a colliding Q_PROPERTY is skipped (the event name is
                // checked against the same reserved set).
                string eventName = B.Pascal(sname) + sSuffix;
                if (B.reservedByClass.TryGetValue(name, out var reservedNames) && !reservedNames.Contains(eventName))
                {
                    string delegateType = csTypes.Count == 0
                        ? "Action"
                        : "Action<" + string.Join(", ", csTypes) + ">";
                    string readArgs = string.Join(", ", ps.Select((p, i) => B.SignalRead(B.PType(p), i)));
                    string valueCall = csTypes.Count == 0 ? "value()" : "value(" + readArgs + ")";
                    sb.AppendLine($"        /// <summary>The <c>{mocSigDoc}</c> signal as a C# event (`+=` handler, `-=` handler disconnects).</summary>");
                    sb.AppendLine($"        [QtNativeSignal({B.CsString(mocSig)})]");
                    sb.AppendLine($"        public {B.NewName(inherited, eventName)}event {delegateType} {eventName}");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            add => ApiRuntime.Connect(Handle, \"{mocSig}\", (argv, _) => {valueCall}, value);");
                    sb.AppendLine($"            remove => ApiRuntime.Disconnect(Handle, \"{mocSig}\", value);");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }

            // native subclass forwarding the virtuals into Dispatch
            if (virtuals.Count > 0)
            {
                // Native calls this through a function pointer, so an exception that escapes it would
                // unwind into Qt's C++ frames and kill the process (see ReportCallbackException). Catch
                // it here and report it; the dispatch switch itself stays untouched, in DispatchCore.
                sb.AppendLine("        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
                string modifier = B.NewMethod(inherited, "Dispatch", "IntPtr,int,IntPtr");
                sb.AppendLine($"        internal {modifier}static void Dispatch(IntPtr userData, int methodId, IntPtr argv)");
                sb.AppendLine("        {");
                sb.AppendLine("            try");
                sb.AppendLine("            {");
                sb.AppendLine("                DispatchCore(userData, methodId, argv);");
                sb.AppendLine("            }");
                sb.AppendLine("            catch (Exception __exception)");
                sb.AppendLine("            {");
                sb.AppendLine("                ApiRuntime.ReportCallbackException(\"a virtual override\", __exception);");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine($"        static void DispatchCore(IntPtr userData, int methodId, IntPtr argv)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var target = ({name})GCHandle.FromIntPtr(userData).Target;");
                sb.AppendLine("            switch (methodId)");
                sb.AppendLine("            {");
                for (int i = 0; i < virtuals.Count; i++)
                {
                    // A blocked slot has no native hook, so there is nothing to dispatch. The slot id
                    // below stays the position in the chain, which keeps the numbering aligned.
                    if (virtuals[i].Blocked)
                        continue;
                    var v = virtuals[i].Virtual;
                    var ps = B.Params(v);
                    // The slot id is the position in the whole chain, and an inherited slot is
                    // dispatched into the member the declaring class already published. A slot the
                    // class redeclares but a base published first uses the base's C# spelling (an
                    // override cannot rename the member), the same rule as the virtual emitter.
                    string member = B.VirtualCsName(name, v);
                    sb.AppendLine($"                case {i + 1}: // {B.Name(v)}");
                    sb.AppendLine("                {");
                    // A by-reference parameter crosses as the caller's storage address. The override
                    // receives it as a C# ref/out and the result is copied back into that storage
                    // once it returns - the argv convention has no other way to carry the value out.
                    var prelude = new List<string>();
                    var args = new List<string>();
                    var postlude = new List<string>();
                    for (int a = 0; a < ps.Count; a++)
                    {
                        var p = ps[a];
                        int slot = a + 1;
                        var paramHandler = B.ParamFor(p);
                        if (paramHandler.IsByRef(p))
                        {
                            string valueType = B.RefValueType(B.PType(p));
                            string local = "__ref" + slot;
                            // A ref reads the value Qt passed; an out has nothing to read.
                            prelude.Add(paramHandler.IsOut(p)
                                ? $"{B.ManagedType(valueType)} {local};"
                                : $"var {local} = {B.DispatchRead(valueType, slot)};");
                            args.Add($"{(paramHandler.IsOut(p) ? "out" : "ref")} {local}");
                            postlude.Add(B.DispatchStore(valueType, local, slot));
                            continue;
                        }
                        // A `const T *items, int count` pair: the managed override gets an array of
                        // the value type. The count is the next parameter (PublishesAsVirtual only
                        // accepts that shape).
                        if (B.IsStructPtr(B.PType(p)) && !B.IsOpaqueStructPtr(p))
                        {
                            string count = a + 1 < ps.Count
                                ? $"Marshal.ReadInt32(Marshal.ReadIntPtr(argv, {slot + 1} * IntPtr.Size))"
                                : "0";
                            string elem = B.RefValueType(B.PType(p));
                            args.Add($"ApiRuntime.ReadArray<{B.CsTypeName(elem)}>(Marshal.ReadIntPtr(argv, {slot} * IntPtr.Size), {count})");
                            continue;
                        }
                        args.Add(B.DispatchRead(B.PType(p), slot));
                    }
                    foreach (string line in prelude)
                        sb.AppendLine("                    " + line);
                    string argList = string.Join(", ", args);
                    if (B.ReturnType(v) == "void")
                        sb.AppendLine($"                    target.{member}({argList});");
                    else
                    {
                        sb.AppendLine($"                    var result = target.{member}({argList});");
                        sb.AppendLine("                    " + B.DispatchStore(B.ReturnType(v), "result"));
                    }
                    foreach (string line in postlude)
                        sb.AppendLine("                    " + line);
                    sb.AppendLine("                    break;");
                    sb.AppendLine("                }");
                }
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            B.WriteEnums(sb, name, 2, inherited);

            sb.AppendLine("    }");
            sb.AppendLine();
        }
    
    }
}
