using System.Drawing;
using System.Numerics;

// The source-side spelling of the QtGui value-type bridges: `color.ToQColor()`,
// `vector.ToQVector3D()`, ... Extension methods, like the QtCore ones: a new method name cannot
// change overload resolution, and every conversion stays visible at the call site.
//
// The Qt structs returned here are values, not handles - nothing to dispose. QMatrix4x4 is the
// exception: it is a handle class, so ToQMatrix4x4() returns an owned wrapper the caller disposes.
// The geometry types (Point/Size/Rectangle -> QPoint/QSize/QRect) live in QtCore and are in
// runtime/QtCore/BclInterop.ToQt.cs, because those Qt types belong to QtCore.
namespace QtSharp.QtGui
{
    /// <summary>BCL -> Qt conversions for the QtGui color, vector and matrix types.</summary>
    public static class BclToQtExtensions
    {
        /// <summary>A QColor with the same ARGB channels.</summary>
        public static QColor ToQColor(this Color value) => new QColor(value);

        /// <summary>A QVector2D with the same components.</summary>
        public static QVector2D ToQVector2D(this Vector2 value) => new QVector2D(value);

        /// <summary>A QVector3D with the same components.</summary>
        public static QVector3D ToQVector3D(this Vector3 value) => new QVector3D(value);

        /// <summary>A QVector4D with the same components.</summary>
        public static QVector4D ToQVector4D(this Vector4 value) => new QVector4D(value);

        /// <summary>A QQuaternion with the same components.</summary>
        public static QQuaternion ToQQuaternion(this Quaternion value) => new QQuaternion(value);

        /// <summary>
        /// A QMatrix4x4 for the same transform. The result owns a native object and is the
        /// caller's to dispose; the conversion transposes between Qt's column-vector convention
        /// and System.Numerics' row-vector one.
        /// </summary>
        public static QMatrix4x4 ToQMatrix4x4(this Matrix4x4 value) => new QMatrix4x4(value);
    }
}
