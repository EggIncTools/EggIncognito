using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.DataApi;

public static class ConsumeRawSource {
    public const int DefaultLimit = 1000;
    public const int MaxLimit = 5000;

    public static async Task<DataPayload?> ProduceAsync(DataProduceContext ctx, CancellationToken ct) {
        if (ctx.Services.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db) return null;
        (long after, int limit) = Paging(ctx.Http.Request.Query);

        var scope = db.ArtifactConsumeObservations.AsNoTracking().Where(o => o.Action == "consume" && o.Success);
        int total = await scope.CountAsync(ct);
        var rows = await scope.Where(o => o.Id > after).OrderBy(o => o.Id).Take(limit + 1)
            .Select(o => new {
                o.Id,
                o.SpecName,
                o.SpecLevel,
                o.SpecRarity,
                o.Byproducts,
                o.OtherRewards,
                o.GoldenEggs,
                o.ClientVersion,
                o.ObservedAt
            })
            .ToListAsync(ct);

        bool more = rows.Count > limit;
        var page = rows.Take(limit).ToList();
        var doc = new JsonObject {
            ["rows"] = new JsonArray([.. page.Select(r => (JsonNode)new JsonObject {
                ["id"] = r.Id,
                ["specName"] = r.SpecName,
                ["level"] = r.SpecLevel,
                ["rarity"] = r.SpecRarity,
                ["byproducts"] = ArrayOf(r.Byproducts),
                ["otherRewards"] = ArrayOf(r.OtherRewards),
                ["goldenEggs"] = r.GoldenEggs,
                ["clientVersion"] = r.ClientVersion,
                ["observedAt"] = r.ObservedAt
            })]),
            ["nextAfter"] = more ? JsonValue.Create(page[^1].Id) : null,
            ["total"] = total,
            ["provenance"] = new JsonObject {
                ["rows"] = new JsonObject {
                    ["origin"] = "observed",
                    ["locator"] = "device harvest",
                    ["method"] = "captured"
                }
            }
        };
        return DataPayload.Json(doc.ToJsonString(JsonPresets.Indented));
    }

    public static (long After, int Limit) Paging(IQueryCollection query) {
        long after = long.TryParse(query["after"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long a)
            ? Math.Max(0, a)
            : 0;
        int limit = int.TryParse(query["limit"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int l)
            ? Math.Clamp(l, 1, MaxLimit)
            : DefaultLimit;
        return (after, limit);
    }

    public static JsonArray ArrayOf(string json) {
        try {
            return JsonNode.Parse(json) as JsonArray ?? [];
        } catch (JsonException) {
            return [];
        }
    }
}
