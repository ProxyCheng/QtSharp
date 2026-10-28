using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QtSharp.Tests
{
    /// <summary>
    /// The explicit conversion between System.Text.RegularExpressions.Regex and
    /// QRegularExpression: the pattern and the options both engines spell the same way cross;
    /// everything else is a documented failure rather than a silent semantic change.
    /// </summary>
    [Test("test-regexinterop")]
    public static class RegexInteropTests
    {
        public static int Run(TestContext context)
        {
            // ------------------------------------------------------- round trip, both ways
            // Named and numbered captures, so a conversion that lost the pattern or a capture is
            // visible in both engines' own match APIs.
            const string Pattern = @"^(?<year>\d{4})-(\d{2})-(\d{2})$";
            var regex = new Regex(Pattern, RegexOptions.Multiline);
            using (var expression = regex.ToQRegularExpression())
            {
                context.Check(expression.IsValid(), "Regex -> QRegularExpression is a valid expression");
                context.Check(expression.Pattern() == Pattern, $"the pattern is carried ('{expression.Pattern()}')");
                context.Check(expression.patternOptions() == QRegularExpression.PatternOptions.MultilineOption,
                    $"and the options are carried ({expression.patternOptions()})");

                // Qt evaluates it with PCRE2: the named and numbered groups are all there.
                using (var qtMatch = expression.Match("2026-10-28"))
                {
                    context.Check(qtMatch.HasMatch()
                          && qtMatch.Captured("year") == "2026"
                          && qtMatch.Captured(2) == "10"
                          && qtMatch.Captured(3) == "28",
                        "Qt's PCRE2 match sees the named and numbered captures");
                }
                context.Check(Array.IndexOf(expression.NamedCaptureGroups(), "year") >= 0,
                    "NamedCaptureGroups carries the named group");

                // A real Qt consumer: the validator compiles and evaluates the converted
                // expression, it does not look at the managed Regex at all.
                using (var validator = new QRegularExpressionValidator(expression))
                {
                    string accepted = "2026-10-28";
                    int acceptedPos = 0;
                    context.Check(validator.Validate(ref accepted, ref acceptedPos) == QValidator.State.Acceptable,
                        "QRegularExpressionValidator accepts a matching input");

                    string rejected = "2026-1";
                    int rejectedPos = 0;
                    context.Check(validator.Validate(ref rejected, ref rejectedPos) != QValidator.State.Acceptable,
                        "and rejects a non-matching one");
                }

                // ... and back: the .NET engine gets the same pattern and options.
                var roundTripped = expression.ToRegex();
                context.Check(roundTripped.ToString() == Pattern, "QRegularExpression -> Regex keeps the pattern");
                context.Check(roundTripped.Options == RegexOptions.Multiline,
                    $"and the options ({roundTripped.Options})");
                var match = roundTripped.Match("2026-10-28");
                // .NET numbers unnamed groups before named ones, so the month is group 1 here
                // while PCRE2 (above) numbers the named group first. The captures are the same;
                // the numbering is an engine detail, which is exactly what this conversion does
                // not translate.
                context.Check(match.Success
                      && match.Groups["year"].Value == "2026"
                      && match.Groups[1].Value == "10"
                      && match.Groups[2].Value == "28",
                    "the .NET match sees the named and numbered captures");
            }

            // ------------------------------------------------------- a Qt-returned expression
            // The option carry has to survive a round trip through Qt's own object, not just the
            // managed wrapper the conversion built.
            using (var proxy = new QSortFilterProxyModel())
            using (var filter = new QRegularExpression("^ab+c$",
                       QRegularExpression.PatternOptions.CaseInsensitiveOption
                       | QRegularExpression.PatternOptions.MultilineOption))
            {
                proxy.SetFilterRegularExpression(filter);
                using (var returned = proxy.filterRegularExpression())
                {
                    var converted = returned.ToRegex();
                    context.Check(converted.ToString() == "^ab+c$",
                        $"a proxy-returned expression carries its pattern ('{converted.ToString()}')");
                    context.Check(converted.Options == (RegexOptions.IgnoreCase | RegexOptions.Multiline),
                        $"and its options ({converted.Options})");
                }
            }

            // ------------------------------------------------------- every mappable option
            var mappable = new[]
            {
                (Qt: QRegularExpression.PatternOptions.CaseInsensitiveOption,
                 Net: RegexOptions.IgnoreCase, Name: "CaseInsensitive/IgnoreCase"),
                (Qt: QRegularExpression.PatternOptions.MultilineOption,
                 Net: RegexOptions.Multiline, Name: "Multiline/Multiline"),
                (Qt: QRegularExpression.PatternOptions.DotMatchesEverythingOption,
                 Net: RegexOptions.Singleline, Name: "DotMatchesEverything/Singleline"),
                (Qt: QRegularExpression.PatternOptions.ExtendedPatternSyntaxOption,
                 Net: RegexOptions.IgnorePatternWhitespace, Name: "ExtendedPatternSyntax/IgnorePatternWhitespace"),
                (Qt: QRegularExpression.PatternOptions.DontCaptureOption,
                 Net: RegexOptions.ExplicitCapture, Name: "DontCapture/ExplicitCapture"),
            };
            foreach (var option in mappable)
            {
                using (var expression = new Regex("a", option.Net).ToQRegularExpression())
                    context.Check(expression.patternOptions() == option.Qt,
                        $"RegexOptions.{option.Net} -> PatternOptions.{expression.patternOptions()} ({option.Name})");

                using (var expression = new QRegularExpression("a", option.Qt))
                {
                    var converted = expression.ToRegex();
                    context.Check(converted.Options == option.Net,
                        $"PatternOptions.{option.Qt} -> RegexOptions.{converted.Options} ({option.Name})");
                }
            }

            // The .NET-only extras: Compiled is an execution strategy and CultureInvariant a
            // no-op, so neither adds a Qt flag; both stay on the returned Regex.
            using (var expression = new Regex("a",
                       RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)
                       .ToQRegularExpression())
            {
                context.Check(expression.patternOptions() == QRegularExpression.PatternOptions.CaseInsensitiveOption,
                    $"Compiled and CultureInvariant add no Qt flag ({expression.patternOptions()})");
            }
            using (var expression = new QRegularExpression("a",
                       QRegularExpression.PatternOptions.MultilineOption))
            {
                var converted = expression.ToRegex(
                    RegexOptions.Compiled | RegexOptions.CultureInvariant);
                context.Check(converted.Options == (RegexOptions.Multiline
                                            | RegexOptions.Compiled
                                            | RegexOptions.CultureInvariant),
                    $"extraOptions are added on the .NET side ({converted.Options})");
            }

            // ------------------------------------------------------- unsupported options throw
            foreach (var qtOption in new[]
                     {
                         QRegularExpression.PatternOptions.InvertedGreedinessOption,
                         QRegularExpression.PatternOptions.UseUnicodePropertiesOption,
                     })
            {
                using (var expression = new QRegularExpression("a+", qtOption))
                {
                    try
                    {
                        var converted = expression.ToRegex();
                        context.Check(false, $"PatternOptions.{qtOption} should not convert (got '{converted}')");
                    }
                    catch (NotSupportedException)
                    {
                        context.Check(true, $"PatternOptions.{qtOption} throws NotSupportedException");
                    }
                }
            }

            foreach (var netOption in new[]
                     {
                         RegexOptions.RightToLeft,
                         RegexOptions.ECMAScript,
                         RegexOptions.NonBacktracking,
                         RegexOptions.IgnoreCase | RegexOptions.RightToLeft,
                     })
            {
                var unsupportedRegex = new Regex("a+", netOption);
                try
                {
                    using (var converted = unsupportedRegex.ToQRegularExpression())
                        context.Check(false, $"RegexOptions.{netOption} should not convert (got '{converted.Pattern()}')");
                }
                catch (NotSupportedException)
                {
                    context.Check(true, $"RegexOptions.{netOption} throws NotSupportedException");
                }
            }

            // ------------------------------------------------------- dialect failure, both ways
            // \K is a PCRE2 construct .NET does not have: Qt accepts it, the .NET constructor does not.
            using (var expression = new QRegularExpression(@"a\Kb"))
            {
                context.Check(expression.IsValid(), "Qt accepts the PCRE2-only \\K");
                try
                {
                    var converted = expression.ToRegex();
                    context.Check(false, $"PCRE2-only \\K should not compile as .NET (got '{converted}')");
                }
                catch (RegexParseException)
                {
                    context.Check(true, "a PCRE2-only pattern makes ToRegex throw RegexParseException");
                }
            }

            // A balancing group is .NET-only: Regex builds it, Qt's PCRE2 rejects it.
            const string NetOnlyPattern = @"^(?<open>a)+(?<-open>b)+$";
            string qtError;
            long qtOffset;
            using (var rejected = new QRegularExpression(NetOnlyPattern))
            {
                context.Check(!rejected.IsValid(), "Qt rejects the .NET-only balancing group");
                qtError = rejected.ErrorString();
                qtOffset = rejected.PatternErrorOffset();
            }
            context.Check(!string.IsNullOrEmpty(qtError) && qtOffset > 0,
                $"Qt reports an error and a non-zero offset ('{qtError}' at {qtOffset})");

            var netOnlyRegex = new Regex(NetOnlyPattern);
            try
            {
                using (var converted = netOnlyRegex.ToQRegularExpression())
                    context.Check(false, "a .NET-only pattern should not convert to Qt");
            }
            catch (ArgumentException e)
            {
                context.Check(e.Message.Contains(qtError)
                      && e.Message.Contains(qtOffset.ToString(CultureInfo.InvariantCulture)),
                    $"ToQRegularExpression throws ArgumentException with Qt's error and offset ({e.Message})");
            }

            return context.ExitCode;
        }
    }
}
