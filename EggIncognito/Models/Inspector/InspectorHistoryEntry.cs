using System.Text.Json.Serialization;
using EggIncognito.Services.Inspector;

namespace EggIncognito.Models.Inspector;

public sealed record InspectorHistoryEntry(
    string Id,
    string Path,
    string Summary,
    Dictionary<string, string> Env,
    string FieldsJson,
    string? PathParam,
    [property: JsonConverter(typeof(InspectorTargetConverter))]
    InspectorTarget Target,
    long Order);
