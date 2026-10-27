using System.Drawing;
using System.Numerics;

// The QtGui value types that have a BCL counterpart: colors map to System.Drawing.Color, vectors
// and quaternions to System.Numerics. As with the date/geometry conversions, they are explicit
// (constructors and To* methods), not implicit operators.
namespace QtSharp.QtGui
{
    public partial struct QColor
    {
        /// <summary>The same ARGB color as a System.Drawing.Color.</summary>
        public QColor(Color color) : this(color.R, color.G, color.B, color.A) { }

        /// <summary>The color as a System.Drawing.Color; an invalid QColor is Color.Empty.</summary>
        public Color ToColor()
            => IsValid() ? Color.FromArgb(Alpha(), Red(), Green(), Blue()) : Color.Empty;

        public static explicit operator Color(QColor value) => value.ToColor();
        public static explicit operator QColor(Color value) => new QColor(value);
    }

    public partial struct QVector2D
    {
        public QVector2D(Vector2 vector) : this(vector.X, vector.Y) { }
        public Vector2 ToVector2() => new Vector2(X(), Y());
        public static explicit operator Vector2(QVector2D value) => value.ToVector2();
        public static explicit operator QVector2D(Vector2 value) => new QVector2D(value);
    }

    public partial struct QVector3D
    {
        public QVector3D(Vector3 vector) : this(vector.X, vector.Y, vector.Z) { }
        public Vector3 ToVector3() => new Vector3(X(), Y(), Z());
        public static explicit operator Vector3(QVector3D value) => value.ToVector3();
        public static explicit operator QVector3D(Vector3 value) => new QVector3D(value);
    }

    public partial struct QVector4D
    {
        public QVector4D(Vector4 vector) : this(vector.X, vector.Y, vector.Z, vector.W) { }
        public Vector4 ToVector4() => new Vector4(X(), Y(), Z(), W());
        public static explicit operator Vector4(QVector4D value) => value.ToVector4();
        public static explicit operator QVector4D(Vector4 value) => new QVector4D(value);
    }

    public partial struct QQuaternion
    {
        public QQuaternion(Quaternion quaternion) : this(quaternion.W, quaternion.X, quaternion.Y, quaternion.Z) { }
        public Quaternion ToQuaternion() => new Quaternion(X(), Y(), Z(), Scalar());
        public static explicit operator Quaternion(QQuaternion value) => value.ToQuaternion();
        public static explicit operator QQuaternion(Quaternion value) => new QQuaternion(value);
    }
}
