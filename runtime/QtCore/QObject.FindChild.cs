using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// QObject::findChild<T>() / findChildren<T>() are templates, so the model cannot publish them: the
// type filter is a C# generic here and the walk uses the wrapper tree the binding already owns.
// The children come back as their actual bound types (QObjectTypeRegistry), which makes `is T` the
// same test Qt's qobject_cast<T*> performs - and it also matches a managed subclass, which a
// static meta-object cast cannot. Qt::FindChildOptions is already generated, so this file only adds
// the lookups.
//
// Qt offers each lookup twice: a name (exact, case-sensitive, empty matches anything) and a
// QRegularExpression (a *search* over the object name, so "di" matches "edit"; Qt calls
// re.match(name).hasMatch()). Both are here. Because the name and pattern overloads are both
// reference types, a bare `null` argument is now ambiguous: write `(string)null` for the old form.
//
// The QMetaObject overloads are the reflection form of the same lookup, the one PyQt's
// `findChild(type)` exposes: Qt only has the templates, but the runtime filter is
// QMetaObject::cast, and these overloads do the walk natively (qs_api_qobject_find_children,
// the equivalent of qt_qFindChildren_helper) so only matched objects are wrapped. A managed
// subclass matches its Qt base's metaobject through the inheritance chain; it cannot be found by
// a QMetaObject containing its C#-declared type, because that metaobject is per instance - the
// generic overload is the one that matches it exactly.
namespace QtSharp.QtCore
{
    public partial class QObject
    {
        /// <summary>
        /// The first descendant that is a <typeparamref name="T"/> and whose
        /// <see cref="ObjectName"/> is <paramref name="name"/>, or <c>null</c>.
        /// </summary>
        /// <param name="name">
        /// The Qt object name to match. <c>null</c> or an empty string matches any object, which is
        /// what Qt's default-constructed <c>QString</c> does; the match is exact, not a pattern.
        /// </param>
        /// <param name="options">
        /// <see cref="Qt.FindChildOptions.FindChildrenRecursively"/> (the default) searches the whole
        /// subtree; <see cref="Qt.FindChildOptions.FindDirectChildrenOnly"/> stops at the direct
        /// children.
        /// </param>
        /// <remarks>
        /// The order is Qt's: every direct child is tested first, then each child's subtree in
        /// child order, so the answer is the same object <c>QObject::findChild</c> returns. The
        /// object itself is never a candidate. The wrapper is the actual bound type, so
        /// <c>FindChild&lt;QLineEdit&gt;()</c> finds an editor that a base-typed list would only
        /// describe as <c>QObject</c>.
        /// </remarks>
        public T FindChild<T>(string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            where T : QObject
            => FindFirst<T>(NameMatcher(name), options);

        /// <summary>
        /// The first descendant that is a <typeparamref name="T"/> and whose
        /// <see cref="ObjectName"/> the pattern matches, or <c>null</c>. The managed equivalent of
        /// Qt's <c>findChild&lt;T&gt;(const QRegularExpression &amp;, Qt::FindChildOptions)</c>.
        /// </summary>
        /// <param name="pattern">
        /// The pattern matched against each candidate's object name. Qt uses
        /// <c>re.match(name).hasMatch()</c>, so the pattern is a search, not a full match
        /// (<c>"di"</c> matches <c>"edit"</c>); matching is case-sensitive unless the pattern's
        /// options say otherwise.
        /// </param>
        /// <param name="options">As for the name overload.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is null.</exception>
        public T FindChild<T>(QRegularExpression pattern,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            where T : QObject
        {
            if (pattern is null)
                throw new ArgumentNullException(nameof(pattern));
            return FindFirst<T>(candidate => PatternMatches(pattern, candidate.ObjectName), options);
        }

        /// <summary>
        /// Every descendant that is a <typeparamref name="T"/> and whose
        /// <see cref="ObjectName"/> is <paramref name="name"/>, in the order
        /// <c>QObject::findChildren</c> produces them; an empty array when there is none.
        /// </summary>
        public T[] FindChildren<T>(string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            where T : QObject
            => FindAll<T>(NameMatcher(name), options);

        /// <summary>
        /// Every descendant that is a <typeparamref name="T"/> and whose
        /// <see cref="ObjectName"/> the pattern matches, in Qt's order; an empty array when there
        /// is none. The managed equivalent of Qt's
        /// <c>findChildren&lt;T&gt;(const QRegularExpression &amp;, Qt::FindChildOptions)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is null.</exception>
        public T[] FindChildren<T>(QRegularExpression pattern,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            where T : QObject
        {
            if (pattern is null)
                throw new ArgumentNullException(nameof(pattern));
            return FindAll<T>(candidate => PatternMatches(pattern, candidate.ObjectName), options);
        }

        // ---------------------------------------------------------------- QMetaObject reflection

        /// <summary>
        /// The first descendant whose meta-object derives from <paramref name="metaObject"/> and
        /// whose object name is <paramref name="name"/>, or <c>null</c>. The reflection form of
        /// <see cref="FindChild{T}(string, Qt.FindChildOptions)"/> (PyQt's
        /// <c>findChild(type)</c>), backed by <c>QMetaObject::cast</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="metaObject"/> is null.</exception>
        public QObject FindChild(QMetaObject metaObject, string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            => First(FindByMetaObject(metaObject, name, null, options, firstOnly: true));

        /// <summary>
        /// The first descendant whose meta-object derives from <paramref name="metaObject"/> and
        /// whose object name the pattern matches, or <c>null</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="metaObject"/> or <paramref name="pattern"/> is null.
        /// </exception>
        public QObject FindChild(QMetaObject metaObject, QRegularExpression pattern,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            => First(FindByMetaObject(metaObject, null, pattern, options, firstOnly: true));

        /// <summary>
        /// Every descendant whose meta-object derives from <paramref name="metaObject"/> and whose
        /// object name is <paramref name="name"/>, in Qt's order; an empty array when there is
        /// none.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="metaObject"/> is null.</exception>
        public QObject[] FindChildren(QMetaObject metaObject, string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            => FindByMetaObject(metaObject, name, null, options, firstOnly: false);

        /// <summary>
        /// Every descendant whose meta-object derives from <paramref name="metaObject"/> and whose
        /// object name the pattern matches, in Qt's order.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="metaObject"/> or <paramref name="pattern"/> is null.
        /// </exception>
        public QObject[] FindChildren(QMetaObject metaObject, QRegularExpression pattern,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            => FindByMetaObject(metaObject, null, pattern, options, firstOnly: false);

        QObject[] FindByMetaObject(QMetaObject metaObject, string name, QRegularExpression pattern,
            Qt.FindChildOptions options, bool firstOnly)
        {
            if (metaObject is null)
                throw new ArgumentNullException(nameof(metaObject));
            if (pattern is not null && !pattern.IsValid())
                throw new ArgumentException("The regular expression is not valid.", nameof(pattern));

            IntPtr list = QObjectNative.FindChildren(Handle, metaObject.Handle,
                pattern?.Handle ?? IntPtr.Zero, name, (int)options, firstOnly ? 1 : 0);
            if (list == IntPtr.Zero)
                return Array.Empty<QObject>();
            try
            {
                int count = QObjectNative.Count(list);
                var found = new QObject[count];
                for (int i = 0; i < count; i++)
                    found[i] = FromHandle(QObjectNative.At(list, i));
                return found;
            }
            finally
            {
                QObjectNative.Free(list);
            }
        }

        static QObject First(QObject[] found) => found.Length == 0 ? null : found[0];

        // ---------------------------------------------------------------- managed walk

        static Func<QObject, bool> NameMatcher(string name)
            => name is null || name.Length == 0
                ? (_ => true)
                : candidate => candidate.ObjectName == name;

        // The native helper reads the pattern and subject without allocating a managed
        // QRegularExpressionMatch per candidate.
        static bool PatternMatches(QRegularExpression pattern, string name)
            => QObjectNative.HasMatch(pattern.Handle, name ?? string.Empty) != 0;

        T FindFirst<T>(Func<QObject, bool> matches, Qt.FindChildOptions options)
            where T : QObject
        {
            var found = new List<T>(1);
            CollectChildren(matches, options, found, firstOnly: true);
            return found.Count == 0 ? null : found[0];
        }

        T[] FindAll<T>(Func<QObject, bool> matches, Qt.FindChildOptions options)
            where T : QObject
        {
            var found = new List<T>();
            CollectChildren(matches, options, found, firstOnly: false);
            return found.ToArray();
        }

        // Qt checks a whole level before descending (qt_qFindChildren_helper): the direct children
        // are tested in order, then each child's subtree in child order. Recursion happens only
        // when FindChildrenRecursively is set, exactly as Qt tests it.
        void CollectChildren<T>(Func<QObject, bool> matches, Qt.FindChildOptions options,
            List<T> found, bool firstOnly)
            where T : QObject
        {
            QObject[] children = Children();
            foreach (QObject child in children)
            {
                if (child is T match && matches(child))
                {
                    found.Add(match);
                    if (firstOnly)
                        return;
                }
            }
            if ((options & Qt.FindChildOptions.FindChildrenRecursively) == 0)
                return;
            foreach (QObject child in children)
            {
                child.CollectChildren(matches, options, found, firstOnly);
                if (firstOnly && found.Count > 0)
                    return;
            }
        }
    }

    static class QObjectNative
    {
        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qobject_find_children",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FindChildren(IntPtr self, IntPtr metaObject, IntPtr pattern,
            [MarshalAs(UnmanagedType.LPWStr)] string name, int options, int firstOnly);

        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qobject_find_children_count",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Count(IntPtr list);

        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qobject_find_children_at",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr At(IntPtr list, int index);

        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qobject_find_children_free",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Free(IntPtr list);

        [DllImport(ApiNative.Library, EntryPoint = "qs_api_qregularexpression_has_match",
            CallingConvention = CallingConvention.Cdecl)]
        internal static extern int HasMatch(IntPtr pattern,
            [MarshalAs(UnmanagedType.LPWStr)] string subject);
    }
}
