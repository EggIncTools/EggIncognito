using EggIncognito.Services.Artifacts;
using EggIncognito.Services.Coverage;
using EggSharp.GameData.Artifacts;

namespace EggIncognito.Tests.Artifacts;

public sealed class ArtifactTreeBuilderTests {
    private static ArtifactFamily Family(string name, int afxId, ArtifactKind kind, params ArtifactTier[] tiers) =>
        new(name, afxId, name, name + "S", kind, 0, afxId, "", tiers);

    private static ArtifactTier Tier(int level, string name, int rarityCount, IReadOnlyList<ArtifactIngredient>? recipe = null,
        IReadOnlyList<ArtifactEffect>? effects = null) =>
        new(level, name, rarityCount, recipe ?? [], effects ?? []);

    [Fact]
    public void BuildCraftingGraph_AddsOneNodePerTier() {
        var family = Family("LUNAR_TOTEM", 1, ArtifactKind.Artifact, Tier(0, "LESSER", 2), Tier(1, "GREATER", 3));
        var graph = ArtifactTreeBuilder.BuildCraftingGraph([family], family);

        Assert.Equal([ArtifactTreeBuilder.TierNodeId("LUNAR_TOTEM", 0), ArtifactTreeBuilder.TierNodeId("LUNAR_TOTEM", 1)],
            graph.Nodes.Select(n => n.Id));
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void BuildCraftingGraph_FollowsCrossFamilyIngredients() {
        var stone = Family("LUNAR_STONE", 2, ArtifactKind.Stone, Tier(0, "INFERIOR", 1));
        var artifact = Family("LUNAR_TOTEM", 1, ArtifactKind.Artifact,
            Tier(0, "LESSER", 1, [new ArtifactIngredient(2, 0, 3)]));
        var all = new List<ArtifactFamily> { artifact, stone };

        var graph = ArtifactTreeBuilder.BuildCraftingGraph(all, artifact);

        string artifactTierId = ArtifactTreeBuilder.TierNodeId("LUNAR_TOTEM", 0);
        string stoneTierId = ArtifactTreeBuilder.TierNodeId("LUNAR_STONE", 0);
        Assert.Contains(stoneTierId, graph.Nodes.Select(n => n.Id));
        var edge = Assert.Single(graph.Edges, e => e.From == artifactTierId && e.To == stoneTierId);
        Assert.Equal(3, edge.Weight);
    }

    [Fact]
    public void BuildCraftingGraph_SkipsUnresolvableIngredient() {
        var family = Family("LUNAR_TOTEM", 1, ArtifactKind.Artifact,
            Tier(0, "LESSER", 1, [new ArtifactIngredient(999, 0, 1)]));

        var graph = ArtifactTreeBuilder.BuildCraftingGraph([family], family);

        Assert.DoesNotContain(graph.Edges, e => e.To.Contains("999"));
    }

    [Fact]
    public void CraftingRoots_ReturnsEveryTier() {
        var family = Family("LUNAR_TOTEM", 1, ArtifactKind.Artifact, Tier(0, "LESSER", 1), Tier(1, "GREATER", 1));
        var roots = ArtifactTreeBuilder.CraftingRoots(family);
        Assert.Equal([ArtifactTreeBuilder.TierNodeId("LUNAR_TOTEM", 0), ArtifactTreeBuilder.TierNodeId("LUNAR_TOTEM", 1)], roots);
    }

    [Fact]
    public void Icon_FragmentUsesFirstStoneIcon() {
        Assert.EndsWith("LUNAR_STONE/LUNAR_STONE_1.png", ArtifactTreeBuilder.Icon("LUNAR_STONE_FRAGMENT", 0));
    }

    [Fact]
    public void Icon_StoneSkipsFragmentSlot() {
        Assert.EndsWith("LUNAR_STONE/LUNAR_STONE_2.png", ArtifactTreeBuilder.Icon("LUNAR_STONE", 0));
    }

    [Fact]
    public void BuildConsumptionGraph_AddsOneChildPerRow() {
        var summary = new DropSummary("LUNAR_TOTEM", "GREATER", "COMMON", 3,
            [new DropRow("LUNAR_STONE_FRAGMENT", "INFERIOR", "COMMON", 2, 2.0 / 3, 1.5, 0, 2, [])]);

        var graph = ArtifactTreeBuilder.BuildConsumptionGraph(summary, "LUNAR_TOTEM", "GREATER", "COMMON");

        string rootId = ArtifactTreeBuilder.CellNodeId("LUNAR_TOTEM", "GREATER", "COMMON");
        string childId = ArtifactTreeBuilder.CellNodeId("LUNAR_STONE_FRAGMENT", "INFERIOR", "COMMON");
        Assert.Contains(rootId, graph.Nodes.Select(n => n.Id));
        Assert.Contains(childId, graph.Nodes.Select(n => n.Id));
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(2.0 / 3, edge.Weight);
        Assert.Equal("67%", edge.Label);
    }

    [Fact]
    public void ResolveTierNode_RoundTrips() {
        var family = Family("LUNAR_TOTEM", 1, ArtifactKind.Artifact, Tier(0, "LESSER", 1));
        var resolved = ArtifactTreeBuilder.ResolveTierNode([family], ArtifactTreeBuilder.TierNodeId("LUNAR_TOTEM", 0));
        Assert.NotNull(resolved);
        Assert.Equal("LUNAR_TOTEM", resolved!.Value.Family.Name);
        Assert.Equal("LESSER", resolved.Value.Tier.Name);
    }

    [Fact]
    public void ResolveCellNode_SplitsThreeParts() {
        var resolved = ArtifactTreeBuilder.ResolveCellNode(ArtifactTreeBuilder.CellNodeId("LUNAR_TOTEM", "GREATER", "COMMON"));
        Assert.Equal(("LUNAR_TOTEM", "GREATER", "COMMON"), resolved);
    }
}
