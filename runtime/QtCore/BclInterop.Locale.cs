using System;
using System.Globalization;

// QLocale and System.Globalization.CultureInfo both identify a language/territory combination, but
// they are not the same formatter: Qt formats with its own CLDR data and .NET with ICU, so the
// converted locale is an identity only. Number grouping, date patterns, currency spelling and the
// first day of the week can differ even for the same id; this conversion carries the name, not the
// formatted output. The constructor and ToCultureInfo() are explicit, with no operators, the same
// shape as the other BCL interop files.
//
// The ids cross through the locale's dashed name ("en-US"), which both sides understand.
// QLocale.C() (Name() "C") is CultureInfo.InvariantCulture, and the invariant culture's empty name
// becomes QLocale.C(). QLocale.Bcp47Name() is deliberately not the source: Qt omits the territory
// when it is the language's default territory ("en_US" -> "en"), which would lose the identity
// even though ICU would resolve "en" back to en-US. Name(Dash) keeps the full language/territory.
namespace QtSharp.QtCore
{
    public partial class QLocale
    {
        /// <summary>
        /// Builds a QLocale that identifies the same language/territory as a
        /// <see cref="CultureInfo"/>.
        /// </summary>
        /// <param name="culture">The culture whose name is carried.</param>
        /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
        /// <remarks>
        /// The name crosses in its dashed spelling (<see cref="CultureInfo.Name"/>); the invariant
        /// culture has no name, so it becomes <c>QLocale.C()</c>. Only the identity crosses: see
        /// the file comment about CLDR versus ICU formatting. A culture whose name includes script
        /// or variant subtags is handed to Qt as-is.
        /// </remarks>
        public QLocale(CultureInfo culture) : this(ToLocaleName(culture))
        {
        }

        /// <summary>
        /// The culture that identifies the same language/territory as this QLocale.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// The dashed name Qt reports is not known to this host's <see cref="CultureInfo"/> data
        /// (for example a locale Qt's CLDR table has but ICU does not).
        /// </exception>
        /// <remarks>
        /// <c>QLocale.C()</c> comes back as <see cref="CultureInfo.InvariantCulture"/>; every other
        /// locale uses <see cref="QLocale.Name(QLocale.TagSeparator)"/> with the dash separator and
        /// <see cref="CultureInfo.GetCultureInfo(string)"/>. <see cref="QLocale.Bcp47Name(QLocale.TagSeparator)"/>
        /// is not used because it omits a default territory ("en_US" becomes "en"), which would
        /// make two different identities look alike. This is an identity conversion: Qt and .NET
        /// can format the same locale differently, because Qt uses its own CLDR data and .NET uses
        /// ICU.
        /// </remarks>
        public CultureInfo ToCultureInfo()
        {
            string name = Name();
            if (string.Equals(name, "C", StringComparison.OrdinalIgnoreCase))
                return CultureInfo.InvariantCulture;

            string cultureName = Name(QLocale.TagSeparator.Dash);
            if (string.IsNullOrEmpty(cultureName))
                cultureName = Bcp47Name(QLocale.TagSeparator.Dash);
            try
            {
                return CultureInfo.GetCultureInfo(cultureName);
            }
            catch (CultureNotFoundException e)
            {
                throw new NotSupportedException(
                    $"The QLocale '{name}' (dashed name '{cultureName}') is not known to this "
                    + "host's System.Globalization.CultureInfo data.", e);
            }
        }

        /// <summary>The BCP 47 name to hand Qt, or "C" for the invariant culture.</summary>
        static string ToLocaleName(CultureInfo culture)
        {
            if (culture is null)
                throw new ArgumentNullException(nameof(culture));
            return culture.Name.Length == 0 ? "C" : culture.Name;
        }
    }
}
