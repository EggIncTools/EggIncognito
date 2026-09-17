using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EggIncognito.Core.Services;

public static class JsonPresets {
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static readonly JsonSerializerOptions CamelSkipNull = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions CamelIndentedRelaxed = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
