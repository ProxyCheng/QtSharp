using System;
using System.Collections.Generic;

// QObject::findChild/findChildren as C# instance methods. Wrappers are created as the object's
// actual bound type (QObjectTypeRegistry), so the type test is the ordinary `is`/`IsInstanceOfType`
// on the managed wrapper; there is no second type table and no qobject_cast helper. A child whose
// C++ class is not published resolves to the nearest bound base, exactly what a cast to that base
// would find.
namespace QtSharp.QtCore
{
    public partial class QObject
    {
        /// <summary>
        /// The first child whose managed type is <typeparamref name="T"/> (or a subclass of it),
        /// optionally filtered by <paramref name="name"/>. Qt's <c>QObject::findChild&lt;T&gt;</c>:
        /// every direct child is tested first, then each child's subtree is searched in order.
        /// A null <paramref name="name"/> means "any name"; an empty string matches an object whose
        /// <see cref="ObjectName"/> is empty.
        /// </summary>
        public T FindChild<T>(string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            where T : QObject
            => FindChildCore<T>(this, child => child is T, name, null, options);

        /// <summary>The runtime-type spelling of <see cref="FindChild{T}(string, Qt.FindChildOptions)"/>.</summary>
        public QObject FindChild(Type type, string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
        {
            ValidateLookupType(type, nameof(type));
            return FindChildCore<QObject>(this, type.IsInstanceOfType, name, null, options);
        }

        /// <summary>The PyQt-style spelling: the first child matching any of <paramref name="types"/>.</summary>
        public QObject FindChild(Type[] types, string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
        {
            ValidateLookupTypes(types, nameof(types));
            return FindChildCore<QObject>(this, child => MatchesAny(child, types), name, null, options);
        }

        /// <summary>
        /// All matching children in Qt's traversal order: a child is added before its subtree, and
        /// the next sibling follows that subtree. See <see cref="FindChild{T}(string, Qt.FindChildOptions)"/>
        /// for the name rules.
        /// </summary>
        public T[] FindChildren<T>(string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            where T : QObject
        {
            var result = new List<T>();
            CollectChildren(this, child => child is T, name, null, options, result);
            return result.ToArray();
        }

        /// <summary>The runtime-type spelling of <see cref="FindChildren{T}(string, Qt.FindChildOptions)"/>.</summary>
        public QObject[] FindChildren(Type type, string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
        {
            ValidateLookupType(type, nameof(type));
            var result = new List<QObject>();
            CollectChildren(this, type.IsInstanceOfType, name, null, options, result);
            return result.ToArray();
        }

        /// <summary>The PyQt-style spelling: all children matching any of <paramref name="types"/>.</summary>
        public QObject[] FindChildren(Type[] types, string name = null,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
        {
            ValidateLookupTypes(types, nameof(types));
            var result = new List<QObject>();
            CollectChildren(this, child => MatchesAny(child, types), name, null, options, result);
            return result.ToArray();
        }

        /// <summary>
        /// All matching children whose <see cref="ObjectName"/> matches <paramref name="pattern"/>
        /// (Qt's <c>findChildren(QRegularExpression)</c> overload), in traversal order.
        /// </summary>
        public T[] FindChildren<T>(QRegularExpression pattern,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
            where T : QObject
        {
            if (pattern == null)
                throw new ArgumentNullException(nameof(pattern));
            var result = new List<T>();
            CollectChildren(this, child => child is T, null, pattern, options, result);
            return result.ToArray();
        }

        /// <summary>The runtime-type spelling of the regular-expression overload.</summary>
        public QObject[] FindChildren(Type type, QRegularExpression pattern,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
        {
            ValidateLookupType(type, nameof(type));
            if (pattern == null)
                throw new ArgumentNullException(nameof(pattern));
            var result = new List<QObject>();
            CollectChildren(this, type.IsInstanceOfType, null, pattern, options, result);
            return result.ToArray();
        }

        /// <summary>The PyQt-style spelling: all children matching one of the types and the pattern.</summary>
        public QObject[] FindChildren(Type[] types, QRegularExpression pattern,
            Qt.FindChildOptions options = Qt.FindChildOptions.FindChildrenRecursively)
        {
            ValidateLookupTypes(types, nameof(types));
            if (pattern == null)
                throw new ArgumentNullException(nameof(pattern));
            var result = new List<QObject>();
            CollectChildren(this, child => MatchesAny(child, types), null, pattern, options, result);
            return result.ToArray();
        }

        static T FindChildCore<T>(QObject parent, Func<QObject, bool> typeMatches,
            string name, QRegularExpression pattern, Qt.FindChildOptions options) where T : QObject
        {
            QObject[] children = parent.Children();
            foreach (QObject child in children)
                if (Matches(child, name, pattern) && typeMatches(child))
                    return (T)child;

            if ((options & Qt.FindChildOptions.FindChildrenRecursively) != 0)
                foreach (QObject child in children)
                    if (FindChildCore<T>(child, typeMatches, name, pattern, options) is { } found)
                        return found;

            return null;
        }

        static void CollectChildren<T>(QObject parent, Func<QObject, bool> typeMatches,
            string name, QRegularExpression pattern, Qt.FindChildOptions options, List<T> result)
            where T : QObject
        {
            foreach (QObject child in parent.Children())
            {
                if (Matches(child, name, pattern) && typeMatches(child))
                    result.Add((T)child);

                if ((options & Qt.FindChildOptions.FindChildrenRecursively) != 0)
                    CollectChildren(child, typeMatches, name, pattern, options, result);
            }
        }

        static bool Matches(QObject child, string name, QRegularExpression pattern)
        {
            if (pattern != null)
            {
                using QRegularExpressionMatch match = pattern.Match(child.ObjectName);
                return match.HasMatch();
            }
            return name == null || child.ObjectName == name;
        }

        static bool MatchesAny(QObject child, Type[] types)
        {
            foreach (Type type in types)
                if (type.IsInstanceOfType(child))
                    return true;
            return false;
        }

        static void ValidateLookupType(Type type, string parameterName)
        {
            if (type == null)
                throw new ArgumentNullException(parameterName);
            if (!typeof(QObject).IsAssignableFrom(type))
                throw new ArgumentException("the type must derive from QObject", parameterName);
        }

        static void ValidateLookupTypes(Type[] types, string parameterName)
        {
            if (types == null)
                throw new ArgumentNullException(parameterName);
            if (types.Length == 0)
                throw new ArgumentException("at least one type is required", parameterName);
            foreach (Type type in types)
                ValidateLookupType(type, parameterName);
        }
    }
}
