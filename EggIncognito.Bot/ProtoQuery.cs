using EggIncognito.Core.Services;

namespace EggIncognito.Bot;

public static class ProtoQuery {
    public const int PerPage = 25;

    public const int MaxDescription = 4000;

    public static (IReadOnlyList<string> Slice, int Page, int Pages) Page(
        IReadOnlyList<string> names, int requestedPage, int perPage = PerPage) {
        int pages = Math.Max(1, (names.Count + perPage - 1) / perPage);
        int page = Math.Clamp(requestedPage, 1, pages);
        var slice = names.Skip((page - 1) * perPage).Take(perPage).ToList();
        return (slice, page, pages);
    }

    public static IReadOnlyList<string> Autocomplete(IReadOnlyList<string> names, string query) {
        return string.IsNullOrEmpty(query)
            ? [.. names.Take(25)]
            : [
                .. names
                    .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(25)
            ];
    }

    public const string TruncatedMarker = "\n... (truncated)";

    public static string TypeLines(SchemaMessage msg) {
        if (msg.Fields.Count == 0) return "(no fields)";
        var lines = msg.Fields.Select(f => {
            string rep = f.Repeated ? " repeated" : "";
            string en = f.EnumValues is { Count: > 0 }
                ? " = enum{" + string.Join(",", f.EnumValues.Select(v => v.Name)) + "}"
                : "";
            string mt = f.Type == "message" && f.MessageType is not null ? $"<{f.MessageType}>" : "";
            return $"#{f.Number} {f.Name}: {f.Type}{mt}{rep}{en}";
        });
        return string.Join("\n", lines);
    }
}
