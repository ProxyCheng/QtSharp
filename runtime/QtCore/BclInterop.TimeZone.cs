using System;
using System.Globalization;
using System.Text;
using QtSharp.Runtime;

// QTimeZone and System.TimeZoneInfo both name a UTC offset and its daylight-saving rules, but they
// use different id dialects: Qt is IANA-only ("Asia/Shanghai"), while .NET on Windows uses the
// Windows database ("China Standard Time") and on Unix the IANA one. The bridge is Qt's own
// translation tables, so a converted zone keeps the same rules instead of being re-derived:
//
//   Qt -> .NET: QTimeZone.IanaIdToWindowsId(iana) -> TimeZoneInfo.FindSystemTimeZoneById(...)
//   .NET -> Qt: QTimeZone.WindowsIdToDefaultIanaId(windowsId) -> QTimeZone(iana)
//
// A fixed-offset zone has no rules to keep, so it crosses as an offset:
// QTimeZone.FixedSecondsAheadOfUtc() <-> TimeZoneInfo.BaseUtcOffset (CreateCustomTimeZone on the
// .NET side, FromSecondsAheadOfUtc on the Qt side). UTC itself is TimeZoneInfo.Utc and
// QTimeZone.Utc(), and an offset-style Qt id ("UTC+05:30") is treated as an offset, not looked up
// as a zone name.
//
// The conversions are explicit (a constructor and ToTimeZoneInfo()), with no operators. Invalid
// zones throw InvalidOperationException. A zone that cannot be represented - a fixed offset with
// sub-minute seconds, a custom TimeZoneInfo whose adjustment rules have no Qt id, a zone missing
// from this host's database - throws NotSupportedException rather than silently becoming a
// different zone with the same-looking clock. Qt's tzdata and this host's database can still be
// different versions; the ids and rules are copied, not translated or merged.
namespace QtSharp.QtCore
{
    public partial class QTimeZone
    {
        /// <summary>
        /// Builds a QTimeZone with the same id and rules as a BCL <see cref="TimeZoneInfo"/>.
        /// </summary>
        /// <param name="timeZone">The zone to copy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// A fixed-offset zone's offset is outside QTimeZone's -14:00..+14:00 range.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// The zone has no Qt id (a custom id with adjustment rules), or Qt rejects the id derived
        /// from it.
        /// </exception>
        /// <remarks>
        /// <see cref="TimeZoneInfo.Utc"/> becomes <see cref="QTimeZone.Utc"/>. Any other zone whose
        /// id <see cref="QTimeZone.IsTimeZoneIdAvailable(byte[])"/> accepts is used directly; a
        /// Windows id goes through <see cref="QTimeZone.WindowsIdToDefaultIanaId(QByteArray)"/>.
        /// A zone with neither an available id nor adjustment rules is a fixed offset and becomes
        /// <see cref="QTimeZone.FromSecondsAheadOfUtc"/>. A zone with rules but no available id is
        /// an error, because QTimeZone cannot carry arbitrary transition rules.
        /// </remarks>
        public QTimeZone(TimeZoneInfo timeZone)
        {
            if (timeZone is null)
                throw new ArgumentNullException(nameof(timeZone));

            // Create the Qt zone first, then take its owned handle over. A constructor cannot
            // choose a different generated constructor per branch, so this is the same
            // handle-adoption the QVariant constructor uses.
            QTimeZone created = CreateTimeZone(timeZone);
            IntPtr handle = created.Handle;
            ApiRuntime.Forget(handle, created);
            created.__handle = IntPtr.Zero;
            Handle = handle;
            ApiRuntime.Register(Handle, this);
            __ownsNative = true;
            __managedSubclass = true;
        }

        /// <summary>
        /// The zone as a BCL <see cref="TimeZoneInfo"/> with the same id and rules.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The QTimeZone is null/invalid, has no id, or has a fixed offset outside
        /// <see cref="TimeZoneInfo"/>'s -14:00..+14:00 range.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// A fixed offset is not a whole number of minutes, or a named zone is missing from this
        /// host's time-zone database.
        /// </exception>
        /// <remarks>
        /// UTC becomes <see cref="TimeZoneInfo.Utc"/>; any other fixed-offset zone becomes a
        /// custom zone made by <see cref="TimeZoneInfo.CreateCustomTimeZone(string, TimeSpan, string, string)"/>.
        /// A named zone is looked up by its Windows id from
        /// <see cref="QTimeZone.IanaIdToWindowsId(QByteArray)"/>, falling back to the IANA id
        /// itself (which .NET accepts on Unix, and on Windows with ICU). The returned zone is the
        /// host's own object; the QTimeZone keeps its own copy and is unaffected.
        /// </remarks>
        public TimeZoneInfo ToTimeZoneInfo()
        {
            if (!IsValid())
                throw new InvalidOperationException(
                    "The QTimeZone is null or invalid and has no System.TimeZoneInfo equivalent.");

            string ianaId = IdOf(this);

            // Qt 6 returns QTimeZone.Utc() with Qt::TimeSpec.TimeZone (id "UTC"), and an offset
            // zone of zero with Qt::TimeSpec.UTC; both mean TimeZoneInfo.Utc.
            if (string.Equals(ianaId, "UTC", StringComparison.Ordinal)
                || TimeSpec() == Qt.TimeSpec.UTC)
                return TimeZoneInfo.Utc;

            if (IsUtcOrFixedOffset())
            {
                int seconds = FixedSecondsAheadOfUtc();
                if (seconds % 60 != 0)
                    throw new NotSupportedException(
                        $"The QTimeZone fixed offset ({seconds} s) is not a whole number of "
                        + "minutes; System.TimeZoneInfo only stores whole-minute offsets.");
                if (seconds < -14 * 3600 || seconds > 14 * 3600)
                    throw new InvalidOperationException(
                        $"The QTimeZone fixed offset ({seconds} s) is outside System.TimeZoneInfo's "
                        + "-14:00..+14:00 range.");

                string fixedId = ianaId;
                if (string.IsNullOrEmpty(fixedId))
                    fixedId = FormattableString.Invariant(
                        $"UTC{seconds / 3600:+00;-00}:{Math.Abs(seconds / 60) % 60:00}");
                return TimeZoneInfo.CreateCustomTimeZone(
                    fixedId, TimeSpan.FromSeconds(seconds), fixedId, fixedId);
            }

            if (ianaId.Length == 0)
                throw new InvalidOperationException(
                    "The QTimeZone is valid but has no id, so it has no System.TimeZoneInfo "
                    + "equivalent.");

            string windowsId = IanaIdToWindows(ianaId);
            if (windowsId.Length > 0
                && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out TimeZoneInfo mapped))
                return mapped;
            if (TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out mapped))
                return mapped;

