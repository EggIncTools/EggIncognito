using System.Text.Json;
using System.Text.Json.Serialization;
using EggIncognito.Services.Inspector;

namespace EggIncognito.Models.Inspector;

public sealed class InspectorTargetConverter : JsonConverter<InspectorTarget> {
    public override InspectorTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number
            ? (InspectorTarget)reader.GetInt32()
            : InspectorTargets.Parse(reader.GetString());

    public override void Write(Utf8JsonWriter writer, InspectorTarget value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
