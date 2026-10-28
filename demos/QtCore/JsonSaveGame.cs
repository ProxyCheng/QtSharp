using System;
using System.Collections.Generic;

// Qt's JSON Save Game example (examples/corelib/serialization/savegame): the same document model,
// built field by field. A game holds one player and a list of levels, a level holds NPCs, and each
// object turns itself into a QJsonObject; the document is then written with
// QJsonDocument::toJson and read back through fromJson.
//
// The upstream code assigns through QJsonObject::operator[], which returns an iterator. The
// binding publishes the equivalent insert() (the iterator's value is what operator[] assigns), so
// the construction below is the same sequence of keys and arrays, only spelled with insert/append.
public sealed class SaveGameCharacterApi
{
    public enum ClassType { Warrior = 1, Mage = 2 }

    public string Name;
    public int Level;
    public ClassType CharacterClass;

    public SaveGameCharacterApi() { }

    public SaveGameCharacterApi(string name, int level, ClassType characterClass)
    {
        Name = name;
        Level = level;
        CharacterClass = characterClass;
    }

    public QJsonObject ToJson()
    {
        var json = new QJsonObject();
        using var name = new QJsonValue(Name);
        using var level = new QJsonValue(Level);
        using var classType = new QJsonValue((int)CharacterClass);
        json.Insert("name", name).Dispose();
        json.Insert("level", level).Dispose();
        json.Insert("classType", classType).Dispose();
        return json;
    }
    public static bool FromJson(QJsonObject json, out SaveGameCharacterApi character)
    {
        character = new SaveGameCharacterApi();
        using var name = json.Value("name");
        if (name.IsString())
            character.Name = name.ToString("");
        using var level = json.Value("level");
        if (level.IsDouble())
            character.Level = level.ToInt();
        using var classType = json.Value("classType");
        if (classType.IsDouble())
            character.CharacterClass = (ClassType)classType.ToInt();
        return true;
    }
}

public sealed class SaveGameLevelApi
{
    public string Name;
    public readonly List<SaveGameCharacterApi> Npcs = new();

    public SaveGameLevelApi() { }
    public SaveGameLevelApi(string name) => Name = name;

    public QJsonObject ToJson()
    {
        var json = new QJsonObject();
        using var name = new QJsonValue(Name);
        json.Insert("name", name).Dispose();
        using var npcArray = new QJsonArray();
        foreach (var npc in Npcs)
        {
            using var npcObject = npc.ToJson();
            using var npcValue = new QJsonValue(npcObject);
            npcArray.Append(npcValue);
        }
        using var npcs = new QJsonValue(npcArray);
        json.Insert("npcs", npcs).Dispose();
        return json;
    }
}

public sealed class SaveGameGameApi
{
    public SaveGameCharacterApi Player = new();
    public readonly List<SaveGameLevelApi> Levels = new();

    public QJsonObject ToJson()
    {
        var json = new QJsonObject();
        using var playerObject = Player.ToJson();
        using var playerValue = new QJsonValue(playerObject);
        json.Insert("player", playerValue).Dispose();
        using var levelArray = new QJsonArray();
        foreach (var level in Levels)
        {
            using var levelObject = level.ToJson();
            using var levelValue = new QJsonValue(levelObject);
            levelArray.Append(levelValue);
        }
        using var levels = new QJsonValue(levelArray);
        json.Insert("levels", levels).Dispose();
        return json;
    }
}

[Demo("qtcore-savegame")]
public static class JsonSaveGame
{
    public static int Run(DemoContext demo)
    {

        // ---- the game state the example saves ------------------------------------------------
        var game = new SaveGameGameApi
        {
            Player = new SaveGameCharacterApi("Jack", 10, SaveGameCharacterApi.ClassType.Mage),
        };
        var level1 = new SaveGameLevelApi("level1");
        level1.Npcs.Add(new SaveGameCharacterApi("goblin", 3, SaveGameCharacterApi.ClassType.Warrior));
        level1.Npcs.Add(new SaveGameCharacterApi("wizard", 5, SaveGameCharacterApi.ClassType.Mage));
        var level2 = new SaveGameLevelApi("level2");
        game.Levels.Add(level1);
        game.Levels.Add(level2);

        // ---- construction, field by field ---------------------------------------------------
        using var gameObject = game.ToJson();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(gameObject.Contains("player") && gameObject.Contains("levels"),
            "the game object has the player and the levels");
        #endif
        using var player = gameObject.Value("player").ToObject();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(player.Value("name").ToString("") == "Jack" &&
              player.Value("level").ToInt() == 10 &&
              player.Value("classType").ToInt() == (int)SaveGameCharacterApi.ClassType.Mage,
            "the player was written field by field");
        #endif
        using var levels = gameObject.Value("levels").ToArray();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(levels.Count() == 2, $"the level array has both levels ({levels.Count()})");
        #endif
        using var firstLevel = levels.At(0).ToObject();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(firstLevel.Value("name").ToString("") == "level1", "the first level keeps its name");
        #endif
        using var npcs = firstLevel.Value("npcs").ToArray();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(npcs.Count() == 2 && npcs.At(1).ToObject().Value("name").ToString("") == "wizard",
            $"the npc array keeps its order and fields ({npcs.Count()} npc(s))");
        #endif

        // ---- save and load through a document, exactly like the example ----------------------
        using var document = new QJsonDocument(gameObject);
        using var saved = document.ToJson(QJsonDocument.JsonFormat.Compact);
        using var reloaded = QJsonDocument.FromJson(saved);
        #if QTSHARP_DEMO_SMOKE
        demo.Check(!reloaded.IsNull(), "the written document parses again");
        #endif

        using var root = reloaded.Object();
        using var loadedPlayer = root.Value("player").ToObject();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(SaveGameCharacterApi.FromJson(loadedPlayer, out var playerFromDisk) &&
              playerFromDisk.Name == "Jack" && playerFromDisk.Level == 10 &&
              playerFromDisk.CharacterClass == SaveGameCharacterApi.ClassType.Mage,
            "the player came back through fromJson");
        #endif

        using var loadedLevels = root.Value("levels").ToArray();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(loadedLevels.Count() == 2 &&
              loadedLevels.At(0).ToObject().Value("name").ToString("") == "level1",
            "the levels came back through the document");
        #endif

        // The exact bytes are a stable, readable JSON document (keys sorted by QJsonObject).
        string text = saved.Data();
        #if QTSHARP_DEMO_SMOKE
        demo.Check(text.Contains("\"player\"") && text.Contains("\"levels\"") && text.Contains("\"npcs\""),
            $"the saved document is the example's JSON shape ({text.Length} byte(s))");
        #endif

        #if QTSHARP_DEMO_SMOKE
        return demo.ExitCode;
        #else
        // What the example writes to disk, for a human; Smoke asserts on the same bytes.
        Console.WriteLine(text);
        return 0;
        #endif
    }
}
