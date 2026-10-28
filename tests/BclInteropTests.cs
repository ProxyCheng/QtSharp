using System;
using System.Drawing;
using System.Globalization;
using System.Numerics;

namespace QtSharp.Tests
{
    /// <summary>
    /// The conversions between Qt value types and the BCL types that mean the same thing - the
    /// C# spelling of PyQt's toPyDateTime()/toPyDate()/... and of QVariant.value().
    /// </summary>
    [Test("test-bclinterop")]
    public static class BclInteropTests
    {
        public static int Run(TestContext context)
        {

            // ---------------------------------------------------------------- geometry
            var point = new QPoint(3, 4);
            Point drawingPoint = (Point)point;
            context.Check(drawingPoint == new Point(3, 4), $"QPoint -> Point ({drawingPoint})");
            context.Check(((QPoint)drawingPoint).X() == 3 && ((QPoint)drawingPoint).Y() == 4, "Point -> QPoint");

            var pointF = new QPointF(1.5, -2.25);
            PointF drawingPointF = (PointF)pointF;
            context.Check(drawingPointF == new PointF(1.5f, -2.25f), $"QPointF -> PointF ({drawingPointF})");
            var pointFRoundTrip = (QPointF)drawingPointF;
            context.Check(pointFRoundTrip.X() == 1.5 && pointFRoundTrip.Y() == -2.25, "PointF -> QPointF");

            var size = new QSize(320, 200);
            context.Check(((Size)size) == new Size(320, 200), "QSize <-> Size");
            context.Check(new QSize(new Size(17, 19)).Width() == 17, "Size -> QSize");

            var rectangle = new QRect(10, 20, 30, 40);
            var drawingRectangle = rectangle.ToRectangle();
            context.Check(drawingRectangle == new Rectangle(10, 20, 30, 40),
                $"QRect -> Rectangle ({drawingRectangle})");
            var rectangleRoundTrip = (QRect)new Rectangle(1, 2, 3, 4);
            context.Check(rectangleRoundTrip.X() == 1 && rectangleRoundTrip.Y() == 2
                  && rectangleRoundTrip.Width() == 3 && rectangleRoundTrip.Height() == 4,
                "Rectangle -> QRect");

            var rectangleF = new QRectF(1.5, 2.5, 3.5, 4.5);
            var drawingRectangleF = rectangleF.ToRectangleF();
            context.Check(drawingRectangleF == new RectangleF(1.5f, 2.5f, 3.5f, 4.5f),
                $"QRectF -> RectangleF ({drawingRectangleF})");

            // ---------------------------------------------------------------- QtGui value types
            var color = new QColor(1, 2, 3, 4);
            {
                Color drawingColor = (Color)color;
                context.Check(drawingColor.A == 4 && drawingColor.R == 1 && drawingColor.G == 2 && drawingColor.B == 3,
                    $"QColor -> Color ({drawingColor})");
                var back = (QColor)Color.FromArgb(9, 8, 7, 6);
                    context.Check(back.Alpha() == 9 && back.Red() == 8 && back.Green() == 7 && back.Blue() == 6,
                        "Color -> QColor");
            }

            var vector2 = new QVector2D(1.0f, 2.0f);
            context.Check(vector2.ToVector2() == new Vector2(1.0f, 2.0f), "QVector2D <-> Vector2");
            var vector3 = new QVector3D(1.0f, 2.0f, 3.0f);
            context.Check(vector3.ToVector3() == new Vector3(1.0f, 2.0f, 3.0f), "QVector3D <-> Vector3");
            var vector4 = new QVector4D(1.0f, 2.0f, 3.0f, 4.0f);
            context.Check(vector4.ToVector4() == new Vector4(1.0f, 2.0f, 3.0f, 4.0f), "QVector4D <-> Vector4");

            var quaternion = new QQuaternion(0.5f, 0.1f, 0.2f, 0.3f);
            Quaternion numerics = (Quaternion)quaternion;
            context.Check(numerics.W == 0.5f && numerics.X == 0.1f && numerics.Y == 0.2f && numerics.Z == 0.3f,
                "QQuaternion -> Quaternion");
            var quaternionBack = (QQuaternion)new Quaternion(0.1f, 0.2f, 0.3f, 0.4f);
            context.Check(quaternionBack.Scalar() == 0.4f && quaternionBack.X() == 0.1f,
                "Quaternion -> QQuaternion");

            // ---------------------------------------------------------------- URLs and ids
            var uri = new Uri("https://example.com/path?q=1");
            using (var url = new QUrl(uri))
            {
                context.Check(url.ToUri() == uri, $"Uri -> QUrl -> Uri ({url.ToUri()})");
            }
            using (var relative = new QUrl(new Uri("relative/path", UriKind.Relative)))
            {
                context.Check(!relative.ToUri().IsAbsoluteUri && relative.ToUri().OriginalString == "relative/path",
                    $"relative Uri round-trip ({relative.ToUri()})");
            }

            // ---------------------------------------------------------------- UUIDs
            // QUuid is erased to System.Guid - there is no QUuid type, the same way there is no
            // QString. A Qt signature that takes or returns one surfaces as a Guid and crosses the
            // ABI as the same 16 bytes; the Qt-only operations are extensions on Guid.
            var guid = Guid.NewGuid();
            context.Check(guid.Version() == UuidVersion.Random,
                $"Guid.NewGuid is a version-4 UUID (got {guid.Version()})");
            context.Check(guid.Variant() == UuidVariant.RFC4122,
                $"and carries the RFC 4122 variant (got {guid.Variant()})");
            context.Check(Guid.FromRfc4122(guid.ToRfc4122()) == guid, "Guid -> RFC 4122 bytes -> Guid");

            // The two byte orders differ, so a fixed value makes the difference visible instead of
            // trusting a round-trip that would hide a swap.
            var ordered = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            var rfc = ordered.ToRfc4122();
            context.Check(rfc.Length == 16 && rfc[0] == 0x00 && rfc[3] == 0x33 && rfc[4] == 0x44 && rfc[15] == 0xff,
                $"RFC 4122 bytes are network order ({BitConverter.ToString(rfc)})");
            var microsoft = ordered.ToByteArray();
            context.Check(microsoft[0] == 0x33 && microsoft[3] == 0x00 && microsoft[4] == 0x55,
                $"Guid.ToByteArray keeps the Microsoft order ({BitConverter.ToString(microsoft)})");

            // Qt's four string formats are Guid's B/D/N, and the null UUID is Guid.Empty.
            context.Check(default(Guid) == Guid.Empty && Guid.Empty.Version() == UuidVersion.VerUnknown,
                $"Guid.Empty is the null UUID with no version (got {Guid.Empty.Version()})");
            context.Check(ordered.ToString("B") == "{00112233-4455-6677-8899-aabbccddeeff}",
                $"braced form ({ordered.ToString("B")})");
            context.Check(ordered.ToString("D") == "00112233-4455-6677-8899-aabbccddeeff",
                $"dashed form ({ordered.ToString("D")})");
            context.Check(ordered.ToString("N") == "00112233445566778899aabbccddeeff",
                $"Qt's Id128 form is Guid's N ({ordered.ToString("N")})");

            // Name-based UUIDs are RFC 4122; the DNS namespace and the name are the RFC's example
            // pair, so a wrong namespace byte order would fail here.
            var dnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
            context.Check(Guid.CreateV3(dnsNamespace, "www.widgets.com")
                  == new Guid("3d813cbb-47fb-32ba-91df-831e1593ac29"),
                $"v3 UUID matches RFC 4122 (got {Guid.CreateV3(dnsNamespace, "www.widgets.com")})");
            context.Check(Guid.CreateV5(dnsNamespace, "www.widgets.com")
                  == new Guid("21f7f8de-8051-5b89-8680-0195ef798b6a"),
                $"v5 UUID matches RFC 4122 (got {Guid.CreateV5(dnsNamespace, "www.widgets.com")})");

            // The same value reaches Qt as a real Uuid, and Qt's own signatures are Guid-typed.
            using (var variant = new QVariant(guid))
            {
                context.Check(variant.type() == (int)QVariant.Type.Uuid,
                    $"QVariant(Guid) is a Uuid variant (got {variant.type()})");
                context.Check((Guid)variant.ToObject() == guid, "and unwraps to the same Guid");
                context.Check(variant.ToUuid() == guid, "ToUuid returns the Guid");
            }
            using (var cbor = new QCborValue(guid))
                context.Check(cbor.ToUuid() == guid, "QCborValue <-> Guid");
            context.Check(Qt.QHash(guid) == Qt.QHash(guid), "qHash(Guid) is deterministic");

            // ---------------------------------------------------------------- QChar is char
            // QChar is one UTF-16 code unit, so it is erased the way QUuid and QString are: the
            // value is a char and there is no QChar type. Classification comes from the BCL's
            // Unicode tables; the properties the BCL has no API for call Qt's tables.
            context.Check('A'.IsLetter() && !'7'.IsLetter(), "'A' is a letter, '7' is not");
            context.Check('7'.IsDigit() && '7'.IsNumber() && !'7'.IsLetter(), "'7' is a digit and a number");
            context.Check('A'.IsUpper() && !'A'.IsLower() && 'a'.IsLower(), "upper/lower");
            context.Check('a'.ToUpper() == 'A' && 'A'.ToLower() == 'a', "ToUpper/ToLower");
            context.Check('A'.ToCaseFolded() == 'a' && 'ß'.ToCaseFolded() == 'ß', "ToCaseFolded");
            context.Check('7'.DigitValue() == 7 && 'A'.DigitValue() == -1 && '!'.DigitValue() == -1,
                "DigitValue: decimal digits only, -1 otherwise (Qt's actual behaviour)");
            context.Check('\0'.IsNull() && ' '.IsSpace() && !'A'.IsSpace(), "IsNull/IsSpace");
            context.Check('A'.Category() == UnicodeCategory.UppercaseLetter, "Category");
            context.Check('A'.Script() == CharScript.Script_Latin, "Script");
            context.Check('́'.CombiningClass() == 230, "CombiningClass of a combining acute");
            context.Check('א'.Direction() == CharDirection.DirR, "bidi direction of Hebrew alef");
            context.Check('('.HasMirrored() && '('.MirroredChar() == ')', "mirroring");
            context.Check('﷐'.IsNonCharacter() && !'A'.IsNonCharacter(), "IsNonCharacter");
            context.Check('\uD83D'.IsHighSurrogate() && '\uDE00'.IsLowSurrogate()
                  && !'A'.IsSurrogate(), "surrogate classification");
            context.Check(char.SurrogateToUcs4('\uD83D', '\uDE00') == 0x1F600, "surrogate pair -> code point");
            context.Check(char.HighSurrogate(0x1F600) == '\uD83D' && char.LowSurrogate(0x1F600) == '\uDE00'
                  && char.RequiresSurrogates(0x1F600), "code point -> surrogate pair");
            context.Check(char.Category(0x41) == UnicodeCategory.UppercaseLetter
                  && char.Script(0x41) == CharScript.Script_Latin
                  && char.DigitValue(0x41) == -1
                  && char.ToUpper(0x61) == 0x41,
                "the static (code point) overloads");
            context.Check(char.CurrentUnicodeVersion() >= UnicodeVersion.Unicode_1_1,
                $"current Unicode version ({char.CurrentUnicodeVersion()})");
            context.Check(QDir.Separator() == System.IO.Path.DirectorySeparatorChar,
                $"QDir.Separator is the platform separator ({QDir.Separator()})");

            using (var variant = new QVariant('A'))
            {
                context.Check(variant.type() == (int)QVariant.Type.Char,
                    $"QVariant(char) is a Char variant (got {variant.type()})");
                context.Check((char)variant.ToObject() == 'A', "and unwraps to the same char");
            }

            // ---------------------------------------------------------------- QByteArray
            using (var byteArray = (QByteArray)new byte[] { 0, 127, 255 })
            {
                var copy = (byte[])byteArray;
                context.Check(copy.Length == 3 && copy[0] == 0 && copy[2] == 255,
                    "byte[] <-> QByteArray");
            }

            // ---------------------------------------------------------------- QVariant
            context.Check((int)QVariant.From(42).ToObject() == 42, "QVariant int round-trip");
            context.Check((bool)QVariant.From(true).ToObject(), "QVariant bool round-trip");
            context.Check((string)QVariant.From("hello").ToObject() == "hello", "QVariant string round-trip");
            context.Check(Math.Abs((double)QVariant.From(2.5).ToObject() - 2.5) < 1e-9, "QVariant double round-trip");

            var strings = (string[])QVariant.From(new[] { "a", "b" }).ToObject();
            context.Check(strings.Length == 2 && strings[1] == "b", "QVariant string[] round-trip");

            var bytes = (byte[])QVariant.From(new byte[] { 1, 2, 255 }).ToObject();
            context.Check(bytes.Length == 3 && bytes[2] == 255, "QVariant byte[] round-trip");

            context.Check((DateOnly)QVariant.From(new DateOnly(2026, 10, 25)).ToObject() == new DateOnly(2026, 10, 25),
                "QVariant DateOnly round-trip");
            context.Check((TimeOnly)QVariant.From(new TimeOnly(12, 34, 56, 789)).ToObject() == new TimeOnly(12, 34, 56, 789),
                "QVariant TimeOnly round-trip");
            var timestamp = new DateTime(2026, 10, 25, 12, 34, 56, 789, DateTimeKind.Utc);
            context.Check((DateTime)QVariant.From(timestamp).ToObject() == timestamp, "QVariant DateTime round-trip");

            context.Check((Uri)QVariant.From(uri).ToObject() == uri, "QVariant Uri round-trip");
            context.Check((Guid)QVariant.From(guid).ToObject() == guid, "QVariant Guid round-trip");
            context.Check((Point)QVariant.From(new Point(4, 5)).ToObject() == new Point(4, 5), "QVariant Point round-trip");

            // A managed type with no Qt metatype is not rejected any more: it crosses as an opaque
            // managed value, the PySide PyObjectWrapper contract (see VariantObjectTests).
            var payload = new object();
            using (var wrapped = QVariant.From(payload))
                context.Check(ReferenceEquals(wrapped.ToObject(), payload),
                    "QVariant carries a type without a Qt mapping as an opaque value");

            context.Check(QVariant.From(null).ToObject() == null, "an invalid QVariant unwraps to null");

            return context.ExitCode;
        }
    }
}
