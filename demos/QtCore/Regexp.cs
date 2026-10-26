using System;
using System.Collections.Generic;

// Qt's regexp example (examples/corelib/tools/regexp): a pattern is compiled once, every match in
// a subject is walked with globalMatch(), and the captures and their offsets are shown. The
// console loop of the upstream example is replaced by assertions, which is how the gate reads it.
[Demo("qtcore-regexp")]
public static class Regexp
{
    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        const string pattern = @"(?<key>\w+)=(?<value>\d+)";
        using var expression = new QRegularExpression(pattern);
        Check(expression.IsValid(), "the pattern compiles");
        Check(expression.Pattern() == pattern, "the expression keeps its pattern");

        const string subject = "a=1, b=22, c=333";
        var found = new List<string>();
        using (var matches = expression.GlobalMatch(subject))
        {
            while (matches.HasNext())
            {
                using var match = matches.Next();
                found.Add(match.Captured("key") + ":" + match.Captured("value"));
            }
        }
        Check(found.Count == 3, $"globalMatch() finds every occurrence ({found.Count})");
        Check(string.Join(",", found) == "a:1,b:22,c:333",
            $"the captures come back in order ({string.Join(",", found)})");

        using (var first = expression.Match(subject))
        {
            Check(first.HasMatch(), "match() reports the first occurrence");
            Check(first.Captured(2) == "1", "a numbered capture reads back");
            Check(first.CapturedStart(1) == 0 && first.CapturedEnd(1) == 1,
                "a capture reports its offsets");
        }

        using var none = expression.Match("no numbers here");
        Check(!none.HasMatch(), "a subject without the pattern reports no match");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
