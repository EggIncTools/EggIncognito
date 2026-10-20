using System.Globalization;
using EggIdentity.Diagrams;
using EggIncognito.Core.Services;
using EggIncognito.Services.Assets;
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

    public static string DisplayName(string familyName, int level) {
        var family = ArtifactDefinitions.All.FirstOrDefault(f => f.Name == familyName);
        if (family is null) return EggIcons.Pretty(familyName);
        var tier = family.Tiers.FirstOrDefault(t => t.Level == level);
        return EggIcons.Pretty(tier?.Name ?? family.PluralName);
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

    public static DiagramGraph BuildRecipeGraph(IReadOnlyList<ArtifactFamily> all, ArtifactFamily family, ArtifactTier root) {
        var byAfxId = all.ToDictionary(f => f.AfxId);
        var nodes = new List<DiagramNode>();
        var edges = new List<DiagramEdge>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(ArtifactFamily Family, ArtifactTier Tier)>();
        queue.Enqueue((family, root));
        seen.Add(TierNodeId(family.Name, root.Level));

        while (queue.Count > 0) {
            var (current, tier) = queue.Dequeue();
            string tierId = TierNodeId(current.Name, tier.Level);
            nodes.Add(new DiagramNode(tierId) { Width = TierNodeWidth, Height = TierNodeHeight });
            foreach (var ingredient in tier.Recipe.OrderByDescending(i => i.Count)) {
                if (!byAfxId.TryGetValue(ingredient.AfxId, out var ingredientFamily)) continue;
                if (ingredientFamily.Tiers.FirstOrDefault(t => t.Level == ingredient.Level) is not { } ingredientTier) continue;
                string ingredientId = TierNodeId(ingredientFamily.Name, ingredient.Level);
                edges.Add(new DiagramEdge(tierId, ingredientId) {
                    Weight = ingredient.Count,
                    Label = ingredient.Count.ToString(CultureInfo.InvariantCulture)
                });
                if (seen.Add(ingredientId)) queue.Enqueue((ingredientFamily, ingredientTier));
            }
        }

        return new DiagramGraph(nodes, edges);
    }

    public static DiagramGraph BuildUsageGraph(IReadOnlyList<ArtifactFamily> all, ArtifactFamily family, ArtifactTier root) {
        var nodes = new List<DiagramNode>();
        var edges = new List<DiagramEdge>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(ArtifactFamily Family, ArtifactTier Tier)>();
        queue.Enqueue((family, root));
        seen.Add(TierNodeId(family.Name, root.Level));

        while (queue.Count > 0) {
            var (current, tier) = queue.Dequeue();
            string tierId = TierNodeId(current.Name, tier.Level);
            nodes.Add(new DiagramNode(tierId) { Width = TierNodeWidth, Height = TierNodeHeight });
            var users = all.SelectMany(f => f.Tiers.Select(t => (Family: f, Tier: t,
                    Count: t.Recipe.Where(i => i.AfxId == current.AfxId && i.Level == tier.Level).Sum(i => i.Count))))
                .Where(u => u.Count > 0)
                .OrderByDescending(u => u.Count);
            foreach (var (userFamily, userTier, count) in users) {
                string userId = TierNodeId(userFamily.Name, userTier.Level);
                edges.Add(new DiagramEdge(tierId, userId) {
                    Weight = count,
                    Label = count.ToString(CultureInfo.InvariantCulture)
                });
                if (seen.Add(userId)) queue.Enqueue((userFamily, userTier));
            }
        }

        return new DiagramGraph(nodes, edges);
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
        void Add(string childId, double share) {
            nodes.Add(new DiagramNode(childId) { Width = TierNodeWidth, Height = TierNodeHeight });
            edges.Add(new DiagramEdge(rootId, childId) { Weight = share, Label = FormatShare(share) });
        }

        if (summary.Gold is { } gold) Add(GoldNodeId, gold.Share);
        if (summary.Piggy is { } piggy) Add(PiggyNodeId, piggy.Share);
        foreach (var row in summary.Rows) Add(CellNodeId(row.Name, row.Level, row.Rarity), row.Share);

        return new DiagramGraph(nodes, edges);
    }

    public const string GoldNodeId = "r:gold";
    public const string PiggyNodeId = "r:piggy";

    public static bool IsShareGraph(DiagramGraph graph) => graph.Nodes.Any(n => !n.Id.StartsWith("t:", StringComparison.Ordinal));

    public static async Task<DiagramGraph> BuildReverseConsumptionGraphAsync(DropStats dropStats,
        string targetName, string targetLevel, string targetRarity, CancellationToken ct) {
        string targetId = CellNodeId(targetName, targetLevel, targetRarity);
        var nodes = new List<DiagramNode> { new(targetId) { Width = TierNodeWidth, Height = TierNodeHeight } };
        var edges = new List<DiagramEdge>();

        var sources = await dropStats.SourcesOfAsync(targetName, targetLevel, targetRarity, ct);
        foreach (var (family, level, rarity, hit) in sources.OrderByDescending(s => s.Hit.Share)) {
            string sourceId = CellNodeId(family, level, rarity);
            nodes.Add(new DiagramNode(sourceId) { Width = TierNodeWidth, Height = TierNodeHeight });
            edges.Add(new DiagramEdge(targetId, sourceId) { Weight = hit.Share, Label = FormatShare(hit.Share) });
        }

        return new DiagramGraph(nodes, edges);
    }

    private static string FormatShare(double share) => EggIdentity.Formatting.NumberFormat.Percent(share);
}
