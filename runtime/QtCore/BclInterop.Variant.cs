using System;
using System.Collections.Generic;
using System.Drawing;

// QVariant is Qt's boxed value. PyQt's QVariant.value() unwraps the primitive Qt values into
// Python objects, so a C# caller gets the same convenience through ToObject()/From().
namespace QtSharp.QtCore
{
    public partial class QVariant
    {
        /// <summary>
        /// Wraps a BCL value as a QVariant: the primitive types, string, string[], byte[], the date
        /// types, Uri, Guid and the System.Drawing geometry. A null becomes an invalid QVariant.
        /// A type without a Qt mapping throws NotSupportedException; GUI value types (QColor,
        /// QImage, ...) are constructed through their own wrapper: <c>new QVariant(new QColor(...))</c>.
        /// </summary>
        public static QVariant From(object value)
        {
            switch (value)
            {
                case null: return new QVariant();
                case bool b: return new QVariant(b);
                case int i: return new QVariant(i);
                case uint ui: return new QVariant(ui);
                case long l: return new QVariant(l);
                case ulong ul: return new QVariant(ul);
                case float f: return new QVariant(f);
                case double d: return new QVariant(d);
                case string s: return new QVariant(s);
                case string[] strings: return new QVariant(strings);
                case byte[] bytes:
                    using (var array = new QByteArray(bytes))
                        return new QVariant(array);
                case DateOnly date:
                    return new QVariant(new QDate(date));
                case TimeOnly time:
                    return new QVariant(new QTime(time));
                case DateTime dateTime:
                    using (var qtDateTime = new QDateTime(dateTime))
                        return new QVariant(qtDateTime);
                case DateTimeOffset dateTimeOffset:
                    using (var qtDateTime = new QDateTime(dateTimeOffset))
                        return new QVariant(qtDateTime);
                case Uri uri:
                    using (var url = new QUrl(uri))
                        return new QVariant(url);
                case Guid guid:
                    using (var uuid = new QUuid(guid))
                        return new QVariant(uuid);
                case Point point: return new QVariant(new QPoint(point));
                case PointF pointF: return new QVariant(new QPointF(pointF));
                case Size size: return new QVariant(new QSize(size));
                case SizeF sizeF: return new QVariant(new QSizeF(sizeF));
                case Rectangle rectangle: return new QVariant(new QRect(rectangle));
                case RectangleF rectangleF: return new QVariant(new QRectF(rectangleF));
                default:
                    throw new NotSupportedException(
                        $"QVariant.From does not know how to wrap a '{value.GetType().FullName}'. " +
                        "Use the QVariant constructor for the Qt wrapper instead.");
            }
        }

        /// <summary>
        /// The contained value as the closest BCL type, the way PyQt's QVariant.value() unwraps
        /// primitive Qt values: int/uint/long/ulong/float/double/bool, string, string[],
        /// byte[], DateOnly, TimeOnly, DateTime, Uri, Guid and the System.Drawing geometry. Lists
        /// and maps are unwrapped recursively (object[] / Dictionary&lt;string, object&gt;). A value
        /// with no BCL equivalent is returned as the QVariant itself, so its Qt wrapper can still
        /// be used.
        /// </summary>
        public object ToObject()
        {
            switch (type())
            {
                case QVariant.Type.Invalid: return null;
                case QVariant.Type.Bool: return ToBool();
                case QVariant.Type.Int: return ToInt();
                case QVariant.Type.UInt: return ToUInt();
                case QVariant.Type.LongLong: return ToLongLong();
                case QVariant.Type.ULongLong: return ToULongLong();
                case QVariant.Type.Double: return ToDouble();
                case QVariant.Type.Char:
                    using (var c = ToChar()) return c.Unicode();
                case QVariant.Type.String: return ToString();
                case QVariant.Type.StringList: return ToStringList();
                case QVariant.Type.ByteArray:
                    using (var bytes = ToByteArray()) return bytes.ToArray();
                case QVariant.Type.Date: return ToDate().ToDateOnly();
                case QVariant.Type.Time: return ToTime().ToTimeOnly();
                case QVariant.Type.DateTime:
                    using (var dateTime = ToDateTime()) return dateTime.ToDateTime();
                case QVariant.Type.Url:
                    using (var url = ToUrl()) return url.ToUri();
                case QVariant.Type.Uuid:
                    using (var uuid = ToUuid()) return uuid.ToGuid();
                case QVariant.Type.Point: return ToPoint().ToPoint();
                case QVariant.Type.PointF: return ToPointF().ToPointF();
                case QVariant.Type.Size: return ToSize().ToSize();
                case QVariant.Type.SizeF: return ToSizeF().ToSizeF();
                case QVariant.Type.Rect: return ToRect().ToRectangle();
                case QVariant.Type.RectF: return ToRectF().ToRectangleF();
                case QVariant.Type.List:
                    var items = ToList();
                    var result = new object[items.Length];
                    for (int i = 0; i < items.Length; i++)
                    {
                        result[i] = items[i].ToObject();
                        if (!ReferenceEquals(result[i], items[i]))
                            items[i].Dispose();
                    }
                    return result;
                case QVariant.Type.Map:
                case QVariant.Type.Hash:
                    var entries = type() == QVariant.Type.Map ? ToMap() : ToHash();
                    var dictionary = new Dictionary<string, object>(entries.Count, StringComparer.Ordinal);
                    foreach (var entry in entries)
                    {
                        dictionary[entry.Key] = entry.Value.ToObject();
                        if (!ReferenceEquals(dictionary[entry.Key], entry.Value))
                            entry.Value.Dispose();
                    }
                    return dictionary;
                default: return this;
            }
        }
    }
}
