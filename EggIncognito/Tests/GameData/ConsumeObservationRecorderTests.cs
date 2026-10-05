using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Capture;
using EggIncognito.Services.Devices;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests.GameData;

public class ConsumeObservationRecorderTests {
    [Fact]
    public void ActionFor_MatchesConsumeAndDemoteOnly() {
        Assert.Equal("consume", ConsumeObservationRecorder.ActionFor("ei_afx/consume_artifact"));
        Assert.Equal("demote", ConsumeObservationRecorder.ActionFor("ei_afx/demote_artifact"));
        Assert.Equal("craft", ConsumeObservationRecorder.ActionFor("ei_afx/craft_artifact"));
        Assert.Null(ConsumeObservationRecorder.ActionFor("ei_afx/set_artifact"));
        Assert.Null(ConsumeObservationRecorder.ActionFor("ei/get_config"));
    }

    [Fact]
    public void Build_RecordsSpecByproductsAndGoldenEggs() {
        var request = new ConsumeArtifactRequest {
            Rinfo = new BasicRequestInfo { Version = "1.37" },
            Spec = new ArtifactSpec {
                Name = ArtifactSpec.Types.Name.BookOfBasan,
                Level = ArtifactSpec.Types.Level.Greater,
                Rarity = ArtifactSpec.Types.Rarity.Legendary
            },
            Quantity = 3
        };
        var response = new ConsumeArtifactResponse {
            Success = true,
            Byproducts = {
                Fragment(ArtifactSpec.Types.Name.ProphecyStoneFragment),
                Fragment(ArtifactSpec.Types.Name.ProphecyStoneFragment),
                Fragment(ArtifactSpec.Types.Name.SoulStoneFragment)
            },
            OtherRewards = {
                new Reward { RewardType = RewardType.Gold, RewardAmount = 42 },
                new Reward { RewardType = RewardType.PiggyFill, RewardAmount = 7 }
            }
        };

        var row = ConsumeObservationRecorder.Build("consume", "ios-1",
            Flow("ei_afx/consume_artifact", request, response), DateTimeOffset.UnixEpoch);

        Assert.NotNull(row);
        Assert.Equal("consume", row.Action);
        Assert.Equal("BOOK_OF_BASAN", row.SpecName);
        Assert.Equal("GREATER", row.SpecLevel);
        Assert.Equal("LEGENDARY", row.SpecRarity);
        Assert.Equal(3, row.CountRequested);
        Assert.Equal(42, row.GoldenEggs);
        Assert.True(row.Success);
        Assert.Equal("1.37", row.ClientVersion);
        Assert.Equal("ios-1", row.DeviceId);

        var byproducts = JsonNode.Parse(row.Byproducts)!.AsArray();
        Assert.Equal(2, byproducts.Count);
        var prophecy = byproducts.First(b => b!["name"]!.GetValue<string>() == "PROPHECY_STONE_FRAGMENT")!;
        Assert.Equal(2, prophecy["count"]!.GetValue<int>());

        var rewards = JsonNode.Parse(row.OtherRewards)!.AsArray();
        Assert.Equal(2, rewards.Count);
        Assert.Contains(rewards, r => r!["rewardType"]!.GetValue<string>() == "PIGGY_FILL");
    }

