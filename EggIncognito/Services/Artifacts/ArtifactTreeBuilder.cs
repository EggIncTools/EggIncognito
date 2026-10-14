using System.Globalization;
using EggIdentity.Diagrams;
using EggIncognito.Core.Services;
using EggIncognito.Services.Coverage;
using EggSharp.GameData.Artifacts;
using Ei;

namespace EggIncognito.Services.Artifacts;

public static class ArtifactTreeBuilder {
    public const double TierNodeWidth = 160;
    public const double TierNodeHeight = 88;

    private const string FragmentSuffix = "_FRAGMENT";
    private const string StoneSuffix = "_STONE";

    public static string TierNodeId(string familyName, int level) => $"t:{familyName}:{level}";

    public static string CellNodeId(string familyName, string levelName, string rarityName) => $"c:{familyName}:{levelName}:{rarityName}";

    public static string RarityName(int rarity) => ProtoEnumNames.RarityName((ArtifactSpec.Types.Rarity)rarity);

    public static string LevelName(int level) => ProtoEnumNames.LevelName((ArtifactSpec.Types.Level)level);

    public static bool IsFragment(string familyName) => familyName.EndsWith(FragmentSuffix, StringComparison.Ordinal);

    public static string Icon(string familyName, int level) {
        if (IsFragment(familyName)) return ArtifactAssets.IconPath(familyName[..^FragmentSuffix.Length], 1);
        int offset = familyName.EndsWith(StoneSuffix, StringComparison.Ordinal) ? 2 : 1;
        return ArtifactAssets.IconPath(ArtifactAssets.KeyFor(familyName), level + offset);
    }

    public static int LevelIndex(string levelName) =>
        Enum.TryParse<ArtifactSpec.Types.Level>(levelName, true, out var level) ? (int)level : 0;

    public static int RarityIndex(string rarityName) =>
        Enum.TryParse<ArtifactSpec.Types.Rarity>(rarityName, true, out var rarity) ? (int)rarity : 0;

    public static (ArtifactFamily Family, ArtifactTier Tier)? ResolveTierNode(IReadOnlyList<ArtifactFamily> all, string id) {
        if (!id.StartsWith("t:", StringComparison.Ordinal)) return null;
        string[] parts = id[2..].Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int level)) return null;
        var family = all.FirstOrDefault(f => f.Name == parts[0]);
        var tier = family?.Tiers.FirstOrDefault(t => t.Level == level);
        return family is null || tier is null ? null : (family, tier);
    }

    public static (string FamilyName, string LevelName, string RarityName)? ResolveCellNode(string id) {
        if (!id.StartsWith("c:", StringComparison.Ordinal)) return null;
        string[] parts = id[2..].Split(':');
        return parts.Length != 3 ? null : (parts[0], parts[1], parts[2]);
    }

    public static DiagramGraph BuildCraftingGraph(IReadOnlyList<ArtifactFamily> all, ArtifactFamily family) {
        var byAfxId = all.ToDictionary(f => f.AfxId);
        var nodes = new List<DiagramNode>();
        var edges = new List<DiagramEdge>();
        var seenFamilies = new HashSet<string>(StringComparer.Ordinal) { family.Name };
        var queue = new Queue<ArtifactFamily>();
        queue.Enqueue(family);

        while (queue.Count > 0) {
            var current = queue.Dequeue();
            foreach (var tier in current.Tiers) {
                string tierId = TierNodeId(current.Name, tier.Level);
                nodes.Add(new DiagramNode(tierId) { Width = TierNodeWidth, Height = TierNodeHeight });

                foreach (var ingredient in tier.Recipe) {
                    if (!byAfxId.TryGetValue(ingredient.AfxId, out var ingredientFamily)) continue;
                    string ingredientId = TierNodeId(ingredientFamily.Name, ingredient.Level);
                    edges.Add(new DiagramEdge(tierId, ingredientId) {
                        Weight = ingredient.Count,
                        Label = ingredient.Count.ToString(CultureInfo.InvariantCulture)
                    });
                    if (seenFamilies.Add(ingredientFamily.Name)) queue.Enqueue(ingredientFamily);
                }
            }
        }

        return new DiagramGraph(nodes, edges);
    }

    public static IReadOnlyList<string> CraftingRoots(ArtifactFamily family) =>
        [.. family.Tiers.Select(t => TierNodeId(family.Name, t.Level))];

    public static DiagramGraph BuildConsumptionGraph(DropSummary summary, string familyName, string levelName, string rarityName) {
        string rootId = CellNodeId(familyName, levelName, rarityName);
        var nodes = new List<DiagramNode> { new(rootId) { Width = TierNodeWidth, Height = TierNodeHeight } };
        var edges = new List<DiagramEdge>();
        foreach (var row in summary.Rows) {
            string childId = CellNodeId(row.Name, row.Level, row.Rarity);
            nodes.Add(new DiagramNode(childId) { Width = TierNodeWidth, Height = TierNodeHeight });
            edges.Add(new DiagramEdge(rootId, childId) { Weight = row.Mean, Label = FormatShare(row.Share) });
        }

        return new DiagramGraph(nodes, edges);
    }

    public static async Task<DiagramGraph> BuildReverseConsumptionGraphAsync(DropStats dropStats,
        IReadOnlyList<ArtifactFamily> all, string targetName, string targetLevel, string targetRarity, CancellationToken ct) {
        string targetId = CellNodeId(targetName, targetLevel, targetRarity);
        var nodes = new List<DiagramNode> { new(targetId) { Width = TierNodeWidth, Height = TierNodeHeight } };
        var edges = new List<DiagramEdge>();

        foreach (var family in all) {
            foreach (var tier in family.Tiers) {
                for (int rarity = 0; rarity < tier.RarityCount; rarity++) {
                    string rarityName = RarityName(rarity);
                    string levelName = LevelName(tier.Level);
                    var summary = await dropStats.ForCellAsync(family.Name, levelName, rarityName, ct);
                    var hit = summary.Rows.FirstOrDefault(r => r.Name == targetName && r.Level == targetLevel && r.Rarity == targetRarity);
                    if (hit is null) continue;
                    string sourceId = CellNodeId(family.Name, levelName, rarityName);
                    nodes.Add(new DiagramNode(sourceId) { Width = TierNodeWidth, Height = TierNodeHeight });
                    edges.Add(new DiagramEdge(targetId, sourceId) { Weight = hit.Mean, Label = FormatShare(hit.Share) });
                }
            }
        }

        return new DiagramGraph(nodes, edges);
    }

    private static string FormatShare(double share) => (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
