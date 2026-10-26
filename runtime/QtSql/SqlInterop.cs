using System;
using System.Collections.Generic;
using System.Globalization;
using QtSharp.QtCore;

namespace QtSharp.QtSql
{
    /// <summary>
    /// The BCL-facing half of QtSql: Qt's cursor-and-record API stays what it is, and these
    /// extension methods turn it into the shape C# code wants. LINQ to Objects works over the
    /// rows; LINQ itself is not a query language here - no expression is translated to SQL, and
    /// there is deliberately no IQueryable provider (use EF Core's SQLite provider for that).
    /// Values come out of <see cref="QVariant.ToObject"/>, the same conversion PyQt exposes as
    /// QVariant.value().
    /// </summary>
    public static class QSqlQueryExtensions
    {
        /// <summary>
        /// The remaining rows as plain dictionaries (column name -&gt; BCL value). This is the
        /// LINQ-friendly shape: the record is a disposable wrapper around a Qt value, while each
        /// dictionary is a copy that outlives the cursor.
        /// </summary>
        public static IEnumerable<Dictionary<string, object>> Rows(this QSqlQuery query)
        {
            while (query.Next())
                using (var record = query.Record())
                    yield return record.ToDictionary();
        }

        /// <summary>
        /// The remaining rows as <see cref="QSqlRecord"/> copies, for code that wants the Qt
        /// accessors (field types, null-ness, raw QVariant). Forward-only, like the cursor: each
        /// step calls next(), so the query is consumed. The caller owns each record and should
        /// dispose it - prefer <see cref="Rows"/> or <see cref="SelectRows{T}"/>.
        /// </summary>
        public static IEnumerable<QSqlRecord> Records(this QSqlQuery query)
        {
            while (query.Next())
                yield return query.Record();
        }

        /// <summary>
        /// Projects each remaining row through <paramref name="map"/>; the record is disposed once
        /// the projection returns, so the result must not keep it.
        /// </summary>
        public static IEnumerable<T> SelectRows<T>(this QSqlQuery query, Func<QSqlRecord, T> map)
        {
            foreach (var record in query.Records())
                using (record)
                    yield return map(record);
        }
    }

    /// <summary>The same shapes for a model that already holds its rows.</summary>
    public static class QSqlQueryModelExtensions
    {
        /// <summary>The model's rows as dictionaries, in row order.</summary>
        public static IEnumerable<Dictionary<string, object>> Rows(this QSqlQueryModel model)
        {
            for (int row = 0; row < model.RowCount(); row++)
                using (var record = model.Record(row))
                    yield return record.ToDictionary();
        }

        /// <summary>The model's rows as copies; the caller owns (and should dispose) each one.</summary>
        public static IEnumerable<QSqlRecord> Records(this QSqlQueryModel model)
        {
            for (int row = 0; row < model.RowCount(); row++)
                yield return model.Record(row);
        }
    }

    /// <summary>Record accessors in the BCL's vocabulary.</summary>
    public static class QSqlRecordExtensions
    {
        /// <summary>
        /// The record as a dictionary. The column order is the record's, so the dictionary keeps
        /// the SELECT's order for callers that iterate it; lookups are by name.
        /// </summary>
        public static Dictionary<string, object> ToDictionary(this QSqlRecord record)
        {
            var result = new Dictionary<string, object>(record.Count(), StringComparer.Ordinal);
            for (int i = 0; i < record.Count(); i++)
                result[record.FieldName(i)] = record.Value(i).ToObject();
            return result;
        }

        /// <summary>
        /// A typed read: the QVariant unwrapped with <see cref="QVariant.ToObject"/> and then
        /// converted. SQLite hands INTEGER columns back as QVariant::LongLong, so
        /// <c>Get&lt;int&gt;("id")</c> is the natural spelling even though the variant is a long.
        /// </summary>
        public static T Get<T>(this QSqlRecord record, string name)
        {
            object value = record.Value(name).ToObject();
            if (value == null)
                return default;
            if (value is T typed)
                return typed;
            if (typeof(T).IsEnum)
                return (T)Enum.ToObject(typeof(T), value);
            return (T)Convert.ChangeType(
                value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
        }

        /// <summary><c>Get&lt;T&gt;</c> with a fallback for a NULL column or a missing field.</summary>
        public static T Get<T>(this QSqlRecord record, string name, T fallback)
        {
            try
            {
                return record.Value(name).ToObject() == null ? fallback : record.Get<T>(name);
            }
            catch (ArgumentException)
            {
                // QSqlRecord::value(name) on a field the result does not have.
                return fallback;
            }
        }
    }
}
