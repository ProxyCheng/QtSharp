using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

namespace QtSharp.QtCore
{
    /// <summary>
    /// The strong roots behind QVariant values that hold a managed object. Qt copies and destroys
    /// QVariant values freely, so the managed object cannot be stored in the variant itself; the
    /// native side holds a ref-counted id, and this table owns the object until the last copy of
    /// the QVariant dies. That is the C# shape of PySide's PyObjectWrapper.
    /// </summary>
    internal static unsafe class ManagedVariantStore
    {
        static readonly ConcurrentDictionary<long, object> values = new();
        static long nextId;
        static int initialized;

        internal static void EnsureInitialized()
        {
            if (Interlocked.Exchange(ref initialized, 1) != 0)
                return;
            VariantNative.SetManagedReleaseCallback(
                (IntPtr)(delegate* unmanaged[Cdecl]<long, void>)&Release);
        }

        internal static long Add(object value)
        {
            EnsureInitialized();
            long id = Interlocked.Increment(ref nextId);
            values[id] = value;
            return id;
        }

        internal static bool TryGet(long id, out object value)
            => values.TryGetValue(id, out value);

        /// <summary>Live managed payloads. Used by the lifetime test; zero after the last copy.</summary>
        internal static int Count => values.Count;

        /// <summary>Live native refcounted boxes; must match <see cref="Count"/>.</summary>
        internal static long NativeBoxCount => VariantNative.ManagedBoxCount();

        // Native calls this when the last QVariant copy of the value is destroyed. It runs on an
        // arbitrary thread and must not throw: an exception would unwind into a C++ destructor.
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Release(long id)
        {
            try
            {
                values.TryRemove(id, out _);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// An owned native QVariant handle for the duration of one call. The generated wrappers build
    /// one from a managed value, hand its <see cref="Handle"/> to the glue, and dispose it once the
    /// native copy has been made. Neither this type nor the marshaller names QVariant: the class is
    /// internal to this assembly, and the rest of the binding only ever sees object.
    ///
    /// It is a reference type on purpose: a lease owns one native QVariant, and two copies of a
    /// value-type lease would each release it. Copies of the class share the same instance, and
    /// Dispose exchanges the handle away, so it is idempotent.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class VariantLease : IDisposable
    {
        // The lease that owns nothing: Parameter(null) returns it, so every generated call site
        // can read .Handle without a null check and Dispose is a no-op.
        internal static readonly VariantLease Empty = new(IntPtr.Zero, false);

        readonly bool owned;
        IntPtr handle;

        internal VariantLease(IntPtr handle, bool owned)
        {
            this.handle = handle;
            this.owned = owned;
        }

        /// <summary>The native QVariant*, or <see cref="IntPtr.Zero"/> for "not given" / disposed.</summary>
        public IntPtr Handle => handle;

        public void Dispose()
        {
            if (!owned)
                return;
            IntPtr released = Interlocked.Exchange(ref handle, IntPtr.Zero);
            if (released != IntPtr.Zero)
                VariantMarshalling.Release(released);
        }
    }

    /// <summary>
    /// The boundary conversion the generated wrappers use. The managed surface says <c>object</c>
    /// wherever Qt says QVariant - as PySide6 does, where the class is not exposed at all - so every
    /// call site crosses here: a parameter becomes a real QVariant for the duration of the call, and
    /// a returned variant becomes the value it holds. null is the invalid variant. The QVariant
    /// wrapper itself never leaves this assembly.
    /// Public because the QtGui/QtWidgets/extension assemblies are separate from this one and their
    /// generated code calls it too.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class VariantMarshalling
    {
        /// <summary>
        /// A value the caller supplied for a parameter. null means "not given", so the native side
        /// applies the declaration's own default; the lease is empty and nothing is allocated.
        /// </summary>
        public static VariantLease Parameter(object value)
            => value == null ? VariantLease.Empty : new VariantLease(Create(value), true);

        /// <summary>
        /// A value to store or emit. null is the invalid QVariant (PySide's None); the caller
        /// disposes the lease once the native copy has been made.
        /// </summary>
        public static VariantLease Value(object value)
            => new VariantLease(Create(value), true);

        /// <summary>
        /// A QVariant the native side allocated for us: the value it holds, with the temporary
        /// released. A Qt value type with no managed mapping comes back as an opaque object - the
        /// internal QVariant wrapper, owned by the caller. It can be passed straight back to any
        /// API that takes an <c>object</c> variant and disposed as <see cref="IDisposable"/>, but
        /// its metatype/value members are not reachable from another assembly.
        /// </summary>
        public static object ToManaged(IntPtr variant)
        {
            if (variant == IntPtr.Zero)
                return null;
            QVariant wrapper = QVariant.FromOwnedHandle(variant);
            object value = wrapper.ToObject();
            if (ReferenceEquals(value, wrapper))
                return wrapper;
            wrapper.Dispose();
            return value;
        }

        /// <summary>
        /// A QVariant that belongs to Qt (a signal's or a virtual override's argument): the same
        /// conversion, over a wrapper that never owns the native value.
        /// </summary>
        public static object ToManagedTransient(IntPtr variant)
        {
            if (variant == IntPtr.Zero)
                return null;
            QVariant wrapper = QVariant.FromTransientHandle(variant);
            object value = wrapper.ToObject();
            if (ReferenceEquals(value, wrapper))
                return wrapper;
            wrapper.Dispose();
            return value;
        }

        internal static void Release(IntPtr variant)
        {
            if (variant != IntPtr.Zero)
                ApiNative.Destroy_QVariant(variant);
        }

        static IntPtr Create(object value) => QVariant.From(value).DetachHandle();
    }

    /// <summary>
    /// The hand-written half of the QVariant bridge. The generated binding has its own ApiNative
    /// class; these entry points are the ones that do not come from a Qt declaration.
    /// </summary>
    internal static class VariantNative
    {
        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_QVariant_fromManaged", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FromManaged(long id);

        // QVariant(QMetaType(typeId), copy): the metatype id comes from the generated table, so a
        // conversion neither spells the type name nor allocates a QMetaType wrapper.
        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_QVariant_fromMetaTypeId", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FromMetaTypeId(int typeId, IntPtr copy);

        // QVariant(QVariantMap): the map is built by the generated QMap helper; Qt copies it.
        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_QVariant_fromVariantMap", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FromVariantMap(IntPtr map);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_QVariant_managedId", CallingConvention = CallingConvention.Cdecl)]
        internal static extern long ManagedId(IntPtr variant);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_QVariant_fromQObject", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FromQObject(IntPtr objectHandle);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_QVariant_toQObject", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ToQObject(IntPtr variant);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_QVariant_clone", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Clone(IntPtr variant);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_set_managed_release_callback", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetManagedReleaseCallback(IntPtr callback);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_api_managed_box_count", CallingConvention = CallingConvention.Cdecl)]
        internal static extern long ManagedBoxCount();
    }
}
