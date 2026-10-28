using System;
using System.Collections.Generic;

// QProcessEnvironment is the environment block QProcess hands to a child. It is deliberately not
// exposed as a live IDictionary view: QProcessEnvironment can inherit the parent environment
// (QProcessEnvironment::InheritsFromParent()), so "the entries" are only fully known at spawn
// time, and a live view would have to guess whether an unset name means "inherited" or "absent".
// The conversion is therefore a snapshot in both directions:
//
//     Dictionary<string, string> snapshot = environment.ToDictionary(comparer);
//     var environment = new QProcessEnvironment(snapshot);   // or FromDictionary(snapshot)
//
// The caller supplies the comparer because the right one depends on what the snapshot is for:
// environment names are case-insensitive on Windows and case-sensitive elsewhere, so
// StringComparer.OrdinalIgnoreCase is the Windows spelling and StringComparer.Ordinal is the
// portable one. The default is Ordinal; the comparer only shapes the returned Dictionary, never
// the Qt object. If the caller's comparer treats two distinct environment names as equal, the
// conversion throws instead of dropping one of them.
//
// A QProcessEnvironment built from a dictionary starts empty and does not inherit the parent
// (InheritsFromParent() is false). Its entries are then whatever the dictionary held: the
// snapshot cannot carry inheritance, because that state has no dictionary equivalent.
namespace QtSharp.QtCore
{
    public partial class QProcessEnvironment
    {
        /// <summary>
        /// Builds a QProcessEnvironment from a snapshot of name/value pairs. The result is a fresh,
        /// non-inheriting environment (the Qt default constructor), populated with the entries.
        /// </summary>
        /// <param name="values">The environment entries to copy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// An entry has a null name or a null value; neither can be stored in a Qt environment.
        /// </exception>
        /// <remarks>
        /// The pairs are copied in enumeration order; later writes do not affect the Qt object.
        /// The result does not inherit the parent environment (<see cref="InheritsFromParent"/>
        /// is false), so it is a complete block, not a patch on the process's environment.
        /// </remarks>
        public QProcessEnvironment(IReadOnlyDictionary<string, string> values) : this()
        {
            if (values is null)
                throw new ArgumentNullException(nameof(values));

            foreach (KeyValuePair<string, string> pair in values)
            {
                if (pair.Key is null)
                    throw new InvalidOperationException(
                        "A process environment name cannot be null.");
                if (pair.Value is null)
                    throw new InvalidOperationException(
                        $"The process environment value for '{pair.Key}' is null; Qt environment "
                        + "values are strings and cannot be null.");
                Insert(pair.Key, pair.Value);
            }
        }

        /// <summary>
        /// A snapshot of the environment as a dictionary with the supplied name comparer (Ordinal
        /// when null).
        /// </summary>
        /// <param name="comparer">
        /// The name comparer for the returned dictionary; for example
        /// <see cref="StringComparer.OrdinalIgnoreCase"/> for Windows-style environment names.
        /// It does not affect the QProcessEnvironment.
        /// </param>
        /// <returns>
        /// A new dictionary copied from the current entries; changing it does not change the Qt
        /// environment and vice versa. It implements <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Two distinct environment names compare equal under <paramref name="comparer"/> (for
        /// example "PATH" and "Path" with <see cref="StringComparer.OrdinalIgnoreCase"/>); the
        /// snapshot refuses to drop either.
        /// </exception>
        /// <remarks>
        /// This is a snapshot, not a live view. A QProcessEnvironment that inherits the parent
        /// environment reports only its own entries here; the inherited entries are added by
        /// QProcess when the child is started and are not visible as members.
        /// </remarks>
        public Dictionary<string, string> ToDictionary(IEqualityComparer<string> comparer = null)
        {
            var result = new Dictionary<string, string>(comparer ?? StringComparer.Ordinal);
            string[] keys = Keys() ?? Array.Empty<string>();
            foreach (string key in keys)
            {
                if (!result.TryAdd(key, Value(key)))
                    throw new InvalidOperationException(
                        $"The process environment contains '{key}' and another name that compares "
                        + "equal under the supplied comparer; a dictionary cannot hold both.");
            }
            return result;
        }

        /// <summary>
        /// A QProcessEnvironment snapshot of a dictionary; same as the
        /// <see cref="QProcessEnvironment(IReadOnlyDictionary{string,string})"/> constructor.
        /// </summary>
        /// <param name="values">The environment entries to copy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
        /// <exception cref="InvalidOperationException">An entry has a null name or value.</exception>
        public static QProcessEnvironment FromDictionary(IReadOnlyDictionary<string, string> values)
            => new QProcessEnvironment(values);
    }
}
