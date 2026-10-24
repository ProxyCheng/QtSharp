using System;
using QtSharp.Runtime;

#pragma warning disable 660, 661 // the generated classes' ==/!= are wrapper identity, like the generated file

// Explicit conversions between Qt's value types and the BCL types that mean the same thing.
//
// PyQt/PySide do not replace QDateTime with datetime either: they add an explicit constructor
// (QDateTime(datetime)) and a named conversion (toPyDateTime()), and keep the two type systems
// apart everywhere else. This file is the same idea in C#:
//
//     var qdt = new QDateTime(dateTime);     DateTime / DateTimeOffset -> QDateTime
//     DateTime back = qdt.ToDateTime();      and back
//     DateOnly d = qdate.ToDateOnly();       QDate  <-> System.DateOnly
//     TimeOnly t = qtime.ToTimeOnly();       QTime  <-> System.TimeOnly
//     byte[] bytes = qba.ToArray();          QByteArray -> byte[] (the ctor already takes byte[])
//
// There are deliberately no implicit operators. The generated == on a handle class compares
// wrapper identity (see the generated QDateTime), not the instant, so with an implicit conversion
// `qdt == dateTime` would compile and silently be false even for the same moment. Explicit
// conversions make every crossing visible; the two explicit operators below exist only because a
// cast is the idiomatic way to spell ToDateTime()/ToDateTimeOffset() at a call site.
//
// Mapping (Qt::TimeSpec <-> DateTimeKind):
//
//   Qt::UTC           -> DateTimeKind.Utc          Kind == Utc          -> Qt::UTC
//   Qt::LocalTime     -> DateTimeKind.Local        Kind == Local        -> Qt::LocalTime
//   Qt::OffsetFromUTC -> Kind Unspecified          Kind == Unspecified  -> Qt::LocalTime
//   Qt::TimeZone      -> Kind Unspecified
//
// A DateTimeOffset always maps to/from Qt::OffsetFromUTC (the offset is part of its value).
// Qt stores milliseconds, so sub-millisecond ticks are truncated on the way in. A QDateTime whose
// instant or offset cannot be represented by System.DateTime/DateTimeOffset throws
// InvalidOperationException instead of silently wrapping; an invalid (null) QDateTime does too.
// A QDateTime in a named zone keeps its wall clock in ToDateTime(); ToDateTimeOffset() is the
// conversion that keeps the offset as well (the zone rules themselves have no BCL equivalent).
namespace QtSharp.QtCore
{
    public partial class QDateTime
    {
        /// <summary>Builds a QDateTime from a BCL timestamp. Utc stays UTC; Local and Unspecified are local time.</summary>
        public QDateTime(DateTime value)
            : this(new QDate(value.Year, value.Month, value.Day),
                   new QTime(value.Hour, value.Minute, value.Second, value.Millisecond),
                   value.Kind == DateTimeKind.Utc ? Qt.TimeSpec.UTC : Qt.TimeSpec.LocalTime)
        {
        }

        /// <summary>Builds a QDateTime that keeps the DateTimeOffset's wall clock and UTC offset.</summary>
        public QDateTime(DateTimeOffset value)
            : this(new QDate(value.Year, value.Month, value.Day),
                   new QTime(value.Hour, value.Minute, value.Second, value.Millisecond),
                   Qt.TimeSpec.OffsetFromUTC,
                   (int)value.Offset.TotalSeconds)
        {
        }

        /// <summary>
        /// The wall-clock DateTime. UTC comes back Kind.Utc; local time Kind.Local; an offset or
        /// zone datetime Kind.Unspecified (use ToDateTimeOffset to keep the offset). A QDateTime
        /// outside System.DateTime's range throws InvalidOperationException.
        /// </summary>
        public DateTime ToDateTime()
        {
            DateTimeOffset instant = ToInstant();
            switch (TimeSpec())
            {
                case Qt.TimeSpec.UTC:
                    return instant.UtcDateTime;
                case Qt.TimeSpec.LocalTime:
                    return ToLocal(instant).LocalDateTime;
                default: // OffsetFromUTC and TimeZone: keep the wall clock and the stored offset
                    return WithOffset(instant, OffsetFromUtc()).DateTime;
            }
        }