    [Fact]
    public void Build_ReturnsNullWhenEitherSideIsMissing() {
        var request = new ConsumeArtifactRequest { Spec = new ArtifactSpec() };
        var flow = Flow("ei_afx/consume_artifact", request, new ConsumeArtifactResponse()) with {
            ResponseJsonRaw = null
        };
        Assert.Null(ConsumeObservationRecorder.Build("consume", "ios-1", flow, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Build_DefaultsCountToOneWhenQuantityIsAbsent() {
        var request = new ConsumeArtifactRequest {
            Spec = new ArtifactSpec { Name = ArtifactSpec.Types.Name.LunarTotem }
        };
        var row = ConsumeObservationRecorder.Build("demote", "android-1",
            Flow("ei_afx/demote_artifact", request, new ConsumeArtifactResponse { Success = true }),
            DateTimeOffset.UnixEpoch);

        Assert.NotNull(row);
        Assert.Equal(1, row.CountRequested);
        Assert.Equal(0, row.GoldenEggs);
        Assert.Equal("[]", Compact(row.Byproducts));
    }

    [Fact]
    public void Build_RecordsCraftedRarityAgainstTheRequestedSpec() {
        var request = new CraftArtifactRequest {
            Rinfo = new BasicRequestInfo { Version = "1.37" },
            Spec = new ArtifactSpec {
                Name = ArtifactSpec.Types.Name.TungstenAnkh,
                Level = ArtifactSpec.Types.Level.Lesser,
                Rarity = ArtifactSpec.Types.Rarity.Common
            },
            GoldPricePaid = 3982,
            CraftingCount = 17
        };
        var response = new CraftArtifactResponse {
            ItemId = 991,
            RarityAchieved = ArtifactSpec.Types.Rarity.Rare
        };

        var row = ConsumeObservationRecorder.Build("craft", "ios-1",
            Flow("ei_afx/craft_artifact", request, response), DateTimeOffset.UnixEpoch);

        Assert.NotNull(row);
        Assert.Equal("craft", row.Action);
        Assert.Equal("TUNGSTEN_ANKH", row.SpecName);
        Assert.Equal("LESSER", row.SpecLevel);
        Assert.Equal("COMMON", row.SpecRarity);
        Assert.Equal("RARE", row.RarityAchieved);
        Assert.Equal(3982, row.GoldPricePaid);
        Assert.Equal(17, row.CraftingCount);
        Assert.True(row.Success);
        Assert.Equal("ios-1", row.DeviceId);
    }

    [Fact]
    public void ConsumeRow_CarriesTheCallersSuccessVerdict() {
        var request = new ConsumeArtifactRequest {
            Spec = new ArtifactSpec { Name = ArtifactSpec.Types.Name.LunarTotem }
        };
        var row = ConsumeObservationRecorder.ConsumeRow("consume", "android-1", request,
            new ConsumeArtifactResponse { Success = true }, success: false, DateTimeOffset.UnixEpoch);

        Assert.NotNull(row);
        Assert.Equal("consume", row.Action);
        Assert.False(row.Success);
    }

    [Fact]
    public void CraftRow_MapsSpecRarityPriceAndCount() {
        var request = new CraftArtifactRequest {
            Spec = new ArtifactSpec {
                Name = ArtifactSpec.Types.Name.TungstenAnkh,
                Level = ArtifactSpec.Types.Level.Lesser,
                Rarity = ArtifactSpec.Types.Rarity.Common
            },
            GoldPricePaid = 1200,
            CraftingCount = 4
        };
        var row = ConsumeObservationRecorder.CraftRow(null, request,
            new CraftArtifactResponse { ItemId = 5, RarityAchieved = ArtifactSpec.Types.Rarity.Epic },
            DateTimeOffset.UnixEpoch);

        Assert.NotNull(row);
        Assert.Equal("TUNGSTEN_ANKH", row.SpecName);
        Assert.Equal("LESSER", row.SpecLevel);
        Assert.Equal("COMMON", row.SpecRarity);
        Assert.Equal("EPIC", row.RarityAchieved);
        Assert.Equal(1200, row.GoldPricePaid);
        Assert.Equal(4, row.CraftingCount);
        Assert.Null(row.DeviceId);
    }

    private static ArtifactSpec Fragment(ArtifactSpec.Types.Name name) =>
        new() {
            Name = name,
            Level = ArtifactSpec.Types.Level.Inferior,
            Rarity = ArtifactSpec.Types.Rarity.Common
        };

    private static DashboardFlow Flow(string path, IMessage request, IMessage response) =>
        new(0, "", path, "POST", 200,
            null, null, "", null,
            RequestJsonRaw: JsonFormatter.Default.Format(request),
            ResponseJsonRaw: JsonFormatter.Default.Format(response));

    private static string Compact(string json) =>
        JsonSerializer.Serialize(JsonNode.Parse(json));
}
