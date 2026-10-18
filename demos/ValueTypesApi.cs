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

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
            return failures == 0 ? 0 : 1;
        }
    }
