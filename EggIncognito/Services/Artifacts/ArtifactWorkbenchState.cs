using EggIdentity.UI;
using EggSharp.GameData.Artifacts;

namespace EggIncognito.Services.Artifacts;

public enum ArtifactTreeKind {
    Craft,
    Consume
}

public sealed class ArtifactWorkbenchState : WorkbenchStateBase {
    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public override string HashPrefix => "artifacts";

    public string Family { get; set; } = "";
    public ArtifactTreeKind Tree { get; set; } = ArtifactTreeKind.Craft;
    public int? Tier { get; set; }
    public int? Rarity { get; set; }
    public bool Reverse { get; set; }

    public bool CoverageOverlay { get; set; }
    public bool EffectsOverlay { get; set; }
    public bool RarityOverlay { get; set; }

    public IReadOnlySet<string> Collapsed { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public bool HasFamily => Family.Length > 0;

    public void SelectFamily(string family) {
        Family = family;
        Tree = ArtifactTreeKind.Craft;
        Tier = null;
        Rarity = null;
        Reverse = false;
    }

    public override string? Hash() {
        if (!HasFamily) return "artifacts";
        string body = $"artifacts/{Family}/{(Tree == ArtifactTreeKind.Consume ? "consume" : "craft")}";
        if (Tier is not { } tier) return body;
        body += $"/{tier}";
        if (Rarity is not { } rarity) return body;
        body += $"/{rarity}";
        return Reverse ? body + "/rev" : body;
    }

    public override bool ApplyHash(string? hash) {
        string body = (hash ?? "").TrimStart('#');
        if (!body.StartsWith("artifacts", StringComparison.Ordinal)) return false;
        string rest = body.Length > "artifacts".Length && body["artifacts".Length] == '/' ? body["artifacts/".Length..]
            : body == "artifacts" ? "" : "\0none";
        if (rest == "\0none") return false;
        if (rest.Length == 0) {
            Family = "";
            return true;
        }

        string[] parts = rest.Split('/');
        if (parts.Length == 0 || parts[0].Length == 0) return false;
        var family = ArtifactDefinitions.All.FirstOrDefault(f => f.Name == parts[0]);
        if (family is null) {
            Family = "";
            return true;
        }

        SelectFamily(family.Name);
        if (parts.Length < 2) return true;

        Tree = parts[1] == "consume" ? ArtifactTreeKind.Consume : ArtifactTreeKind.Craft;
        if (parts.Length < 3 || !int.TryParse(parts[2], out int tierLevel)) return true;
        var tier = family.Tiers.FirstOrDefault(t => t.Level == tierLevel);
        if (tier is null) return true;
        Tier = tier.Level;

        if (parts.Length < 4 || !int.TryParse(parts[3], out int rarity) || rarity < 0 || rarity >= tier.RarityCount) return true;
        Rarity = rarity;
        Reverse = Tree == ArtifactTreeKind.Consume && parts.Length >= 5 && parts[4] == "rev";
        return true;
    }
}
