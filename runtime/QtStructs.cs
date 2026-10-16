using System;
using System.Globalization;

// The Qt types that are *values*: the geometry family, which C++ passes by value and which PyQt and
// PySide both model as value types (SIP inlines them in the Python object, shiboken's typesystem
// marks them one). Each one here is a struct laid out exactly like the C++ type, so it crosses the
// boundary as its own bytes - no handle, no wrapper, no entry in the runtime's tables, nothing for
// the caller to release.
//
// The admission test is *trivially copyable*: a copy has to own nothing. Qt spells these `class`,
// and QColor, QPen, QString and the rest of the implicitly shared classes are passed by value too -
// but they carry a pointer with a reference count, so a bitwise copy would double-free. Those keep
// their wrapper (and its Dispose). The names of the members are Qt's own, so code written against
// the C++ API or against PyQt reads the same here.
//
// The mirror of each type is the POD in native/QtSharpApiRuntime.h, and the generator's table in
// gen/ApiModel.cs (ApiTypes.RuntimeStructs) says how the two correspond. Adding a type means adding
// all three: the struct here, the POD there, and the row in that table.
namespace QtSharp.Api
{
    /// <summary>
    /// The Qt types that are *values* rather than objects.
    ///
    /// C++ says a QPointF is two qreals: it is copyable, it is passed and returned by value, and
    /// almost every member is arithmetic. PyQt and PySide model them the same way - SIP inlines the
    /// C++ object inside the Python one, and shiboken's typesystem marks them value types, so no
    /// wrapper and no ownership table is involved. That is the shape here as well: a struct, with the
    /// coordinates inline, passed across the boundary as its coordinates.
    ///
    /// The alternative - a class with a handle, which is how these were modelled before - costs one
    /// native allocation, one entry in the runtime's registration tables and one finalizer per point.
    /// A chart that samples and replaces a few thousand points a second therefore leaves hundreds of
    /// thousands of unreclaimable objects behind (the finalizer queue is the bottleneck), and the
    /// caller has to dispose each one. A value has none of that.
    ///
    /// The member names are Qt's own (x, y, rx, ry, setX, setY, isNull, manhattanLength, transposed,
    /// toPoint, dotProduct), so code written against the C++ API or against PyQt reads the same here.
    /// </summary>
    public struct QPointF : IEquatable<QPointF>
    {
        double _x;
        double _y;

        public QPointF(double xpos, double ypos)
        {
            _x = xpos;
            _y = ypos;
        }

        /// <summary>The conversion Qt declares from QPoint.</summary>
        public QPointF(QPoint p)
        {
            _x = p.X();
            _y = p.Y();
        }

        public double X() => _x;
        public double Y() => _y;

        // Qt returns an lvalue reference here (qreal &rx()); a struct cannot hand one out, so the
        // value is returned instead - the same thing PyQt does with it.
        public double Rx() => _x;
        public double Ry() => _y;

        public void SetX(double xpos) => _x = xpos;
        public void SetY(double ypos) => _y = ypos;

        public bool IsNull() => _x == 0.0 && _y == 0.0;

        public double ManhattanLength() => Math.Abs(_x) + Math.Abs(_y);

        public QPointF Transposed() => new QPointF(_y, _x);

        /// <summary>Rounds, as Qt's QPointF::toPoint() does.</summary>
        public QPoint ToPoint() => new QPoint(QtMath.Round(_x), QtMath.Round(_y));

        public static double DotProduct(QPointF p1, QPointF p2) => p1._x * p2._x + p1._y * p2._y;

        public bool Equals(QPointF other) => _x == other._x && _y == other._y;
        public override bool Equals(object obj) => obj is QPointF other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_x, _y);
        public static bool operator ==(QPointF a, QPointF b) => a.Equals(b);
        public static bool operator !=(QPointF a, QPointF b) => !a.Equals(b);

        public override string ToString()
            => "QPointF(" + _x.ToString(CultureInfo.InvariantCulture)
               + ", " + _y.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The integer twin of QPointF; see the note there.</summary>
    public struct QPoint : IEquatable<QPoint>
    {
        int _x;
        int _y;

        public QPoint(int xpos, int ypos)
        {
            _x = xpos;
            _y = ypos;
        }

        public int X() => _x;
        public int Y() => _y;

        public int Rx() => _x;
        public int Ry() => _y;

        public void SetX(int xpos) => _x = xpos;
        public void SetY(int ypos) => _y = ypos;

        public bool IsNull() => _x == 0 && _y == 0;

        public int ManhattanLength() => Math.Abs(_x) + Math.Abs(_y);

        public QPoint Transposed() => new QPoint(_y, _x);

        public QPointF ToPointF() => new QPointF(_x, _y);

