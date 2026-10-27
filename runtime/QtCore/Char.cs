using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using QtSharp.Runtime;

namespace QtSharp.QtCore
{
    /// <summary>
    /// Qt's QChar API, carried by <see cref="char"/>.
    /// <para>
    /// A QChar is one UTF-16 code unit and a copy owns nothing, so it is a value on both sides of
    /// the binding: every Qt signature that takes or returns one surfaces as <see cref="char"/>
    /// (generated code passes the same 16 bits over the ABI), and there is no QChar type - the same
    /// treatment QUuid gets (Guid) and QString gets (string).
    /// </para>
    /// <para>
    /// The classification predicates and <c>Category()</c> use the BCL's Unicode tables
    /// (<see cref="System.Globalization.UnicodeCategory"/>), because the BCL provides the same
    /// taxonomy; a category is the enum form of all those Is* tests. The properties the BCL has no
    /// API for - script, combining class, bidi direction, joining type, decomposition, mirroring,
    /// case folding and the Unicode version - call Qt's tables through a handful of small native
    /// helpers, so they agree with Qt exactly.
    /// </para>
    /// <para>
    /// Two QChar members are not carried over: <c>setCell</c>/<c>setRow</c> mutate the value, and a
    /// <see cref="char"/> is immutable - construct the new code unit instead
    /// (<c>(char)((row &lt;&lt; 8) | cell)</c>).
    /// </para>
    /// <para>
    /// These are C# 14 extension members, so a call site must be compiled with the .NET 10 compiler
    /// (the default for a project targeting net10.0).
    /// </para>
    /// </summary>
    public static class CharExtensions
    {
        extension(char value)
        {
            // ------------------------------------------------------------ classification

            /// <summary><c>QChar::isNull</c>: the NUL character.</summary>
            public bool IsNull() => value == '\0';

            /// <summary><c>QChar::isDigit</c>: a decimal digit (Unicode category Nd).</summary>
            public bool IsDigit() => CategoryOf(value) == UnicodeCategory.DecimalDigitNumber;

            /// <summary><c>QChar::isLetter</c>: any letter (L*).</summary>
            public bool IsLetter() => IsLetterCategory(CategoryOf(value));

            /// <summary><c>QChar::isLetterOrNumber</c>: L* or N* (all numbers, not only digits).</summary>
            public bool IsLetterOrNumber()
                => IsLetterCategory(CategoryOf(value)) || IsNumberCategory(CategoryOf(value));

            /// <summary><c>QChar::isLower</c>.</summary>
            public bool IsLower() => CategoryOf(value) == UnicodeCategory.LowercaseLetter;

            /// <summary><c>QChar::isUpper</c>.</summary>
            public bool IsUpper() => CategoryOf(value) == UnicodeCategory.UppercaseLetter;

            /// <summary><c>QChar::isTitleCase</c>.</summary>
            public bool IsTitleCase() => CategoryOf(value) == UnicodeCategory.TitlecaseLetter;

            /// <summary><c>QChar::isMark</c>: a combining mark (M*).</summary>
            public bool IsMark() => IsMarkCategory(CategoryOf(value));

            /// <summary><c>QChar::isNumber</c>: any number (N*).</summary>
            public bool IsNumber() => IsNumberCategory(CategoryOf(value));

            /// <summary><c>QChar::isPunct</c>: punctuation (P*).</summary>
            public bool IsPunct() => IsPunctCategory(CategoryOf(value));

            /// <summary><c>QChar::isSymbol</c>: a symbol (S*).</summary>
            public bool IsSymbol() => IsSymbolCategory(CategoryOf(value));

            /// <summary><c>QChar::isSpace</c>: whitespace, which is what <see cref="char.IsWhiteSpace(char)"/> means.</summary>
            public bool IsSpace() => char.IsWhiteSpace(value);

            /// <summary><c>QChar::isSurrogate</c>.</summary>
            public bool IsSurrogate() => char.IsSurrogate(value);

            /// <summary><c>QChar::isHighSurrogate</c>.</summary>
            public bool IsHighSurrogate() => char.IsHighSurrogate(value);

            /// <summary><c>QChar::isLowSurrogate</c>.</summary>
            public bool IsLowSurrogate() => char.IsLowSurrogate(value);

            /// <summary><c>QChar::isNonCharacter</c>: the FDD0..FDEF and xFFFE/xFFFF code points.</summary>
            public bool IsNonCharacter() => IsNonCharacterCodePoint(value);

            /// <summary><c>QChar::isPrint</c>: any category except Other_*.</summary>
            public bool IsPrint() => !IsOtherCategory(CategoryOf(value));

