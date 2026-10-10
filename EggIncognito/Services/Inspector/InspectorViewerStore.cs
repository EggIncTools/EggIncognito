using System.Text.Json;
using System.Text.RegularExpressions;
using EggIdentity.UI;
using EggIncognito.Models.Inspector;

namespace EggIncognito.Services.Inspector;

public sealed partial class InspectorViewerStore(ViewerCookies cookies) {
    public const string SaltKey = "inspector.salt";
    public const string RinfoKey = "inspector.rinfo";
    public const string TargetKey = "inspector.target";
    public const string ConsentKey = "inspector.consent";
    public const string EidsKey = "inspector.eids";
    public const string HistoryKey = "inspector.history";

    public static readonly IReadOnlyList<string> Keys = [SaltKey, RinfoKey, TargetKey, ConsentKey, EidsKey, HistoryKey];

    private const int EidMax = 12;
    private const int HistoryMax = 50;
    private const int ValueBudget = 3800;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Salt {
        get => cookies.Get(SaltKey) ?? "";
        set => cookies.Set(SaltKey, value);
    }

    public string CustomTarget {
        get => cookies.Get(TargetKey) ?? "";
        set => cookies.Set(TargetKey, value.Trim());
    }

    public bool LiveConsent => cookies.Get(ConsentKey) == "1";

    public void GrantLiveConsent() => cookies.Set(ConsentKey, "1");

    public RinfoSeed? Rinfo => Read<RinfoSeed>(RinfoKey);

    public void SaveRinfo(RinfoSeed rinfo) => cookies.Set(RinfoKey, JsonSerializer.Serialize(rinfo, Json));

    public string[] RecentEids => Read<string[]>(EidsKey) ?? [];

    public string[] RememberEids(IEnumerable<string> values) {
        var list = RecentEids.ToList();
        foreach (var eid in values.Select(v => v.Trim()).Where(v => EidRegex().IsMatch(v))) {
            list.Remove(eid);
            list.Insert(0, eid);
        }

        var next = list.Take(EidMax).ToArray();
        cookies.Set(EidsKey, JsonSerializer.Serialize(next, Json));
        return next;
    }

    public string[] ForgetEids() {
        cookies.Set(EidsKey, "[]");
        return [];
    }

    public List<InspectorHistoryEntry> History =>
        [.. (Read<List<InspectorHistoryEntry>>(HistoryKey) ?? []).OrderByDescending(e => e.Order)];

    public List<InspectorHistoryEntry> SaveHistory(InspectorHistoryEntry entry) {
        List<InspectorHistoryEntry> list = [.. History.Where(e => !(e.Path == entry.Path && e.FieldsJson == entry.FieldsJson
                                                                  && (e.PathParam ?? "") == (entry.PathParam ?? "")))];
        long order = list.Count == 0 ? 1 : list.Max(e => e.Order) + 1;
        list.Insert(0, entry with { Order = order });
        return WriteHistory([.. list.Take(HistoryMax)]);
    }

    public List<InspectorHistoryEntry> DeleteHistory(string id) => WriteHistory([.. History.Where(e => e.Id != id)]);

    public List<InspectorHistoryEntry> ClearHistory() => WriteHistory([]);

    private List<InspectorHistoryEntry> WriteHistory(List<InspectorHistoryEntry> list) {
        string json = JsonSerializer.Serialize(list, Json);
        while (json.Length > ValueBudget && list.Count > 0) {
            list.RemoveAt(list.Count - 1);
            json = JsonSerializer.Serialize(list, Json);
        }

        cookies.Set(HistoryKey, json);
        return list;
    }

    private T? Read<T>(string key) {
        if (cookies.Get(key) is not { Length: > 0 } raw) return default;
        try {
            return JsonSerializer.Deserialize<T>(raw, Json);
        } catch (JsonException) {
            return default;
        }
    }

    [GeneratedRegex(@"^EI\d{10,}$")]
    private static partial Regex EidRegex();
}
