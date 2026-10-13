using EggIncognito.Core.Services.Farm;
using EggSharp.GameData.Farm;
using AssetType = Ei.ShellSpec.Types.AssetType;
using FarmElement = Ei.ShellDB.Types.FarmElement;

namespace EggIncognito.Tests.Farm;

public class FarmPlacementEngineTests {
    private static readonly FarmPlacementData Data = FarmPlacementDataFixture.Build();

    private static FarmState Universe() => new() {
        Habs = [18, 18, 18, 18],
        SilosOwned = 4,
        SiloAssetType = AssetType.Silo1Large,
        LabTier = 0,
        DepotTier = 0,
        HomeFarm = true
    };

    [Fact]
    public void HabRow_MatchesGetHabPosition() {
        var s = Universe();
        Assert.Equal(-12f, FarmPlacementEngine.HabPosition(s, Data, 0).X, 4);
        Assert.Equal(0.5f, FarmPlacementEngine.HabPosition(s, Data, 1).X, 4);
        Assert.Equal(-24.5f, FarmPlacementEngine.HabPosition(s, Data, 2).X, 4);
        Assert.Equal(13f, FarmPlacementEngine.HabPosition(s, Data, 3).X, 4);
    }

    [Fact]
    public void HabRow_ReadsLeftToRightAsSlots2013() {
        var s = Universe();
        float[] xs = [.. Enumerable.Range(0, 4).Select(i => FarmPlacementEngine.HabPosition(s, Data, i).X)];
        int[] order = [.. Enumerable.Range(0, 4).OrderBy(i => xs[i])];
        Assert.Equal([2, 0, 1, 3], order);
    }

    [Fact]
    public void HabRow_SharesRowYAndZ() {
        var s = Universe();
        for (int i = 0; i < 4; i++) {
            var p = FarmPlacementEngine.HabPosition(s, Data, i);
            Assert.Equal(0f, p.Y);
            Assert.Equal(-10.5f, p.Z);
        }
    }

    [Fact]
    public void HabRow_ShiftsWhenSlotZeroChangesTier() {
        var narrow = Universe() with { Habs = [0, 18, 18, 18] };
        Assert.Equal(-12f, FarmPlacementEngine.HabPosition(narrow, Data, 0).X, 4);
        Assert.Equal(-12f + 1.5f + 4.75f + 3f, FarmPlacementEngine.HabPosition(narrow, Data, 1).X, 4);
    }

    [Fact]
    public void Silos_MatchUpdateSilo() {
        Assert.Equal(new Vec3(-5f, 0f, 5.5f), FarmPlacementEngine.SiloPosition(Data, 0));
        Assert.Equal(new Vec3(-5f, 0f, -0.5f), FarmPlacementEngine.SiloPosition(Data, 1));
        Assert.Equal(new Vec3(-11f, 0f, 5.5f), FarmPlacementEngine.SiloPosition(Data, 2));
        Assert.Equal(new Vec3(-11f, 0f, -0.5f), FarmPlacementEngine.SiloPosition(Data, 3));
        Assert.Equal(new Vec3(-17f, 0f, 5.5f), FarmPlacementEngine.SiloPosition(Data, 4));
    }

    [Fact]
    public void TrophyGrid_IsFiveColumns() {
        Assert.Equal(new Vec3(-6.831f, 0.143f, 11.4539995f), FarmPlacementEngine.TrophyPosition(Data, 0));
        Assert.Equal(-4.063f, FarmPlacementEngine.TrophyPosition(Data, 4).X, 3);
        var second = FarmPlacementEngine.TrophyPosition(Data, 5);
        Assert.Equal(-6.831f, second.X, 3);
        Assert.Equal(0.842f, second.Y, 3);
        Assert.Equal(11.154f, second.Z, 3);
    }

    [Fact]
    public void TrophyBonus_SitsInGridSlotNineteen() {
        var slot19 = FarmPlacementEngine.TrophyPosition(Data, 19);
        Assert.Equal(Data.Trophy.BonusPos.X, slot19.X, 3);
    }

    [Fact]
    public void Hoa_UsesTheLargestExtentOnAHomeFarm() {
        var s = Universe();
        var extents = FarmPlacementEngine.ResolveExtents(s, Data);
        Assert.Equal(10.2f, extents.Lab, 3);
        Assert.Equal(9.0f, extents.Depot, 3);

        Assert.Equal(12f, extents.Hatchery, 3);

        var hoa = FarmPlacementEngine.HoaPosition(s, Data, extents);
        Assert.Equal(14f, hoa.X, 3);
        Assert.Equal(0f, hoa.Y);
        Assert.Equal(-3.5f, hoa.Z, 3);
    }

    [Fact]
    public void Hoa_TakesTheLabOnlyBranchOffHomeFarm() {
        var s = Universe() with { HomeFarm = false };
        var extents = FarmPlacementEngine.ResolveExtents(s, Data);
        Assert.Equal(11.9f, FarmPlacementEngine.HoaPosition(s, Data, extents).X, 3);
    }