        public static int DotProduct(QPoint p1, QPoint p2) => p1._x * p2._x + p1._y * p2._y;

        public bool Equals(QPoint other) => _x == other._x && _y == other._y;
        public override bool Equals(object obj) => obj is QPoint other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_x, _y);
        public static bool operator ==(QPoint a, QPoint b) => a.Equals(b);
        public static bool operator !=(QPoint a, QPoint b) => !a.Equals(b);

        public override string ToString()
            => "QPoint(" + _x.ToString(CultureInfo.InvariantCulture)
               + ", " + _y.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The integer size twin of QSizeF; see the note on QPointF above.</summary>
    public struct QSize : IEquatable<QSize>
    {
        int _width, _height;

        public QSize(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public int Width() => _width;
        public int Height() => _height;
        public int Rwidth() => _width;
        public int Rheight() => _height;

        public void SetWidth(int width) => _width = width;
        public void SetHeight(int height) => _height = height;

        public bool IsNull() => _width == 0 && _height == 0;
        public bool IsEmpty() => _width <= 0 || _height <= 0;
        public bool IsValid() => _width >= 0 && _height >= 0;

        public QSize Transposed() => new QSize(_height, _width);

        /// <summary>Qt's grownBy/shrunkBy: the box grows (shrinks) by a margin on each side.</summary>
        public QSize GrownBy(QMargins m)
            => new QSize(_width + m.Left() + m.Right(), _height + m.Top() + m.Bottom());

        public QSize ShrunkBy(QMargins m)
            => new QSize(_width - m.Left() - m.Right(), _height - m.Top() - m.Bottom());

        public QSize ExpandedTo(QSize other)
            => new QSize(Math.Max(_width, other._width), Math.Max(_height, other._height));

        public QSize BoundedTo(QSize other)
            => new QSize(Math.Min(_width, other._width), Math.Min(_height, other._height));

        /// <summary>Scales in place, keeping the aspect ratio the way Qt's QSize::scale does.</summary>
        public void Scale(int width, int height, Qt.AspectRatioMode mode)
        {
            if (mode == Qt.AspectRatioMode.IgnoreAspectRatio || _width == 0 || _height == 0)
            {
                _width = width;
                _height = height;
                return;
            }
            bool fit = mode == Qt.AspectRatioMode.KeepAspectRatio;
            double ratio = Math.Min((double)width / _width, (double)height / _height);
            if (!fit)
                ratio = Math.Max((double)width / _width, (double)height / _height);
            _width = (int)(_width * ratio);
            _height = (int)(_height * ratio);
        }

        public QSizeF ToSizeF() => new QSizeF(_width, _height);

        public static QSize operator +(QSize a, QSize b) => new QSize(a._width + b._width, a._height + b._height);
        public static QSize operator -(QSize a, QSize b) => new QSize(a._width - b._width, a._height - b._height);
        public static QSize operator *(QSize a, int factor) => new QSize(a._width * factor, a._height * factor);
        public static QSize operator *(int factor, QSize a) => a * factor;
        public static QSize operator /(QSize a, int divisor) => new QSize(a._width / divisor, a._height / divisor);

        public bool Equals(QSize other) => _width == other._width && _height == other._height;
        public override bool Equals(object obj) => obj is QSize other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_width, _height);
        public static bool operator ==(QSize a, QSize b) => a.Equals(b);
        public static bool operator !=(QSize a, QSize b) => !a.Equals(b);

        public override string ToString()
            => "QSize(" + _width.ToString(CultureInfo.InvariantCulture)
               + ", " + _height.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The real-numbered size; see the note on QPointF above.</summary>
    public struct QSizeF : IEquatable<QSizeF>
    {
        double _width, _height;

        public QSizeF(double width, double height)
        {
            _width = width;
            _height = height;
        }

        public double Width() => _width;
        public double Height() => _height;
        public double Rwidth() => _width;
        public double Rheight() => _height;

        public void SetWidth(double width) => _width = width;
        public void SetHeight(double height) => _height = height;

        public bool IsNull() => _width == 0.0 && _height == 0.0;
        public bool IsEmpty() => _width <= 0.0 || _height <= 0.0;
        public bool IsValid() => _width >= 0.0 && _height >= 0.0;

        public QSizeF Transposed() => new QSizeF(_height, _width);

        public QSizeF GrownBy(QMarginsF m)
            => new QSizeF(_width + m.Left() + m.Right(), _height + m.Top() + m.Bottom());

        public QSizeF ShrunkBy(QMarginsF m)
            => new QSizeF(_width - m.Left() - m.Right(), _height - m.Top() - m.Bottom());

        public QSizeF ExpandedTo(QSizeF other)
            => new QSizeF(Math.Max(_width, other._width), Math.Max(_height, other._height));

