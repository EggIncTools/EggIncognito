using EggIncognito.Services.Periodicals;

namespace EggIncognito.Tests.Periodicals;

public class SeasonColleggtiblesTests {
    private static readonly string[] Seasons = ["winter_2026", "fall_2025", "summer_2025", "spring_2025"];

    [Fact]
    public void EggFirstSeenInASeason_BelongsToThatSeasonOnly() {
        EggSighting[] sightings = [
            new("pegg", "fall_2025", 1_760_000_000, "Pegg Debut"),
            new("pegg", "fall_2025", 1_762_000_000, "Pegg Again"),
            new("pegg", "winter_2026", 1_766_000_000, "Pegg Rerun")
        ];

        var result = SeasonColleggtibles.Attribute(sightings, Seasons, _ => null);

        var egg = Assert.Single(Assert.Single(result).Value);
        Assert.Equal("fall_2025", result.Keys.Single());
        Assert.Equal("pegg", egg.Id);
        Assert.Equal(["Pegg Debut", "Pegg Again"], egg.Contracts);
        Assert.DoesNotContain("winter_2026", result.Keys);
    }

    [Fact]
    public void EggThatExistedBeforeSeasons_IsNeverASeasonEgg() {
        EggSighting[] sightings = [
            new("pumpkin", "", 1_696_000_000, "Smells Like October"),
            new("pumpkin", "fall_2025", 1_758_000_000, "Smells Like September")
        ];

        var result = SeasonColleggtibles.Attribute(sightings, Seasons, _ => null);

        Assert.Empty(result);
    }

    [Fact]
    public void FirstSightingInUnknownSeason_IsDropped() {
        EggSighting[] sightings = [
            new("firework", "spring_2024", 1_710_000_000, "Boom"),
            new("firework", "summer_2025", 1_750_000_000, "Boom Again")
        ];

        var result = SeasonColleggtibles.Attribute(sightings, Seasons, _ => null);

        Assert.Empty(result);
    }

    [Fact]
    public void IconResolvesPerEggAndZeroStartTimesAreIgnored() {
        EggSighting[] sightings = [
            new("ice", "winter_2026", 0, "Phantom"),
            new("ice", "winter_2026", 1_767_000_000, "Frozen")
        ];

        var result = SeasonColleggtibles.Attribute(sightings, Seasons, id => $"/icon/{id}");

        var egg = Assert.Single(result["winter_2026"]);
        Assert.Equal("/icon/ice", egg.Icon);
        Assert.Equal(["Frozen"], egg.Contracts);
    }
}
