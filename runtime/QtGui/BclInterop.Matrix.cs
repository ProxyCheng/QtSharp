using System.Numerics;

// QMatrix4x4 and System.Numerics.Matrix4x4 hold the same kind of 4x4 transform but use opposite
// vector conventions:
//
//   Qt                M * v   (column vector; the translation is in the last column, m14/m24/m34)
//   System.Numerics   v * M   (row vector; the translation is in the last row, M41/M42/M43)
//
// The same geometric transform is therefore the transpose on the other side, and this conversion
// transposes. Copying the sixteen floats in positional order would put a translation in M14 (which
// System.Numerics ignores for Vector3.Transform) and silently produce a matrix that does not move
// anything; the constructor and ToMatrix4x4() both go through Qt's Column() so that cannot happen.
//
// The conversion is a representation change, not a projection-convention change: a Qt perspective
// matrix converted to System.Numerics is still an OpenGL-style [-1, 1] depth projection, even
// though Matrix4x4.CreatePerspectiveFieldOfView builds a DirectX-style [0, 1] one. Use the
// constructor for the matrix you mean; do not expect the two factories to agree.
//
// Explicit constructor and ToMatrix4x4(), no operators, like the other BCL interop files.
namespace QtSharp.QtGui
{
    public partial class QMatrix4x4
    {
        /// <summary>
        /// Builds the QMatrix4x4 for a System.Numerics <see cref="Matrix4x4"/>, transposing from
        /// the row-vector convention to Qt's column-vector one.
        /// </summary>
        /// <param name="matrix">The matrix to copy.</param>
        /// <remarks>
        /// The result transforms column vectors the way the argument transforms row vectors:
        /// <c>qmatrix.Map(v)</c> and <c>Vector4.Transform(v, matrix)</c> return the same components.
        /// The 16 floats are Qt's <c>m11..m44</c> row-major constructor order, read from the
        /// argument's columns as rows.
        /// </remarks>
        public QMatrix4x4(Matrix4x4 matrix)
            : this(
                matrix.M11, matrix.M21, matrix.M31, matrix.M41,
                matrix.M12, matrix.M22, matrix.M32, matrix.M42,
                matrix.M13, matrix.M23, matrix.M33, matrix.M43,
                matrix.M14, matrix.M24, matrix.M34, matrix.M44)
        {
        }

        /// <summary>
        /// The matrix as a System.Numerics <see cref="Matrix4x4"/>, transposed from Qt's
        /// column-vector convention to the row-vector one.
        /// </summary>
        /// <returns>
        /// A new matrix that transforms row vectors exactly as this QMatrix4x4 transforms column
        /// vectors.
        /// </returns>
        /// <remarks>
        /// Row <c>i</c> of the result is column <c>i</c> of the QMatrix4x4. The conversion is a
        /// representation change only: the projection convention (depth range, handedness) is
        /// whatever the Qt matrix already had, so a Qt perspective matrix does not become
        /// <see cref="Matrix4x4.CreatePerspectiveFieldOfView"/>'s DirectX-style matrix.
        /// </remarks>
        public Matrix4x4 ToMatrix4x4()
        {
            QVector4D c0 = Column(0);
            QVector4D c1 = Column(1);
            QVector4D c2 = Column(2);
            QVector4D c3 = Column(3);
            return new Matrix4x4(
                c0.X(), c0.Y(), c0.Z(), c0.W(),
                c1.X(), c1.Y(), c1.Z(), c1.W(),
                c2.X(), c2.Y(), c2.Z(), c2.W(),
                c3.X(), c3.Y(), c3.Z(), c3.W());
        }
    }
}
