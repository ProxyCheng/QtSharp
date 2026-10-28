using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using QtSharp.Runtime;

// Every QObject wrapper is created as the object's actual bound type, the PyQt/PySide object
// model: the type is resolved once, from the native meta-object, when the wrapper is created, so
// `is`/`as` work on a value returned as QObject*, on a QList<QObject*> element, on a signal
// argument and on a parent/child accessor. The declared type is only the fallback.
//
// The table and the walk live in the QtCore assembly rather than in QtSharp.Runtime, because
// reading a meta-object needs the generated ApiNative entry points, which only this assembly has;
// QtSharp.Runtime is deliberately Qt-type-free. Every module's generated code registers its
// wrappers from a module initializer, and modules above QtCore already reference QtCore.
namespace QtSharp.QtCore
{
    /// <summary>
    /// The generated QObject wrapper classes, keyed by their Qt class name. A wrapper is created
    /// through this table so its managed type is the object's most-derived bound class.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class QObjectTypeRegistry
    {
        // One entry per bound QObject-derived wrapper. The three factories are emitted inside the
        // wrapper class, where the protected IntPtr constructor and __ownsNative are reachable;
        // nothing here sets ownership flags from the outside.
        sealed class Entry
        {
            public Func<IntPtr, QObject> Borrowed;
            public Func<IntPtr, QObject> Owned;
            public Func<IntPtr, QObject> Transient;
        }

        // A class name is Qt's own: the simple name for the core modules and the qualified one for
        // a namespaced addon class (Qt3DCore::QEntity). The simple alias is registered too, so a
        // model that spells only the simple name still resolves.
        static readonly ConcurrentDictionary<string, Entry> byName = new(StringComparer.Ordinal);

        /// <summary>
        /// Registers one generated QObject wrapper. Called from each module's initializer, so a
        /// module's classes are known as soon as its assembly loads.
        /// </summary>
        public static void Register(Type type, string qtName, string simpleName,
            Func<IntPtr, QObject> borrowed, Func<IntPtr, QObject> owned, Func<IntPtr, QObject> transient)
        {
            if (type == null || string.IsNullOrEmpty(qtName) || borrowed == null)
                return;
            var entry = new Entry
            {
                Borrowed = borrowed,
                Owned = owned,
                Transient = transient,
            };
            byName[qtName] = entry;
            if (!string.IsNullOrEmpty(simpleName))
                byName[simpleName] = entry;
        }

        /// <summary>
        /// A borrowed wrapper (Qt owns the object) of the object's actual bound type.
        /// </summary>
        public static QObject Wrap(IntPtr handle, Type declared, Func<IntPtr, QObject> declaredFactory)
        {
            if (handle == IntPtr.Zero)
                return null;
            // Cache first (the SIP/Shiboken rule). An existing wrapper that is compatible with the
            // declared type wins: it preserves reference identity, and a managed subclass - whose
            // dynamic meta-object class is not in the table - stays itself.
            if (ApiRuntime.TryGetWrapper(handle) is QObject existing && declared.IsInstanceOfType(existing))
                return existing;
            var entry = Resolve(handle);
            return entry != null ? entry.Borrowed(handle) : declaredFactory(handle);
        }

        /// <summary>
        /// A wrapper that owns the object Qt handed over. Never adopts an existing wrapper: the
        /// wrapper that took ownership is the only one that may release it.
        /// </summary>
        public static QObject WrapOwned(IntPtr handle, Type declared, Func<IntPtr, QObject> declaredFactory)
        {
            if (handle == IntPtr.Zero)
                return null;
            var entry = Resolve(handle);
            return entry != null ? entry.Owned(handle) : declaredFactory(handle);
        }

        /// <summary>
        /// A short-lived view (an event argument, a non-owning return) that is not registered in
        /// the identity cache unless the program already holds a compatible wrapper.
        /// </summary>
        public static QObject WrapTransient(IntPtr handle, Type declared, Func<IntPtr, QObject> declaredFactory)
        {
            if (handle == IntPtr.Zero)
                return null;
            if (ApiRuntime.TryGetWrapper(handle) is QObject existing && declared.IsInstanceOfType(existing))
                return existing;
            var entry = Resolve(handle);
            return entry != null ? entry.Transient(handle) : declaredFactory(handle);
        }

        // Walks Qt's primary meta-object chain and returns the first class the binding publishes.
        // An internal Qt class (QComboBoxListView, a platform QWindow) is simply not in the table,
        // so its nearest bound base wins instead. Only the raw entry points are used: no wrapper
        // objects are allocated for the walk, and only this new object pays for it.
        static Entry Resolve(IntPtr handle)
        {
            for (IntPtr metaObject = ApiNative.QObject_metaObject(handle);
                 metaObject != IntPtr.Zero;
                 metaObject = ApiNative.QMetaObject_superClass(metaObject))
            {
                string name = ApiRuntime.ToManaged(ApiNative.QMetaObject_className(metaObject));
                if (name != null && byName.TryGetValue(name, out var entry))
                    return entry;
            }
            return null;
        }
    }
}