            /// <summary><c>QChar::category</c>, as the BCL's Unicode category.</summary>
            public UnicodeCategory Category() => CategoryOf(value);

            // ------------------------------------------------------------ properties

            /// <summary><c>QChar::combiningClass</c>.</summary>
            public byte CombiningClass() => CharNative.CombiningClass(value);

            /// <summary><c>QChar::decomposition</c> - the canonical/compatibility decomposition.</summary>
            public string Decomposition() => ApiRuntime.ToManaged(CharNative.Decomposition(value));

            /// <summary><c>QChar::decompositionTag</c>.</summary>
            public CharDecomposition DecompositionTag() => (CharDecomposition)CharNative.DecompositionTag(value);

            /// <summary><c>QChar::direction</c>: the bidirectional class.</summary>
            public CharDirection Direction() => (CharDirection)CharNative.Direction(value);

            /// <summary><c>QChar::joiningType</c>.</summary>
            public CharJoiningType JoiningType() => (CharJoiningType)CharNative.JoiningType(value);

            /// <summary><c>QChar::script</c>.</summary>
            public CharScript Script() => (CharScript)CharNative.Script(value);

            /// <summary><c>QChar::unicodeVersion</c>: the Unicode version that assigned the character.</summary>
            public UnicodeVersion UnicodeVersion() => (UnicodeVersion)CharNative.UnicodeVersion(value);

            /// <summary><c>QChar::hasMirrored</c>: the character has a bidi mirroring glyph.</summary>
            public bool HasMirrored() => CharNative.HasMirrored(value);

            /// <summary><c>QChar::mirroredChar</c>, or the character itself when it has no mirror.</summary>
            public char MirroredChar() => (char)CharNative.MirroredChar(value);

            // ------------------------------------------------------------ conversion

            /// <summary><c>QChar::toUpper</c>. Surrogates are returned unchanged, as in Qt.</summary>
            public char ToUpper() => char.IsSurrogate(value) ? value : char.ToUpperInvariant(value);

            /// <summary><c>QChar::toLower</c>. Surrogates are returned unchanged, as in Qt.</summary>
            public char ToLower() => char.IsSurrogate(value) ? value : char.ToLowerInvariant(value);

            /// <summary><c>QChar::toCaseFolded</c>.</summary>
            public char ToCaseFolded() => (char)CharNative.ToCaseFolded(value);

            /// <summary><c>QChar::toTitleCase</c>.</summary>
            public char ToTitleCase() => (char)CharNative.ToTitleCase(value);

            /// <summary>
            /// <c>QChar::digitValue</c>: the digit value of a Unicode decimal digit (0..9), or -1
            /// for anything else. Qt's implementation does not give letters the 10..35 values its
            /// documentation mentions; this test pins the observed behaviour.
            /// </summary>
            public int DigitValue() => CharNative.DigitValue(value);

            /// <summary><c>QChar::toLatin1</c>: the Latin-1 code unit, or 0 outside Latin-1.</summary>
            public sbyte ToLatin1() => value <= 0xFF ? (sbyte)(byte)value : (sbyte)0;

            /// <summary><c>QChar::cell</c>: the low byte of the code unit.</summary>
            public byte Cell() => (byte)(value & 0xFF);

            /// <summary><c>QChar::row</c>: the high byte of the code unit.</summary>
            public byte Row() => (byte)(value >> 8);

            /// <summary><c>QChar::unicode</c>: the code unit itself, which a char already is.</summary>
            public char Unicode() => value;

            // ------------------------------------------------------------ static (code points)

            /// <summary><c>QChar::category(uint)</c>.</summary>
            public static UnicodeCategory Category(uint codePoint) => CategoryOf(codePoint);

            /// <summary><c>QChar::combiningClass(uint)</c>.</summary>
            public static byte CombiningClass(uint codePoint) => CharNative.CombiningClass(codePoint);

            /// <summary><c>QChar::decomposition(uint)</c>.</summary>
            public static string Decomposition(uint codePoint) => ApiRuntime.ToManaged(CharNative.Decomposition(codePoint));

            /// <summary><c>QChar::decompositionTag(uint)</c>.</summary>
            public static CharDecomposition DecompositionTag(uint codePoint) => (CharDecomposition)CharNative.DecompositionTag(codePoint);

            /// <summary><c>QChar::direction(uint)</c>.</summary>
            public static CharDirection Direction(uint codePoint) => (CharDirection)CharNative.Direction(codePoint);

            /// <summary><c>QChar::joiningType(uint)</c>.</summary>
            public static CharJoiningType JoiningType(uint codePoint) => (CharJoiningType)CharNative.JoiningType(codePoint);