        public QSizeF BoundedTo(QSizeF other)
            => new QSizeF(Math.Min(_width, other._width), Math.Min(_height, other._height));

        /// <summary>Rounds, as Qt's QSizeF::toSize() does.</summary>
        public QSize ToSize() => new QSize(QtMath.Round(_width), QtMath.Round(_height));

        public static QSizeF operator +(QSizeF a, QSizeF b) => new QSizeF(a._width + b._width, a._height + b._height);
        public static QSizeF operator -(QSizeF a, QSizeF b) => new QSizeF(a._width - b._width, a._height - b._height);
        public static QSizeF operator *(QSizeF a, double factor) => new QSizeF(a._width * factor, a._height * factor);
        public static QSizeF operator *(double factor, QSizeF a) => a * factor;
        public static QSizeF operator /(QSizeF a, double divisor) => new QSizeF(a._width / divisor, a._height / divisor);

        public bool Equals(QSizeF other) => _width == other._width && _height == other._height;
        public override bool Equals(object obj) => obj is QSizeF other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_width, _height);
        public static bool operator ==(QSizeF a, QSizeF b) => a.Equals(b);
        public static bool operator !=(QSizeF a, QSizeF b) => !a.Equals(b);

        public override string ToString()
            => "QSizeF(" + _width.ToString(CultureInfo.InvariantCulture)
               + ", " + _height.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The integer margins; see the note on QPointF above.</summary>
    public struct QMargins : IEquatable<QMargins>
    {
        int _left, _top, _right, _bottom;

        public QMargins(int left, int top, int right, int bottom)
        {
            _left = left;
            _top = top;
            _right = right;
            _bottom = bottom;
        }

        public int Left() => _left;
        public int Top() => _top;
        public int Right() => _right;
        public int Bottom() => _bottom;

        public void SetLeft(int left) => _left = left;
        public void SetTop(int top) => _top = top;
        public void SetRight(int right) => _right = right;
        public void SetBottom(int bottom) => _bottom = bottom;

        public bool IsNull() => _left == 0 && _top == 0 && _right == 0 && _bottom == 0;

        public QMarginsF ToMarginsF() => new QMarginsF(_left, _top, _right, _bottom);

        public static QMargins operator +(QMargins a, QMargins b)
            => new QMargins(a._left + b._left, a._top + b._top, a._right + b._right, a._bottom + b._bottom);
        public static QMargins operator -(QMargins a, QMargins b)
            => new QMargins(a._left - b._left, a._top - b._top, a._right - b._right, a._bottom - b._bottom);
        /// <summary>Qt grows a margin by a scalar on every side.</summary>
        public static QMargins operator +(QMargins m, int addend)
            => new QMargins(m._left + addend, m._top + addend, m._right + addend, m._bottom + addend);
        public static QMargins operator -(QMargins m, int subtrahend)
            => new QMargins(m._left - subtrahend, m._top - subtrahend, m._right - subtrahend, m._bottom - subtrahend);
        public static QMargins operator *(QMargins m, int factor)
            => new QMargins(m._left * factor, m._top * factor, m._right * factor, m._bottom * factor);
        public static QMargins operator *(int factor, QMargins m) => m * factor;
        public static QMargins operator /(QMargins m, int divisor)
            => new QMargins(m._left / divisor, m._top / divisor, m._right / divisor, m._bottom / divisor);

        public bool Equals(QMargins other)
            => _left == other._left && _top == other._top && _right == other._right && _bottom == other._bottom;
        public override bool Equals(object obj) => obj is QMargins other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_left, _top, _right, _bottom);
        public static bool operator ==(QMargins a, QMargins b) => a.Equals(b);
        public static bool operator !=(QMargins a, QMargins b) => !a.Equals(b);

        public override string ToString()
            => "QMargins(" + _left.ToString(CultureInfo.InvariantCulture)
               + ", " + _top.ToString(CultureInfo.InvariantCulture)
               + ", " + _right.ToString(CultureInfo.InvariantCulture)
               + ", " + _bottom.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The real-numbered margins; see the note on QPointF above.</summary>
    public struct QMarginsF : IEquatable<QMarginsF>
    {
        double _left, _top, _right, _bottom;

        public QMarginsF(double left, double top, double right, double bottom)
        {
            _left = left;
            _top = top;
            _right = right;
            _bottom = bottom;
        }

        public double Left() => _left;
        public double Top() => _top;
        public double Right() => _right;
        public double Bottom() => _bottom;

        public void SetLeft(double left) => _left = left;
        public void SetTop(double top) => _top = top;
        public void SetRight(double right) => _right = right;
        public void SetBottom(double bottom) => _bottom = bottom;

