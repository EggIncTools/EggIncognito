using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using Ei;
using Microsoft.EntityFrameworkCore;
using ProvenanceSource = EggIncognito.Core.Services.ProvenanceSource;

namespace EggIncognito.Services.DataApi;

public sealed record LiveColleggtibles(
    ColleggtibleExtract Extract,
    IReadOnlyDictionary<string, string> Icons,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyList<string> Identifiers,
    string GameVersion,
    IReadOnlyDictionary<string, ProvenanceSource> Provenance,
    string Json);

public static class LiveColleggtibleSource {
    private static readonly JsonSerializerOptions CamelJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly FrozenDictionary<int, string> DimensionNames =
        ColleggtibleCatalog.DimensionCodes.ToFrozenDictionary(kv => kv.Value, kv => kv.Key);

    public static LiveColleggtibles? Derive(IServiceProvider services, string route) {
        foreach ((string? json, string origin) in Candidates(services, route)) {
            if (json is null) continue;
            PeriodicalsResponse per;
            try {
                per = PeriodicalsResponse.Parser.ParseJson(json);
            } catch {
                continue;
            }

            var extract = ColleggtibleExtractor.FromPeriodicals(per);
            if (extract.Eggs.Count == 0) continue;
            return Build(services, route, extract, IconMap(per), NameMap(per), Identifiers(per), origin);
        }

        return null;
    }

    private static Dictionary<string, string> IconMap(PeriodicalsResponse per) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var egg in per.Contracts?.CustomEggs ?? []) {
            string? url = egg.Icon?.Url;
            if (!string.IsNullOrEmpty(egg.Identifier) && !string.IsNullOrEmpty(url)) map[egg.Identifier] = url;
        }

        return map;
    }

    private static Dictionary<string, string> NameMap(PeriodicalsResponse per) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var egg in per.Contracts?.CustomEggs ?? []) {
            if (!string.IsNullOrEmpty(egg.Identifier) && !string.IsNullOrEmpty(egg.Name)) map[egg.Identifier] = egg.Name;
        }

        return map;
    }

    private static List<string> Identifiers(PeriodicalsResponse per) {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var egg in per.Contracts?.CustomEggs ?? []) {
            if (!string.IsNullOrEmpty(egg.Identifier) && seen.Add(egg.Identifier)) ordered.Add(egg.Identifier);
        }

        foreach (var contract in per.Contracts?.Contracts ?? []) {
            if (!string.IsNullOrEmpty(contract.CustomEggId) && seen.Add(contract.CustomEggId))
                ordered.Add(contract.CustomEggId);
        }

        return ordered;
    }

    private static IEnumerable<(string? Json, string Origin)> Candidates(IServiceProvider services, string route) {
        var db = services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;
        (string? Json, string Origin, DateTimeOffset Updated)[] rows = [DbRow(db, route), FixtureRow(services, route)];
        return rows.OrderByDescending(r => r.Updated).Select(r => (r.Json, r.Origin));
    }

    private static (string? Json, string Origin, DateTimeOffset Updated) DbRow(EggIncognitoDbContext? db, string route) {
        if (db is null) return (null, "db", DateTimeOffset.MinValue);
        try {
            var row = db.StoredEndpoints.AsNoTracking()
                .Where(e => e.Path == route && e.Eid == null)
                .OrderByDescending(e => e.UpdatedAt)
                .Select(e => new { e.ResponseJson, e.UpdatedAt })
                .FirstOrDefault();
            return row is null ? (null, "db", DateTimeOffset.MinValue) : (row.ResponseJson, "db", row.UpdatedAt);
        } catch {
            return (null, "db", DateTimeOffset.MinValue);
        }
    }

    private static (string? Json, string Origin, DateTimeOffset Updated) FixtureRow(IServiceProvider services,
        string route) {
        string path = DataCatalog.FixturePath(services, route);
        return File.Exists(path)
            ? (File.ReadAllText(path), "fixture", File.GetLastWriteTimeUtc(path))
            : (null, "fixture", DateTimeOffset.MinValue);
    }

    private static LiveColleggtibles Build(IServiceProvider services, string route, ColleggtibleExtract extract,
        IReadOnlyDictionary<string, string> icons, IReadOnlyDictionary<string, string> names,
        IReadOnlyList<string> identifiers, string origin) {
        string gameVersion = services.GetService(typeof(GameDataStore)) is GameDataStore store
            ? store.Provider?.Colleggtibles.GameVersion ?? ""
            : "";
        var source = new ProvenanceSource(origin, route, "derived from captured get_periodicals");
        var provenance = new Dictionary<string, ProvenanceSource>(StringComparer.Ordinal) {
            ["buffs"] = source,
            ["contractEggMap"] = source
        };
        var doc = new {
            eggs = extract.Eggs.Select(e => new {
                identifier = e.Identifier,
                dimension = DimensionName(e.Dimension),
                tierValues = e.TierValues
            }).ToArray(),
            contractEggMap = extract.ContractEggMap,
            gameVersion,
            provenance
        };
        return new LiveColleggtibles(extract, icons, names, identifiers, gameVersion, provenance,
            JsonSerializer.Serialize(doc, CamelJson));
    }

    private static string DimensionName(int code) =>
        DimensionNames.TryGetValue(code, out string? name) ? name : code.ToString(CultureInfo.InvariantCulture);
}
