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
    [Test("test-json")]
    public static class JsonContainerTests
    {
        public static int Run(TestContext context)
        {

            const string json = @"{
                ""player"": { ""name"": ""Jack"", ""level"": 10 },
                ""levels"": [ ""level1"", ""level2"" ],
                ""active"": true
            }";

            using var document = QJsonDocument.FromJson(new QByteArray(Encoding.UTF8.GetBytes(json)));
            using var root = document.Object();

            // ---- the unwrapped view ------------------------------------------------------
            var view = root.ToObjectDictionary();
            context.Check(view.Count == 3, $"ToObjectDictionary() reports the key count ({view.Count})");

            // QJsonObject keeps its keys sorted, so enumeration is alphabetical, not document order.
            context.Check(view.Keys.SequenceEqual(new[] { "active", "levels", "player" }),
                $"the keys enumerate in Qt's sorted order ({string.Join(",", view.Keys)})");
            context.Check(view.ContainsKey("player") && !view.ContainsKey("missing"),
                "ContainsKey() follows the Qt contains()");
            context.Check(!view.TryGetValue("missing", out _), "TryGetValue() reports a missing key");

            context.Check(view.TryGetValue("player", out object playerObject) &&
                  playerObject is Dictionary<string, object>,
                "a nested object unwraps to Dictionary<string, object>");
            var player = (Dictionary<string, object>)playerObject;
            context.Check((string)player["name"] == "Jack", "a nested string reads back");
            context.Check(Convert.ToInt32(player["level"]) == 10, "a nested number reads back");

            context.Check(view.TryGetValue("levels", out object levelsObject) && levelsObject is object[],
                "a nested array unwraps to object[]");
            var levels = (object[])levelsObject;
            context.Check(levels.Length == 2 && (string)levels[1] == "level2",
                "the array keeps its elements and order");

            context.Check(view.TryGetValue("active", out object active) && active is bool flag && flag,
                "a boolean reads back");

            context.Check(view.Count(entry => entry.Value is Dictionary<string, object>) == 1,
                "LINQ filters over the snapshot");
            context.Check(view.Where(entry => entry.Key.StartsWith("level")).Count() == 1,
                "LINQ works on the keys as well");

            // ---- the writable typed view ------------------------------------------------
            using (var direct = new QJsonValue("direct"))
            using (var returned = root.Insert("direct", direct))
                context.Check(Convert.ToString(returned.ToVariant()) == "direct",
                    "Insert returns the value the iterator points at");
            context.Check(root.Contains("direct"), "Insert wrote the key");

            var writable = (IDictionary<string, QJsonValue>)root;
            using (var added = new QJsonValue(42))
                writable["added"] = added;
            using (var addedValue = root.Value("added"))
                context.Check(root.Contains("added") &&
                      Convert.ToInt32(addedValue.ToVariant()) == 42,
                    "the indexer setter inserts through QJsonObject::insert");
            using (var name = new QJsonValue("Jack"))
                writable.Add("name", name);
            context.Check(root.Contains("name"), "Add() inserts another key");
            context.Check(writable.TryGetValue("added", out var readBack), "the typed TryGetValue finds the key");
            using (readBack)
                context.Check(readBack != null && Convert.ToInt32(readBack.ToVariant()) == 42,
                    "the typed TryGetValue reads the inserted value");
            context.Check(writable.Remove("added") && !root.Contains("added"),
                "Remove() reports and removes the key");
            context.Check(writable.Remove("missing") == false, "Remove() reports a missing key");

            writable.Clear();
            context.Check(root.Count() == 0, "Clear() empties the object");

            // ---- QJsonArray as a list ---------------------------------------------------
            using (var array = new QJsonArray())
            {
                var list = (IList<QJsonValue>)array;
                using (var a = new QJsonValue("a"))
                    list.Add(a);
                using (var b = new QJsonValue("b"))
                    list.Add(b);
                context.Check(list.Count == 2, "IList.Add appends");

                using (var z = new QJsonValue("z"))
                    list.Insert(0, z);
                using (var first = list[0])
                    context.Check(list.Count == 3 && (string)first.ToVariant() == "z",
                        "IList.Insert inserts at the index");

                using (var capitalB = new QJsonValue("B"))
                    list[1] = capitalB;
                using (var replaced = list[1])
                    context.Check((string)replaced.ToVariant() == "B",
                        "the indexer setter replaces the element");

                using (var probe = new QJsonValue("B"))
                    context.Check(list.IndexOf(probe) == 1, "IndexOf compares the values");

                list.RemoveAt(0);
                context.Check(list.Count == 2, "RemoveAt removes the element");

                var snapshot = array.ToObjectList();
                context.Check(snapshot.Count == 2 && (string)snapshot[0] == "B",
                    "ToObjectList() unwraps the elements in order");

                list.Clear();
                context.Check(list.Count == 0, "Clear() empties the array");
            }

            return context.ExitCode;
        }
    }
}
