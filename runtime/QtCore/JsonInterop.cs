using System;
using System.Collections;
using System.Collections.Generic;

namespace QtSharp.QtCore
{
    /// <summary>
    /// QJsonObject through C#'s container interfaces. The Qt type stays the Qt type; this part
    /// makes it enumerate like a dictionary of BCL values - each value unwrapped through
    /// <see cref="QVariant.ToObject"/>, the same conversion QtSql's Rows() uses, so a nested object
    /// arrives as a Dictionary and an array as an object[]. That means LINQ works over a document
    /// without the caller disposing a QJsonValue wrapper per element.
    ///
    /// It is read-only on purpose: QJsonObject's mutators (insert, operator[]) return an iterator
    /// that the model does not publish, so there is no IDictionary here yet.
    /// </summary>
    public partial class QJsonObject : IReadOnlyDictionary<string, object>
    {
        /// <summary>The unwrapped value of a key; a missing key reads as null.</summary>
        public object this[string key] => Unwrap(key);

        // Keys and Count are explicit: the generated class already has Keys() and Count() methods,
        // and C# does not let a property share a name with a method in the same type.
        IEnumerable<string> IReadOnlyDictionary<string, object>.Keys => Keys();

        IEnumerable<object> IReadOnlyDictionary<string, object>.Values
        {
            get
            {
                foreach (string key in Keys())
                    yield return Unwrap(key);
            }
        }

        int IReadOnlyCollection<KeyValuePair<string, object>>.Count => (int)Count();

        public bool ContainsKey(string key) => Contains(key);

        public bool TryGetValue(string key, out object value)
        {
            if (!Contains(key))
            {
                value = null;
                return false;
            }
            value = Unwrap(key);
            return true;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (string key in Keys())
                yield return new KeyValuePair<string, object>(key, Unwrap(key));
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        object Unwrap(string key)
        {
            using (var json = Value(key))
            {
                var variant = json.ToVariant();
                object value = variant.ToObject();
                // ToObject returns the QVariant itself for a type with no BCL equivalent; that
                // wrapper is the result, so it must stay alive.
                if (!ReferenceEquals(value, variant))
                    variant.Dispose();
                return value;
            }
        }
    }
}
