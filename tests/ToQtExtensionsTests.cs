using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QtSharp.Tests
{
    /// <summary>
    /// The BCL -> Qt extension methods (`dateTime.ToQDateTime()`, `point.ToQPoint()`, ...): each is
    /// the named-constructor bridge with the receiver moved to the left, so a reference receiver
    /// keeps the constructor's null check and the handle results stay the caller's to dispose.
    /// </summary>
    [Test("test-toqt-extensions")]
    public static class ToQtExtensionsTests
    {
        public static int Run(TestContext context)
        {
            // ---------------------------------------------------------------- QtCore value types
            var instant = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            using (QDateTime stamp = instant.ToQDateTime())
                context.Check(stamp.ToDateTime() == instant, "DateTime.ToQDateTime round-trips");

            var offsetInstant = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(8));
            using (QDateTime stamp = offsetInstant.ToQDateTime())
                context.Check(stamp.ToDateTimeOffset() == offsetInstant,
                    "DateTimeOffset.ToQDateTime round-trips");

            var date = new DateOnly(2026, 10, 30);
            QDate qdate = date.ToQDate();
            context.Check(qdate.ToDateOnly() == date, "DateOnly.ToQDate");

            var time = new TimeOnly(12, 34, 56);
            QTime qtime = time.ToQTime();
            context.Check(qtime.ToTimeOnly() == time, "TimeOnly.ToQTime");

            var uri = new Uri("https://example.com/a?b=1");
            using (QUrl url = uri.ToQUrl())
                context.Check(url.ToUri() == uri, "Uri.ToQUrl");

            using (QTimeZone zone = TimeZoneInfo.Utc.ToQTimeZone())
                context.Check(zone.IsValid(), "TimeZoneInfo.Utc.ToQTimeZone is valid");

            using (QVersionNumber version = new Version(1, 2, 3).ToQVersionNumber())
                context.Check(version.ToString() == "1.2.3",
                    $"Version.ToQVersionNumber ({version})");

            var bits = new BitArray(new[] { true, false, true });
            using (QBitArray qbits = bits.ToQBitArray())
            {
                BitArray back = qbits.ToBitArray();
                context.Check(back.Length == 3 && back[0] && !back[1] && back[2],
                    "BitArray.ToQBitArray");
            }

            using (QRegularExpression regex = new Regex("a+b", RegexOptions.IgnoreCase).ToQRegularExpression())
                context.Check(regex.Pattern() == "a+b", "Regex.ToQRegularExpression");

            byte[] bytes = { 1, 2, 250 };
            using (QByteArray qbytes = bytes.ToQByteArray())
                context.Check(qbytes.ToArray().SequenceEqual(bytes), "byte[].ToQByteArray");

            JsonNode node = new JsonObject { ["answer"] = 42.0, ["tags"] = new JsonArray("a") };
            using (QJsonValue value = node.ToQJsonValue())
            using (QJsonObject obj = ((JsonObject)node).ToQJsonObject())
            using (QJsonArray array = new JsonArray(1.5).ToQJsonArray())
            {
                context.Check(value.IsObject() && obj.Contains("answer") && array.Size() == 1,
                    "JsonNode/JsonObject/JsonArray.ToQJson*");
                using (QJsonValue nullValue = ((JsonNode)null).ToQJsonValue())
                    context.Check(nullValue.IsNull(), "a null JsonNode is JSON null");
            }

            // ---------------------------------------------------------------- QtGui value types
            var point = new Point(3, 4);
            context.Check(point.ToQPoint().ToPoint() == point, "Point.ToQPoint");
            var pointF = new PointF(1.5f, -2.25f);
            context.Check(pointF.ToQPointF().ToPointF() == pointF, "PointF.ToQPointF");
            context.Check(point.ToQPointF().X() == 3 && point.ToQPointF().Y() == 4,
                "Point.ToQPointF widens in both directions");

            var size = new Size(320, 200);
            context.Check(size.ToQSize().ToSize() == size, "Size.ToQSize");
            var sizeF = new SizeF(1.5f, 2.5f);
            context.Check(sizeF.ToQSizeF().ToSizeF() == sizeF, "SizeF.ToQSizeF");

            var rectangle = new Rectangle(1, 2, 30, 40);
            context.Check(rectangle.ToQRect().ToRectangle() == rectangle, "Rectangle.ToQRect");
            var rectangleF = new RectangleF(1.5f, 2.5f, 3.5f, 4.5f);
            context.Check(rectangleF.ToQRectF().ToRectangleF() == rectangleF,
                "RectangleF.ToQRectF");

            var color = Color.FromArgb(4, 1, 2, 3);
            context.Check(color.ToQColor().ToColor() == color, "Color.ToQColor");

            var vector = new Vector3(1.0f, 2.0f, 3.0f);
            context.Check(vector.ToQVector3D().ToVector3() == vector, "Vector3.ToQVector3D");
            var quaternion = new Quaternion(0.1f, 0.2f, 0.3f, 0.4f);
            context.Check(quaternion.ToQQuaternion().ToQuaternion() == quaternion,
                "Quaternion.ToQQuaternion");

            var matrix = Matrix4x4.CreateTranslation(1.0f, 2.0f, 3.0f);
            using (QMatrix4x4 qmatrix = matrix.ToQMatrix4x4())
                context.Check(qmatrix.ToMatrix4x4() == matrix, "Matrix4x4.ToQMatrix4x4");

            // ---------------------------------------------------------------- QtNetwork
            using (QHostAddress address = IPAddress.Loopback.ToQHostAddress())
                context.Check(address.ToIPAddress().Equals(IPAddress.Loopback),
                    "IPAddress.ToQHostAddress");

            // ---------------------------------------------------------------- filesystem
            string root = Path.Combine(Path.GetTempPath(),
                "qtsharp-toqt-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            try
            {
                using (QDir dir = new DirectoryInfo(root).ToQDir())
                    context.Check(dir.Exists(), "DirectoryInfo.ToQDir");

                string filePath = Path.Combine(root, "payload.txt");
                File.WriteAllText(filePath, "hello");
                using (QFileInfo info = new FileInfo(filePath).ToQFileInfo())
                    context.Check(info.Exists() && info.Size() == 5, "FileInfo.ToQFileInfo");
                using (QFileInfo info = new DirectoryInfo(root).ToQFileInfo())
                    context.Check(info.IsDir(), "DirectoryInfo.ToQFileInfo");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { }
            }

            // ---------------------------------------------------------------- null receiver
            bool nullRefused = false;
            try { ((Uri)null).ToQUrl(); }
            catch (ArgumentNullException) { nullRefused = true; }
            context.Check(nullRefused, "a null reference receiver throws ArgumentNullException");

            return context.ExitCode;
        }
    }
}
