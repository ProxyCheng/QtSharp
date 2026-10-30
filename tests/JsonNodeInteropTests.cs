using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QtSharp.Tests
{
    /// <summary>
    /// QJsonValue/QJsonObject/QJsonArray/QJsonDocument and System.Text.Json.Nodes: the value tree
    /// crosses in both directions, with Qt's double number representation and sorted keys.
    /// </summary>
    [Test("test-jsonnode-interop")]
    public static class JsonNodeInteropTests
    {
        public static int Run(TestContext context)
        {
            var node = new JsonObject
            {
                ["name"] = "QtSharp",
                ["version"] = 0.23,
                ["enabled"] = true,
                ["nothing"] = null,
                ["tags"] = new JsonArray("qt", "csharp"),
                ["nested"] = new JsonObject { ["answer"] = 42.0 },
            };

            using (var value = QJsonValue.FromJsonNode(node))
            {
                context.Check(value.IsObject(), "FromJsonNode builds an object value");
                using (var obj = value.ToObject())
                {
                    context.Check(obj.Contains("name"), "the object carries the keys");
                    using (var name = obj.Value("name"))
                        context.Check(name.ToString() == "QtSharp", $"string values survive ({name})");
                    using (var enabled = obj.Value("enabled"))
                        context.Check(enabled.ToBool(), "bool values survive");
                    using (var nothing = obj.Value("nothing"))
                        context.Check(nothing.IsNull(), "JSON null becomes QJsonValue::Null");
                    using (var tags = obj.Value("tags"))
                    using (var tagsArray = tags.ToArray())
                        context.Check(tagsArray.Size() == 2, "arrays survive");
                }

                JsonNode back = value.ToJsonNode();
                context.Check(JsonNode.DeepEquals(node, back),
                    $"the tree round-trips ({back?.ToJsonString()})");
            }

            // ---------------------------------------------------------------- scalars
            using (var number = QJsonValue.FromJsonNode(JsonValue.Create(42)))
            {
                context.Check(number.IsDouble() && number.ToDouble() == 42,
                    "a number becomes a Qt double");
                context.Check(JsonNode.DeepEquals(number.ToJsonNode(), JsonValue.Create(42.0)),
                    "and converts back to the same JSON number");
            }
            using (var text = QJsonValue.FromJsonNode(JsonValue.Create("hello")))
                context.Check(text.IsString() && text.ToString() == "hello", "a string survives");
            using (var flag = QJsonValue.FromJsonNode(JsonValue.Create(false)))
                context.Check(flag.IsBool() && !flag.ToBool(), "a bool survives");
            using (var name = QJsonValue.FromJsonNode(null))
            {
                context.Check(name.IsNull(), "a null node becomes QJsonValue::Null");
                context.Check(name.ToJsonNode() == null, "Null converts back to a null node");
            }
            using (var undefined = new QJsonValue(QJsonValue.Type.Undefined))
                context.Check(undefined.ToJsonNode() == null, "Undefined has no JSON spelling");

            // ---------------------------------------------------------------- containers
            var arrayNode = new JsonArray(1.5, 2.5);
            using (var array = new QJsonArray(arrayNode))
            {
                context.Check(array.Size() == 2, "JsonArray -> QJsonArray");
                context.Check(JsonNode.DeepEquals(array.ToJsonArray(), arrayNode), "and back");
            }
            using (var obj = new QJsonObject(node))
            {
                context.Check(obj.Count() == 6, $"JsonObject -> QJsonObject ({obj.Count()} keys)");
                context.Check(JsonNode.DeepEquals(obj.ToJsonObject(), node), "and back");
            }

            // ---------------------------------------------------------------- document
            using (var document = QJsonDocument.FromJsonNode(node))
            {
                context.Check(document.IsObject(), "FromJsonNode builds an object document");
                context.Check(JsonNode.DeepEquals(document.ToJsonNode(), node),
                    "the document round-trips");
            }
            using (var document = new QJsonDocument())
                context.Check(document.ToJsonNode() == null, "an empty document has no node");

            bool scalarRefused = false;
            try { QJsonDocument.FromJsonNode(JsonValue.Create(1)); }
            catch (ArgumentException) { scalarRefused = true; }
            context.Check(scalarRefused, "a scalar is not a QJsonDocument");

            return context.ExitCode;
        }
    }
}