        public bool IsNull() => _left == 0.0 && _top == 0.0 && _right == 0.0 && _bottom == 0.0;

        public QMargins ToMargins() => new QMargins(QtMath.Round(_left), QtMath.Round(_top), QtMath.Round(_right), QtMath.Round(_bottom));

        public static QMarginsF operator +(QMarginsF a, QMarginsF b)
            => new QMarginsF(a._left + b._left, a._top + b._top, a._right + b._right, a._bottom + b._bottom);
        public static QMarginsF operator -(QMarginsF a, QMarginsF b)
            => new QMarginsF(a._left - b._left, a._top - b._top, a._right - b._right, a._bottom - b._bottom);
        public static QMarginsF operator +(QMarginsF m, double addend)
            => new QMarginsF(m._left + addend, m._top + addend, m._right + addend, m._bottom + addend);
        public static QMarginsF operator -(QMarginsF m, double subtrahend)
            => new QMarginsF(m._left - subtrahend, m._top - subtrahend, m._right - subtrahend, m._bottom - subtrahend);
        public static QMarginsF operator *(QMarginsF m, double factor)
            => new QMarginsF(m._left * factor, m._top * factor, m._right * factor, m._bottom * factor);
        public static QMarginsF operator *(double factor, QMarginsF m) => m * factor;
        public static QMarginsF operator /(QMarginsF m, double divisor)
            => new QMarginsF(m._left / divisor, m._top / divisor, m._right / divisor, m._bottom / divisor);

        public bool Equals(QMarginsF other)
            => _left == other._left && _top == other._top && _right == other._right && _bottom == other._bottom;
        public override bool Equals(object obj) => obj is QMarginsF other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_left, _top, _right, _bottom);
        public static bool operator ==(QMarginsF a, QMarginsF b) => a.Equals(b);
        public static bool operator !=(QMarginsF a, QMarginsF b) => !a.Equals(b);

        public override string ToString()
            => "QMarginsF(" + _left.ToString(CultureInfo.InvariantCulture)
               + ", " + _top.ToString(CultureInfo.InvariantCulture)
               + ", " + _right.ToString(CultureInfo.InvariantCulture)
               + ", " + _bottom.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The integer line; see the note on QPointF above. Qt stores it as two points, so the fields are
    /// x1,y1,x2,y2 in memory order - which is what the POD mirror of it keeps.
    /// </summary>
    public struct QLine : IEquatable<QLine>
    {
        int _x1, _y1, _x2, _y2;

        public QLine(int x1, int y1, int x2, int y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
        }

        public QLine(QPoint p1, QPoint p2)
        {
            _x1 = p1.X();
            _y1 = p1.Y();
            _x2 = p2.X();
            _y2 = p2.Y();
        }

        public int X1() => _x1;
        public int Y1() => _y1;
        public int X2() => _x2;
        public int Y2() => _y2;

        public QPoint P1() => new QPoint(_x1, _y1);
        public QPoint P2() => new QPoint(_x2, _y2);

        public void SetLine(int x1, int y1, int x2, int y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
        }

        public void SetPoints(QPoint p1, QPoint p2)
        {
            _x1 = p1.X();
            _y1 = p1.Y();
            _x2 = p2.X();
            _y2 = p2.Y();
        }

        public void SetP1(QPoint p1)
        {
            _x1 = p1.X();
            _y1 = p1.Y();
        }

        public void SetP2(QPoint p2)
        {
            _x2 = p2.X();
            _y2 = p2.Y();
        }

        public int Dx() => _x2 - _x1;
        public int Dy() => _y2 - _y1;

        public bool IsNull() => _x1 == _x2 && _y1 == _y2;

        /// <summary>Qt rounds the midpoint to the nearest integers (qRound, halves away from zero).</summary>
        public QPoint Center() => new QPoint(QtMath.Round((_x1 + _x2) / 2.0), QtMath.Round((_y1 + _y2) / 2.0));

        /// <summary>The line with its points swapped.</summary>
        public QLine Transposed() => new QLine(_x2, _y2, _x1, _y1);

        public QLineF ToLineF() => new QLineF(_x1, _y1, _x2, _y2);

        public bool Equals(QLine other)
            => _x1 == other._x1 && _y1 == other._y1 && _x2 == other._x2 && _y2 == other._y2;
        public override bool Equals(object obj) => obj is QLine other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_x1, _y1, _x2, _y2);
        public static bool operator ==(QLine a, QLine b) => a.Equals(b);
        public static bool operator !=(QLine a, QLine b) => !a.Equals(b);

        public override string ToString()
            => "QLine(" + _x1.ToString(CultureInfo.InvariantCulture)
               + ", " + _y1.ToString(CultureInfo.InvariantCulture)
               + ", " + _x2.ToString(CultureInfo.InvariantCulture)
               + ", " + _y2.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The real-numbered line; see the note on QPointF above.</summary>
    public struct QLineF : IEquatable<QLineF>
    {
        double _x1, _y1, _x2, _y2;

