using System;
using System.Text;

// Qt's JSON Save Game example (examples/corelib/serialization/savegame): the document model of the
// example - a game state as JSON, read through QJsonDocument/QJsonObject/QJsonValue and serialized
// again. The upstream example builds its object field by field; QJsonObject::insert and
// operator[] return an iterator, which this binding does not publish, so the construction side
// here starts from the document text and the round trip is checked through toJson().
[Demo("qtcore-savegame")]
public static class JsonSaveGame
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
            ""player"": { ""name"": ""Jack"", ""level"": 10, ""health"": 73.5 },
            ""levels"": [ ""level1"", ""level2"" ]
        }";

        using var document = QJsonDocument.FromJson(new QByteArray(Encoding.UTF8.GetBytes(json)));
        Check(!document.IsNull(), "QJsonDocument parses the save-game document");

        using var root = document.Object();
        Check(root.Contains("player") && root.Contains("levels"),
            "the root object has the player and the levels");

        using var player = root.Value("player").ToObject();
        Check(player.Value("name").ToString("") == "Jack", "the player's name reads back");
        Check(player.Value("level").ToInt() == 10, "the player's level reads back");
        Check(Math.Abs(player.Value("health").ToDouble() - 73.5) < 1e-9,
            "the player's health reads back");

        using var levels = root.Value("levels").ToArray();
        Check(levels.Count() == 2 && levels.At(0).ToString("") == "level1" &&
              levels.At(1).ToString("") == "level2",
            "the level list keeps its order");

        // The other half of the example: writing the document back out and reading it again.
        using var compact = document.ToJson(QJsonDocument.JsonFormat.Compact);
        using var reparsed = QJsonDocument.FromJson(compact);
        Check(!reparsed.IsNull(), "toJson() produces a document that parses again");
        using var reparsedRoot = reparsed.Object();
        using var reparsedPlayer = reparsedRoot.Value("player").ToObject();
        Check(reparsedPlayer.Value("level").ToInt() == 10 &&
              reparsedPlayer.Value("name").ToString("") == "Jack",
            "the round-tripped document still has the player");

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
