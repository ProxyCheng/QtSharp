using System;
using System.Drawing;

// Explicit conversions between Qt's integer/float geometry and System.Drawing. They are
// explicit (constructors and To* methods) for the same reason the date conversions are: the
// generated == on a Qt struct compares Qt values, and an implicit operator would hide which
// representation a comparison or an overload picked.
namespace QtSharp.QtCore
{
    public partial struct QPoint
    {
        public QPoint(Point point) : this(point.X, point.Y) { }
        public Point ToPoint() => new Point(X(), Y());
        public static explicit operator Point(QPoint value) => value.ToPoint();
        public static explicit operator QPoint(Point value) => new QPoint(value);
    }

    public partial struct QPointF
    {
        public QPointF(PointF point) : this(point.X, point.Y) { }
        public PointF ToPointF() => new PointF((float)X(), (float)Y());
        public static explicit operator PointF(QPointF value) => value.ToPointF();
        public static explicit operator QPointF(PointF value) => new QPointF(value);
    }

    public partial struct QSize
    {
        public QSize(Size size) : this(size.Width, size.Height) { }
        public Size ToSize() => new Size(Width(), Height());
        public static explicit operator Size(QSize value) => value.ToSize();
        public static explicit operator QSize(Size value) => new QSize(value);
    }

    public partial struct QSizeF
    {
        public QSizeF(SizeF size) : this(size.Width, size.Height) { }
        public SizeF ToSizeF() => new SizeF((float)Width(), (float)Height());
        public static explicit operator SizeF(QSizeF value) => value.ToSizeF();
        public static explicit operator QSizeF(SizeF value) => new QSizeF(value);
    }

    public partial struct QRect
    {
        public QRect(Rectangle rectangle) : this(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height) { }
        // QRect's right()/bottom() are inclusive; Rectangle's Width/Height express the extent, so
        // they are the pair to carry across.
        public Rectangle ToRectangle() => new Rectangle(X(), Y(), Width(), Height());
        public static explicit operator Rectangle(QRect value) => value.ToRectangle();
        public static explicit operator QRect(Rectangle value) => new QRect(value);
    }

    public partial struct QRectF
    {
        public QRectF(RectangleF rectangle) : this(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height) { }
        public RectangleF ToRectangleF() => new RectangleF((float)X(), (float)Y(), (float)Width(), (float)Height());
        public static explicit operator RectangleF(QRectF value) => value.ToRectangleF();
        public static explicit operator QRectF(RectangleF value) => new QRectF(value);
    }

    public partial class QUrl
    {
        /// <summary>The QUrl for a BCL Uri (a relative URI keeps its original spelling).</summary>
        public QUrl(Uri uri) : this((uri ?? throw new ArgumentNullException(nameof(uri))).IsAbsoluteUri
            ? uri.AbsoluteUri
            : uri.OriginalString) { }

        /// <summary>The URI as a BCL Uri. An invalid QUrl throws InvalidOperationException.</summary>
        public Uri ToUri()
        {
            if (!IsValid())
                throw new InvalidOperationException("The QUrl is null or invalid and has no System.Uri equivalent.");
            return new Uri(ToString(), UriKind.RelativeOrAbsolute);
        }

        public static explicit operator Uri(QUrl value)
            => (value ?? throw new ArgumentNullException(nameof(value))).ToUri();
        public static explicit operator QUrl(Uri value) => new QUrl(value);
    }

    public partial class QUuid
    {
        /// <summary>A QUuid with the same 128 bits as the BCL Guid.</summary>
        public QUuid(Guid value) : this(value.ToString("D")) { }

        /// <summary>The GUID, or Guid.Empty for a null QUuid.</summary>
        public Guid ToGuid()
            => IsNull() ? Guid.Empty : Guid.Parse(ToString(QUuid.StringFormat.WithoutBraces));

        public static explicit operator Guid(QUuid value)
            => (value ?? throw new ArgumentNullException(nameof(value))).ToGuid();
        public static explicit operator QUuid(Guid value) => new QUuid(value);
    }
}
