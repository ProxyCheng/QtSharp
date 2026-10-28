using System;
using QtSharp.Qt3DCore;

namespace QtSharp.Qt3D.Examples
{
    /// <summary>
    /// The float comparisons the Qt3D ports assert on. QNodeId/QVector3D/QQuaternion equality is
    /// exact, while the C++ examples compare component floats with a tolerance.
    /// </summary>
    internal static class Assert
    {
        public static bool Near(float actual, float expected, float tolerance = 1e-4f)
            => Math.Abs(actual - expected) <= tolerance;

        public static bool Near(QVector3D actual, QVector3D expected, float tolerance = 1e-4f)
            => Near(actual.X(), expected.X(), tolerance)
               && Near(actual.Y(), expected.Y(), tolerance)
               && Near(actual.Z(), expected.Z(), tolerance);

        public static bool Near(QQuaternion actual, QQuaternion expected, float tolerance = 1e-4f)
            => Near(actual.Scalar(), expected.Scalar(), tolerance)
               && Near(actual.X(), expected.X(), tolerance)
               && Near(actual.Y(), expected.Y(), tolerance)
               && Near(actual.Z(), expected.Z(), tolerance);
    }
}