        public QLineF(double x1, double y1, double x2, double y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
        }

        public QLineF(QPointF p1, QPointF p2)
        {
            _x1 = p1.X();
            _y1 = p1.Y();
            _x2 = p2.X();
            _y2 = p2.Y();
        }

        public double X1() => _x1;
        public double Y1() => _y1;
        public double X2() => _x2;
        public double Y2() => _y2;

        public QPointF P1() => new QPointF(_x1, _y1);
        public QPointF P2() => new QPointF(_x2, _y2);

        public void SetLine(double x1, double y1, double x2, double y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
        }

        public void SetPoints(QPointF p1, QPointF p2)
        {
            _x1 = p1.X();
            _y1 = p1.Y();
            _x2 = p2.X();
            _y2 = p2.Y();
        }

        public double Dx() => _x2 - _x1;
        public double Dy() => _y2 - _y1;

        public bool IsNull() => _x1 == _x2 && _y1 == _y2;

        public QPointF Center() => new QPointF((_x1 + _x2) / 2.0, (_y1 + _y2) / 2.0);

        /// <summary>Qt's QLineF::length(): the Euclidean distance between the two points.</summary>
        public double Length()
        {
            double dx = _x2 - _x1, dy = _y2 - _y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public QLineF Transposed() => new QLineF(_x2, _y2, _x1, _y1);

        public QLine ToLine() => new QLine(QtMath.Round(_x1), QtMath.Round(_y1), QtMath.Round(_x2), QtMath.Round(_y2));

        /// <summary>The angle of the line in degrees, measured counter-clockwise from the x axis.</summary>
        public double Angle() => Math.Atan2(-(_y2 - _y1), _x2 - _x1) * 180.0 / Math.PI;

        public bool Equals(QLineF other)
            => _x1 == other._x1 && _y1 == other._y1 && _x2 == other._x2 && _y2 == other._y2;
        public override bool Equals(object obj) => obj is QLineF other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_x1, _y1, _x2, _y2);
        public static bool operator ==(QLineF a, QLineF b) => a.Equals(b);
        public static bool operator !=(QLineF a, QLineF b) => !a.Equals(b);

        public override string ToString()
            => "QLineF(" + _x1.ToString(CultureInfo.InvariantCulture)
               + ", " + _y1.ToString(CultureInfo.InvariantCulture)
               + ", " + _x2.ToString(CultureInfo.InvariantCulture)
               + ", " + _y2.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The integer rectangle; see the note on QPointF above. Qt stores the two corners (x1,y1) and
    /// (x2,y2) - not x,y,width,height - and right()/bottom() are *inclusive*, which is the detail
    /// that makes width() equal x2-x1+1. The fields here follow that memory order so the array
    /// crossing can hand Qt the same bytes; the code generator rebuilds Qt's type from the corners
    /// rather than from the accessors for exactly that reason.
    /// </summary>
    public struct QRect : IEquatable<QRect>
    {
        int _x1, _y1, _x2, _y2;

        public QRect(int x, int y, int width, int height)
        {
            _x1 = x;
            _y1 = y;
            _x2 = x + width - 1;
            _y2 = y + height - 1;
        }

        /// <summary>Qt's (topLeft, bottomRight) constructor: the corners are both inclusive.</summary>
        public QRect(QPoint topLeft, QPoint bottomRight)
        {
            _x1 = topLeft.X();
            _y1 = topLeft.Y();
            _x2 = bottomRight.X();
            _y2 = bottomRight.Y();
        }

        public QRect(QPoint topLeft, QSize size)
        {
            _x1 = topLeft.X();
            _y1 = topLeft.Y();
            _x2 = _x1 + size.Width() - 1;
            _y2 = _y1 + size.Height() - 1;
        }

        public int X() => _x1;
        public int Y() => _y1;
        public int Left() => _x1;
        public int Top() => _y1;
        /// <summary>Inclusive, as Qt's QRect::right() is: x2, which is x + width() - 1.</summary>
        public int Right() => _x2 - 1;
        public int Bottom() => _y2 - 1;

        public int Width() => _x2 - _x1 + 1;
        public int Height() => _y2 - _y1 + 1;

        public void SetLeft(int left) => _x1 = left;
        public void SetTop(int top) => _y1 = top;
        public void SetRight(int right) => _x2 = right + 1;
        public void SetBottom(int bottom) => _y2 = bottom + 1;

        public void SetX(int x) => _x1 = x;
        public void SetY(int y) => _y1 = y;

