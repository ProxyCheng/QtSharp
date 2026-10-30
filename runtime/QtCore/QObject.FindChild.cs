using System;
using System.Collections.Generic;

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
            return FindFirst<T>(name => PatternMatches(pattern, name), options);
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
            return FindAll<T>(name => PatternMatches(pattern, name), options);
        }

        static Func<string, bool> NameMatcher(string name)
            => name is null || name.Length == 0
                ? (_ => true)
                : candidate => candidate == name;

        // QRegularExpression::match() returns an owned match object; dispose it once the answer is
        // read. An object with no name has an empty object name, which a pattern can still match.
        static bool PatternMatches(QRegularExpression pattern, string name)
        {
            using var match = pattern.Match(name ?? string.Empty);
            return match.HasMatch();
        }

        T FindFirst<T>(Func<string, bool> matches, Qt.FindChildOptions options)
            where T : QObject
        {
            var found = new List<T>(1);
            CollectChildren(matches, options, found, firstOnly: true);
            return found.Count == 0 ? null : found[0];
        }

        T[] FindAll<T>(Func<string, bool> matches, Qt.FindChildOptions options)
            where T : QObject
        {
            var found = new List<T>();
            CollectChildren(matches, options, found, firstOnly: false);
            return found.ToArray();
        }

        // Qt checks a whole level before descending (qt_qFindChildren_helper): the direct children
        // are tested in order, then each child's subtree in child order. Recursion happens only
        // when FindChildrenRecursively is set, exactly as Qt tests it.
        void CollectChildren<T>(Func<string, bool> matches, Qt.FindChildOptions options,
            List<T> found, bool firstOnly)
            where T : QObject
        {
            QObject[] children = Children();
            foreach (QObject child in children)
            {
                if (child is T match && matches(child.ObjectName))
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
}
