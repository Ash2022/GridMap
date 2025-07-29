using Newtonsoft.Json;
using System;
using UnityEngine;

public class Vector2IntConverter : JsonConverter<Vector2Int>
{
    public override void WriteJson(JsonWriter writer, Vector2Int value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.x);
        writer.WritePropertyName("y");
        writer.WriteValue(value.y);
        writer.WriteEndObject();
    }

    public override Vector2Int ReadJson(JsonReader reader, Type objectType, Vector2Int existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        int x = 0, y = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.PropertyName)
            {
                switch ((string)reader.Value)
                {
                    case "x":
                        reader.Read();
                        x = Convert.ToInt32(reader.Value);
                        break;
                    case "y":
                        reader.Read();
                        y = Convert.ToInt32(reader.Value);
                        break;
                }
            }
            else if (reader.TokenType == JsonToken.EndObject)
                break;
        }
        return new Vector2Int(x, y);
    }
}
