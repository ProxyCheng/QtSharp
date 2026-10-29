using System;
using System.Collections.Generic;

// QObject::findChild<T>() / findChildren<T>() are templates, so the model cannot publish them: the
// type filter is a C# generic here and the walk uses the wrapper tree the binding already owns.
// The children come back as their actual bound types (QObjectTypeRegistry), which makes `is T` the
// same test Qt's qobject_cast<T*> performs - and it also matches a managed subclass, which a
// static meta-object cast cannot. Qt's Qt::FindChildOptions enum is already generated, so this file
// only adds the two lookups.
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
        {
            var found = new List<T>(1);
            CollectChildren(name, options, found, firstOnly: true);
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>
        /// Every descendant that is a <typeparamref name="T"/> and whose
        /// <see cref="ObjectName"/> is <paramref name="name"/>, in the order
        /// <c>QObject::findChildren</c> produces them; an empty array when there is none.
        /// </summary>
        public T[] FindChildren<T>(string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            where T : QObject
        {
            var found = new List<T>();
            CollectChildren(name, options, found, firstOnly: false);
            return found.ToArray();
        }

        // Qt checks a whole level before descending (qt_qFindChildren_helper): the direct children
        // are tested in order, then each child's subtree in child order. Recursion happens only
        // when FindChildrenRecursively is set, exactly as Qt tests it.
        void CollectChildren<T>(string name, Qt.FindChildOptions options, List<T> found, bool firstOnly)
            where T : QObject
        {
            QObject[] children = Children();
            foreach (QObject child in children)
            {
                if ((name == null || name.Length == 0 || child.ObjectName == name) && child is T match)
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
                child.CollectChildren(name, options, found, firstOnly);
                if (firstOnly && found.Count > 0)
                    return;
            }
        }
    }
}
