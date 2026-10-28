﻿using System;
using System.Collections.Generic;

// Qt's regexp example (examples/corelib/tools/regexp): a pattern is compiled once, every match in
// a subject is walked with globalMatch(), and the captures and their offsets are shown. The
// console loop of the upstream example is replaced by assertions, which is how the gate reads it.
[Example("qtcore-regexp")]
public static class Regexp
{
    public static int Run(ExampleContext example)
    {

        const string pattern = @"(?<key>\w+)=(?<value>\d+)";
        using var expression = new QRegularExpression(pattern);
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(expression.IsValid(), "the pattern compiles");
        #endif
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(expression.Pattern() == pattern, "the expression keeps its pattern");
        #endif

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
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(found.Count == 3, $"globalMatch() finds every occurrence ({found.Count})");
        #endif
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(string.Join(",", found) == "a:1,b:22,c:333",
            $"the captures come back in order ({string.Join(",", found)})");
        #endif

        using (var first = expression.Match(subject))
        {
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(first.HasMatch(), "match() reports the first occurrence");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(first.Captured(2) == "1", "a numbered capture reads back");
            #endif
            #if QTSHARP_EXAMPLE_SMOKE
            example.Check(first.CapturedStart(1) == 0 && first.CapturedEnd(1) == 1,
                "a capture reports its offsets");
            #endif
        }

        using var none = expression.Match("no numbers here");
        #if QTSHARP_EXAMPLE_SMOKE
        example.Check(!none.HasMatch(), "a subject without the pattern reports no match");
        #endif

        #if QTSHARP_EXAMPLE_SMOKE
        return example.ExitCode;
        #else
        // The upstream console loop, for a human: one line per match.
        Console.WriteLine($"[regexp] {pattern} over \"{subject}\"");
        foreach (var capture in found)
            Console.WriteLine($"[regexp] {capture}");
        return 0;
        #endif
    }
}
