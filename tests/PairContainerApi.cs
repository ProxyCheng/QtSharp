using System;
using QtSharp.QtCore;
using QtSharp.QtGui;
using QtSharp.QtNetwork;
using QtSharp.QtWidgets;

// QPair/std::pair is a ValueTuple, and containers may nest in value positions. The pair cases
// cover a direct return, a list element, a parameter, and pairs whose halves are a scalar, a
// runtime struct, a boxed class and a QString. The enum list is the other half of the element
// vocabulary this change opened.
[Test("test-pair-container")]
public static class PairContainerApi
{
    public static int Run(TestContext context)
    {
        // QPair<int, int> as a direct return value.
        using (var format = new QSurfaceFormat())
        {
            format.SetMajorVersion(3);
            format.SetMinorVersion(2);
            var (major, minor) = format.Version();
            context.Check(major == 3 && minor == 2,
                $"QSurfaceFormat::version returns (3, 2) (got ({major}, {minor}))");
        }

        // QList<QPair<QString, QString>> in both directions.
        using (var query = new QUrlQuery())
        {
            query.SetQueryItems(new[] { ("a", "1"), ("b", "2") });
            var items = query.QueryItems();
            context.Check(items.Length == 2
                          && Array.IndexOf(items, ("a", "1")) >= 0
                          && Array.IndexOf(items, ("b", "2")) >= 0,
                $"QUrlQuery::queryItems round-trips the pair list (got {items.Length})");
        }

        // QPair<QHostAddress, int>: a pair with a boxed class half, in both directions.
        var subnet = QHostAddress.ParseSubnet("192.168.1.0/24");
        using (var address = new QHostAddress("192.168.1.42"))
        {
            context.Check(subnet.Item1.ToString() == "192.168.1.0" && subnet.Item2 == 24,
                $"QHostAddress::parseSubnet (got {subnet.Item1}, {subnet.Item2})");
            context.Check(address.IsInSubnet(subnet),
                "QHostAddress::isInSubnet takes the pair back");
        }
        subnet.Item1.Dispose();

        // QList<QPair<qreal, qreal>> and QList<QPair<qreal, QPointF>>: a pair list whose second
        // half is a scalar and one whose second half is a runtime struct.
        using (var animation = new QGraphicsItemAnimation())
        {
            animation.SetRotationAt(0.0, 0.0);
            animation.SetRotationAt(1.0, 90.0);
            animation.SetPosAt(0.5, new QPointF(10, 20));
            var rotations = animation.RotationList();
            var positions = animation.PosList();
            context.Check(rotations.Length == 2 && rotations[1] == (1.0, 90.0),
                "QGraphicsItemAnimation::rotationList keeps the pairs");
            context.Check(positions.Length == 1
                          && positions[0].Item1 == 0.5
                          && positions[0].Item2 == new QPointF(10, 20),
                "QGraphicsItemAnimation::posList keeps (step, QPointF)");
        }

        // QPair<qreal, QColor>: a pair of a scalar and a Qt value struct, through QGradient.
        using (var gradient = new QLinearGradient())
        {
            gradient.SetStops(new[] { (0.0, new QColor(255, 0, 0)), (1.0, new QColor(0, 0, 255)) });
            var stops = gradient.Stops();
            context.Check(stops.Length == 2 && stops[0].Item2.Red() == 255 && stops[1].Item2.Blue() == 255,
                "QGradient::stops carries (position, QColor)");
        }

        // A pair of boxed values as a parameter.
        using (var map = new QCborMap())
        {
            map.Insert((new QCborValue(1), new QCborValue("one")));
            context.Check(map.Size() == 1, $"QCborMap::insert takes the pair (got {map.Size()})");
        }

        // A list of enums: the element codec's enum branch, which the same check opened. Qt
        // returns the locale's *weekdays*, not all seven days, so only Monday is guaranteed.
        var days = new QLocale().Weekdays();
        context.Check(days.Length is > 0 and <= 7
                      && Array.IndexOf(days, Qt.DayOfWeek.Monday) >= 0,
            $"QLocale::weekdays returns the locale's DayOfWeek list (got {days.Length})");

        return context.ExitCode;
    }
}
