using System;

namespace QtSharp.Tests
{
    /// <summary>
    /// QtPositioning's value types and sources: coordinate math (distance/azimuth), shapes
    /// (rectangle/circle/path containment), a position info carrying a coordinate, and the source
    /// enumeration. No hardware is needed - position plugins simply may not be present, which the
    /// source check tolerates.
    /// </summary>
    [Demo("test-positioning")]
    public static class PositioningTests
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

            using var berlin = new QGeoCoordinate(52.52, 13.405);
            using var paris = new QGeoCoordinate(48.8566, 2.3522);
            Check(berlin.IsValid && Math.Abs(berlin.latitude() - 52.52) < 1e-9,
                $"QGeoCoordinate keeps its latitude ({berlin.latitude()})");

            double distance = berlin.DistanceTo(paris);
            Check(distance > 870_000 && distance < 885_000,
                $"DistanceTo measures Berlin-Paris ({distance / 1000:F0} km)");

            double azimuth = berlin.AzimuthTo(paris);
            Check(azimuth > 235 && azimuth < 255,
                $"AzimuthTo points south-south-west ({azimuth:F0} degrees)");

            using (var withAltitude = new QGeoCoordinate(52.52, 13.405, 34))
                Check(Math.Abs(withAltitude.altitude() - 34) < 1e-9, "the altitude round-trips");

            using (var rectangle = new QGeoRectangle(new QGeoCoordinate(53, 3), new QGeoCoordinate(48, 14)))
            using (var origin = new QGeoCoordinate(0, 0))
            {
                Check(rectangle.Contains(berlin), "QGeoRectangle contains Berlin");
                Check(!rectangle.Contains(origin), "QGeoRectangle rejects a point outside it");
            }

            using (var circle = new QGeoCircle(berlin, 50_000))
            using (var nearby = new QGeoCoordinate(52.6, 13.5))
            {
                Check(circle.Contains(nearby), "QGeoCircle contains a point within its radius");
                Check(!circle.Contains(paris), "QGeoCircle rejects Paris, 880 km away");
            }

            var pathCoordinates = new[]
            {
                new QGeoCoordinate(52.52, 13.405),
                new QGeoCoordinate(52.53, 13.42),
                new QGeoCoordinate(52.55, 13.44),
            };
            using (var path = new QGeoPath(pathCoordinates, 0))
                Check(path.Length() > 0, $"QGeoPath measures its length ({path.Length():F0} m)");
            foreach (var coordinate in pathCoordinates)
                coordinate.Dispose();

            using (var when = new QDateTime(DateTime.UtcNow))
            using (var info = new QGeoPositionInfo(berlin, when))
            using (var coordinate = info.Coordinate())
                Check(info.IsValid() && coordinate.IsValid,
                    "QGeoPositionInfo carries its coordinate");

            Check(QGeoPositionInfoSource.AvailableSources() != null,
                $"the position sources enumerate ({QGeoPositionInfoSource.AvailableSources().Length} available)");

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
