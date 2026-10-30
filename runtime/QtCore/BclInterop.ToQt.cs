using System;
using System.Collections;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;

// The source-side spelling of the BCL -> Qt bridges: `value.ToQDateTime()` instead of
// `new QDateTime(value)`. They are extension methods rather than implicit operators for the
// reasons the bridges themselves document, and rather than overloads because a new method name
// cannot change overload resolution for existing code:
//
//     using (QDateTime stamp = DateTime.UtcNow.ToQDateTime()) { ... }
//     using (QUrl url = uri.ToQUrl()) { ... }
//
// A returned handle class (QDateTime, QUrl, QLocale, QTimeZone, QVersionNumber, QBitArray, QDir,
// QFileInfo, QByteArray, QJsonValue, QJsonObject, QJsonArray) owns a native object and is the
// caller's to dispose. QDate and QTime are structs and need no disposal. A null reference
// receiver throws ArgumentNullException through the bridge constructor, except JsonNode: a null
// node is JSON null, so ToQJsonValue() returns QJsonValue::Null (use QJsonValue.FromJsonNode for
// the same rule explicitly).
namespace QtSharp.QtCore
{
    /// <summary>BCL -> Qt conversions for the QtCore types, as extension methods on the BCL type.</summary>
    public static class BclToQtExtensions
    {
        /// <summary>A QDateTime for the same instant and Kind.</summary>
        public static QDateTime ToQDateTime(this DateTime value) => new QDateTime(value);

        /// <summary>A QDateTime carrying the offset (Qt's OffsetFromUTC time spec).</summary>
        public static QDateTime ToQDateTime(this DateTimeOffset value) => new QDateTime(value);

        /// <summary>A QDate for the same calendar date.</summary>
        public static QDate ToQDate(this DateOnly value) => new QDate(value);

        /// <summary>A QTime for the same time of day (sub-millisecond ticks are truncated).</summary>
        public static QTime ToQTime(this TimeOnly value) => new QTime(value);

        /// <summary>A QUrl for the URI (a relative URI keeps its original spelling).</summary>
        public static QUrl ToQUrl(this Uri value) => new QUrl(value);

        /// <summary>A QLocale naming the same language/territory.</summary>
        public static QLocale ToQLocale(this CultureInfo value) => new QLocale(value);

        /// <summary>A QTimeZone with the same id and rules; an untranslatable zone throws.</summary>
        public static QTimeZone ToQTimeZone(this TimeZoneInfo value) => new QTimeZone(value);

        /// <summary>A QVersionNumber with the same segments.</summary>
        public static QVersionNumber ToQVersionNumber(this Version value) => new QVersionNumber(value);

        /// <summary>A QBitArray with the same bits.</summary>
        public static QBitArray ToQBitArray(this BitArray value) => new QBitArray(value);

        /// <summary>A QDir for the directory (the filter/sort state is Qt's default).</summary>
        public static QDir ToQDir(this DirectoryInfo value) => new QDir(value);

        /// <summary>A QFileInfo for the path (accepts FileInfo and DirectoryInfo).</summary>
        public static QFileInfo ToQFileInfo(this FileSystemInfo value) => new QFileInfo(value);

        /// <summary>A QByteArray copying the bytes.</summary>
        public static QByteArray ToQByteArray(this byte[] value) => new QByteArray(value);

        /// <summary>
        /// A QJsonValue copying the node; a null node is QJsonValue::Null, because that is what a
        /// null JsonNode means in a JSON tree.
        /// </summary>
        public static QJsonValue ToQJsonValue(this JsonNode value) => QJsonValue.FromJsonNode(value);

        /// <summary>A QJsonObject copying the object.</summary>
        public static QJsonObject ToQJsonObject(this JsonObject value) => new QJsonObject(value);

        /// <summary>A QJsonArray copying the array.</summary>
        public static QJsonArray ToQJsonArray(this JsonArray value) => new QJsonArray(value);

        // The geometry types live in QtCore (qpoint.h/qsize.h/qrect.h), so their extensions do
        // too. The one lossy direction (QPointF -> PointF) is deliberately absent: qreal is a
        // double, and truncating it to int would be silent. QRect carries Qt's inclusive
        // right()/bottom() convention while Rectangle expresses the same extent through
        // Width/Height/X/Y; ToQRect() copies those four, exactly like the explicit constructor.

        /// <summary>A QPoint with the same coordinates.</summary>
        public static QPoint ToQPoint(this Point value) => new QPoint(value);

        /// <summary>A QPointF with the same coordinates (int -> double is exact).</summary>
        public static QPointF ToQPointF(this Point value) => new QPointF(value.X, value.Y);

        /// <summary>A QPointF with the same coordinates (float -> double is exact).</summary>
        public static QPointF ToQPointF(this PointF value) => new QPointF(value);

        /// <summary>A QSize with the same width and height.</summary>
        public static QSize ToQSize(this Size value) => new QSize(value);

        /// <summary>A QSizeF with the same width and height (int -> double is exact).</summary>
        public static QSizeF ToQSizeF(this Size value) => new QSizeF(value.Width, value.Height);

        /// <summary>A QSizeF with the same width and height (float -> double is exact).</summary>
        public static QSizeF ToQSizeF(this SizeF value) => new QSizeF(value);

        /// <summary>A QRect with the same x, y, width and height.</summary>
        public static QRect ToQRect(this Rectangle value) => new QRect(value);

        /// <summary>A QRectF with the same x, y, width and height.</summary>
        public static QRectF ToQRectF(this Rectangle value)
            => new QRectF(value.X, value.Y, value.Width, value.Height);

        /// <summary>A QRectF with the same x, y, width and height (float -> double is exact).</summary>
        public static QRectF ToQRectF(this RectangleF value) => new QRectF(value);
    }
}
