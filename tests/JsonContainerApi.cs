using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QtSharp.Tests
{
    /// <summary>
    /// QJsonObject as a C# container: the writable IDictionary&lt;string, QJsonValue&gt; view and
    /// the LINQ-friendly ToObjectDictionary() snapshot of unwrapped values.
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

            // ---- the unwrapped view ------------------------------------------------------
            var view = root.ToObjectDictionary();
            Check(view.Count == 3, $"ToObjectDictionary() reports the key count ({view.Count})");

            // QJsonObject keeps its keys sorted, so enumeration is alphabetical, not document order.
            Check(view.Keys.SequenceEqual(new[] { "active", "levels", "player" }),
                $"the keys enumerate in Qt's sorted order ({string.Join(",", view.Keys)})");
            Check(view.ContainsKey("player") && !view.ContainsKey("missing"),
                "ContainsKey() follows the Qt contains()");
            Check(!view.TryGetValue("missing", out _), "TryGetValue() reports a missing key");

            Check(view.TryGetValue("player", out object playerObject) &&
                  playerObject is Dictionary<string, object>,
                "a nested object unwraps to Dictionary<string, object>");
            var player = (Dictionary<string, object>)playerObject;
            Check((string)player["name"] == "Jack", "a nested string reads back");
            Check(Convert.ToInt32(player["level"]) == 10, "a nested number reads back");

            Check(view.TryGetValue("levels", out object levelsObject) && levelsObject is object[],
                "a nested array unwraps to object[]");
            var levels = (object[])levelsObject;
            Check(levels.Length == 2 && (string)levels[1] == "level2",
                "the array keeps its elements and order");

            Check(view.TryGetValue("active", out object active) && active is bool flag && flag,
                "a boolean reads back");

            Check(view.Count(entry => entry.Value is Dictionary<string, object>) == 1,
                "LINQ filters over the snapshot");
            Check(view.Where(entry => entry.Key.StartsWith("level")).Count() == 1,
                "LINQ works on the keys as well");

            // ---- the writable typed view ------------------------------------------------
            using (var direct = new QJsonValue("direct"))
            using (var returned = root.Insert("direct", direct))
                Check(Convert.ToString(returned.ToVariant().ToObject()) == "direct",
                    "Insert returns the value the iterator points at");
            Check(root.Contains("direct"), "Insert wrote the key");

            var writable = (IDictionary<string, QJsonValue>)root;
            using (var added = new QJsonValue(42))
                writable["added"] = added;
            using (var addedValue = root.Value("added"))
                Check(root.Contains("added") &&
                      Convert.ToInt32(addedValue.ToVariant().ToObject()) == 42,
                    "the indexer setter inserts through QJsonObject::insert");
            using (var name = new QJsonValue("Jack"))
                writable.Add("name", name);
            Check(root.Contains("name"), "Add() inserts another key");
            Check(writable.TryGetValue("added", out var readBack), "the typed TryGetValue finds the key");
            using (readBack)
                Check(readBack != null && Convert.ToInt32(readBack.ToVariant().ToObject()) == 42,
                    "the typed TryGetValue reads the inserted value");
            Check(writable.Remove("added") && !root.Contains("added"),
                "Remove() reports and removes the key");
            Check(writable.Remove("missing") == false, "Remove() reports a missing key");

            writable.Clear();
            Check(root.Count() == 0, "Clear() empties the object");

            Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
