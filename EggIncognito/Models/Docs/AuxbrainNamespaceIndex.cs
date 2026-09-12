using System.Text.Json.Serialization;

namespace EggIncognito.Models.Docs;

public sealed record AuxbrainNamespaceIndex(
    [property: JsonPropertyName("namespace")] string Namespace,
    IReadOnlyList<AuxbrainRouteWire> Routes);
