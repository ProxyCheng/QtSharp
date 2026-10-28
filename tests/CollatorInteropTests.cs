using System;
using System.Collections.Generic;
using System.Globalization;

namespace QtSharp.Tests
{
    /// <summary>
    /// QCollator implements IComparer&lt;string&gt;: a configured collator drives Array.Sort and
    /// LINQ ordering, and the adapter states its null order instead of inheriting Qt's.
    /// </summary>
    [Test("test-collatorinterop")]
    public static class CollatorInteropTests
    {
        public static int Run(TestContext context)
        {
            using (var locale = new QLocale(CultureInfo.GetCultureInfo("en-US")))
            using (var collator = new QCollator(locale))
            {
                collator.SetCaseSensitivity(Qt.CaseSensitivity.CaseInsensitive);

                // Array.Sort takes the IComparer<string> the class now implements.
                string[] words = { "banana", "Apple", "cherry" };
                Array.Sort(words, collator);
                context.Check(words[0] == "Apple" && words[1] == "banana" && words[2] == "cherry",
                    $"Array.Sort uses the collator ({string.Join(", ", words)})");

                // Numeric mode is the collator configuration coming through the interface.
                collator.SetNumericMode(true);
                context.Check(collator.Compare("file2", "file10") < 0,
                    "numeric mode orders file2 before file10");
                IComparer<string> comparer = collator;
                context.Check(comparer.Compare("file2", "file10") < 0,
                    "the IComparer<string> call path sees the same numeric mode");

                // The adapter's null order is explicit: null first, as Comparer<string>.Default.
                context.Check(comparer.Compare(null, "a") < 0,
                    "null sorts before a string");
                context.Check(comparer.Compare("a", null) > 0,
                    "and a string sorts after null");
                context.Check(comparer.Compare(null, null) == 0,
                    "two nulls compare equal");
            }

            return context.ExitCode;
        }
    }
}
