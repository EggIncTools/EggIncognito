using System.Text.Json;
using EggIncognito.Core.Services;
using Ei;
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
    public const string ContractsInfoRoute = "ei_ctx/get_contracts_info";

    public static string? IconUrl(IServiceProvider services, string? periodicalsRoute, string identifier) {
        if (periodicalsRoute is not null && Derive(services, periodicalsRoute)?.Icons.GetValueOrDefault(identifier) is { } live)
            return live;
        foreach ((string? json, _) in Candidates(services, ContractsInfoRoute)) {
            if (json is null) continue;
            try {
                var info = ContractsInfoResponse.Parser.ParseJson(json);
                if (info.CustomEggs.FirstOrDefault(e => e.Identifier == identifier)?.Icon?.Url is { Length: > 0 } url)
                    return url;
            } catch {
                continue;
            }
        }

        return null;
    }

    public static IEnumerable<string> IconIdentifiers(IServiceProvider services, string? periodicalsRoute) {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (periodicalsRoute is not null && Derive(services, periodicalsRoute) is { } live) ids.UnionWith(live.Icons.Keys);
        foreach ((string? json, _) in Candidates(services, ContractsInfoRoute)) {
            if (json is null) continue;
            try {
                foreach (var egg in ContractsInfoResponse.Parser.ParseJson(json).CustomEggs)
                    if (!string.IsNullOrEmpty(egg.Identifier) && !string.IsNullOrEmpty(egg.Icon?.Url)) ids.Add(egg.Identifier);
            } catch {
                continue;
            }
        }

        return ids;
    }

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
        var store = (EndpointStore)services.GetService(typeof(EndpointStore))!;
        string? json = store.CurrentJsonAsync(route, CancellationToken.None).GetAwaiter().GetResult();
        yield return (json, "db");
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
                dimension = DimensionNames.Of(e.Dimension),
                tierValues = e.TierValues
            }).ToArray(),
            contractEggMap = extract.ContractEggMap,
            gameVersion,
            provenance
        };
        return new LiveColleggtibles(extract, icons, names, identifiers, gameVersion, provenance,
            JsonSerializer.Serialize(doc, JsonPresets.CamelIndentedRelaxed));
    }
}