        public void SetWidth(int width) => _x2 = _x1 + width - 1;
        public void SetHeight(int height) => _y2 = _y1 + height - 1;

        public void SetRect(int x, int y, int width, int height)
        {
            _x1 = x;
            _y1 = y;
            _x2 = x + width - 1;
            _y2 = y + height - 1;
        }

        /// <summary>Qt's setCoords: the raw corners, x2 and y2 inclusive.</summary>
        public void SetCoords(int x1, int y1, int x2, int y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
        }

        /// <summary>Qt's getRect: the four components the constructor takes.</summary>
        public void GetRect(out int x, out int y, out int width, out int height)
        {
            x = _x1;
            y = _y1;
            width = Width();
            height = Height();
        }

        /// <summary>Qt's getCoords: the raw corners, x2 and y2 inclusive.</summary>
        public void GetCoords(out int x1, out int y1, out int x2, out int y2)
        {
            x1 = _x1;
            y1 = _y1;
            x2 = _x2;
            y2 = _y2;
        }

        public QPoint TopLeft() => new QPoint(_x1, _y1);
        public QPoint TopRight() => new QPoint(_x2, _y1);
        public QPoint BottomLeft() => new QPoint(_x1, _y2);
        public QPoint BottomRight() => new QPoint(_x2, _y2);

        /// <summary>Qt rounds the centre to the nearest integers.</summary>
        public QPoint Center() => new QPoint(QtMath.Round((_x1 + _x2) / 2.0), QtMath.Round((_y1 + _y2) / 2.0));

        public QSize Size() => new QSize(Width(), Height());

        public void MoveTo(int x, int y)
        {
            int w = Width(), h = Height();
            _x1 = x;
            _y1 = y;
            _x2 = x + w - 1;
            _y2 = y + h - 1;
        }

        public void MoveTo(QPoint p) => MoveTo(p.X(), p.Y());

        public void MoveLeft(int left)
        {
            int w = Width();
            _x1 = left;
            _x2 = left + w - 1;
        }

        public void MoveTop(int top)
        {
            int h = Height();
            _y1 = top;
            _y2 = top + h - 1;
        }

        public void MoveRight(int right)
        {
            int w = Width();
            _x2 = right + 1;
            _x1 = _x2 - w;
        }

        public void MoveBottom(int bottom)
        {
            int h = Height();
            _y2 = bottom + 1;
            _y1 = _y2 - h;
        }

        /// <summary>Qt's moveCenter: the rectangle keeps its size and moves so the centre lands there.</summary>
        public void MoveCenter(QPoint p)
        {
            int w = Width(), h = Height();
            _x1 = p.X() - w / 2;
            _y1 = p.Y() - h / 2;
            _x2 = _x1 + w - 1;
            _y2 = _y1 + h - 1;
        }

        public void Translate(int dx, int dy)
        {
            _x1 += dx;
            _y1 += dy;
            _x2 += dx;
            _y2 += dy;
        }

        public void Translate(QPoint offset) => Translate(offset.X(), offset.Y());

        public QRect Translated(int dx, int dy)
        {
            var r = this;
            r.Translate(dx, dy);
            return r;
        }

        public QRect Translated(QPoint offset) => Translated(offset.X(), offset.Y());

        /// <summary>Grows the rectangle by a margin on each side.</summary>
        public QRect Adjusted(int dx1, int dy1, int dx2, int dy2)
            => new QRect(new QPoint(_x1 + dx1, _y1 + dy1), new QPoint(_x2 + dx2, _y2 + dy2));

        public QRect Adjusted(QMargins m)
            => Adjusted(-m.Left(), -m.Top(), m.Right(), m.Bottom());

        /// <summary>The same rectangle with left() &lt;= right() and top() &lt;= bottom().</summary>
        public QRect Normalized()
        {
            var r = this;
            if (r._x2 < r._x1)
            {
                (r._x1, r._x2) = (r._x2, r._x1);
            }
            if (r._y2 < r._y1)
            {
                (r._y1, r._y2) = (r._y2, r._y1);
            }
            return r;
        }

        /// <summary>Qt's isEmpty: a rectangle with no pixels inside, which a negative width means.</summary>
        public bool IsEmpty() => _x1 > _x2 || _y1 > _y2;

        public bool IsNull() => Width() == 0 && Height() == 0;

        public bool IsValid() => _x1 <= _x2 && _y1 <= _y2;

        public bool Contains(int x, int y, bool proper = false)
            => proper
                ? x > _x1 && x < _x2 && y > _y1 && y < _y2
                : x >= _x1 && x <= _x2 && y >= _y1 && y <= _y2;

        public bool Contains(QPoint p, bool proper = false) => Contains(p.X(), p.Y(), proper);

