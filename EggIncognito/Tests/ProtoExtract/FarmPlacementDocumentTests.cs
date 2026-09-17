using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Farm;
using EggIncognito.GameData;

namespace EggIncognito.Tests.ProtoExtract;

public class FarmPlacementDocumentTests {
    private const string Version = "1.37.0";

    private static FarmPlacementData Sample() => new() {
        Habs = [.. Enumerable.Range(0, 19).Select(i => new HabGeometry {
            Index = i,
            Name = "HAB " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Width = 3d + i,
            Extent = 5d + i,
            Depth = 2.2d
        })],
        HabAnchorX = -12f,
        HabRowY = 0f,
        HabRowZ = -10.5f,
        HabGap = 3f,
        SiloStepX = -6f,
        SiloBaseX = -5f,
        SiloY = 0f,
        SiloZEven = 5.5f,
        SiloZOdd = -0.5f,
        Trophy = new TrophyGeometry {
            CasePos = new Vec3(-5.45f, 0f, 11.254f),
            ColumnStepX = 0.692f,
            OriginX = -6.831f,
            RowStepY = 0.699f,
            OriginY = 0.143f,
            RowStepZ = -0.3f,
            OriginZ = 11.4539995f,
            Columns = 5,
            Count = 19,
            BonusScale = 1.8f,
            BonusPos = new Vec3(-4.0629997f, 2.2399998f, 10.554f)
        },
        LabExtents = [10.2f, 9.2f, 10.5f, 13.2f, 18.2f, 18.5f],
        DepotExtents = [9f, 9f, 10.1f, 11.8f, 13.8f, 15.9f, 23.1f],
        Eggs = [
            new EggGeometry { Index = 0, Name = "EDIBLE", HatcheryExtent = 6.5d },
            new EggGeometry { Index = 1, Name = "SUPERFOOD", HatcheryExtent = 7d }
        ],
        MissionControlPose = [new Vec3(2.8f, 0f, 3.7f), new Vec3(4.5f, 0f, 6f), new Vec3(5.5f, 0f, 6f)],
        FuelTankSpacing = [3.2f, 4.75f, 7.2f, 1.1f, 2.2f, 1f],
        SingletonFloor = 10f,
        HoaHomeOffset = 2f,
        HoaAltOffset = 1.7f,
        HoaZ = -3.5f,
        MissionControlOffset = 1.5f,
        FuelTankBaseOffset = 1.5f,
        FuelTankLockedExtra = 1.5f,
        FuelTankZUnlocked = 3.7f,
        FuelTankZLocked = 4.2f,
        CameraDistance = [1f, 1.3f, 3f, 2f, 0.7f, 0.7f, 1f, 1f, 1f, 1f, 1f, 1f, 1f],
        CameraHeight = [5f, 4f, 0.5f, 0.3f, 1f, 1f, 5f, 5f, 5f, 5f, 5f, 5f, 1.5f],
        CameraStaticFocus = [
            Vec3.Zero, Vec3.Zero, new Vec3(-3.5f, 0f, 10.5f), new Vec3(-5.5f, 0f, 11f), Vec3.Zero,
            new Vec3(-3f, 0f, 0f), new Vec3(12f, 0f, 21f), Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero,
            Vec3.Zero, Vec3.Zero
        ],
        HabFocusOffset = new Vec3(0f, 0f, -2f),
        LabFocusBase = new Vec3(3.5f, 0f, -1f),
        DepotFocusBase = new Vec3(3.5f, 0f, 9.5f),
        HatcheryFocusBase = new Vec3(3.5f, 0f, 2.5999999f),
        FuelTankFocusOffset = new Vec3(1f, 0f, -1f),
        FocusExtentPivot = 3.5f,
        FocusExtentScale = 0.5f,
        HoaFocusExtra = 3f,
        CameraUiDivisor = 40f,
        CameraUiHeightScale = 0.5f,
        CameraUiDistanceScale = 0.1f,
        Vehicles = [
            new VehicleGeometry { Index = 0, Name = "TRIKE", Length = 2.1d },
            new VehicleGeometry { Index = 11, Name = "HYPERLOOP TRAIN", Length = 7d }
        ],
        Road = new RoadGeometry {
            SpawnX = 48f,
            RoadZ = 13.33f,
            RoadY = 0f,
            DepotStopX = 7.1f,
            DespawnX = -35f,
            FollowGap = 2.5f,
            MaxSpeedMult = 1.5f,
            RoundTripSeconds = 100f,
            HyperloopVehicleIndex = 11,
            EmptyVehicleIndex = 12
        },
        BinaryVersion = Version,
        Provenance = new Dictionary<string, PlacementProvenance>(StringComparer.Ordinal) {
            ["habRow"] = PlacementProvenance.FromBinary("GameController::getHabPosition"),
            ["road"] = PlacementProvenance.FromBinary("VehicleManager::update")
        }
    };

