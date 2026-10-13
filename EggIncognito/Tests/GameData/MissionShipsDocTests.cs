using System.Text.Json.Nodes;
using EggIncognito.Core.Services;
using EggSharp.GameData;
using Ei;
using Spaceship = Ei.MissionInfo.Types.Spaceship;
using DurationType = Ei.MissionInfo.Types.DurationType;
using MissionParameters = Ei.ArtifactsConfigurationResponse.Types.MissionParameters;

namespace EggIncognito.Tests.GameData;

public sealed class MissionShipsDocTests {
    private static MissionParameters.Types.Duration Duration(DurationType type, double seconds, uint capacity) =>
        new() {
            DurationType = type,
            Seconds = seconds,
            Capacity = capacity,
            LevelCapacityBump = 0,
            Quality = 1.0f,
            MinQuality = 0.0f,
            MaxQuality = 1.4f,
            LevelQualityBump = 0.1f
        };

    private static ArtifactsConfigurationResponse Config() {
        var one = new MissionParameters { Ship = Spaceship.ChickenOne };
        one.LevelMissionRequirements.Add([4, 6, 12]);
        one.Durations.Add([
            Duration(DurationType.Tutorial, 60, 2),
            Duration(DurationType.Short, 1200, 4),
            Duration(DurationType.Long, 3600, 5)
        ]);
        var nine = new MissionParameters { Ship = Spaceship.ChickenNine };
        nine.Durations.Add(Duration(DurationType.Short, 1800, 9));
        var config = new ArtifactsConfigurationResponse();
        config.MissionParameters.Add([one, nine]);
        return config;
    }

    [Fact]
    public void BuildMissionShips_NestsDurationsPerShip_AndValidates() {
        var doc = GameDataDocBuilders.BuildMissionShips(Config(), "1.37.2");

        GameDataProvider.Validate(MissionShipCatalog.DocumentId, doc.Json);
        Assert.Equal(4, doc.Count);

        var root = JsonNode.Parse(doc.Json)!.AsObject();
        Assert.Equal(["ships", "binaryVersion", "provenance"], root.Select(p => p.Key));
        Assert.Equal("1.37.2", (string?)root["binaryVersion"]);

        var ships = root["ships"]!.AsArray();
        Assert.Equal(["CHICKEN_ONE", "CHICKEN_NINE"], ships.Select(s => (string?)s!["ship"]));

        var first = ships[0]!;
        Assert.Equal([4, 6, 12], first["levelMissionRequirements"]!.AsArray().Select(n => (int)n!));
        Assert.Equal(["TUTORIAL", "SHORT", "LONG"], first["durations"]!.AsArray().Select(d => (string?)d!["type"]));

        var shortRow = first["durations"]![1]!;
        Assert.Equal(1200.0, (double)shortRow["seconds"]!);
        Assert.Equal(4, (int)shortRow["capacity"]!);
        Assert.Equal(0, (int)shortRow["levelCapacityBump"]!);
        Assert.Equal(1.4, (double)shortRow["maxQuality"]!, 5);
        Assert.Equal(0.1, (double)shortRow["levelQualityBump"]!, 5);
        Assert.Null(shortRow["fuel"]);
        Assert.Null(shortRow["id"]);

        Assert.False(ships[1]!.AsObject().ContainsKey("levelMissionRequirements"));

        var provenance = root["provenance"]!["ships"]!;
        Assert.Equal("fixture", (string?)provenance["origin"]);
        Assert.Equal("ei_afx/config", (string?)provenance["locator"]);
        Assert.Equal("captured", (string?)provenance["method"]);
    }

    [Fact]
    public void BuildMissionShipsFromJson_ParsesProtoJson() {
        string json = Google.Protobuf.JsonFormatter.Default.Format(Config());

        var doc = GameDataDocBuilders.BuildMissionShipsFromJson(json, "1.37.2");

        Assert.Equal(GameDataDocBuilders.BuildMissionShips(Config(), "1.37.2").Json, doc.Json);
    }
}
