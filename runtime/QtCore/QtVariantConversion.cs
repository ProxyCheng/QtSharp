using System;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace QtSharp.QtCore
{
    /// <summary>
    /// The type table behind <see cref="QVariant"/>'s object dispatch. Two kinds of Qt value type
    /// register here, and the generated module initializers do it for every type the header
    /// publishes, so a QVariant that arrives from Qt can be unwrapped even before managed code has
    /// touched the wrapper type.
    /// <list type="bullet">
    /// <item>A class that Qt converts with <c>operator QVariant()</c> (QColor, QBrush, QIcon, ...)
    /// registers its metatype id and the factory that owns the copy <c>QMetaType::create</c> made.
    /// QVariant has no constructor for these, so the metatype is the only way in.</item>
    /// <item>A type qvariant.h declares a constructor and accessor for (QPoint, QUrl, QDate,
    /// QModelIndex, ...) registers the generated constructor and accessor directly, so
    /// <c>new QVariant(object)</c>/<c>ToObject()</c> follow the header instead of a hand-kept list.</item>
    /// </list>
    /// This is the C# counterpart of the SIP/Shiboken type table: QVariant is untyped on the C++
    /// side, so the runtime has to map a metatype id back to the managed type that knows how to
    /// own the value.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class QtVariantConversion
    {
        public sealed class Entry
        {
            public Type ManagedType;
            public string QtTypeName;
            public int MetaTypeId;
            public Func<IntPtr, object> FromOwnedHandle;
        }

        // One per type QVariant has a constructor for. Either direction can be missing: a header
        // type without an accessor still registers its create factory.
        sealed class Factory
        {
            public Type ManagedType;
            public string QtTypeName;
            public int MetaTypeId;
            // Handles, not wrappers: QVariant is internal to this assembly. Create returns an
            // owned native QVariant* the caller releases through VariantMarshalling; Unwrap takes
            // the native variant (borrowed) and hands back the managed value.
            public Func<object, IntPtr> Create;
            public Func<IntPtr, object> Unwrap;
        }

        // Readers sit on QVariant's hot path (a model role read or write per cell), so the tables
        // are concurrent: registration never blocks a conversion, and conversions never take a lock.
        static readonly ConcurrentDictionary<Type, Entry> byType = new();
        static readonly ConcurrentDictionary<int, Entry> byId = new();
        static readonly ConcurrentDictionary<Type, Factory> factoriesByType = new();
        static readonly ConcurrentDictionary<int, Factory> factoriesById = new();

        public static void Register(Type managedType, string qtTypeName, Func<IntPtr, object> fromOwnedHandle)
        {
            if (managedType == null || string.IsNullOrEmpty(qtTypeName) || fromOwnedHandle == null)
                return;
            var entry = new Entry
            {
                ManagedType = managedType,
                QtTypeName = qtTypeName,
                FromOwnedHandle = fromOwnedHandle,
                MetaTypeId = QMetaType.Type(qtTypeName),
            };
            byType[managedType] = entry;
            // QMetaType::type() can legitimately return 0 before Qt has registered a built-in
            // type; TryGet retries those names instead of dropping the type for the process.
            if (entry.MetaTypeId != 0)
                byId[entry.MetaTypeId] = entry;
        }

        /// <summary>
        /// Registers a type QVariant itself can be constructed from: <paramref name="create"/> is
        /// the generated <c>QVariant(T)</c> constructor, <paramref name="unwrap"/> the generated
        /// <c>ToXxx()</c> accessor.
        /// </summary>
        public static void RegisterFactory(Type managedType, string qtTypeName,
                                           Func<object, IntPtr> create, Func<IntPtr, object> unwrap)
        {
            if (managedType == null || string.IsNullOrEmpty(qtTypeName) || create == null && unwrap == null)
                return;
            var factory = new Factory
            {
                ManagedType = managedType,
                QtTypeName = qtTypeName,
                MetaTypeId = QMetaType.Type(qtTypeName),
                Create = create,
                Unwrap = unwrap,
            };
            if (create != null)
                factoriesByType[managedType] = factory;
            if (factory.MetaTypeId != 0)
                factoriesById[factory.MetaTypeId] = factory;
        }

        public static bool TryGet(Type managedType, out Entry entry)
            => byType.TryGetValue(managedType, out entry);

        public static bool TryGet(int metaTypeId, out Entry entry)
        {
            if (byId.TryGetValue(metaTypeId, out entry))
                return true;
            foreach (var candidate in byType.Values)
            {
                if (candidate.MetaTypeId != 0)
                    continue;
                int id = QMetaType.Type(candidate.QtTypeName);
                if (id == 0)
                    continue;
                candidate.MetaTypeId = id;
                byId.TryAdd(id, candidate);
                if (id == metaTypeId)
                {
                    entry = candidate;
                    return true;
                }
            }
            entry = null;
            return false;
        }

        /// <summary>
        /// The managed value as a real Qt QVariant, when its type is one the generated table knows.
        /// </summary>
        public static bool TryCreate(object value, out IntPtr variant)
        {
            variant = IntPtr.Zero;
            if (value == null)
                return false;
            if (!factoriesByType.TryGetValue(value.GetType(), out var factory) || factory.Create == null)
                return false;
            variant = factory.Create(value);
            return variant != IntPtr.Zero;
        }

        /// <summary>
        /// The contained value as the wrapper or struct QVariant's own accessor returns, when the
        /// generated table has an entry for its metatype.
        /// </summary>
        public static bool TryUnwrap(int metaTypeId, IntPtr source, out object value)
        {
            value = null;
            if (!factoriesById.TryGetValue(metaTypeId, out var factory))
            {
                // Same lazy retry as TryGet: a name Qt did not know at module-load time. The
                // factories whose id is unresolved live only in the by-type table.
                foreach (var candidate in factoriesByType.Values)
                    if (candidate.MetaTypeId == 0)
                        Resolve(candidate);
                factoriesById.TryGetValue(metaTypeId, out factory);
            }
            if (factory == null || factory.Unwrap == null)
                return false;
            value = factory.Unwrap(source);
            return true;
        }

        static void Resolve(Factory factory)
        {
            int id = QMetaType.Type(factory.QtTypeName);
            if (id == 0)
                return;
            factory.MetaTypeId = id;
            factoriesById.TryAdd(id, factory);
        }
    }
}