            /// <summary><c>QChar::script(uint)</c>.</summary>
            public static CharScript Script(uint codePoint) => (CharScript)CharNative.Script(codePoint);

            /// <summary><c>QChar::unicodeVersion(uint)</c>.</summary>
            public static UnicodeVersion UnicodeVersion(uint codePoint) => (UnicodeVersion)CharNative.UnicodeVersion(codePoint);

            /// <summary><c>QChar::currentUnicodeVersion</c>.</summary>
            public static UnicodeVersion CurrentUnicodeVersion() => (UnicodeVersion)CharNative.CurrentUnicodeVersion();

            /// <summary><c>QChar::hasMirrored(uint)</c>.</summary>
            public static bool HasMirrored(uint codePoint) => CharNative.HasMirrored(codePoint);

            /// <summary><c>QChar::mirroredChar(uint)</c>.</summary>
            public static uint MirroredChar(uint codePoint) => CharNative.MirroredChar(codePoint);

            /// <summary><c>QChar::isDigit(uint)</c>.</summary>
            public static bool IsDigit(uint codePoint) => CategoryOf(codePoint) == UnicodeCategory.DecimalDigitNumber;

            /// <summary><c>QChar::isLetter(uint)</c>.</summary>
            public static bool IsLetter(uint codePoint) => IsLetterCategory(CategoryOf(codePoint));

            /// <summary><c>QChar::isLetterOrNumber(uint)</c>.</summary>
            public static bool IsLetterOrNumber(uint codePoint)
                => IsLetterCategory(CategoryOf(codePoint)) || IsNumberCategory(CategoryOf(codePoint));

            /// <summary><c>QChar::isLower(uint)</c>.</summary>
            public static bool IsLower(uint codePoint) => CategoryOf(codePoint) == UnicodeCategory.LowercaseLetter;

            /// <summary><c>QChar::isUpper(uint)</c>.</summary>
            public static bool IsUpper(uint codePoint) => CategoryOf(codePoint) == UnicodeCategory.UppercaseLetter;

            /// <summary><c>QChar::isTitleCase(uint)</c>.</summary>
            public static bool IsTitleCase(uint codePoint) => CategoryOf(codePoint) == UnicodeCategory.TitlecaseLetter;

            /// <summary><c>QChar::isMark(uint)</c>.</summary>
            public static bool IsMark(uint codePoint) => IsMarkCategory(CategoryOf(codePoint));

            /// <summary><c>QChar::isNumber(uint)</c>.</summary>
            public static bool IsNumber(uint codePoint) => IsNumberCategory(CategoryOf(codePoint));

            /// <summary><c>QChar::isPunct(uint)</c>.</summary>
            public static bool IsPunct(uint codePoint) => IsPunctCategory(CategoryOf(codePoint));

            /// <summary><c>QChar::isSymbol(uint)</c>.</summary>
            public static bool IsSymbol(uint codePoint) => IsSymbolCategory(CategoryOf(codePoint));

            /// <summary><c>QChar::isSpace(uint)</c>.</summary>
            public static bool IsSpace(uint codePoint)
                => codePoint <= 0x10FFFF && !IsSurrogateCodePoint(codePoint) && Rune.IsWhiteSpace(new Rune((int)codePoint));

            /// <summary><c>QChar::isSurrogate(uint)</c>.</summary>
            public static bool IsSurrogate(uint codePoint) => IsSurrogateCodePoint(codePoint);

            /// <summary><c>QChar::isHighSurrogate(uint)</c>.</summary>
            public static bool IsHighSurrogate(uint codePoint) => codePoint >= 0xD800 && codePoint <= 0xDBFF;

            /// <summary><c>QChar::isLowSurrogate(uint)</c>.</summary>
            public static bool IsLowSurrogate(uint codePoint) => codePoint >= 0xDC00 && codePoint <= 0xDFFF;

            /// <summary><c>QChar::isNonCharacter(uint)</c>.</summary>
            public static bool IsNonCharacter(uint codePoint) => IsNonCharacterCodePoint(codePoint);

            /// <summary><c>QChar::isPrint(uint)</c>.</summary>
            public static bool IsPrint(uint codePoint) => !IsOtherCategory(CategoryOf(codePoint));

            /// <summary><c>QChar::digitValue(uint)</c>.</summary>
            public static int DigitValue(uint codePoint) => CharNative.DigitValue(codePoint);

            /// <summary><c>QChar::toCaseFolded(uint)</c>.</summary>
            public static uint ToCaseFolded(uint codePoint) => CharNative.ToCaseFolded(codePoint);