        /// <summary>Qt's contains(QRect): the whole rectangle has to be inside, edges included.</summary>
        public bool Contains(QRect other, bool proper = false)
        {
            if (proper)
                return other._x1 > _x1 && other._x2 < _x2 && other._y1 > _y1 && other._y2 < _y2;
            return other._x1 >= _x1 && other._x2 <= _x2 && other._y1 >= _y1 && other._y2 <= _y2;
        }

        public bool Intersects(QRect other)
            => Math.Max(_x1, other._x1) <= Math.Min(_x2, other._x2)
               && Math.Max(_y1, other._y1) <= Math.Min(_y2, other._y2);

        /// <summary>The intersection, which Qt returns as an empty rectangle when there is none.</summary>
        public QRect Intersected(QRect other)
        {
            if (!Intersects(other))
                return default;
            return new QRect(new QPoint(Math.Max(_x1, other._x1), Math.Max(_y1, other._y1)),
                             new QPoint(Math.Min(_x2, other._x2), Math.Min(_y2, other._y2)));
        }

        /// <summary>The bounding rectangle of the two; Qt keeps an empty operand as it is.</summary>
        public QRect United(QRect other)
        {
            if (IsEmpty())
                return other;
            if (other.IsEmpty())
                return this;
            return new QRect(new QPoint(Math.Min(_x1, other._x1), Math.Min(_y1, other._y1)),
                             new QPoint(Math.Max(_x2, other._x2), Math.Max(_y2, other._y2)));
        }

        public QRectF ToRectF() => new QRectF(_x1, _y1, Width(), Height());

        public bool Equals(QRect other)
            => _x1 == other._x1 && _y1 == other._y1 && _x2 == other._x2 && _y2 == other._y2;
        public override bool Equals(object obj) => obj is QRect other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_x1, _y1, _x2, _y2);
        public static bool operator ==(QRect a, QRect b) => a.Equals(b);
        public static bool operator !=(QRect a, QRect b) => !a.Equals(b);

        public override string ToString()
            => "QRect(" + _x1.ToString(CultureInfo.InvariantCulture)
               + ", " + _y1.ToString(CultureInfo.InvariantCulture)
               + ", " + Width().ToString(CultureInfo.InvariantCulture)
               + "x" + Height().ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The real-numbered rectangle; see the note on QPointF above. Qt stores this one as
    /// x,y,width,height, and right()/bottom() are exclusive here (x + width) - unlike the integer
    /// QRect, whose corners are inclusive.
    /// </summary>
    public struct QRectF : IEquatable<QRectF>
    {
        double _x, _y, _width, _height;

        public QRectF(double x, double y, double width, double height)
        {
            _x = x;
            _y = y;
            _width = width;
            _height = height;
        }

        public QRectF(QPointF topLeft, QSizeF size)
        {
            _x = topLeft.X();
            _y = topLeft.Y();
            _width = size.Width();
            _height = size.Height();
        }

        public QRectF(QPointF topLeft, QPointF bottomRight)
        {
            _x = topLeft.X();
            _y = topLeft.Y();
            _width = bottomRight.X() - _x;
            _height = bottomRight.Y() - _y;
        }

        public double X() => _x;
        public double Y() => _y;
        public double Left() => _x;
        public double Top() => _y;
        public double Right() => _x + _width;
        public double Bottom() => _y + _height;

        public double Width() => _width;
        public double Height() => _height;

        public void SetLeft(double left)
        {
            double right = Right();
            _x = left;
            _width = right - left;
        }

        public void SetTop(double top)
        {
            double bottom = Bottom();
            _y = top;
            _height = bottom - top;
        }

        public void SetRight(double right) => _width = right - _x;
        public void SetBottom(double bottom) => _height = bottom - _y;

        public void SetX(double x) => _x = x;
        public void SetY(double y) => _y = y;
        public void SetWidth(double width) => _width = width;
        public void SetHeight(double height) => _height = height;

        public void SetRect(double x, double y, double width, double height)
        {
            _x = x;
            _y = y;
            _width = width;
            _height = height;
        }

        /// <summary>Qt's getRect: x, y, width and height.</summary>
        public void GetRect(out double x, out double y, out double width, out double height)
        {
            x = _x;
            y = _y;
            width = _width;
            height = _height;
        }

        /// <summary>Qt's getCoords: the two corners, where right()/bottom() are exclusive.</summary>
        public void GetCoords(out double x1, out double y1, out double x2, out double y2)
        {
            x1 = _x;
            y1 = _y;
            x2 = _x + _width;
            y2 = _y + _height;
        }

        public QPointF TopLeft() => new QPointF(_x, _y);
        public QPointF TopRight() => new QPointF(_x + _width, _y);
        public QPointF BottomLeft() => new QPointF(_x, _y + _height);
        public QPointF BottomRight() => new QPointF(_x + _width, _y + _height);

