using System;
using System.Text;

namespace QtSharp.Tests
{
    /// <summary>
    /// QTimeZone and System.TimeZoneInfo: a named zone through the IANA/Windows id bridge, a
    /// fixed-offset zone, UTC, and the zones that cannot be represented.
    /// </summary>
    [Test("test-timezoneinterop")]
    public static class TimeZoneInteropTests
    {
        public static int Run(TestContext context)
        {
            // ------------------------------------------------------- a named zone, both directions
            using (var zone = new QTimeZone(new QByteArray("Asia/Shanghai")))
            {
                context.Check(zone.IsValid(), "QTimeZone(Asia/Shanghai) is valid");
                TimeZoneInfo info = zone.ToTimeZoneInfo();
                context.Check(info.BaseUtcOffset == TimeSpan.FromHours(8),
                    $"Asia/Shanghai -> TimeZoneInfo keeps its offset ({info.BaseUtcOffset})");

                using (var back = new QTimeZone(info))
                {
                    context.Check(back.IsValid(), "TimeZoneInfo -> QTimeZone is valid");
                    context.Check(IdText(back) == "Asia/Shanghai",
                        $"and keeps the IANA id ({IdText(back)})");
                }
            }

            // ------------------------------------------------------- a fixed-offset zone
            using (var zone = QTimeZone.FromSecondsAheadOfUtc(5 * 3600 + 30 * 60))
            {
                context.Check(zone.IsUtcOrFixedOffset(),
                    "FromSecondsAheadOfUtc(19800) is a fixed-offset zone");
                TimeZoneInfo info = zone.ToTimeZoneInfo();
                context.Check(info.BaseUtcOffset == TimeSpan.FromMinutes(330),
                    $"Qt fixed offset -> TimeZoneInfo offset ({info.BaseUtcOffset})");
                context.Check(info.GetAdjustmentRules().Length == 0,
                    "the converted fixed zone has no adjustment rules");

                using (var back = new QTimeZone(info))
                {
                    context.Check(back.IsUtcOrFixedOffset(),
                        "TimeZoneInfo fixed offset -> QTimeZone is a fixed-offset zone");
                    context.Check(back.FixedSecondsAheadOfUtc() == 19800,
                        $"and keeps the offset ({back.FixedSecondsAheadOfUtc()} s)");
                }
            }

            // ------------------------------------------------------- UTC
            using (var zone = QTimeZone.Utc())
            {
                context.Check(IdText(zone) == "UTC",
                    $"QTimeZone.Utc() has the UTC id ({IdText(zone)})");
                TimeZoneInfo info = zone.ToTimeZoneInfo();
                context.Check(info.Id == TimeZoneInfo.Utc.Id,
                    $"QTimeZone.Utc() -> TimeZoneInfo.Utc ({info.Id})");
                using (var back = new QTimeZone(info))
                {
                    context.Check(back.ToTimeZoneInfo().Id == TimeZoneInfo.Utc.Id,
                        $"and TimeZoneInfo.Utc -> QTimeZone -> TimeZoneInfo.Utc ({IdText(back)})");
                }
            }

            // ------------------------------------------------------- the system zone round-trips
            using (var zone = QTimeZone.SystemTimeZone())
            {
                TimeZoneInfo info = zone.ToTimeZoneInfo();
                using (var back = new QTimeZone(info))
                {
                    context.Check(back.IsValid(),
                        $"the system zone converts to TimeZoneInfo and back ({IdText(back)})");
                }
            }

            // ------------------------------------------------------- sub-minute offsets throw
            using (var zone = QTimeZone.FromSecondsAheadOfUtc(90))
            {
                try
                {
                    TimeZoneInfo converted = zone.ToTimeZoneInfo();
                    context.Check(false, $"a 90-second fixed offset should not convert (got {converted.Id})");
                }
                catch (NotSupportedException)
                {
                    context.Check(true, "a sub-minute fixed offset throws NotSupportedException");
                }
            }

            // ------------------------------------------------------- custom rules with no Qt id throw
            var custom = CreateCustomDaylightZone();
            try
            {
                using var converted = new QTimeZone(custom);
                context.Check(false, $"a custom DST zone should not convert (got {IdText(converted)})");
            }
            catch (NotSupportedException)
            {
                context.Check(true, "a custom TimeZoneInfo with unattached DST rules throws NotSupportedException");
            }

            // ------------------------------------------------------- invalid input
            using (var invalid = new QTimeZone())
            {
                context.Check(!invalid.IsValid(), "the default QTimeZone is invalid");
                try
                {
                    TimeZoneInfo converted = invalid.ToTimeZoneInfo();
                    context.Check(false, $"an invalid QTimeZone should not convert (got {converted.Id})");
                }
                catch (InvalidOperationException)
                {
                    context.Check(true, "an invalid QTimeZone throws InvalidOperationException");
                }
            }

            try
            {
                using var converted = new QTimeZone((TimeZoneInfo)null);
                context.Check(false, "a null TimeZoneInfo should not convert to QTimeZone");
            }
            catch (ArgumentNullException)
            {
                context.Check(true, "a null TimeZoneInfo throws ArgumentNullException");
            }

            return context.ExitCode;
        }

        static string IdText(QTimeZone zone)
        {
            using QByteArray id = zone.Id();
            return id is null ? string.Empty : Encoding.UTF8.GetString(id.ToArray());
        }

        static TimeZoneInfo CreateCustomDaylightZone()
        {
            var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
            var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue, DateTime.MaxValue, TimeSpan.FromHours(1), start, end);
            return TimeZoneInfo.CreateCustomTimeZone(
                "QtSharp Custom DST", TimeSpan.Zero,
                "QtSharp Custom DST", "QtSharp Standard", "QtSharp Daylight", new[] { rule });
        }
    }
}
