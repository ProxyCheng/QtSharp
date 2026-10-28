using System;
using System.Text.RegularExpressions;

// Explicit conversion between System.Text.RegularExpressions.Regex and QRegularExpression.
//
// The generated QRegularExpression is not partial, so the conversion is a pair of extension
// methods here rather than ToRegex()/ToQRegularExpression() members. As with the BCL value-type
// conversions in BclInterop.cs there are no implicit operators: the two types are different
// regex engines (the BCL's versus PCRE2), so a conversion has to stay visible at every crossing
// and cannot be assumed to preserve matching behaviour.
//
// Conversion carries the pattern string and the options that mean the same thing in both engines:
//
//   Qt PatternOptions                  RegexOptions
//   -------------------------------    -----------------------------------
//   CaseInsensitiveOption              IgnoreCase
//   MultilineOption                    Multiline
//   DotMatchesEverythingOption         Singleline
//   ExtendedPatternSyntaxOption        IgnorePatternWhitespace
//   DontCaptureOption                  ExplicitCapture
//
// Everything else is an error, never a silent drop. Qt's InvertedGreedinessOption and
// UseUnicodePropertiesOption throw NotSupportedException; so do .NET's RightToLeft, ECMAScript
// and NonBacktracking. In the .NET-to-Qt direction Compiled is ignored (an execution strategy,
// not pattern semantics) and CultureInvariant is accepted as a no-op (Qt's case folding is
// locale-independent), though culture-sensitive .NET matching may still differ.
//
// This is not a dialect translation. The pattern is handed to the target engine unchanged and
// only checked to compile there: a pattern that both engines accept may still match differently.
// Qt keeps evaluating with PCRE2, so a converted Regex is a pattern carrier - calling
// Regex.Match() re-evaluates it with the .NET engine, while QRegularExpression.Match() keeps
// returning Qt's QRegularExpressionMatch.
namespace QtSharp.QtCore
{
    /// <summary>
    /// Explicit conversions between <see cref="QRegularExpression"/> and
    /// <see cref="Regex"/>: the pattern string and the options the two engines spell the same way.
    /// </summary>
    public static class QRegularExpressionInterop
    {
        // The white-listed options in both directions. Anything outside a whitelist throws, so an
        // option added to either enum in the future is a compile-time review, not a silent drop.
        const QRegularExpression.PatternOptions MappableOptions =
            QRegularExpression.PatternOptions.CaseInsensitiveOption
            | QRegularExpression.PatternOptions.MultilineOption
            | QRegularExpression.PatternOptions.DotMatchesEverythingOption
            | QRegularExpression.PatternOptions.ExtendedPatternSyntaxOption
            | QRegularExpression.PatternOptions.DontCaptureOption;

        const RegexOptions MappableRegexOptions =
            RegexOptions.IgnoreCase
            | RegexOptions.Multiline
            | RegexOptions.Singleline
            | RegexOptions.IgnorePatternWhitespace
            | RegexOptions.ExplicitCapture;

        // Compiled is an execution strategy and CultureInvariant a no-op for Qt, so both may be
        // supplied without changing the Qt pattern; everything else must map.
        const RegexOptions AcceptedRegexOptions =
            MappableRegexOptions | RegexOptions.Compiled | RegexOptions.CultureInvariant;

        /// <summary>
        /// Compiles the Qt expression's pattern with the .NET engine as a <see cref="Regex"/>,
        /// carrying the options the two engines spell the same way.
        /// </summary>
        /// <param name="expression">The Qt expression whose pattern and options are carried.</param>
        /// <param name="extraOptions">
        /// .NET-only options added to the mapped ones (for example <see cref="RegexOptions.Compiled"/>).
        /// They shape the returned Regex; Qt has no counterpart for them.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
        /// <exception cref="NotSupportedException">
        /// The expression has a <see cref="QRegularExpression.PatternOptions"/> value with no
        /// <see cref="RegexOptions"/> equivalent (<c>InvertedGreedinessOption</c>,
        /// <c>UseUnicodePropertiesOption</c>, or a value this binding does not know).
        /// </exception>
        /// <exception cref="RegexParseException">
        /// The pattern is not valid in the .NET dialect. The exception is the constructor's own;
        /// this conversion does not catch or rephrase it.
        /// </exception>
        /// <remarks>
        /// This is not a dialect translation: the pattern is handed to the .NET engine unchanged,
        /// so a pattern both engines accept can still match differently. The returned Regex is a
        /// pattern carrier; Qt APIs still evaluate with PCRE2, and <see cref="QRegularExpression.Match"/>
        /// keeps returning Qt match objects. Calling <see cref="Regex.Match(string)"/> on the result
        /// re-evaluates with the .NET engine.
        /// </remarks>
        public static Regex ToRegex(this QRegularExpression expression,
                                    RegexOptions extraOptions = RegexOptions.None)
        {
            if (expression is null)
                throw new ArgumentNullException(nameof(expression));

            QRegularExpression.PatternOptions options = expression.patternOptions();
            QRegularExpression.PatternOptions unsupported = options & ~MappableOptions;
            if (unsupported != QRegularExpression.PatternOptions.NoPatternOption)
                throw new NotSupportedException(
                    $"QRegularExpression.PatternOptions.{unsupported} has no "
                    + "System.Text.RegularExpressions.RegexOptions equivalent and cannot be "
                    + "carried across.");

            return new Regex(expression.Pattern(), ToRegexOptions(options) | extraOptions);
        }

