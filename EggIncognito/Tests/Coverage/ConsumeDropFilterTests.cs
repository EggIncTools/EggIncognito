using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using EggIncognito.Services.Coverage;
using Ei;

namespace EggIncognito.Tests.Coverage;

public class ConsumeDropFilterTests {
    private static readonly Func<ArtifactConsumeObservation, bool> Counted = ConsumeDropFilter.Counted.Compile();

    private static ArtifactConsumeObservation Consume(ArtifactSpec.Types.Rarity rarity, double gold) => new() {
        Action = ConsumeDropFilter.ConsumeAction,
        SpecName = "LUNAR_TOTEM",
        SpecLevel = "GREATER",
        SpecRarity = ProtoEnumNames.RarityName(rarity),
        GoldenEggs = gold,
        Success = true
    };

    [Theory]
    [InlineData(ArtifactSpec.Types.Rarity.Rare)]
    [InlineData(ArtifactSpec.Types.Rarity.Epic)]
    [InlineData(ArtifactSpec.Types.Rarity.Legendary)]
    public void OneGoldenEgg_FromRarePlus_IsServerDenial(ArtifactSpec.Types.Rarity rarity) {
        Assert.False(Counted(Consume(rarity, 1)));
    }

    [Fact]
    public void OneGoldenEgg_FromCommon_IsCounted() {
        Assert.True(Counted(Consume(ArtifactSpec.Types.Rarity.Common, 1)));
    }

    [Fact]
    public void ZeroGoldenEggs_FromRarePlus_IsCounted() {
        Assert.True(Counted(Consume(ArtifactSpec.Types.Rarity.Legendary, 0)));
    }
}
