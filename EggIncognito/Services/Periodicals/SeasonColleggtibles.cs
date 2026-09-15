using EggIncognito.Models.Periodicals;

namespace EggIncognito.Services.Periodicals;

public sealed record EggSighting(string EggId, string? SeasonId, double StartTime, string ContractName);

public static class SeasonColleggtibles {
    public static Dictionary<string, List<SeasonEgg>> Attribute(
        IEnumerable<EggSighting> sightings, IReadOnlyCollection<string> seasonIds,
        Func<string, string?> icon) {
        var byEgg = sightings
            .Where(s => !string.IsNullOrEmpty(s.EggId) && s.StartTime > 0)
            .GroupBy(s => s.EggId, StringComparer.Ordinal)
            .Select(g => (Egg: g, First: g.MinBy(s => s.StartTime)!))
            .OrderBy(x => x.First.StartTime)
            .ThenBy(x => x.Egg.Key, StringComparer.Ordinal);

        var result = new Dictionary<string, List<SeasonEgg>>(StringComparer.Ordinal);
        foreach (var (egg, first) in byEgg) {
            if (string.IsNullOrEmpty(first.SeasonId) || !seasonIds.Contains(first.SeasonId)) continue;

            var contracts = egg
                .Where(s => s.SeasonId == first.SeasonId)
                .OrderBy(s => s.StartTime)
                .Select(s => s.ContractName)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (!result.TryGetValue(first.SeasonId, out var list)) {
                list = [];
                result[first.SeasonId] = list;
            }

            list.Add(new SeasonEgg(egg.Key, icon(egg.Key), contracts));
        }

        return result;
    }
}
