using EggIdentity.UI;
using EggIncognito.Components.Capture;
using EggIncognito.Models.Data;

namespace EggIncognito.Services.Api;

public enum DataPaneKind {
    Dataset,
    Coverage
}

public sealed class DataWorkbenchState : WorkbenchStateBase {
    public const string PeriodicalGroup = "periodical";

    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public override string HashPrefix => "data";

    public DataPaneKind Kind { get; set; } = DataPaneKind.Dataset;

    public string Group { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Sub { get; set; }
    public List<DataSourceRow>? Sources { get; set; }
    public bool KeysOpen { get; set; }

#pragma warning disable IDE0028
    public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Formats { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DataPayloadEntry> Payloads { get; } = new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    public CaptureViewState View { get; } = new();

    public bool HasDataset => Kind == DataPaneKind.Dataset && Group.Length > 0 && Id.Length > 0;

    public string DatasetKey => Sub is { Length: > 0 } sub ? $"{Group}/{Id}/{sub}" : $"{Group}/{Id}";

    public string NameFor(string key) => Names.GetValueOrDefault(key, "");

    public DataSourceRow? CurrentDataset() {
        var root = Sources?.Find(s => s.Group == Group && s.Id == Id);
        if (root is null) return null;
        return Sub is not { Length: > 0 } sub ? root : root.Children?.Find(c => c.Id == sub);
    }

    public void SelectDataset(string group, string id, string? sub) {
        Kind = DataPaneKind.Dataset;
        Group = group;
        Id = id;
        Sub = string.IsNullOrEmpty(sub) ? null : sub;
    }

    public override string? Hash() {
        return Kind switch {
            DataPaneKind.Dataset when Group.Length > 0 && Id.Length > 0 =>
                Sub is { Length: > 0 } sub ? $"data/{Group}/{Id}/{sub}" : $"data/{Group}/{Id}",
            DataPaneKind.Coverage => "data/coverage",
            _ => "data"
        };
    }

    public override bool ApplyHash(string? hash) {
        string body = (hash ?? "").TrimStart('#');
        if (!body.StartsWith("data", StringComparison.Ordinal)) return false;
        string? rest = body.Length > 4 && body[4] == '/' ? body[5..] : body == "data" ? "" : null;
        if (rest is null) return false;

        if (rest == "coverage") {
            Kind = DataPaneKind.Coverage;
            return true;
        }

        if (rest.Length == 0) return true;

        string[] parts = rest.Split('/');
        if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0 || p.Contains('.', StringComparison.Ordinal))) return false;
        SelectDataset(parts[0], parts[1], parts.Length == 3 ? parts[2] : null);
        return true;
    }
}
