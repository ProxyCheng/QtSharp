using System;
using System.Numerics;

namespace QtSharp.Tests
{
    /// <summary>
    /// QMatrix4x4 and System.Numerics.Matrix4x4: the conversion transposes between the
    /// column-vector and row-vector conventions. Every check uses a real transform, because the
    /// identity matrix is its own transpose and would pass a broken conversion.
    /// </summary>
    [Test("test-matrixinterop")]
    public static class MatrixInteropTests
    {
        const float Epsilon = 1e-5f;

        public static int Run(TestContext context)
        {
            // ------------------------------------------------------- Qt Translate -> .NET
            using (var matrix = new QMatrix4x4())
            {
                matrix.Translate(1f, 2f, 3f);
                Matrix4x4 converted = matrix.ToMatrix4x4();
                Vector3 moved = Vector3.Transform(new Vector3(1f, 1f, 1f), converted);
                context.Check(Near(moved, new Vector3(2f, 3f, 4f)),
                    $"Qt translate -> Matrix4x4 moves the point in the row-vector convention ({moved})");

                // The last row of the .NET matrix is the translation, i.e. Qt's last column.
                QVector4D lastColumn = matrix.Column(3);
                context.Check(Near(converted.M41, lastColumn.X())
                      && Near(converted.M42, lastColumn.Y())
                      && Near(converted.M43, lastColumn.Z())
                      && Near(converted.M44, lastColumn.W()),
                    $"Matrix4x4's last row is QMatrix4x4's last column ({converted.M41}, {converted.M42}, {converted.M43}, {converted.M44})");

                using (var back = new QMatrix4x4(converted))
                {
                    CheckSameMatrix(context, "translate round-trip", converted, back.ToMatrix4x4());
                    QVector3D qtMoved = back.Map(new QVector3D(1f, 1f, 1f));
                    context.Check(Near(qtMoved.X(), 2f) && Near(qtMoved.Y(), 3f) && Near(qtMoved.Z(), 4f),
                        $"and Matrix4x4 -> QMatrix4x4 moves the same point ({qtMoved.X()}, {qtMoved.Y()}, {qtMoved.Z()})");
                }
            }

            // ------------------------------------------------------- .NET perspective -> Qt
            Matrix4x4 perspective = Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 3f, 1.5f, 0.1f, 100f);
            using (var matrix = new QMatrix4x4(perspective))
            {
                CheckSameMatrix(context, "System.Numerics perspective round-trip",
                    perspective, matrix.ToMatrix4x4());

                // Vector4.Transform is v * M; Qt's Map is M * v. They agree for the same
                // components, which is the transpose contract in action.
                var point = new Vector4(0.2f, -0.1f, -5f, 1f);
                Vector4 net = Vector4.Transform(point, perspective);
                QVector4D qt = matrix.Map(new QVector4D(point.X, point.Y, point.Z, point.W));
                context.Check(Near(net.X, qt.X()) && Near(net.Y, qt.Y())
                      && Near(net.Z, qt.Z()) && Near(net.W, qt.W()),
                    $"the same perspective transform agrees on a real point (net {net}, Qt {qt.X()},{qt.Y()},{qt.Z()},{qt.W()})");
            }

            // ------------------------------------------------------- Qt perspective -> .NET
            using (var matrix = new QMatrix4x4())
            {
                matrix.Perspective(60f, 1.5f, 0.1f, 100f);
                Matrix4x4 converted = matrix.ToMatrix4x4();

                // The projective row/column must have moved, not been copied positionally.
                QVector4D lastColumn = matrix.Column(3);
                context.Check(Near(converted.M41, lastColumn.X())
                      && Near(converted.M42, lastColumn.Y())
                      && Near(converted.M43, lastColumn.Z())
                      && Near(converted.M44, lastColumn.W()),
                    "Qt perspective's projective column becomes Matrix4x4's projective row");

                var point = new Vector4(0.2f, -0.1f, -5f, 1f);
                Vector4 net = Vector4.Transform(point, converted);
                QVector4D qt = matrix.Map(new QVector4D(point.X, point.Y, point.Z, point.W));
                context.Check(Near(net.X, qt.X()) && Near(net.Y, qt.Y())
                      && Near(net.Z, qt.Z()) && Near(net.W, qt.W()),
                    $"Qt perspective and its transposed Matrix4x4 agree on a real point (net {net}, Qt {qt.X()},{qt.Y()},{qt.Z()},{qt.W()})");
            }

            return context.ExitCode;
        }

        static void CheckSameMatrix(TestContext context, string what, Matrix4x4 expected, Matrix4x4 actual)
        {
            bool same = Near(expected.M11, actual.M11) && Near(expected.M12, actual.M12)
                && Near(expected.M13, actual.M13) && Near(expected.M14, actual.M14)
                && Near(expected.M21, actual.M21) && Near(expected.M22, actual.M22)
                && Near(expected.M23, actual.M23) && Near(expected.M24, actual.M24)
                && Near(expected.M31, actual.M31) && Near(expected.M32, actual.M32)
                && Near(expected.M33, actual.M33) && Near(expected.M34, actual.M34)
                && Near(expected.M41, actual.M41) && Near(expected.M42, actual.M42)
                && Near(expected.M43, actual.M43) && Near(expected.M44, actual.M44);
            context.Check(same, what);
        }

        static bool Near(float a, float b) => Math.Abs(a - b) <= Epsilon;

        static bool Near(Vector3 a, Vector3 b)
            => Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);
    }
}