        /// <summary>
        /// The instant and offset as a DateTimeOffset. Local time uses the machine's zone; an
        /// offset or zone datetime keeps its stored offset. An invalid or out-of-range QDateTime
        /// throws InvalidOperationException.
        /// </summary>
        public DateTimeOffset ToDateTimeOffset()
        {
            DateTimeOffset instant = ToInstant();
            switch (TimeSpec())
            {
                case Qt.TimeSpec.UTC:
                    return instant;
                case Qt.TimeSpec.LocalTime:
                    return ToLocal(instant);
                default:
                    return WithOffset(instant, OffsetFromUtc());
            }
        }

        public static explicit operator DateTime(QDateTime value)
            => (value ?? throw new ArgumentNullException(nameof(value))).ToDateTime();

        public static explicit operator DateTimeOffset(QDateTime value)
            => (value ?? throw new ArgumentNullException(nameof(value))).ToDateTimeOffset();

        DateTimeOffset ToInstant()
        {
            if (!IsValid())
                throw new InvalidOperationException(
                    "The QDateTime is null or invalid and has no System.DateTime equivalent.");
            long msecs = ToMSecsSinceEpoch();
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(msecs);
            }
            catch (ArgumentException e)
            {
                throw new InvalidOperationException(
                    $"The QDateTime instant ({msecs} ms since 1970-01-01T00:00:00Z) is outside "
                    + "System.DateTime's range (0001-01-01 through 9999-12-31).", e);
            }
        }

        static DateTimeOffset ToLocal(DateTimeOffset instant)
        {
            try
            {
                return instant.ToLocalTime();
            }
            catch (ArgumentException e)
            {
                throw new InvalidOperationException(
                    "The QDateTime instant cannot be represented in the local time zone "
                    + "(outside System.DateTime's range).", e);
            }
        }

        static DateTimeOffset WithOffset(DateTimeOffset instant, int offsetSeconds)
        {
            try
            {
                return instant.ToOffset(TimeSpan.FromSeconds(offsetSeconds));
            }
            catch (ArgumentException e)
            {
                throw new InvalidOperationException(
                    $"The QDateTime offset ({offsetSeconds} s) cannot be represented by "
                    + "System.DateTimeOffset (offset within -14:00..+14:00 and in whole minutes, "
                    + "clock within years 1..9999).", e);
            }
        }
    }

    public partial struct QDate
    {
        /// <summary>The same calendar date as a BCL DateOnly (the Gregorian default calendar).</summary>
        public QDate(DateOnly value) : this(value.Year, value.Month, value.Day)
        {
        }

        /// <summary>
        /// The date as a BCL DateOnly. A null/invalid QDate, or a valid Qt date outside years
        /// 1..9999, throws InvalidOperationException rather than returning a wrapped default.
        /// </summary>
        public DateOnly ToDateOnly()
        {
            if (!IsValid())
                throw new InvalidOperationException("The QDate is null or invalid.");
            GetDate(out int year, out int month, out int day);
            if (year < 1 || year > 9999)
                throw new InvalidOperationException(
                    $"The QDate year ({year}) is outside System.DateOnly's range (1 through 9999).");
            return new DateOnly(year, month, day);
        }
    }

    public partial struct QTime
    {
        /// <summary>The same time of day as a BCL TimeOnly; sub-millisecond ticks are truncated.</summary>
        public QTime(TimeOnly value) : this(value.Hour, value.Minute, value.Second, value.Millisecond)
        {
        }

        /// <summary>The time of day as a BCL TimeOnly; an invalid QTime throws InvalidOperationException.</summary>
        public TimeOnly ToTimeOnly()
        {
            // Defensive: today no invalid QTime crosses the P/Invoke boundary (the POD carries
            // msecsSinceStartOfDay(), which maps Qt's null time to 0), but the range is still checked.
            if (!IsValid())
                throw new InvalidOperationException("The QTime is null or invalid.");
            return new TimeOnly(Hour(), Minute(), Second(), Msec());
        }
    }

    public partial class QByteArray
    {
        /// <summary>
        /// Copies the bytes out, binary-safe (embedded NUL and high bytes included). The wrapper
        /// keeps ownership of its QByteArray; unlike ApiRuntime.ToManagedBytes this never frees it.
        /// A null or empty QByteArray both come back as a zero-length array.
        /// </summary>
        public byte[] ToArray() => ApiRuntime.CopyBytes(Handle);
    }
}
