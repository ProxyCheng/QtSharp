using System;
using System.Drawing;
using System.Numerics;

namespace QtSharp.Tests
{
    /// <summary>
    /// The conversions between Qt value types and the BCL types that mean the same thing - the
    /// C# spelling of PyQt's toPyDateTime()/toPyDate()/... and of QVariant.value().
    /// </summary>
    [Demo("test-bclinterop")]
    public static class BclInteropTests
    {
        public static int Run(QApplication app)
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
                if (!ok)
                    failures++;
            }

            // ---------------------------------------------------------------- geometry
            var point = new QPoint(3, 4);
            Point drawingPoint = (Point)point;
            Check(drawingPoint == new Point(3, 4), $"QPoint -> Point ({drawingPoint})");
            Check(((QPoint)drawingPoint).X() == 3 && ((QPoint)drawingPoint).Y() == 4, "Point -> QPoint");

            var pointF = new QPointF(1.5, -2.25);
            PointF drawingPointF = (PointF)pointF;
            Check(drawingPointF == new PointF(1.5f, -2.25f), $"QPointF -> PointF ({drawingPointF})");
            var pointFRoundTrip = (QPointF)drawingPointF;
            Check(pointFRoundTrip.X() == 1.5 && pointFRoundTrip.Y() == -2.25, "PointF -> QPointF");

            var size = new QSize(320, 200);
            Check(((Size)size) == new Size(320, 200), "QSize <-> Size");
            Check(new QSize(new Size(17, 19)).Width() == 17, "Size -> QSize");

            var rectangle = new QRect(10, 20, 30, 40);
            var drawingRectangle = rectangle.ToRectangle();
            Check(drawingRectangle == new Rectangle(10, 20, 30, 40),
                $"QRect -> Rectangle ({drawingRectangle})");
            var rectangleRoundTrip = (QRect)new Rectangle(1, 2, 3, 4);
            Check(rectangleRoundTrip.X() == 1 && rectangleRoundTrip.Y() == 2
                  && rectangleRoundTrip.Width() == 3 && rectangleRoundTrip.Height() == 4,
                "Rectangle -> QRect");

            var rectangleF = new QRectF(1.5, 2.5, 3.5, 4.5);
            var drawingRectangleF = rectangleF.ToRectangleF();
            Check(drawingRectangleF == new RectangleF(1.5f, 2.5f, 3.5f, 4.5f),
                $"QRectF -> RectangleF ({drawingRectangleF})");

            // ---------------------------------------------------------------- QtGui value types
            using (var color = new QColor(1, 2, 3, 4))
            {
                Color drawingColor = (Color)color;
                Check(drawingColor.A == 4 && drawingColor.R == 1 && drawingColor.G == 2 && drawingColor.B == 3,
                    $"QColor -> Color ({drawingColor})");
                using (var back = (QColor)Color.FromArgb(9, 8, 7, 6))
                    Check(back.Alpha() == 9 && back.Red() == 8 && back.Green() == 7 && back.Blue() == 6,
                        "Color -> QColor");
            }

            var vector2 = new QVector2D(1.0f, 2.0f);
            Check(vector2.ToVector2() == new Vector2(1.0f, 2.0f), "QVector2D <-> Vector2");
            var vector3 = new QVector3D(1.0f, 2.0f, 3.0f);
            Check(vector3.ToVector3() == new Vector3(1.0f, 2.0f, 3.0f), "QVector3D <-> Vector3");
            var vector4 = new QVector4D(1.0f, 2.0f, 3.0f, 4.0f);
            Check(vector4.ToVector4() == new Vector4(1.0f, 2.0f, 3.0f, 4.0f), "QVector4D <-> Vector4");

            var quaternion = new QQuaternion(0.5f, 0.1f, 0.2f, 0.3f);
            Quaternion numerics = (Quaternion)quaternion;
            Check(numerics.W == 0.5f && numerics.X == 0.1f && numerics.Y == 0.2f && numerics.Z == 0.3f,
                "QQuaternion -> Quaternion");
            var quaternionBack = (QQuaternion)new Quaternion(0.1f, 0.2f, 0.3f, 0.4f);
            Check(quaternionBack.Scalar() == 0.4f && quaternionBack.X() == 0.1f,
                "Quaternion -> QQuaternion");

            // ---------------------------------------------------------------- URLs and ids
            var uri = new Uri("https://example.com/path?q=1");
            using (var url = new QUrl(uri))
            {
                Check(url.ToUri() == uri, $"Uri -> QUrl -> Uri ({url.ToUri()})");
            }
            using (var relative = new QUrl(new Uri("relative/path", UriKind.Relative)))
            {
                Check(!relative.ToUri().IsAbsoluteUri && relative.ToUri().OriginalString == "relative/path",
                    $"relative Uri round-trip ({relative.ToUri()})");
            }

            var guid = Guid.NewGuid();
            using (var uuid = new QUuid(guid))
                Check(uuid.ToGuid() == guid, "Guid -> QUuid -> Guid");
            using (var nullUuid = new QUuid())
                Check(nullUuid.ToGuid() == Guid.Empty, "null QUuid -> Guid.Empty");

            // ---------------------------------------------------------------- QByteArray
            using (var byteArray = (QByteArray)new byte[] { 0, 127, 255 })
            {
                var copy = (byte[])byteArray;
                Check(copy.Length == 3 && copy[0] == 0 && copy[2] == 255,
                    "byte[] <-> QByteArray");
            }

            // ---------------------------------------------------------------- QVariant
            Check((int)QVariant.From(42).ToObject() == 42, "QVariant int round-trip");
            Check((bool)QVariant.From(true).ToObject(), "QVariant bool round-trip");
            Check((string)QVariant.From("hello").ToObject() == "hello", "QVariant string round-trip");
            Check(Math.Abs((double)QVariant.From(2.5).ToObject() - 2.5) < 1e-9, "QVariant double round-trip");

            var strings = (string[])QVariant.From(new[] { "a", "b" }).ToObject();
            Check(strings.Length == 2 && strings[1] == "b", "QVariant string[] round-trip");

            var bytes = (byte[])QVariant.From(new byte[] { 1, 2, 255 }).ToObject();
            Check(bytes.Length == 3 && bytes[2] == 255, "QVariant byte[] round-trip");

            Check((DateOnly)QVariant.From(new DateOnly(2026, 10, 25)).ToObject() == new DateOnly(2026, 10, 25),
                "QVariant DateOnly round-trip");
            Check((TimeOnly)QVariant.From(new TimeOnly(12, 34, 56, 789)).ToObject() == new TimeOnly(12, 34, 56, 789),
                "QVariant TimeOnly round-trip");
            var timestamp = new DateTime(2026, 10, 25, 12, 34, 56, 789, DateTimeKind.Utc);
            Check((DateTime)QVariant.From(timestamp).ToObject() == timestamp, "QVariant DateTime round-trip");

            Check((Uri)QVariant.From(uri).ToObject() == uri, "QVariant Uri round-trip");
            Check((Guid)QVariant.From(guid).ToObject() == guid, "QVariant Guid round-trip");
            Check((Point)QVariant.From(new Point(4, 5)).ToObject() == new Point(4, 5), "QVariant Point round-trip");

            bool rejected = false;
            try { QVariant.From(new object()); }
            catch (NotSupportedException) { rejected = true; }
            Check(rejected, "QVariant.From rejects a type without a Qt mapping");

            Check(QVariant.From(null).ToObject() == null, "an invalid QVariant unwraps to null");

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
