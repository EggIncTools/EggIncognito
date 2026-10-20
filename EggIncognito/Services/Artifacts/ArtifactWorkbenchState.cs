using EggIdentity.UI;
using EggSharp.GameData.Artifacts;

namespace EggIncognito.Services.Artifacts;

public enum ArtifactView {
    Crafting,
    CraftsInto,
    ConsumesInto,
    ComesFrom
}

public sealed class ArtifactWorkbenchState : WorkbenchStateBase {
    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public override string HashPrefix => "artifacts";

    public string Family { get; set; } = "";
    public ArtifactView View { get; set; } = ArtifactView.Crafting;
    public int? Tier { get; set; }
    public int? Rarity { get; set; }

    public IReadOnlySet<string> Collapsed { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public bool HasFamily => Family.Length > 0;
    public bool NeedsRarity => View is ArtifactView.ConsumesInto or ArtifactView.ComesFrom;
    public bool IsRecipeView => View is ArtifactView.Crafting or ArtifactView.CraftsInto;

    public static bool HasRecipe(ArtifactTier tier) => tier.Recipe.Count > 0;

    public static bool CanCraft(ArtifactFamily family) => family.Tiers.Any(HasRecipe);

    public static bool IsUsed(ArtifactFamily family, ArtifactTier tier) =>
        ArtifactDefinitions.All.Any(f => f.Tiers.Any(t => t.Recipe.Any(i => i.AfxId == family.AfxId && i.Level == tier.Level)));

    public static bool IsUsedInCrafts(ArtifactFamily family) => family.Tiers.Any(t => IsUsed(family, t));

    public void SelectFamily(string family, int? tierLevel) {
        Family = family;
        View = ArtifactDefinitions.All.FirstOrDefault(f => f.Name == family) is { } f && !CanCraft(f)
            ? ArtifactView.ConsumesInto
            : ArtifactView.Crafting;
        Tier = tierLevel;
        Rarity = NeedsRarity ? 0 : null;
    }

    private static string ViewKey(ArtifactView view) => view switch {
        ArtifactView.CraftsInto => "uses",
        ArtifactView.ConsumesInto => "into",
        ArtifactView.ComesFrom => "from",
        _ => "craft"
    };

    private static ArtifactView ParseView(string key) => key switch {
        "uses" => ArtifactView.CraftsInto,
        "into" => ArtifactView.ConsumesInto,
        "from" => ArtifactView.ComesFrom,
        _ => ArtifactView.Crafting
    };

    public override string? Hash() {
        if (!HasFamily) return "artifacts";
        string body = $"artifacts/{Family}/{ViewKey(View)}";
        if (Tier is not { } tier) return body;
        body += $"/{tier}";
        return Rarity is not { } rarity ? body : $"{body}/{rarity}";
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

        SelectFamily(family.Name, family.Tiers.Count == 0 ? null : family.Tiers.Max(t => t.Level));
        if (parts.Length < 2) return true;

        View = ParseView(parts[1]);
        if (View == ArtifactView.Crafting && !CanCraft(family)) View = ArtifactView.ConsumesInto;
        if (View == ArtifactView.CraftsInto && !IsUsedInCrafts(family)) View = ArtifactView.ConsumesInto;
        if (!NeedsRarity) Rarity = null;
        if (parts.Length < 3 || !int.TryParse(parts[2], out int tierLevel)) return true;
        var tier = family.Tiers.FirstOrDefault(t => t.Level == tierLevel);
        if (tier is null) return true;
        Tier = tier.Level;

        if (parts.Length < 4 || !int.TryParse(parts[3], out int rarity) || rarity < 0 || rarity > 3) return true;
        Rarity = rarity;
        return true;
    }
}