            throw new NotSupportedException(
                $"The QTimeZone '{ianaId}' has no System.TimeZoneInfo equivalent on this host "
                + $"(Qt's Windows id is '{(windowsId.Length == 0 ? "<none>" : windowsId)}').");
        }

        /// <summary>Creates the owned Qt zone for a <see cref="TimeZoneInfo"/>; the caller adopts it.</summary>
        static QTimeZone CreateTimeZone(TimeZoneInfo timeZone)
        {
            if (string.Equals(timeZone.Id, TimeZoneInfo.Utc.Id, StringComparison.Ordinal))
                return QTimeZone.Utc();

            string id = timeZone.Id ?? string.Empty;
            string ianaId = null;

            // "UTC+05:30" is what Qt calls its own fixed-offset ids, and QTimeZone accepts that
            // spelling as an available id - but constructing from it yields a named zone, not the
            // offset zone that id came from. An offset-style label is therefore an offset, not a
            // named zone, and is handled by the fixed-offset branch below.
            if (id.Length > 0 && !IsUtcOffsetId(id))
            {
                if (QTimeZone.IsTimeZoneIdAvailable(Encoding.UTF8.GetBytes(id)))
                {
                    ianaId = id;
                }
                else
                {
                    string mapped = WindowsToDefaultIana(id);
                    if (mapped.Length > 0
                        && QTimeZone.IsTimeZoneIdAvailable(Encoding.UTF8.GetBytes(mapped)))
                        ianaId = mapped;
                }
            }

            if (ianaId is not null)
            {
                using var bytes = new QByteArray(ianaId);
                QTimeZone named = new QTimeZone(bytes);
                if (named.IsValid())
                    return named;
                named.Dispose();
                throw new NotSupportedException(
                    $"Qt does not know the time-zone id '{ianaId}' derived from the "
                    + $"System.TimeZoneInfo '{id}'.");
            }

            TimeZoneInfo.AdjustmentRule[] rules = timeZone.GetAdjustmentRules();
            if (!timeZone.SupportsDaylightSavingTime && rules.Length == 0)
            {
                var offset = timeZone.BaseUtcOffset;
                int seconds = checked((int)(offset.Ticks / TimeSpan.TicksPerSecond));
                QTimeZone fixedZone = QTimeZone.FromSecondsAheadOfUtc(seconds);
                if (fixedZone.IsValid())
                    return fixedZone;
                fixedZone.Dispose();
                throw new InvalidOperationException(
                    $"The System.TimeZoneInfo offset ({offset}) is outside QTimeZone's "
                    + "-14:00..+14:00 range.");
            }

            throw new NotSupportedException(
                $"The System.TimeZoneInfo '{id}' has custom adjustment rules and no time-zone id "
                + "that Qt knows; QTimeZone cannot represent arbitrary transition rules.");
        }

        /// <summary>The zone's IANA id as text; never null.</summary>
        static string IdOf(QTimeZone zone)
        {
            using QByteArray id = zone.Id();
            return id is null ? string.Empty : Encoding.UTF8.GetString(id.ToArray());
        }

        /// <summary>
        /// Whether an id is Qt's fixed-offset spelling ("UTC+05:30"/"UTC-08:00") rather than a
        /// named-zone id. Qt's own fixed zones use it, and it must not be looked up as a name.
        /// </summary>
        static bool IsUtcOffsetId(string id)
            => id.StartsWith("UTC+", StringComparison.Ordinal)
            || id.StartsWith("UTC-", StringComparison.Ordinal);

        /// <summary>Qt's Windows id for an IANA id, or the empty string when there is none.</summary>
        static string IanaIdToWindows(string ianaId)
        {
            using var bytes = new QByteArray(ianaId);
            using QByteArray windowsId = QTimeZone.IanaIdToWindowsId(bytes);
            return windowsId is null ? string.Empty : Encoding.UTF8.GetString(windowsId.ToArray());
        }

        /// <summary>Qt's default IANA id for a Windows id, or the empty string when there is none.</summary>
        static string WindowsToDefaultIana(string windowsId)
        {
            using var bytes = new QByteArray(windowsId);
            using QByteArray ianaId = QTimeZone.WindowsIdToDefaultIanaId(bytes);
            return ianaId is null ? string.Empty : Encoding.UTF8.GetString(ianaId.ToArray());
        }
    }
}
