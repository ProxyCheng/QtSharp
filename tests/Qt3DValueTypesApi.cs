using System;
using QtSharp.Qt3DCore;
using QtSharp.Qt3DAnimation;

/// <summary>
/// The Qt3D value types the first probe missed: QNodeId is one quint64 and QKeyFrame is three
/// QVector2Ds and an enum. Both are trivially copyable and namespaced in C++ - they were the two
/// cases that needed a C++ spelling of their own on top of the struct table.
///
/// QKeyFrame's equality is not field-wise: Qt ignores the control points unless the interpolation
/// is Bezier, so the struct forwards Qt's operator== and hashes only what equality looks at.
/// </summary>
[Test("test-qt3d-valuetypes")]
public static class Qt3DValueTypesApi
{
    public static int Run(TestContext context)
    {

        // --- they are values, not handles
        context.Check(typeof(QNodeId).IsValueType && !typeof(IDisposable).IsAssignableFrom(typeof(QNodeId)),
            "QNodeId is a value type with nothing to dispose");
        context.Check(typeof(QKeyFrame).IsValueType && !typeof(IDisposable).IsAssignableFrom(typeof(QKeyFrame)),
            "QKeyFrame is a value type with nothing to dispose");

        // --- QNodeId: one quint64, Qt's equality and ordering
        var nullId = new QNodeId();
        context.Check(nullId.IsNull() && nullId.Id() == 0, "default(QNodeId) is Qt's null id");
        var id = QNodeId.CreateId();
        context.Check(!id.IsNull() && id.Id() != 0, $"CreateId() gives a real id ({id.Id()})");
        context.Check(id != nullId && id != QNodeId.CreateId(),
            "ids compare by value (two CreateId() calls are different)");
        var idCopy = id;
        context.Check(idCopy == id, "a copy of an id compares equal");

        // --- QKeyFrame: default construction is Bezier, the one-argument constructor is Linear
        var defaultFrame = new QKeyFrame();
        context.Check(defaultFrame.interpolationType() == QKeyFrame.InterpolationType.BezierInterpolation,
            "new QKeyFrame() is Qt's default (BezierInterpolation), not the zero value");
        var linear = new QKeyFrame(new QVector2D(1, 2));
        context.Check(linear.interpolationType() == QKeyFrame.InterpolationType.LinearInterpolation,
            "the coordinates constructor is LinearInterpolation");
        context.Check(linear.Coordinates().X() == 1 && linear.Coordinates().Y() == 2,
            "and it carries the coordinates");

        // --- equality ignores control points unless the interpolation is Bezier
        var linearA = new QKeyFrame(new QVector2D(1, 2));
        var linearB = new QKeyFrame(new QVector2D(1, 2));
        linearB.SetLeftControlPoint(new QVector2D(7, 7));
        context.Check(linearA == linearB,
            "linear frames ignore their control points, as Qt's operator== does");
        var bezierA = new QKeyFrame(new QVector2D(1, 2), new QVector2D(0, 0), new QVector2D(9, 9));
        var bezierB = new QKeyFrame(new QVector2D(1, 2), new QVector2D(0, 0), new QVector2D(8, 8));
        context.Check(bezierA != bezierB, "Bezier frames compare their control points");
        context.Check(linearA.GetHashCode() == linearB.GetHashCode(),
            "equal frames hash equally (the ignored control points are not hashed)");

        // --- assignment copies
        var copy = linearA;
        copy.SetCoordinates(new QVector2D(3, 4));
        context.Check(linearA.Coordinates().X() == 1 && copy.Coordinates().X() == 3,
            "assigning a QKeyFrame copies it");

        // --- a loop over values registers no wrappers
        long registeredBefore = ApiRuntime.WrappersRegistered;
        var keyFrame = new QKeyFrame(new QVector2D(0, 0));
        for (int i = 0; i < 10000; i++)
        {
            keyFrame.SetCoordinates(new QVector2D(i % 100, i / 100));
            if (keyFrame.Coordinates().Y() != i / 100)
            {
                context.Check(false, $"QKeyFrame round-trip failed in round {i}");
                break;
            }
        }
        context.Check(ApiRuntime.WrappersRegistered == registeredBefore,
            $"10000 QKeyFrame round-trips register no wrappers (+{ApiRuntime.WrappersRegistered - registeredBefore})");

        return context.ExitCode;
    }
}