            /// <summary><c>QChar::toTitleCase(uint)</c>.</summary>
            public static uint ToTitleCase(uint codePoint) => CharNative.ToTitleCase(codePoint);

            /// <summary><c>QChar::toUpper(uint)</c>.</summary>
            public static uint ToUpper(uint codePoint) => ToUpperOrLower(codePoint, upper: true);

            /// <summary><c>QChar::toLower(uint)</c>.</summary>
            public static uint ToLower(uint codePoint) => ToUpperOrLower(codePoint, upper: false);

            /// <summary><c>QChar::fromLatin1</c>: a Latin-1 byte as a code unit.</summary>
            public static char FromLatin1(sbyte latin1) => (char)(byte)latin1;

            /// <summary><c>QChar::fromUcs2</c>: the code unit itself.</summary>
            public static char FromUcs2(char codeUnit) => codeUnit;

            /// <summary><c>QChar::requiresSurrogates</c>: the code point is outside the BMP.</summary>
            public static bool RequiresSurrogates(uint codePoint) => codePoint >= 0x10000;

            /// <summary><c>QChar::highSurrogate</c>.</summary>
            public static char HighSurrogate(uint codePoint) => (char)(0xD800 + ((codePoint - 0x10000) >> 10));

            /// <summary><c>QChar::lowSurrogate</c>.</summary>
            public static char LowSurrogate(uint codePoint) => (char)(0xDC00 + ((codePoint - 0x10000) & 0x3FF));

            /// <summary><c>QChar::surrogateToUcs4</c>.</summary>
            public static uint SurrogateToUcs4(char high, char low)
                => ((uint)(high - 0xD800) << 10) + (uint)(low - 0xDC00) + 0x10000;
        }

        // ------------------------------------------------------------ BCL category helpers

        static UnicodeCategory CategoryOf(char value) => char.GetUnicodeCategory(value);

        static UnicodeCategory CategoryOf(uint codePoint)
        {
            if (codePoint > 0x10FFFF)
                return UnicodeCategory.OtherNotAssigned;
            if (IsSurrogateCodePoint(codePoint))
                return UnicodeCategory.Surrogate;
            return CharUnicodeInfo.GetUnicodeCategory((int)codePoint);
        }

        static bool IsSurrogateCodePoint(uint codePoint) => codePoint >= 0xD800 && codePoint <= 0xDFFF;

        static bool IsNonCharacterCodePoint(uint codePoint)
            => (codePoint >= 0xFDD0 && codePoint <= 0xFDEF) || (codePoint & 0xFFFE) == 0xFFFE;

        static bool IsLetterCategory(UnicodeCategory c)
            => c is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter;

        static bool IsMarkCategory(UnicodeCategory c)
            => c is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark;

        static bool IsNumberCategory(UnicodeCategory c)
            => c is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber
                or UnicodeCategory.OtherNumber;

        static bool IsPunctCategory(UnicodeCategory c)
            => c is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation;

        static bool IsSymbolCategory(UnicodeCategory c)
            => c is UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;

        static bool IsOtherCategory(UnicodeCategory c)
            => c is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;

        // Surrogates and out-of-range values are returned unchanged, as Qt's toUpper/toLower do.
        static uint ToUpperOrLower(uint codePoint, bool upper)
        {
            if (codePoint > 0x10FFFF || IsSurrogateCodePoint(codePoint))
                return codePoint;
            var rune = new Rune((int)codePoint);
            return (uint)(upper ? Rune.ToUpperInvariant(rune) : Rune.ToLowerInvariant(rune)).Value;
        }
    }

    /// <summary>
    /// The Qt-side Unicode properties the BCL has no API for. They call Qt's own tables, so they
    /// agree with Qt exactly instead of following the BCL's Unicode version.
    /// </summary>
    internal static class CharNative
    {
        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_combining_class", CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte CombiningClass(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_decomposition", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Decomposition(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_decomposition_tag", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DecompositionTag(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_direction", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Direction(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_joining_type", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int JoiningType(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_script", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Script(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_unicode_version", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int UnicodeVersion(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_current_unicode_version", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int CurrentUnicodeVersion();

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_digit_value", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DigitValue(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_has_mirrored", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool HasMirrored(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_mirrored_char", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint MirroredChar(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_to_case_folded", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint ToCaseFolded(uint codePoint);

        [SuppressUnmanagedCodeSecurity, DllImport(ApiNative.Library, EntryPoint = "qs_char_to_title_case", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint ToTitleCase(uint codePoint);
    }
}
