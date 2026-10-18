using System;
using QtSharp.Api;

    /// <summary>
    /// The Qt value types are values, and this is the test that keeps them that way.
    ///
    /// A QPointF is two doubles: C++ passes it by value and every member is arithmetic, so a copy
    /// owns nothing. PyQt and PySide model it the same way (SIP inlines it in the Python object,
    /// shiboken's typesystem marks it a value type). In this binding it is a C# struct with the same
    /// layout, which means:
    ///
    ///   * a hundred thousand points cost a hundred thousand structs, and nothing else - no native
    ///     allocation, no entry in the wrapper registry, no entry in the native watch table, no
    ///     finalizer;
    ///   * nothing has to be disposed.
    ///
    /// The alternative - a class holding a handle, which is how these were modelled - costs all four
    /// per point. A chart that samples and replaces a few thousand points a second therefore piled up
    /// hundreds of thousands of unreclaimable objects (the finalizer queue is the bottleneck) and the
    /// page grew to gigabytes. That is the leak this test is here to catch the return of: the two
    /// counters below must not move when points are handed to Qt.
    ///
    /// The admission test for a new type is "a copy of one owns nothing". The geometry family passes:
    /// QPointF, QPoint, QSize, QSizeF, QMargins, QMarginsF, QLine, QLineF, QRect, QRectF. QColor,
    /// QPen, QBrush, QFont, QDateTime and the rest are implicitly shared - a copy carries a pointer
    /// with a reference count - so a bitwise copy would double-free, and they keep their handles.
    /// </summary>
    public static class ValueTypesApi
    {
        static readonly Type[] Structs =
        {
            typeof(QPointF), typeof(QPoint), typeof(QSize), typeof(QSizeF),
            typeof(QMargins), typeof(QMarginsF), typeof(QLine), typeof(QLineF),
            typeof(QRect), typeof(QRectF),
        };

        /// <summary>A polygon of <paramref name="count"/> points that walks the diagonal.</summary>
        static QPoint[] Diagonal(int count)
        {
            var points = new QPoint[count];
            for (int i = 0; i < count; i++)
                points[i] = new QPoint(i % 1000, i / 1000);
            return points;
        }

        public static int Run(QApplication app)
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
                if (!ok) failures++;
            }
            void Info(string what) => Console.WriteLine($"       {what}");

            // --- 1. they are values, not handles
            foreach (var t in Structs)
            {
                Check(t.IsValueType && !t.IsClass, $"{t.Name} is a value type");
                Check(!typeof(IDisposable).IsAssignableFrom(t), $"{t.Name} is not disposable (a value has nothing to free)");
            }
            Check(!typeof(QPolygon).IsValueType, "QPolygon, which owns a QList, is still a class");

            // --- 2. assignment copies, and equality compares the value (C++'s semantics)
            var a = new QPointF(1.0, 2.0);
            var b = a;
            b.SetX(9.0);
            Check(a.X() == 1.0, $"assigning a QPointF copies it (a.X() is {a.X()})");
            Check(b.X() == 9.0, $"and the copy is independent (b.X() is {b.X()})");
            Check(new QPointF(3.0, 4.0) == new QPointF(3.0, 4.0), "two equal points compare equal");
            Check(new QPointF(3.0, 4.0) != new QPointF(3.0, 5.0), "and two different ones do not");
            Check(new QPointF(3.0, 4.0).Equals(new QPointF(3.0, 4.0)), "Equals agrees with ==");
            Check(new QPointF(1.5, -2.5).ToString() == "QPointF(1.5, -2.5)",
                $"struct ToString prints the field values (got '{new QPointF(1.5, -2.5)}')");

            // --- 3. a bulk call creates no wrappers for the values
            const int Points = 100000;
            var points = Diagonal(Points);

            long registeredBefore = ApiRuntime.WrappersRegistered;
            int watchedBefore = ApiRuntime.WatchedObjects;
            var polygon = new QPolygon(points);
            long registeredAfter = ApiRuntime.WrappersRegistered;
            int watchedAfter = ApiRuntime.WatchedObjects;

            Info($"{Points} points -> one QPolygon: wrappers +{registeredAfter - registeredBefore}, watched +{watchedAfter - watchedBefore}");
            Check(registeredAfter - registeredBefore == 1,
                $"only the polygon itself registered (+{registeredAfter - registeredBefore}, expected +1)");
            Check(watchedAfter == watchedBefore,
                $"and nothing was watched (+{watchedAfter - watchedBefore}: a value is not an object)");

            // The values really did reach Qt: the bounding rect is what the diagonal spans. (It is a
            // QRect struct, so there is nothing to dispose - that is the point of the test.)
            var bounds = polygon.BoundingRect();
            Check(bounds.X() == 0 && bounds.Y() == 0,
                $"the polygon's bounding rect starts at the origin ({bounds.X()},{bounds.Y()})");
            Check(bounds.Width() == 1000 && bounds.Height() == Points / 1000,
                $"and covers the diagonal ({bounds.Width()}x{bounds.Height()})");
            Check(polygon.Point(0).X() == 0 && polygon.Point(1).X() == 1,
                $"a point reads back as a value ({polygon.Point(1).X()})");
            polygon.Dispose();

            // --- 4. repeating the same work does not accumulate
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long heapBefore = GC.GetTotalMemory(true);
            long registeredStart = ApiRuntime.WrappersRegistered;
            const int Rounds = 5;
            for (int round = 0; round < Rounds; round++)
            {
                using var each = new QPolygon(points);
                if (each.BoundingRect().Width() != 1000)
                {
                    Check(false, $"round {round} took the points");
                    break;
                }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long heapAfter = GC.GetTotalMemory(true);
            long registeredEnd = ApiRuntime.WrappersRegistered;

            Info($"5 rounds of {Points} points: wrappers +{registeredEnd - registeredStart}, managed heap {heapBefore / 1024} -> {heapAfter / 1024} KB");
            Check(registeredEnd - registeredStart == Rounds,
                $"each round registered its polygon and nothing else (+{registeredEnd - registeredStart}, expected +{Rounds})");
            Check(heapAfter - heapBefore < 8 * 1024 * 1024,
                $"and the managed heap stayed put ({(heapAfter - heapBefore) / 1024} KB over {Rounds} rounds)");

            // --- 5. the counters are not vacuous: an object really does register
            long beforeObject = ApiRuntime.WrappersRegistered;
            using (var timer = new QTimer())
            {
                Check(ApiRuntime.WrappersRegistered == beforeObject + 1,
                    $"a QObject registers exactly one wrapper (+{ApiRuntime.WrappersRegistered - beforeObject})");
                Check(ApiRuntime.WatchedObjects >= 1, $"and is watched ({ApiRuntime.WatchedObjects} watched)");
            }

            // --- 6. explicit BCL interop (PyQt's explicit style: ctor in, ToXxx out, no implicit operators)
            bool Throws<T>(Action action) where T : Exception
            {
                try
                {
                    action();
                    return false;
                }
                catch (T)
                {
                    return true;
                }
                catch (Exception e)
                {
                    Info($"expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}");
                    return false;
                }
            }
            bool SameBytes(byte[] x, byte[] y)
            {
                if (x == null || y == null || x.Length != y.Length)
                    return false;
                for (int i = 0; i < x.Length; i++)
                    if (x[i] != y[i])
                        return false;
                return true;
            }

            // 6a. the surface: constructors and named conversions exist, and nothing is implicit
            bool anyImplicit = false;
            int explicitCount = 0;
            foreach (var m in typeof(QDateTime).GetMethods())
            {
                if (m.Name == "op_Implicit") anyImplicit = true;
                if (m.Name == "op_Explicit") explicitCount++;
            }
            Check(!anyImplicit, "QDateTime has no implicit conversions (a mixed == would compare wrapper identity)");
            Check(explicitCount == 2, $"the two explicit operators exist ({explicitCount})");
            Check(typeof(QDateTime).GetConstructor(new[] { typeof(DateTime) }) != null, "new QDateTime(DateTime) exists");
            Check(typeof(QDateTime).GetConstructor(new[] { typeof(DateTimeOffset) }) != null, "new QDateTime(DateTimeOffset) exists");
            Check(typeof(QDateTime).GetMethod("ToDateTime", Type.EmptyTypes) != null, "QDateTime.ToDateTime() exists");
            Check(typeof(QDateTime).GetMethod("ToDateTimeOffset", Type.EmptyTypes) != null, "QDateTime.ToDateTimeOffset() exists");
            Check(typeof(QDate).GetConstructor(new[] { typeof(DateOnly) }) != null, "new QDate(DateOnly) exists");
            Check(typeof(QDate).GetMethod("ToDateOnly", Type.EmptyTypes) != null, "QDate.ToDateOnly() exists");
            Check(typeof(QTime).GetConstructor(new[] { typeof(TimeOnly) }) != null, "new QTime(TimeOnly) exists");
            Check(typeof(QTime).GetMethod("ToTimeOnly", Type.EmptyTypes) != null, "QTime.ToTimeOnly() exists");
            Check(typeof(QByteArray).GetMethod("ToArray", Type.EmptyTypes) != null, "QByteArray.ToArray() exists");

            // 6b. UTC round-trip
            var utcValue = new DateTime(2024, 5, 17, 12, 34, 56, 789, DateTimeKind.Utc);
            using (var q = new QDateTime(utcValue))
            {
                Check(q.IsValid() && q.TimeSpec() == Qt.TimeSpec.UTC, "DateTimeKind.Utc -> Qt::UTC");
                var back = q.ToDateTime();
                Check(back == utcValue && back.Kind == DateTimeKind.Utc, $"UTC round-trip (got {back:o})");
                var dto = q.ToDateTimeOffset();
                Check(dto == utcValue && dto.Offset == TimeSpan.Zero,
                    "UTC ToDateTimeOffset has offset zero and the same instant");
                Check((DateTime)q == utcValue, "the explicit DateTime cast agrees with ToDateTime()");
            }

            // 6c. local round-trip (noon is never inside a DST gap, so this is stable in any time zone)
            var localValue = new DateTime(2024, 5, 17, 12, 34, 56, 789, DateTimeKind.Local);
            using (var q = new QDateTime(localValue))
            {
                Check(q.TimeSpec() == Qt.TimeSpec.LocalTime, "DateTimeKind.Local -> Qt::LocalTime");
                var back = q.ToDateTime();
                Check(back == localValue && back.Kind == DateTimeKind.Local,
                    "local round-trip keeps the wall clock and Kind.Local");
                Check(q.ToDateTimeOffset().Offset == TimeZoneInfo.Local.GetUtcOffset(localValue),
                    "local ToDateTimeOffset uses the machine's offset");
            }

            // 6d. Unspecified is interpreted as local time
            var naive = new DateTime(2024, 5, 17, 12, 34, 56, 789, DateTimeKind.Unspecified);
            using (var q = new QDateTime(naive))
            {
                Check(q.TimeSpec() == Qt.TimeSpec.LocalTime, "DateTimeKind.Unspecified -> Qt::LocalTime");
                var back = q.ToDateTime();
                Check(back == naive, "the wall clock survives");
                Check(back.Kind == DateTimeKind.Local, "but it comes back explicitly Local, not Unspecified");
            }

            // 6e. DateTimeOffset keeps its offset through Qt::OffsetFromUTC
            var offsetValue = new DateTimeOffset(2024, 5, 17, 12, 34, 56, 789, TimeSpan.FromHours(5.5));
            using (var q = new QDateTime(offsetValue))
            {
                Check(q.TimeSpec() == Qt.TimeSpec.OffsetFromUTC, "DateTimeOffset -> Qt::OffsetFromUTC");
                Check(q.OffsetFromUtc() == 19800, $"the offset reaches Qt ({q.OffsetFromUtc()} s)");
                var dto = q.ToDateTimeOffset();
                Check(dto == offsetValue && dto.Offset == offsetValue.Offset, "instant and offset both round-trip");
                var back = q.ToDateTime();
                Check(back == offsetValue.DateTime && back.Kind == DateTimeKind.Unspecified,
                    "ToDateTime keeps the wall clock, naive (System.DateTime has no offset)");
            }
            var negativeOffset = new DateTimeOffset(2024, 1, 2, 3, 4, 5, 6, TimeSpan.FromHours(-8));
            using (var q = new QDateTime(negativeOffset))
                Check(q.ToDateTimeOffset() == negativeOffset && q.ToDateTimeOffset().Offset == negativeOffset.Offset,
                    "a negative offset round-trips too");

            // 6f. Qt stores milliseconds: sub-millisecond ticks are truncated
            var ticky = new DateTime(2024, 5, 17, 12, 34, 56, 0, DateTimeKind.Utc).AddTicks(4321);
            using (var q = new QDateTime(ticky))
                Check(q.ToDateTime() == new DateTime(2024, 5, 17, 12, 34, 56, 0, DateTimeKind.Utc),
                    "sub-millisecond ticks are truncated on the way in");

            // 6g. TimeZone spec goes through the same wall-clock/offset path
            using (var zone = QTimeZone.Utc())
            using (var q = new QDateTime(new QDate(2024, 5, 17), new QTime(12, 0, 0), zone))
            {
                Check(q.TimeSpec() == Qt.TimeSpec.TimeZone, "a QDateTime built from a QTimeZone reports TimeZone");
                var dto = q.ToDateTimeOffset();
                Check(dto.Offset == TimeSpan.Zero
                      && dto.UtcDateTime == new DateTime(2024, 5, 17, 12, 0, 0, DateTimeKind.Utc),
                    "a zone datetime converts as an instant with its offset");
                Check(q.ToDateTime().Kind == DateTimeKind.Unspecified,
                    "ToDateTime keeps the zone wall clock as Unspecified");
            }

            // 6h. invalid inputs throw rather than returning a default timestamp
            using (var invalid = new QDateTime())
            {
                Check(!invalid.IsValid(), "Qt's default QDateTime is invalid");
                Check(Throws<InvalidOperationException>(() => invalid.ToDateTime()),
                    "ToDateTime on invalid throws InvalidOperationException");
                Check(Throws<InvalidOperationException>(() => invalid.ToDateTimeOffset()),
                    "ToDateTimeOffset on invalid throws InvalidOperationException");
            }

            // 6i. QDate <-> DateOnly
            var leapDay = new DateOnly(2024, 2, 29);
            var qd = new QDate(leapDay);
            Check(qd.IsValid() && qd.Year() == 2024 && qd.Month() == 2 && qd.Day() == 29, "DateOnly -> QDate");
            Check(qd.ToDateOnly() == leapDay, "QDate -> DateOnly round-trip");
            Check(new QDate(DateOnly.MinValue).ToDateOnly() == DateOnly.MinValue, "DateOnly.MinValue round-trips");
            Check(new QDate(DateOnly.MaxValue).ToDateOnly() == DateOnly.MaxValue, "DateOnly.MaxValue round-trips");

            var nullDate = QDate.FromString("not a date", Qt.DateFormat.ISODate);
            Check(!nullDate.IsValid() && nullDate.IsNull(), "an invalid QDate parses as null");
            Check(Throws<InvalidOperationException>(() => nullDate.ToDateOnly()), "a null QDate throws in ToDateOnly()");

            var bce = QDate.FromJulianDay(0); // jd 0 is inside Qt's range: a valid 4713 BCE date
            Check(bce.IsValid(), "Julian day 0 is a valid Qt date (4713 BCE)");
            Check(Throws<InvalidOperationException>(() => bce.ToDateOnly()),
                "a valid Qt date outside years 1..9999 throws (not silently wrapped)");
            Info($"default(QDate) is jd 0 (valid Qt date, year {default(QDate).Year()}), not Qt's null QDate");

            // 6j. QTime <-> TimeOnly
            var lastMs = new TimeOnly(23, 59, 59, 999);
            var qt = new QTime(lastMs);
            Check(qt.IsValid() && qt.Hour() == 23 && qt.Minute() == 59 && qt.Second() == 59 && qt.Msec() == 999,
                "TimeOnly -> QTime keeps hour/minute/second/ms");
            Check(qt.ToTimeOnly() == lastMs, "QTime -> TimeOnly round-trip");
            Check(new QTime(new TimeOnly(0, 0, 0, 0)).ToTimeOnly() == new TimeOnly(0, 0, 0, 0), "midnight round-trips");
            var subMs = new TimeOnly(1, 2, 3, 4).Add(TimeSpan.FromTicks(9999));
            Check(new QTime(subMs).ToTimeOnly() == new TimeOnly(1, 2, 3, 4),
                "TimeOnly sub-millisecond ticks are truncated");
            Check(!QTime.IsValid(24, 0, 0), "Qt has no valid 24:00 (TimeOnly cannot express it either)");
            // Deliberately no assertion on new QTime(24,0,0): a null QTime crosses the POD as
            // msecsSinceStartOfDay() == 0, so it currently reads back as valid midnight. ToTimeOnly's
            // invalid branch is defensive; it is unreachable through the public API today.

            // 6k. QByteArray <-> byte[] (binary-safe; Data() is UTF-8 and truncates at NUL)
            var payload = new byte[] { 0x00, 0xFF, 0x80, 0x41, 0x00, 0x7F, 0xC3, 0x28 };
            using (var ba = new QByteArray(payload))
            {
                Check(ba.Size() == payload.Length && ba.Length() == payload.Length,
                    "the byte[] ctor stores the whole buffer");
                Check(SameBytes(ba.ToArray(), payload), "ToArray round-trips embedded NUL and high bytes");
                Check(SameBytes(ba.ToArray(), payload) && ba.Size() == payload.Length,
                    "ToArray copies and does not free the wrapper's handle");
            }
            using (var emptyBytes = new QByteArray(Array.Empty<byte>()))
                Check(emptyBytes.Size() == 0 && emptyBytes.ToArray().Length == 0,
                    "empty bytes -> empty array (null and empty QByteArrays are indistinguishable here)");
            var big = new byte[4096];
            for (int i = 0; i < big.Length; i++)
                big[i] = (byte)(i * 31 + 7);
            using (var bigBa = new QByteArray(big))
                Check(SameBytes(bigBa.ToArray(), big), "a 4 KB binary payload survives the copy");

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
            return failures == 0 ? 0 : 1;
        }
    }