        /// <summary>
        /// Builds a <see cref="QRegularExpression"/> from a .NET <see cref="Regex"/>, carrying its
        /// pattern and the options the two engines spell the same way.
        /// </summary>
        /// <param name="regex">The .NET regex whose pattern and options are carried.</param>
        /// <exception cref="ArgumentNullException"><paramref name="regex"/> is null.</exception>
        /// <exception cref="NotSupportedException">
        /// The regex has an option with no Qt equivalent (<see cref="RegexOptions.RightToLeft"/>,
        /// <see cref="RegexOptions.ECMAScript"/>, <see cref="RegexOptions.NonBacktracking"/>, or a
        /// value this binding does not know).
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Qt's PCRE2 rejects the pattern. The message carries the expression's
        /// <see cref="QRegularExpression.ErrorString"/> and <see cref="QRegularExpression.PatternErrorOffset"/>.
        /// </exception>
        /// <remarks>
        /// <see cref="RegexOptions.Compiled"/> (an execution strategy) and
        /// <see cref="RegexOptions.CultureInvariant"/> (Qt's case folding is already
        /// locale-independent) are accepted without a Qt flag; everything else must map or throw.
        /// This is not a dialect translation: the pattern is handed to PCRE2 unchanged, so a
        /// pattern both engines accept can still match differently. Qt evaluates the returned
        /// expression with PCRE2; it does not use the .NET engine.
        /// </remarks>
        public static QRegularExpression ToQRegularExpression(this Regex regex)
        {
            if (regex is null)
                throw new ArgumentNullException(nameof(regex));

            RegexOptions options = regex.Options;
            RegexOptions unsupported = options & ~AcceptedRegexOptions;
            if (unsupported != RegexOptions.None)
                throw new NotSupportedException(
                    $"System.Text.RegularExpressions.RegexOptions.{unsupported} cannot be "
                    + "represented by Qt's QRegularExpression.PatternOptions and would change "
                    + "matching semantics.");

            var expression = new QRegularExpression(regex.ToString(), ToPatternOptions(options));
            if (!expression.IsValid())
            {
                string error = expression.ErrorString();
                long offset = expression.PatternErrorOffset();
                expression.Dispose();
                throw new ArgumentException(
                    $"Qt's PCRE2 rejects the pattern at offset {offset}: {error}", nameof(regex));
            }
            return expression;
        }

        static RegexOptions ToRegexOptions(QRegularExpression.PatternOptions options)
        {
            RegexOptions result = RegexOptions.None;
            if ((options & QRegularExpression.PatternOptions.CaseInsensitiveOption) != 0)
                result |= RegexOptions.IgnoreCase;
            if ((options & QRegularExpression.PatternOptions.MultilineOption) != 0)
                result |= RegexOptions.Multiline;
            if ((options & QRegularExpression.PatternOptions.DotMatchesEverythingOption) != 0)
                result |= RegexOptions.Singleline;
            if ((options & QRegularExpression.PatternOptions.ExtendedPatternSyntaxOption) != 0)
                result |= RegexOptions.IgnorePatternWhitespace;
            if ((options & QRegularExpression.PatternOptions.DontCaptureOption) != 0)
                result |= RegexOptions.ExplicitCapture;
            return result;
        }

        static QRegularExpression.PatternOptions ToPatternOptions(RegexOptions options)
        {
            QRegularExpression.PatternOptions result = QRegularExpression.PatternOptions.NoPatternOption;
            if ((options & RegexOptions.IgnoreCase) != 0)
                result |= QRegularExpression.PatternOptions.CaseInsensitiveOption;
            if ((options & RegexOptions.Multiline) != 0)
                result |= QRegularExpression.PatternOptions.MultilineOption;
            if ((options & RegexOptions.Singleline) != 0)
                result |= QRegularExpression.PatternOptions.DotMatchesEverythingOption;
            if ((options & RegexOptions.IgnorePatternWhitespace) != 0)
                result |= QRegularExpression.PatternOptions.ExtendedPatternSyntaxOption;
            if ((options & RegexOptions.ExplicitCapture) != 0)
                result |= QRegularExpression.PatternOptions.DontCaptureOption;
            return result;
        }
    }
}
