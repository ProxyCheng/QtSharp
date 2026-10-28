using System;

namespace QtSharp.Tests
{
    /// <summary>
    /// QtPositioning's value types and sources: coordinate math (distance/azimuth), shapes
    /// (rectangle/circle/path containment), a position info carrying a coordinate, and the source
    /// enumeration. No hardware is needed - position plugins simply may not be present, which the
    /// source check tolerates.
    /// </summary>
    [Test("test-positioning")]
    public static class PositioningTests
    {
        public static int Run(TestContext context)
        {

            using var berlin = new QGeoCoordinate(52.52, 13.405);
            using var paris = new QGeoCoordinate(48.8566, 2.3522);
            context.Check(berlin.IsValid && Math.Abs(berlin.latitude() - 52.52) < 1e-9,
                $"QGeoCoordinate keeps its latitude ({berlin.latitude()})");

            double distance = berlin.DistanceTo(paris);
            context.Check(distance > 870_000 && distance < 885_000,
                $"DistanceTo measures Berlin-Paris ({distance / 1000:F0} km)");

            double azimuth = berlin.AzimuthTo(paris);
            context.Check(azimuth > 235 && azimuth < 255,
                $"AzimuthTo points south-south-west ({azimuth:F0} degrees)");

            using (var withAltitude = new QGeoCoordinate(52.52, 13.405, 34))
                context.Check(Math.Abs(withAltitude.altitude() - 34) < 1e-9, "the altitude round-trips");

            using (var rectangle = new QGeoRectangle(new QGeoCoordinate(53, 3), new QGeoCoordinate(48, 14)))
            using (var origin = new QGeoCoordinate(0, 0))
            {
                context.Check(rectangle.Contains(berlin), "QGeoRectangle contains Berlin");
                context.Check(!rectangle.Contains(origin), "QGeoRectangle rejects a point outside it");
            }

            using (var circle = new QGeoCircle(berlin, 50_000))
            using (var nearby = new QGeoCoordinate(52.6, 13.5))
            {
                context.Check(circle.Contains(nearby), "QGeoCircle contains a point within its radius");
                context.Check(!circle.Contains(paris), "QGeoCircle rejects Paris, 880 km away");
            }

            var pathCoordinates = new[]
            {
                new QGeoCoordinate(52.52, 13.405),
                new QGeoCoordinate(52.53, 13.42),
                new QGeoCoordinate(52.55, 13.44),
            };
            using (var path = new QGeoPath(pathCoordinates, 0))
                context.Check(path.Length() > 0, $"QGeoPath measures its length ({path.Length():F0} m)");
            foreach (var coordinate in pathCoordinates)
                coordinate.Dispose();

            using (var when = new QDateTime(DateTime.UtcNow))
            using (var info = new QGeoPositionInfo(berlin, when))
            using (var coordinate = info.Coordinate())
                context.Check(info.IsValid() && coordinate.IsValid,
                    "QGeoPositionInfo carries its coordinate");

            context.Check(QGeoPositionInfoSource.AvailableSources() != null,
                $"the position sources enumerate ({QGeoPositionInfoSource.AvailableSources().Length} available)");

            return context.ExitCode;
        }
    }
}
