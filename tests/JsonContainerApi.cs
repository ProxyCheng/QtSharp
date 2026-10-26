using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QtSharp.Tests
{
    /// <summary>
    /// QJsonObject as a C# container: enumeration, the read-only dictionary interface and LINQ
    /// over the unwrapped values. Writing is not covered because the Qt mutators are not bound yet.
    /// </summary>
    [Demo("test-json")]
    public static class JsonContainerTests
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

            const string json = @"{
                ""player"": { ""name"": ""Jack"", ""level"": 10 },
                ""levels"": [ ""level1"", ""level2"" ],
                ""active"": true
            }";

            using var document = QJsonDocument.FromJson(new QByteArray(Encoding.UTF8.GetBytes(json)));
            using var root = document.Object();

            var dictionary = (IReadOnlyDictionary<string, object>)root;
            Check(dictionary.Count == 3, $"the dictionary interface reports the key count ({dictionary.Count})");

            var keys = root.Select(entry => entry.Key).ToList();
            // QJsonObject keeps its keys sorted, so enumeration is alphabetical, not document order.
            Check(keys.SequenceEqual(new[] { "active", "levels", "player" }),
                $"foreach/LINQ enumerate the keys in Qt's sorted order ({string.Join(",", keys)})");

            Check(root.ContainsKey("player") && !root.ContainsKey("missing"),
                "ContainsKey() follows the Qt contains()");
            Check(root.TryGetValue("missing", out _) == false, "TryGetValue() reports a missing key");

            Check(root.TryGetValue("player", out object playerObject) &&
                  playerObject is Dictionary<string, object>,
                "a nested object unwraps to Dictionary<string, object>");
            var player = (Dictionary<string, object>)playerObject;
            Check((string)player["name"] == "Jack", "a nested string reads back");
            Check(Convert.ToInt32(player["level"]) == 10, "a nested number reads back");

            Check(root.TryGetValue("levels", out object levelsObject) && levelsObject is object[],
                "a nested array unwraps to object[]");
            var levels = (object[])levelsObject;
            Check(levels.Length == 2 && (string)levels[1] == "level2",
                "the array keeps its elements and order");

            Check(root.TryGetValue("active", out object active) && active is bool flag && flag,
                "a boolean reads back");

            Check(root.Count(entry => entry.Value is Dictionary<string, object>) == 1,
                "LINQ filters over the entries");
            Check(root.Where(entry => entry.Key.StartsWith("level")).Count() == 1,
                "LINQ works on the keys as well");

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