    [Fact]
    public void MissionControl_UsesPoseZeroWithoutArtifacts() {
        var s = Universe();
        var extents = FarmPlacementEngine.ResolveExtents(s, Data);
        var mc = FarmPlacementEngine.MissionControlPosition(s, Data, extents);
        Assert.Equal(16.3f, mc.X, 3);
        Assert.Equal(3.7f, mc.Z, 3);
    }

    [Fact]
    public void MissionControl_UsesTheTierPoseWithArtifacts() {
        var s = Universe() with { ArtifactsEnabled = true, MissionControlLevel = 2 };
        var extents = FarmPlacementEngine.ResolveExtents(s, Data);
        var mc = FarmPlacementEngine.MissionControlPosition(s, Data, extents);
        Assert.Equal(19f, mc.X, 3);
        Assert.Equal(6f, mc.Z, 3);
    }

    [Fact]
    public void FuelTank_AddsSpacingAndTheLockedPenalty() {
        var s = Universe();
        var extents = FarmPlacementEngine.ResolveExtents(s, Data);
        var locked = FarmPlacementEngine.FuelTankPosition(s, Data, extents);
        Assert.Equal(22.5f, locked.X, 3);
        Assert.Equal(4.2f, locked.Z, 3);

        var unlocked = FarmPlacementEngine.FuelTankPosition(s with { FuelTankUnlocked = true }, Data, extents);
        Assert.Equal(21f, unlocked.X, 3);
        Assert.Equal(3.7f, unlocked.Z, 3);
    }

    [Fact]
    public void Place_EmitsGroundHardscapeAndMailboxAtTheOrigin() {
        var placements = FarmPlacementEngine.Place(Universe(), Data).Placements;
        foreach (var type in (AssetType[])[AssetType.Ground, AssetType.Hardscape, AssetType.Mailbox]) {
            var p = Assert.Single(placements, x => x.AssetType == type);
            Assert.Equal(Vec3.Zero, p.Pos);
            Assert.Equal(1f, p.Scale);
        }
    }

    [Fact]
    public void Place_SwapsMailboxForMailboxFullOnUnreadMail() {
        var placements = FarmPlacementEngine.Place(Universe() with { HasUnreadMail = true }, Data).Placements;
        Assert.Contains(placements, p => p.AssetType == AssetType.MailboxFull);
        Assert.DoesNotContain(placements, p => p.AssetType == AssetType.Mailbox);
    }

    [Fact]
    public void Place_SkipsGroundInArMode() {
        var placements = FarmPlacementEngine.Place(Universe() with { ArMode = true }, Data).Placements;
        Assert.DoesNotContain(placements, p => p.AssetType == AssetType.Ground);
    }

    [Fact]
    public void Place_FollowsTheUpdateAllCompositionOrder() {
        var elements = FarmPlacementEngine.Place(Universe(), Data).Placements
            .Select(p => p.Element)
            .Distinct()
            .ToList();

        int Index(FarmElement e) => elements.IndexOf(e);

        Assert.True(Index(FarmElement.Ground) < Index(FarmElement.HenHouse));
        Assert.True(Index(FarmElement.HenHouse) < Index(FarmElement.Silo));
        Assert.True(Index(FarmElement.Silo) < Index(FarmElement.Depot));
        Assert.True(Index(FarmElement.Depot) < Index(FarmElement.Lab));
        Assert.True(Index(FarmElement.Lab) < Index(FarmElement.Hatchery));
        Assert.True(Index(FarmElement.Hatchery) < Index(FarmElement.Hoa));
        Assert.True(Index(FarmElement.Hoa) < Index(FarmElement.MissionControl));
        Assert.True(Index(FarmElement.MissionControl) < Index(FarmElement.FuelTank));
        Assert.True(Index(FarmElement.FuelTank) < Index(FarmElement.TrophyCase));
    }

    [Fact]
    public void Place_OmitsEmptyHabSlots() {
        var s = Universe() with { Habs = [18, FarmState.EmptyHabTier, 18, FarmState.EmptyHabTier] };
        var habs = FarmPlacementEngine.Place(s, Data).Placements
            .Where(p => p.Element == FarmElement.HenHouse)
            .ToList();
        Assert.Equal(2, habs.Count);
        Assert.Equal([0, 2], habs.Select(p => p.Index));
    }

    [Fact]
    public void Place_EmitsOneSiloPerOwnedSilo() {
        var silos = FarmPlacementEngine.Place(Universe() with { SilosOwned = 7 }, Data).Placements
            .Where(p => p.Element == FarmElement.Silo)
            .ToList();
        Assert.Equal(7, silos.Count);
        Assert.All(silos, p => Assert.Equal(AssetType.Silo1Large, p.AssetType));
    }

