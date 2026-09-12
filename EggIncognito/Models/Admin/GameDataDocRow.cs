using EggIncognito.Services.DataApi;

namespace EggIncognito.Models.Admin;

public sealed record GameDataDocRow(string Id, bool Present, DateTimeOffset? UpdatedAt, int? Bytes) {
    public static GameDataDocRow From(string id, IReadOnlyDictionary<string, GameDataDocInfo> rows) =>
        rows.TryGetValue(id, out var doc)
            ? new GameDataDocRow(id, true, doc.UpdatedAt, doc.Bytes)
            : new GameDataDocRow(id, false, null, null);
}
