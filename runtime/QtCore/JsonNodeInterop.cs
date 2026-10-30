using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

// The System.Text.Json bridge for Qt's JSON DOM. Qt's QJsonValue/QJsonObject/QJsonArray are
// handle classes with a native lifetime, so the conversion is explicit in both directions, like
// the other BCL interop files:
//
//     JsonNode node = value.ToJsonNode();
//     QJsonValue value = QJsonValue.FromJsonNode(node);
//
// System.Text.Json.Nodes is the right BCL counterpart rather than JsonDocument/JsonElement:
// JsonNode owns its data, while a JsonElement is only valid for as long as the JsonDocument that
// produced it lives. A bridge to JsonDocument would leak that lifetime rule into every caller.
//
// What crosses is the JSON value tree: null, bool, number, string, array and object. Qt stores
// every number as a double, so a long beyond 2^53 loses precision on the way in - the same limit
// Qt's own parser has. A JsonNode null and Qt's Null both become null; Qt's Undefined has no JSON
// spelling and also becomes null here. Object keys keep Qt's sorted order on the managed side
// (QJsonObject stores them sorted, unlike JsonObject's insertion order).
namespace QtSharp.QtCore
{
    public partial class QJsonValue
    {
        /// <summary>
        /// Builds a QJsonValue from a System.Text.Json node. A null node becomes
        /// <see cref="QJsonValue.Type.Null"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <remarks>
        /// Numbers cross as doubles (Qt's JSON number representation); arrays and objects are
        /// copied, not referenced, so changing the JsonNode afterwards does not change the
        /// QJsonValue.
        /// </remarks>
        public static QJsonValue FromJsonNode(JsonNode node)
        {
            if (node is null)
                return new QJsonValue(QJsonValue.Type.Null);

            switch (node.GetValueKind())
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return new QJsonValue(QJsonValue.Type.Null);
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return new QJsonValue(node.GetValue<bool>());
                case JsonValueKind.String:
                    return new QJsonValue(node.GetValue<string>());
                case JsonValueKind.Number:
                    return new QJsonValue(AsDouble((JsonValue)node));
                case JsonValueKind.Array:
                {
                    using (var array = new QJsonArray((JsonArray)node))
                        return new QJsonValue(array);
                }
                case JsonValueKind.Object:
                {
                    using (var obj = new QJsonObject((JsonObject)node))
                        return new QJsonValue(obj);
                }
                default:
                    throw new NotSupportedException(
                        $"The JsonNode kind '{node.GetValueKind()}' has no QJsonValue equivalent.");
            }
        }

        /// <summary>
        /// The value as a System.Text.Json node: null for Qt's Null and Undefined, a JsonValue for
        /// the scalars, and a JsonArray/JsonObject copy for the containers.
        /// </summary>
        /// <remarks>
        /// The returned nodes are owned by the caller and independent of the QJsonValue; the
        /// QJsonValue may be disposed immediately afterwards.
        /// </remarks>
        public JsonNode ToJsonNode()
        {
            switch (type())
            {
                case QJsonValue.Type.Null:
                case QJsonValue.Type.Undefined:
                    return null;
                case QJsonValue.Type.Bool:
                    return JsonValue.Create(ToBool());
                case QJsonValue.Type.Double:
                    return JsonValue.Create(ToDouble());
                case QJsonValue.Type.String:
                    return JsonValue.Create(ToString());
                case QJsonValue.Type.Array:
                {
                    using (var array = ToArray())
                        return array.ToJsonArray();
                }
                case QJsonValue.Type.Object:
                {
                    using (var obj = ToObject())
                        return obj.ToJsonObject();
                }
                default:
                    return null;
            }
        }

        static double AsDouble(JsonValue value)
        {
            if (value.TryGetValue<double>(out double asDouble))
                return asDouble;
            if (value.TryGetValue<int>(out int asInt))
                return asInt;
            if (value.TryGetValue<long>(out long asLong))
                return asLong;
            if (value.TryGetValue<decimal>(out decimal asDecimal))
                return (double)asDecimal;
            return double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    public partial class QJsonObject
    {
        /// <summary>Copies a System.Text.Json object into a new QJsonObject.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public QJsonObject(JsonObject node) : this()
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            foreach (var pair in node)
                using (QJsonValue value = QJsonValue.FromJsonNode(pair.Value))
                    Insert(pair.Key, value)?.Dispose();
        }

        /// <summary>The object as a new System.Text.Json object, with Qt's sorted key order.</summary>
        public JsonObject ToJsonObject()
        {
            var result = new JsonObject();
            foreach (string key in Keys())
                using (QJsonValue value = Value(key))
                    result[key] = value.ToJsonNode();
            return result;
        }
    }

    public partial class QJsonArray
    {
        /// <summary>Copies a System.Text.Json array into a new QJsonArray.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public QJsonArray(JsonArray node) : this()
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            foreach (JsonNode item in node)
                using (QJsonValue value = QJsonValue.FromJsonNode(item))
                    Append(value);
        }

        /// <summary>The array as a new System.Text.Json array.</summary>
        public JsonArray ToJsonArray()
        {
            var result = new JsonArray();
            for (long i = 0; i < Count(); i++)
                using (QJsonValue value = At(i))
                    result.Add(value.ToJsonNode());
            return result;
        }
    }

    public partial class QJsonDocument
    {
        /// <summary>
        /// A QJsonDocument holding a copy of a System.Text.Json object or array. A scalar throws:
        /// QJsonDocument has no scalar form, and <see cref="QJsonValue.FromJsonNode"/> is the
        /// representation for one.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is a scalar.</exception>
        public static QJsonDocument FromJsonNode(JsonNode node)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            switch (node.GetValueKind())
            {
                case JsonValueKind.Object:
                    using (var obj = new QJsonObject((JsonObject)node))
                        return new QJsonDocument(obj);
                case JsonValueKind.Array:
                    using (var array = new QJsonArray((JsonArray)node))
                        return new QJsonDocument(array);
                default:
                    throw new ArgumentException(
                        "A QJsonDocument holds an object or an array; a scalar goes through "
                        + "QJsonValue.FromJsonNode.", nameof(node));
            }
        }

        /// <summary>The document as a System.Text.Json object or array, or null when empty.</summary>
        public JsonNode ToJsonNode()
        {
            if (IsObject())
            {
                using (var obj = Object())
                    return obj.ToJsonObject();
            }
            if (IsArray())
            {
                using (var array = Array())
                    return array.ToJsonArray();
            }
            return null;
        }
    }
}