        public QPointF Center() => new QPointF(_x + _width / 2.0, _y + _height / 2.0);

        public QSizeF Size() => new QSizeF(_width, _height);

        public void MoveTo(double x, double y)
        {
            _x = x;
            _y = y;
        }

        public void MoveTo(QPointF p) => MoveTo(p.X(), p.Y());

        public void MoveLeft(double left) => _x = left;
        public void MoveTop(double top) => _y = top;
        public void MoveRight(double right) => _x = right - _width;
        public void MoveBottom(double bottom) => _y = bottom - _height;

        public void MoveCenter(QPointF p) => MoveTo(p.X() - _width / 2.0, p.Y() - _height / 2.0);

        public void Translate(double dx, double dy)
        {
            _x += dx;
            _y += dy;
        }

        public void Translate(QPointF offset) => Translate(offset.X(), offset.Y());

        public QRectF Translated(double dx, double dy)
        {
            var r = this;
            r.Translate(dx, dy);
            return r;
        }

        public QRectF Translated(QPointF offset) => Translated(offset.X(), offset.Y());

        public QRectF Adjusted(double dx1, double dy1, double dx2, double dy2)
            => new QRectF(_x + dx1, _y + dy1, _width + dx2 - dx1, _height + dy2 - dy1);

        public QRectF Adjusted(QMarginsF m)
            => Adjusted(-m.Left(), -m.Top(), m.Right(), m.Bottom());

        public QRectF Normalized()
        {
            var r = this;
            if (r._width < 0)
            {
                r._x += r._width;
                r._width = -r._width;
            }
            if (r._height < 0)
            {
                r._y += r._height;
                r._height = -r._height;
            }
            return r;
        }

        public bool IsEmpty() => _width <= 0.0 || _height <= 0.0;

        public bool IsNull() => _width == 0.0 && _height == 0.0;

        public bool IsValid() => _width > 0.0 && _height > 0.0;

        public bool Contains(double x, double y)
            => x >= _x && x <= _x + _width && y >= _y && y <= _y + _height;

        public bool Contains(QPointF p) => Contains(p.X(), p.Y());

        public bool Contains(QRectF other)
            => other._x >= _x && other._x + other._width <= _x + _width
               && other._y >= _y && other._y + other._height <= _y + _height;

        public bool Intersects(QRectF other)
            => Math.Max(_x, other._x) < Math.Min(_x + _width, other._x + other._width)
               && Math.Max(_y, other._y) < Math.Min(_y + _height, other._y + other._height);

        public QRectF Intersected(QRectF other)
        {
            if (!Intersects(other))
                return default;
            double x1 = Math.Max(_x, other._x), y1 = Math.Max(_y, other._y);
            double x2 = Math.Min(_x + _width, other._x + other._width);
            double y2 = Math.Min(_y + _height, other._y + other._height);
            return new QRectF(x1, y1, x2 - x1, y2 - y1);
        }

        public QRectF United(QRectF other)
        {
            if (IsEmpty())
                return other;
            if (other.IsEmpty())
                return this;
            double x1 = Math.Min(_x, other._x), y1 = Math.Min(_y, other._y);
            double x2 = Math.Max(_x + _width, other._x + other._width);
            double y2 = Math.Max(_y + _height, other._y + other._height);
            return new QRectF(x1, y1, x2 - x1, y2 - y1);
        }

        /// <summary>Rounds the corners, as Qt's QRectF::toRect() does.</summary>
        public QRect ToRect()
            => new QRect(QtMath.Round(_x), QtMath.Round(_y), QtMath.Round(_width), QtMath.Round(_height));

        public bool Equals(QRectF other)
            => _x == other._x && _y == other._y && _width == other._width && _height == other._height;
        public override bool Equals(object obj) => obj is QRectF other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_x, _y, _width, _height);
        public static bool operator ==(QRectF a, QRectF b) => a.Equals(b);
        public static bool operator !=(QRectF a, QRectF b) => !a.Equals(b);

        public override string ToString()
            => "QRectF(" + _x.ToString(CultureInfo.InvariantCulture)
               + ", " + _y.ToString(CultureInfo.InvariantCulture)
               + ", " + _width.ToString(CultureInfo.InvariantCulture)
               + "x" + _height.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Qt's rounding, shared by the structs that have to come back to integers (QPointF::toPoint,
    /// QRectF::toRect, QLineF::toLine). qRound rounds halves away from zero, where Math.Round would
    /// round them to even.
    /// </summary>
    internal static class QtMath
    {
        internal static int Round(double value)
            => (int)(value < 0 ? Math.Ceiling(value - 0.5) : Math.Floor(value + 0.5));
    }
}