    [Fact]
    public void Document_RoundTripsThroughJson() {
        string json = GameDataDocBuilders.BuildFarmPlacement(Sample(), Version).Json;
        var parsed = FarmPlacementCatalog.Parse(json);
        string again = GameDataDocBuilders.BuildFarmPlacement(parsed, Version).Json;
        Assert.Equal(json, again);
    }

    [Fact]
    public void Document_RoundTripsEveryTable() {
        var original = Sample();
        var parsed = FarmPlacementCatalog.Parse(GameDataDocBuilders.BuildFarmPlacement(original, Version).Json);

        Assert.True(parsed.IsComplete);
        Assert.Equal(original.HabAnchorX, parsed.HabAnchorX);
        Assert.Equal(original.HabRowZ, parsed.HabRowZ);
        Assert.Equal(original.HabGap, parsed.HabGap);
        Assert.Equal(original.SiloStepX, parsed.SiloStepX);
        Assert.Equal(original.SiloZEven, parsed.SiloZEven);
        Assert.Equal(original.Trophy, parsed.Trophy);
        Assert.Equal(original.Road, parsed.Road);
        Assert.Equal(original.LabExtents, parsed.LabExtents);
        Assert.Equal(original.DepotExtents, parsed.DepotExtents);
        Assert.Equal(original.FuelTankSpacing, parsed.FuelTankSpacing);
        Assert.Equal(original.CameraDistance, parsed.CameraDistance);
        Assert.Equal(original.CameraHeight, parsed.CameraHeight);
        Assert.Equal(original.CameraStaticFocus, parsed.CameraStaticFocus);
        Assert.Equal(original.MissionControlPose, parsed.MissionControlPose);
        Assert.Equal(original.Habs, parsed.Habs);
        Assert.Equal(original.Eggs, parsed.Eggs);
        Assert.Equal(original.Vehicles, parsed.Vehicles);
        Assert.Equal(original.HatcheryFocusBase, parsed.HatcheryFocusBase);
        Assert.Equal(original.FuelTankFocusOffset, parsed.FuelTankFocusOffset);
        Assert.Equal(Version, parsed.BinaryVersion);
        Assert.Equal(original.Provenance.Count, parsed.Provenance.Count);
        Assert.Equal(PlacementOrigin.Binary, parsed.Provenance["habRow"].Origin);
    }

    [Fact]
    public void Validate_AcceptsCompleteDocument() {
        string json = GameDataDocBuilders.BuildFarmPlacement(Sample(), Version).Json;
        GameDataProvider.Validate(FarmPlacementCatalog.DocumentId, json);
    }

    [Fact]
    public void Validate_RejectsShortHabTable() {
        var data = Sample() with { Habs = [.. Sample().Habs.Take(3)] };
        string json = GameDataDocBuilders.BuildFarmPlacement(data, Version).Json;
        Assert.Throws<GameDataSchemaException>(() =>
            GameDataProvider.Validate(FarmPlacementCatalog.DocumentId, json));
    }

    [Fact]
    public void Validate_RejectsWrongExtentTableLengths() {
        var shortLab = Sample() with { LabExtents = [10.2f, 9.2f] };
        Assert.Throws<GameDataSchemaException>(() => GameDataProvider.Validate(FarmPlacementCatalog.DocumentId,
            GameDataDocBuilders.BuildFarmPlacement(shortLab, Version).Json));

        var shortDepot = Sample() with { DepotExtents = [9f, 9f, 10.1f] };
        Assert.Throws<GameDataSchemaException>(() => GameDataProvider.Validate(FarmPlacementCatalog.DocumentId,
            GameDataDocBuilders.BuildFarmPlacement(shortDepot, Version).Json));

        var shortCamera = Sample() with { CameraHeight = [5f, 4f] };
        Assert.Throws<GameDataSchemaException>(() => GameDataProvider.Validate(FarmPlacementCatalog.DocumentId,
            GameDataDocBuilders.BuildFarmPlacement(shortCamera, Version).Json));
    }

    [Fact]
    public void Validate_RejectsMissingBinaryVersion() {
        string json = GameDataDocBuilders.BuildFarmPlacement(Sample(), "").Json;
        Assert.Throws<GameDataSchemaException>(() =>
            GameDataProvider.Validate(FarmPlacementCatalog.DocumentId, json));
    }
}
