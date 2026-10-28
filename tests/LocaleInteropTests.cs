using System;
using System.Globalization;

namespace QtSharp.Tests
{
    /// <summary>
    /// QLocale and System.Globalization.CultureInfo: the identity crosses (dashed names and the
    /// invariant/C special case), while formatting is documented to remain each library's own.
    /// </summary>
    [Test("test-localeinterop")]
    public static class LocaleInteropTests
    {
        public static int Run(TestContext context)
        {
            // ------------------------------------------------------- a real culture, both directions
            var enUs = CultureInfo.GetCultureInfo("en-US");
            using (var locale = new QLocale(enUs))
            {
                context.Check(locale.Name() == "en_US",
                    $"CultureInfo -> QLocale carries the name ({locale.Name()})");
                context.Check(locale.Name(QLocale.TagSeparator.Dash) == "en-US",
                    $"and the dashed spelling ({locale.Name(QLocale.TagSeparator.Dash)})");
                CultureInfo back = locale.ToCultureInfo();
                context.Check(back.Name == "en-US",
                    $"and QLocale -> CultureInfo returns the same culture ({back.Name})");
            }

            // Qt stores en_US but QLocale.Bcp47Name() drops the default territory; the conversion
            // must still return en-US, not the bare language.
            using (var locale = new QLocale("en_US"))
            {
                context.Check(locale.Bcp47Name(QLocale.TagSeparator.Dash) == "en",
                    $"Bcp47Name() omits the default territory ({locale.Bcp47Name(QLocale.TagSeparator.Dash)})");
                context.Check(locale.ToCultureInfo().Name == "en-US",
                    $"yet ToCultureInfo keeps the territory ({locale.ToCultureInfo().Name})");
            }

            // A Qt spelling with an underscore still crosses with dashes.
            using (var locale = new QLocale("de_DE"))
            {
                context.Check(locale.Name() == "de_DE",
                    $"the undashed Qt name is kept ({locale.Name()})");
                context.Check(locale.ToCultureInfo().Name == "de-DE",
                    $"and ToCultureInfo uses the dashed spelling ({locale.ToCultureInfo().Name})");
            }

            // ------------------------------------------------------- C <-> invariant
            using (var locale = new QLocale(CultureInfo.InvariantCulture))
            {
                context.Check(locale.Name() == "C",
                    $"the invariant culture becomes the C locale ({locale.Name()})");
                context.Check(locale.ToCultureInfo().Equals(CultureInfo.InvariantCulture),
                    "and the C locale comes back as CultureInfo.InvariantCulture");
            }

            using (var locale = QLocale.C())
            {
                context.Check(locale.ToCultureInfo().Name.Length == 0,
                    $"QLocale.C() -> CultureInfo.InvariantCulture ({locale.ToCultureInfo().Name})");
            }

            // ------------------------------------------------------- identity only, not formatting
            // The locales name the same language/territory; their formatted output is each
            // library's own (Qt's CLDR data versus .NET's ICU), so this test compares names only.
            using (var locale = new QLocale(CultureInfo.GetCultureInfo("fr-FR")))
            {
                context.Check(locale.ToCultureInfo().Name == "fr-FR",
                    $"fr-FR identity round-trips ({locale.ToCultureInfo().Name})");
            }

            // ------------------------------------------------------- invalid input
            try
            {
                using var locale = new QLocale((CultureInfo)null);
                context.Check(false, "a null CultureInfo should not convert to QLocale");
            }
            catch (ArgumentNullException)
            {
                context.Check(true, "a null CultureInfo throws ArgumentNullException");
            }

            return context.ExitCode;
        }
    }
}
