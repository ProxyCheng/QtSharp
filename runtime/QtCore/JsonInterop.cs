using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace QtSharp.QtCore
{
    /// <summary>
    /// QJsonObject as a writable C# dictionary of <see cref="QJsonValue"/> - the shape Qt itself
    /// has, backed by the published <c>insert</c>/<c>remove</c>/<c>swap</c>. Values it hands out
    /// are Qt wrappers, so the caller owns them (dispose, or let the finalizer run).
    ///
    /// For LINQ over unwrapped BCL values use <see cref="QJsonObjectExtensions.ToObjectDictionary"/>:
    /// implementing a second IEnumerable&lt;T&gt; here would make extension-method inference
    /// ambiguous for every call on the object.
    ///
    /// Keys enumerate in Qt's sorted order, not document order: QJsonObject stores them sorted.
    /// </summary>
    public partial class QJsonObject : IDictionary<string, QJsonValue>
    {
        QJsonValue IDictionary<string, QJsonValue>.this[string key]
        {
            // Value() hands back an owned copy, like every other by-value accessor in the binding.
            get => Value(key);
            set => Insert(key, value)?.Dispose();
        }

        ICollection<string> IDictionary<string, QJsonValue>.Keys =>
            new ReadOnlyCollection<string>(Keys());

        ICollection<QJsonValue> IDictionary<string, QJsonValue>.Values
        {
            get
            {
                var values = new List<QJsonValue>((int)Count());
                foreach (string key in Keys())
                    values.Add(Value(key));
                return new ReadOnlyCollection<QJsonValue>(values);
            }
        }

        void IDictionary<string, QJsonValue>.Add(string key, QJsonValue value)
            => Insert(key, value)?.Dispose();

        bool IDictionary<string, QJsonValue>.ContainsKey(string key) => Contains(key);

        bool IDictionary<string, QJsonValue>.Remove(string key)
        {
            if (!Contains(key))
                return false;
            Remove(key);
            return true;
        }

        bool IDictionary<string, QJsonValue>.TryGetValue(string key, out QJsonValue value)
        {
            if (!Contains(key))
            {
                value = null;
                return false;
            }
            value = Value(key);
            return true;
        }

        // ---------------------------------------------- ICollection<KeyValuePair<string, QJsonValue>>

        int ICollection<KeyValuePair<string, QJsonValue>>.Count => (int)Count();

        bool ICollection<KeyValuePair<string, QJsonValue>>.IsReadOnly => false;

        void ICollection<KeyValuePair<string, QJsonValue>>.Add(KeyValuePair<string, QJsonValue> item)
            => Insert(item.Key, item.Value)?.Dispose();

        void ICollection<KeyValuePair<string, QJsonValue>>.Clear()
        {
            // Qt has no clear(); swapping in an empty object leaves the old contents in the
            // temporary, which owns and frees them on dispose.
            using (var empty = new QJsonObject())
                Swap(empty);
        }

        bool ICollection<KeyValuePair<string, QJsonValue>>.Contains(KeyValuePair<string, QJsonValue> item)
        {
            if (!Contains(item.Key))
                return false;
            using (var current = Value(item.Key))
                return Equals(ToObject(current), ToObject(item.Value));
        }

        void ICollection<KeyValuePair<string, QJsonValue>>.CopyTo(
            KeyValuePair<string, QJsonValue>[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            foreach (var entry in (IEnumerable<KeyValuePair<string, QJsonValue>>)this)
                array[arrayIndex++] = entry;
        }

        bool ICollection<KeyValuePair<string, QJsonValue>>.Remove(KeyValuePair<string, QJsonValue> item)
        {
            if (!((ICollection<KeyValuePair<string, QJsonValue>>)this).Contains(item))
                return false;
            Remove(item.Key);
            return true;
        }

        IEnumerator<KeyValuePair<string, QJsonValue>>
            IEnumerable<KeyValuePair<string, QJsonValue>>.GetEnumerator()
        {
            foreach (string key in Keys())
                yield return new KeyValuePair<string, QJsonValue>(key, Value(key));
        }

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable<KeyValuePair<string, QJsonValue>>)this).GetEnumerator();

        /// <summary>The BCL value behind a QJsonValue, the same unwrapping ToObjectDictionary uses.</summary>
        internal static object ToObject(QJsonValue json)
        {
            if (json == null)
                return null;
            var variant = json.ToVariant();
            object value = variant.ToObject();
            // ToObject returns the QVariant itself for a type with no BCL equivalent; that wrapper
            // is the result, so it must stay alive.
            if (!ReferenceEquals(value, variant))
                variant.Dispose();
            return value;
        }
    }

    /// <summary>
    /// The LINQ-friendly view of a QJsonObject: one snapshot of unwrapped BCL values. Nested
    /// objects come out as Dictionary&lt;string, object&gt; and arrays as object[], because that is
    /// what <see cref="QVariant.ToObject"/> produces for a QVariantMap/QVariantList.
    /// </summary>
    public static class QJsonObjectExtensions
    {
        public static Dictionary<string, object> ToObjectDictionary(this QJsonObject qjson)
        {
            if (qjson == null)
                throw new ArgumentNullException(nameof(qjson));
            var result = new Dictionary<string, object>((int)qjson.Count(), StringComparer.Ordinal);
            foreach (string key in qjson.Keys())
                using (var value = qjson.Value(key))
                    result[key] = QJsonObject.ToObject(value);
            return result;
        }
    }

    /// <summary>
    /// QJsonArray as a writable C# list of <see cref="QJsonValue"/>. Values it hands out are Qt
    /// wrappers, so the caller owns them; <see cref="QJsonArrayExtensions.ToObjectList"/> gives the
    /// unwrapped snapshot for LINQ (a second IEnumerable&lt;T&gt; here would make inference
    /// ambiguous, the same reason QJsonObject's unwrapped view is an extension method).
    /// </summary>
    public partial class QJsonArray : IList<QJsonValue>
    {
        QJsonValue IList<QJsonValue>.this[int index]
        {
            get => At(index);
            set => Replace(index, value);
        }

        int ICollection<QJsonValue>.Count => (int)Count();

        bool ICollection<QJsonValue>.IsReadOnly => false;

        void ICollection<QJsonValue>.Add(QJsonValue item) => Append(item);

        void IList<QJsonValue>.Insert(int index, QJsonValue item) => Insert(index, item);

        void IList<QJsonValue>.RemoveAt(int index) => RemoveAt(index);

        bool ICollection<QJsonValue>.Remove(QJsonValue item)
        {
            int index = ((IList<QJsonValue>)this).IndexOf(item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        void ICollection<QJsonValue>.Clear()
        {
            using (var empty = new QJsonArray())
                Swap(empty);
        }

        bool ICollection<QJsonValue>.Contains(QJsonValue item)
            => ((IList<QJsonValue>)this).IndexOf(item) >= 0;

        int IList<QJsonValue>.IndexOf(QJsonValue item)
        {
            object wanted = QJsonObject.ToObject(item);
            for (int i = 0; i < (int)Count(); i++)
            {
                using (var current = At(i))
                    if (Equals(QJsonObject.ToObject(current), wanted))
                        return i;
            }
            return -1;
        }

        void ICollection<QJsonValue>.CopyTo(QJsonValue[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            foreach (QJsonValue item in (IEnumerable<QJsonValue>)this)
                array[arrayIndex++] = item;
        }

        IEnumerator<QJsonValue> IEnumerable<QJsonValue>.GetEnumerator()
        {
            for (int i = 0; i < (int)Count(); i++)
                yield return At(i);
        }

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable<QJsonValue>)this).GetEnumerator();
    }

    /// <summary>The unwrapped snapshot of a QJsonArray, for LINQ over BCL values.</summary>
    public static class QJsonArrayExtensions
    {
        public static List<object> ToObjectList(this QJsonArray qjson)
        {
            if (qjson == null)
                throw new ArgumentNullException(nameof(qjson));
            var result = new List<object>((int)qjson.Count());
            for (int i = 0; i < (int)qjson.Count(); i++)
                using (var value = qjson.At(i))
                    result.Add(QJsonObject.ToObject(value));
            return result;
        }
    }
}