    [Fact]
    public void Place_EmitsBothHyperloopPiecesOnlyWithAStation() {
        Assert.DoesNotContain(FarmPlacementEngine.Place(Universe(), Data).Placements,
            p => p.Element == FarmElement.Hyperloop);

        var built = FarmPlacementEngine.Place(Universe() with { HyperloopStation = true }, Data).Placements
            .Where(p => p.Element == FarmElement.Hyperloop)
            .ToList();
        Assert.Equal(2, built.Count);
        Assert.Contains(built, p => p.AssetType == AssetType.Hyperloop);
        Assert.Contains(built, p => p.AssetType == AssetType.HyperloopTrack);
    }

    [Fact]
    public void Place_TagsEveryPlacementWithBinaryProvenance() {
        var placements = FarmPlacementEngine.Place(Universe(), Data).Placements;
        Assert.NotEmpty(placements);
        Assert.All(placements, p => Assert.Equal(PlacementOrigin.Binary, p.Provenance.Origin));
        Assert.All(placements, p => Assert.False(string.IsNullOrEmpty(p.Provenance.Locator)));
    }

    [Fact]
    public void Place_PlacesEachSlotExactlyOnce() {
        var s = Universe() with { HyperloopStation = true, SilosOwned = 10 };
        var keys = FarmPlacementEngine.Place(s, Data).Placements
            .Where(p => p.AssetType is not null)
            .Select(p => (p.AssetType, p.Index))
            .ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Trophies_AppearOnlyForEarnedMedals() {
        var s = Universe() with { EggMedalLevel = [1, 0, 3, 5] };
        var trophies = FarmPlacementEngine.Place(s, Data).Placements
            .Where(p => p.Element == FarmElement.TrophyCase && p.Stem is not null)
            .ToList();
        Assert.Equal(3, trophies.Count);
        Assert.Equal("ei_bronze_trophy", trophies[0].Stem);
        Assert.Equal("ei_gold_trophy", trophies[1].Stem);
        Assert.Equal("ei_diamond_trophy", trophies[2].Stem);
    }

    [Fact]
    public void Camera_ComposesTheUiOffsets() {
        var s = Universe();
        var shot = FarmCameraEngine.Shot(s, Data, FarmElement.TrophyCase, 0);
        Assert.Equal(new Vec3(-5.5f, 0f, 11f), shot.Focus);
        Assert.Equal(2f, shot.Distance, 3);

        var composed = FarmCameraEngine.Compose(shot, 40f, Data);
        Assert.Equal(0.3f + 0f + 0.5f, composed.Focus.Y, 3);
        Assert.Equal(2.1f, composed.Distance, 3);
    }

    [Fact]
    public void Camera_PullsTheHabFocusBackByTwo() {
        var s = Universe();
        var shot = FarmCameraEngine.Shot(s, Data, FarmElement.HenHouse, 0);
        Assert.Equal(-12f, shot.Focus.X, 3);
        Assert.Equal(-12.5f, shot.Focus.Z, 3);
    }

    [Fact]
    public void Camera_ShiftsLabAndDepotByHalfTheirExtent() {
        var s = Universe();
        Assert.Equal(3.5f + ((10.2f - 3.5f) * 0.5f), FarmCameraEngine.Focus(s, Data, FarmElement.Lab, 0).X, 3);
        Assert.Equal(3.5f + ((9.0f - 3.5f) * 0.5f), FarmCameraEngine.Focus(s, Data, FarmElement.Depot, 0).X, 3);
    }

    [Fact]
    public void ResolveExtents_FlagsAnEggWithNoHatcheryExtent() {
        var known = FarmPlacementEngine.ResolveExtents(Universe(), Data);
        Assert.True(known.HatcheryResolved);

        var custom = FarmPlacementEngine.ResolveExtents(Universe() with { EggType = Ei.Egg.CustomEgg }, Data);
        Assert.False(custom.HatcheryResolved);
        Assert.Equal(0f, custom.Hatchery);
    }

    [Fact]
    public void EggTableIndex_MapsTheProtoEnumOntoTheBinaryTable() {
        Assert.Equal(0, FarmState.EggTableIndex(Ei.Egg.Edible));
        Assert.Equal(7, FarmState.EggTableIndex(Ei.Egg.Immortality));
        Assert.Equal(18, FarmState.EggTableIndex(Ei.Egg.Enlightenment));
        Assert.Equal(19, FarmState.EggTableIndex(Ei.Egg.Curiosity));
        Assert.Equal(24, FarmState.EggTableIndex(Ei.Egg.Chocolate));
        Assert.Equal(-1, FarmState.EggTableIndex(Ei.Egg.CustomEgg));
    }

    [Fact]
    public void HatcheryExtent_ResolvesByTableIndexNotByName() {
        var immortality = Universe() with { EggType = Ei.Egg.Immortality };
        Assert.Equal(14.1f, FarmPlacementEngine.ResolveExtents(immortality, Data).Hatchery, 3);

        var superMaterial = Universe() with { EggType = Ei.Egg.SuperMaterial };
        Assert.Equal(13.2f, FarmPlacementEngine.ResolveExtents(superMaterial, Data).Hatchery, 3);
    }
}
