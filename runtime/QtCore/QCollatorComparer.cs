using System.Collections.Generic;

// QCollator is an adapter, not a representation conversion: there is no QCollator value to turn
// into a string collection, so this file adds no To* method and no cast. Implementing
// IComparer<string> lets a configured QCollator (locale, case sensitivity, numeric mode,
// punctuation) drive Array.Sort, List.Sort and the LINQ ordering operators directly, without a
// lambda that calls Compare.
//
// The interface is implemented explicitly so the null policy is stated here rather than delegated
// to Qt's null-QString handling: null sorts before any non-null string, the same order
// Comparer<string>.Default uses. QCollator.Compare(string, string) itself remains the generated
// member; callers that want Qt's exact null-as-empty behavior can still call it directly.
namespace QtSharp.QtCore
{
    public partial class QCollator : IComparer<string>
    {
        /// <summary>
        /// Compares two strings with this collator's configuration; null sorts before any
        /// non-null string and two nulls are equal.
        /// </summary>
        /// <param name="x">The first string.</param>
        /// <param name="y">The second string.</param>
        /// <returns>A negative number, zero or a positive number as <paramref name="x"/> sorts
        /// before, equal to, or after <paramref name="y"/>.</returns>
        int IComparer<string>.Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            return Compare(x, y);
        }
    }
}
