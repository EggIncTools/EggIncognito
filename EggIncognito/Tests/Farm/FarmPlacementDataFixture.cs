using EggSharp.GameData.Farm;

namespace EggIncognito.Tests.Farm;

internal static class FarmPlacementDataFixture {
    private static readonly double[] Widths =
        [3, 4, 4.5, 4.5, 4.5, 5, 5.5, 12.2, 12.5, 7.5, 15.5, 9.5, 16.5, 8.2, 12, 17, 14, 11, 9.5, 0.5];

    private static readonly double[] Extents =
        [5, 6, 9, 10, 15, 25, 25, 25, 20, 20, 25, 15, 25, 15, 18, 25, 25, 25, 20, 1];

    private static readonly string[] EggNames = [
        "EDIBLE", "SUPERFOOD", "MEDICAL", "ROCKET FUEL", "SUPER MATERIAL", "FUSION", "QUANTUM", "CRISPR",
        "TACHYON", "GRAVITON", "DILITHIUM", "PRODIGY", "TERRAFORM", "ANTIMATTER", "DARK MATTER", "A.I.",
        "NEBULA", "UNIVERSE", "ENLIGHTENMENT"
    ];

    private static readonly double[] HatcheryExtents = [
        12, 12, 13, 12.5, 13.2, 15.1, 17.6, 14.1, 20.3, 12.9,
        15.5, 17.3, 15.8, 24, 18.5, 19.8, 18.5, 19.4, 13.5
    ];

    public static FarmPlacementData Build() => new() {
        Habs = [.. Widths.Select((w, i) => new HabGeometry {
            Index = i,
            Name = "HAB_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Width = w,
            Extent = Extents[i],
            Depth = i == 18 ? 4.0 : 2.2
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
        DepotExtents = [9.0f, 9.0f, 10.1f, 11.8f, 13.8f, 15.9f, 23.1f],
        Eggs = [.. EggNames.Select((n, i) => new EggGeometry {
            Index = i,
            Name = n,
            HatcheryExtent = HatcheryExtents[i]
        })],

        MissionControlPose = [new Vec3(2.8f, 0f, 3.7f), new Vec3(4.5f, 0f, 6f), new Vec3(5.5f, 0f, 6f)],
        FuelTankSpacing = [3.2f, 4.75f, 7.2f, 1.1f, 2.2f, 1.0f],

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
            Vec3.Zero, Vec3.Zero,
            new Vec3(-3.5f, 0f, 10.5f),
            new Vec3(-5.5f, 0f, 11f),
            Vec3.Zero,
            new Vec3(-3f, 0f, 0f),
            new Vec3(12f, 0f, 21f),
            Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero
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
            new VehicleGeometry { Index = 0, Name = "TRIKE", Length = 2.1 },
            new VehicleGeometry { Index = 5, Name = "SEMI", Length = 6.5 }
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
        BinaryVersion = "fixture"
    };
}
