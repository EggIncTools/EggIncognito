using EggIncognito.Core.Services.Farm;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class FarmPlacementExtractor {
    public const string HabLocator = "GameController::getHabPosition";
    public const string SiloLocator = "FarmScene::updateSilo";
    public const string TrophyLocator = "FarmScene::updateTrophyCase";
    public const string LabLocator = "FarmScene::updateLab";
    public const string DepotLocator = "FarmScene::updateDepot";
    public const string HoaLocator = "FarmScene::hoaPos";
    public const string MissionControlLocator = "FarmScene::missionControlPos";
    public const string FuelTankLocator = "FarmScene::fuelTankPos";
    public const string CameraFocusLocator = "FarmScene::getCameraFocus";
    public const string CameraInfoLocator = "FarmScene::getCameraInfo";
    public const string RoadLocator = "VehicleManager::update";
    public const string VehicleSlotLocator = "GameController::attemptHireVehicle";
    public const string HabTableLocator = "habdata table 0x103146e60";
    public const string EggTableLocator = "eggdata record +0xb8";
    public const string VehicleTableLocator = "vehicledata record +0xe0";

    private const long LabExtentField = 0x3d0;
    private const long DepotExtentField = 0x3d4;
    private const int LabFirstAssetType = 110;
    private const int DepotFirstAssetType = 100;
    private const int LabTierCount = 6;
    private const int DepotTierCount = 7;

    private const int FuelTankSpacingCount = 6;
    private const int CameraElements = 13;
    private const int LocatorCount = 53;
    private const int WrongBuildThreshold = 8;

    private const int HenHouseElement = 1;
    private const int HatcheryElement = 10;
    private const int HoaElement = 11;
    private const int FuelTankElement = 13;
    private static readonly int[] StaticFocusElements = [3, 4, 5, 6, 7];

    public readonly record struct Result(
        bool Ok,
        FarmPlacementData Data,
        IReadOnlyList<string> Missing,
        string Diagnostics);

    private sealed record Anchors(
        ulong Hab, ulong Silo, ulong Trophy, ulong TrophyBonus, ulong Lab, ulong Depot,
        ulong Hoa, ulong Mc, ulong Fuel, ulong Focus, ulong Cam, ulong Road, ulong Hire) {
        public ulong HabAnchorAdrp => Hab;
        public ulong HabRowZAt => Hab + 0xc;
        public ulong HabGapAt => Hab + 0xfc;
        public ulong HabGapDoubledAt => Hab + 0x2d4;

        public ulong SiloZOddAt => Silo + 0x658;
        public ulong SiloZEvenAt => Silo + 0x65c;
        public ulong SiloStepAt => Silo + 0x674;
        public ulong SiloBaseAt => Silo + 0x678;
        public ulong SiloYAt => Silo + 0x694;

        public ulong TrophyCaseXyAdrp => Trophy + 0x33c;
        public ulong TrophyCaseZAt => Trophy + 0x348;
        public ulong TrophyColumnStepAdrp => Trophy + 0x6f8;
        public ulong TrophyOriginXAt => Trophy + 0x700;
        public ulong TrophyRowStepAdrp => Trophy + 0x708;
        public ulong TrophyOriginYzAdrp => Trophy + 0x714;
        public ulong TrophyColumnsAt => Trophy + 0x880;
        public ulong TrophyCountAt => Trophy + 0x738;
        public ulong TrophyBonusScaleAt => TrophyBonus;
        public ulong TrophyBonusXyAdrp => TrophyBonus + 0x1c;
        public ulong TrophyBonusZAdrp => TrophyBonus + 0x28;

        public ulong UpdateLabAt => Lab;
        public ulong LabFocusXAt => Lab + 0x64;
        public ulong LabFocusYzAdrp => Lab + 0x6c;
        public ulong UpdateDepotAt => Depot;
        public ulong DepotFocusXAt => Depot + 0x80;
        public ulong DepotFocusYzAdrp => Depot + 0x88;

        public ulong HoaFloorAt => Hoa + 0x58;
        public ulong HoaHomeOffsetAt => Hoa + 0x60;
        public ulong HoaAltOffsetAt => Hoa + 0x7c;
        public ulong HoaZAdrp => Hoa + 0x94;

        public ulong PoseLowAdrp => Mc + 0x40;
        public ulong PoseHighAdrp => Mc + 0x48;
        public ulong PoseTailAt => Mc + 0x54;
        public ulong MissionControlBaseYzAdrp => Mc + 0x98;
        public ulong MissionControlOffsetAt => Mc + 0xc0;

        public ulong FuelTankSpacingAdrp => Fuel + 0x50;
        public ulong FuelTankBaseOffsetAt => Fuel + 0xc8;
        public ulong FuelTankZeroAt => Fuel + 0xe4;
        public ulong FuelTankSelectAt => Fuel + 0xe8;
        public ulong FuelTankRowOffsetAt => Fuel + 0xf8;
        public ulong FuelTankZLockedAt => Fuel + 0x10c;
        public ulong FuelTankZUnlockedAt => Fuel + 0x118;

        public ulong FocusTableAdrp => Focus + 0x44;
        public ulong FocusTargetAdrAt => Focus + 0x4c;
        public ulong CameraDistanceAdrp => Cam + 0x30;
        public ulong CameraHeightAdrp => Cam + 0x3c;
        public ulong CameraUiDivisorAt => Cam + 0x64;
        public ulong CameraUiHeightScaleAt => Cam + 0x70;
        public ulong CameraUiDistanceScaleAt => Cam + 0x7c;

        public ulong RoadZeroVectorAt => Road + 0x44c;
        public ulong RoadZeroStoreAt => Road + 0x450;
        public ulong RoadSpawnXAt => Road + 0x47c;
        public ulong RoadZAt => Road + 0x484;
        public ulong RoadDespawnXAt => Road + 0x720;
        public ulong RoadMaxSpeedMultAt => Road + 0x748;
        public ulong RoadDepotStopXAt => Road + 0x74c;
        public ulong RoadFollowGapAt => Road + 0x8c8;
        public ulong RoadRoundTripAt => Road + 0x3a8;
        public ulong RoadHyperloopIndexAt => Road + 0x300;
        public ulong VehicleEmptyIndexAt => Hire + 0x1e8;
    }

    private static readonly (string Field, string[] Needles)[] AnchorSymbols = [
        ("Hab", ["GameController14getHabPosition"]),
        ("Silo", ["FarmScene10updateSilo"]),
        ("Trophy", ["FarmScene16updateTrophyCase"]),
        ("TrophyBonus", ["ZN9FarmScene16updateTrophyCase", "$_2", "clEv"]),
        ("Lab", ["FarmScene9updateLab"]),
        ("Depot", ["FarmScene11updateDepot"]),
        ("Hoa", ["FarmScene6hoaPos"]),
        ("Mc", ["FarmScene17missionControlPos"]),
        ("Fuel", ["FarmScene11fuelTankPos"]),
        ("Focus", ["FarmScene14getCameraFocus"]),
        ("Cam", ["FarmScene13getCameraInfo"]),
        ("Road", ["VehicleManager6update"]),
        ("Hire", ["GameController18attemptHireVehicle"])
    ];

    private static Anchors? ResolveAnchors(IReadOnlyList<MachoSymbols.Symbol> syms, out string diagnostics) {
        var found = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach ((string field, string[] needles) in AnchorSymbols) {
            if (MachoSymbols.TryFindFunc(syms, needles, out var fn)) found[field] = fn.Start;
            else unresolved.Add(field);
        }

        if (unresolved.Count > 0) {
            diagnostics = $"{unresolved.Count} placement functions not found by symbol: "
                          + string.Join(", ", unresolved);
            return null;
        }

        diagnostics = "";
        return new Anchors(found["Hab"], found["Silo"], found["Trophy"], found["TrophyBonus"], found["Lab"],
            found["Depot"], found["Hoa"], found["Mc"], found["Fuel"], found["Focus"], found["Cam"],
            found["Road"], found["Hire"]);
    }

    public static Result Extract(byte[] bin,
        IReadOnlyList<HabCatalogExtractor.HabEntry> habs,
        IReadOnlyList<EggCatalogExtractor.EggEntry> eggs,
        IReadOnlyList<VehicleCatalogExtractor.VehicleEntry> vehicles,
        string binaryVersion) {
        if (BinaryImage.Load(bin) is not MachoImage macho)
            return new Result(false, new FarmPlacementData(), [], "farm placement locators are iOS Mach-O only");

        var im = new Arm64Image(bin, macho);
        if (ResolveAnchors(macho.Symbols, out string anchorDiag) is not { } at)
            return new Result(false, new FarmPlacementData(), [], anchorDiag);

        var miss = new List<string>();

        var data = new FarmPlacementData {
            Habs = [
                .. habs.Select(h => new HabGeometry {
                    Index = h.Index,
                    Name = h.Name,
                    Width = h.Width,
                    Extent = h.Extent,
                    Depth = h.Depth
                })
            ],
            Eggs = [
                .. eggs.Select(e => new EggGeometry {
                    Index = e.Index,
                    Name = e.Name,
                    HatcheryExtent = e.HatcheryExtent
                })
            ],
            Vehicles = [
                .. vehicles.Select(v => new VehicleGeometry {
                    Index = v.Index,
                    Name = v.Name,
                    Length = v.Length
                })
            ],
            BinaryVersion = binaryVersion,
            Provenance = BuildProvenance()
        };

        data = ReadHabRow(im, at, miss, data);
        data = ReadSilos(im, at, miss, data);
        data = data with { Trophy = ReadTrophy(im, at, miss) };
        data = ReadExtentTables(im, at, miss, data);
        data = ReadSingletons(im, at, miss, data);
        data = ReadCamera(im, at, miss, data);
        data = data with { Road = ReadRoad(im, at, miss) };

        bool ok = miss.Count == 0 && data.IsComplete;
        string note = miss.Count == 0
            ? $"{data.Habs.Count} habs, {data.LabExtents.Count} lab tiers, {data.DepotExtents.Count} depot tiers, "
              + $"{data.Eggs.Count} eggs, {data.Vehicles.Count} vehicles"
            : miss.Count >= WrongBuildThreshold
                ? $"binary layout not recognised ({miss.Count} of {LocatorCount} locators failed); "
                  + "the offsets in this extractor were authored against a different build"
                : $"{miss.Count} unreadable fields: {string.Join(", ", miss)}";
        return new Result(ok, data, miss, note);
    }

    private static Dictionary<string, PlacementProvenance> BuildProvenance() =>
        new(StringComparer.Ordinal) {
            ["habRow"] = PlacementProvenance.FromBinary(HabLocator),
            ["habs"] = PlacementProvenance.FromBinary(HabTableLocator),
            ["silos"] = PlacementProvenance.FromBinary(SiloLocator),
            ["trophy"] = PlacementProvenance.FromBinary(TrophyLocator),
            ["labExtents"] = PlacementProvenance.FromBinary(LabLocator),
            ["depotExtents"] = PlacementProvenance.FromBinary(DepotLocator),
            ["eggs"] = PlacementProvenance.FromBinary(EggTableLocator),
            ["missionControlPose"] = PlacementProvenance.FromBinary(MissionControlLocator),
            ["fuelTankSpacing"] = PlacementProvenance.FromBinary(FuelTankLocator),
            ["hoa"] = PlacementProvenance.FromBinary(HoaLocator),
            ["camera"] = PlacementProvenance.FromBinary(CameraFocusLocator),
            ["cameraUi"] = PlacementProvenance.FromBinary(CameraInfoLocator),
            ["vehicles"] = PlacementProvenance.FromBinary(VehicleTableLocator),
            ["road"] = PlacementProvenance.FromBinary(RoadLocator),
            ["emptyVehicleIndex"] = PlacementProvenance.FromBinary(VehicleSlotLocator)
        };

    private static FarmPlacementData ReadHabRow(Arm64Image im, Anchors at, List<string> miss, FarmPlacementData data) {
        if (im.TryPageRef(at.HabAnchorAdrp, out ulong anchorVa) && im.TryF32(anchorVa, out float anchorX)
            && im.TryF32(anchorVa + 4, out float anchorY)) {
            data = data with { HabAnchorX = anchorX, HabRowY = anchorY };
        } else {
            miss.Add("habAnchor");
        }

        if (Arm64Bits.TryConst(im, at.HabRowZAt, out ulong zBits, out _)) {
            data = data with { HabRowZ = Arm64Bits.F32(zBits) };
        } else {
            miss.Add("habRowZ");
        }

        if (Arm64Bits.TryFmovImm(im, at.HabGapAt, out double gap, out _)
            && Arm64Bits.TryFmovImm(im, at.HabGapDoubledAt, out double gapAgain, out _)
            && Math.Abs(gap - gapAgain) < 1e-9) {
            data = data with { HabGap = (float)gap };
        } else {
            miss.Add("habGap");
        }

        return data;
    }

    private static FarmPlacementData ReadSilos(Arm64Image im, Anchors at, List<string> miss, FarmPlacementData data) {
        if (Arm64Bits.TryFmovImm(im, at.SiloZOddAt, out double zOdd, out _)
            && Arm64Bits.TryFmovImm(im, at.SiloZEvenAt, out double zEven, out _)) {
            data = data with { SiloZOdd = (float)zOdd, SiloZEven = (float)zEven };
        } else {
            miss.Add("siloZ");
        }

        if (Arm64Bits.TryConst(im, at.SiloStepAt, out ulong stepBits, out bool stepIs64) && !stepIs64) {
            data = data with { SiloStepX = unchecked((int)(uint)stepBits) };
        } else {
            miss.Add("siloStepX");
        }

        if (Arm64Bits.TryConst(im, at.SiloBaseAt, out ulong baseBits, out bool baseIs64) && !baseIs64) {
            data = data with { SiloBaseX = unchecked((int)(uint)baseBits) };
        } else {
            miss.Add("siloBaseX");
        }

        if (im.TryWord(at.SiloYAt, out uint yWord) && Arm64Bits.TryStore(yWord, out var yStore)
            && yStore is { Rt: 31, Size: 2, Fp: false }) {
            data = data with { SiloY = 0f };
        } else {
            miss.Add("siloY");
        }

        return data;
    }

    private static TrophyGeometry ReadTrophy(Arm64Image im, Anchors at, List<string> miss) {
        var trophy = new TrophyGeometry();

        if (im.TryPageRef(at.TrophyCaseXyAdrp, out ulong caseVa) && im.TryF32(caseVa, out float caseX)
            && im.TryF32(caseVa + 4, out float caseY)
            && Arm64Bits.TryConst(im, at.TrophyCaseZAt, out ulong caseZBits, out _)) {
            trophy = trophy with { CasePos = new Vec3(caseX, caseY, Arm64Bits.F32(caseZBits)) };
        } else {
            miss.Add("trophyCasePos");
        }

        if (im.TryPageRef(at.TrophyColumnStepAdrp, out ulong stepVa) && im.TryF64(stepVa, out double columnStep)) {
            trophy = trophy with { ColumnStepX = (float)columnStep };
        } else {
            miss.Add("trophyColumnStepX");
        }

        if (Arm64Bits.TryConst(im, at.TrophyOriginXAt, out ulong originXBits, out _)) {
            trophy = trophy with { OriginX = Arm64Bits.F32(originXBits) };
        } else {
            miss.Add("trophyOriginX");
        }

        if (im.TryPageRef(at.TrophyRowStepAdrp, out ulong rowVa) && im.TryF64(rowVa, out double rowStepY)
            && im.TryF64(rowVa + 8, out double rowStepZ)) {
            trophy = trophy with { RowStepY = (float)rowStepY, RowStepZ = (float)rowStepZ };
        } else {
            miss.Add("trophyRowStep");
        }

        if (im.TryPageRef(at.TrophyOriginYzAdrp, out ulong originVa) && im.TryF32(originVa, out float originY)
            && im.TryF32(originVa + 4, out float originZ)) {
            trophy = trophy with { OriginY = originY, OriginZ = originZ };
        } else {
            miss.Add("trophyOriginYz");
        }

        if (im.TryWord(at.TrophyColumnsAt, out uint colWord) && Arm64Bits.TryAddShifted(colWord, out var add)
            && add.Rn == add.Rm && add.ShiftKind == 0) {
            trophy = trophy with { Columns = 1 + (1 << add.Amount) };
        } else {
            miss.Add("trophyColumns");
        }

        if (Arm64Bits.TryCmpImm(im, at.TrophyCountAt, out ulong count, out _, out _)) {
            trophy = trophy with { Count = (int)count };
        } else {
            miss.Add("trophyCount");
        }

        if (Arm64Bits.TryConst(im, at.TrophyBonusScaleAt, out ulong scaleBits, out _)) {
            trophy = trophy with { BonusScale = Arm64Bits.F32(scaleBits) };
        } else {
            miss.Add("trophyBonusScale");
        }

        if (im.TryPageRef(at.TrophyBonusXyAdrp, out ulong bonusXyVa) && im.TryF32(bonusXyVa + 8, out float bonusX)
            && im.TryF32(bonusXyVa + 12, out float bonusY) && im.TryPageRef(at.TrophyBonusZAdrp, out ulong bonusZVa)
            && im.TryF32(bonusZVa, out float bonusZ)) {
            trophy = trophy with { BonusPos = new Vec3(bonusX, bonusY, bonusZ) };
        } else {
            miss.Add("trophyBonusPos");
        }

        return trophy;
    }

    private static FarmPlacementData ReadExtentTables(Arm64Image im, Anchors at, List<string> miss, FarmPlacementData data) {
        if (Arm64Switch.TryExtents(im, at.UpdateLabAt, LabExtentField, LabFirstAssetType, LabTierCount,
                out float[] lab)) {
            data = data with { LabExtents = lab };
        } else {
            miss.Add("labExtents");
        }

        if (Arm64Switch.TryExtents(im, at.UpdateDepotAt, DepotExtentField, DepotFirstAssetType, DepotTierCount,
                out float[] depot)) {
            data = data with { DepotExtents = depot };
        } else {
            miss.Add("depotExtents");
        }

        if (TryFocusBase(im, at.LabFocusXAt, at.LabFocusYzAdrp, out var labFocus)) {
            data = data with { LabFocusBase = labFocus };
        } else {
            miss.Add("labFocusBase");
        }

        if (TryFocusBase(im, at.DepotFocusXAt, at.DepotFocusYzAdrp, out var depotFocus)) {
            data = data with { DepotFocusBase = depotFocus };
        } else {
            miss.Add("depotFocusBase");
        }

        return data;
    }

    private static bool TryFocusBase(Arm64Image im, ulong xAt, ulong yzAdrp, out Vec3 focus) {
        focus = Vec3.Zero;
        if (!Arm64Bits.TryConst(im, xAt, out ulong xBits, out _)) return false;
        if (!im.TryPageRef(yzAdrp, out ulong yzVa)) return false;
        if (!im.TryF32(yzVa, out float y) || !im.TryF32(yzVa + 4, out float z)) return false;
        focus = new Vec3(Arm64Bits.F32(xBits), y, z);
        return true;
    }

    private static FarmPlacementData ReadSingletons(Arm64Image im, Anchors at, List<string> miss, FarmPlacementData data) {
        if (Arm64Bits.TryFmovImm(im, at.HoaFloorAt, out double floor, out _)) {
            data = data with { SingletonFloor = (float)floor };
        } else {
            miss.Add("singletonFloor");
        }

        if (Arm64Bits.TryFmovImm(im, at.HoaHomeOffsetAt, out double homeOffset, out _)) {
            data = data with { HoaHomeOffset = (float)homeOffset };
        } else {
            miss.Add("hoaHomeOffset");
        }

        if (Arm64Bits.TryConst(im, at.HoaAltOffsetAt, out ulong altBits, out bool altIs64) && altIs64) {
            data = data with { HoaAltOffset = (float)Arm64Bits.F64(altBits) };
        } else {
            miss.Add("hoaAltOffset");
        }

        if (im.TryPageRef(at.HoaZAdrp, out ulong hoaZVa) && im.TryF32(hoaZVa + 4, out float hoaZ)) {
            data = data with { HoaZ = hoaZ };
        } else {
            miss.Add("hoaZ");
        }

        if (TryPoseTable(im, at, out var pose)) {
            data = data with { MissionControlPose = pose };
        } else {
            miss.Add("missionControlPose");
        }

        if (!TryVerifyMissionControlBase(im, at, data)) miss.Add("missionControlBaseYz");

        if (Arm64Bits.TryFmovImm(im, at.MissionControlOffsetAt, out double mcOffset, out _)
            && Arm64Bits.TryFmovImm(im, at.FuelTankRowOffsetAt, out double rowOffset, out _)
            && Math.Abs(mcOffset - rowOffset) < 1e-9) {
            data = data with { MissionControlOffset = (float)mcOffset };
        } else {
            miss.Add("missionControlOffset");
        }

        if (im.TryPageRef(at.FuelTankSpacingAdrp, out ulong spacingVa)
            && im.TryF32Table(spacingVa, FuelTankSpacingCount, out float[] spacing)) {
            data = data with { FuelTankSpacing = spacing };
        } else {
            miss.Add("fuelTankSpacing");
        }

        return ReadFuelTank(im, at, miss, data);
    }

    private static bool TryVerifyMissionControlBase(Arm64Image im, Anchors at, FarmPlacementData data) {
        if (!im.TryPageRef(at.MissionControlBaseYzAdrp, out ulong va) || !im.TryF32(va, out float y)
            || !im.TryF32(va + 4, out float z)) {
            return false;
        }

        return data.MissionControlPose.Count > 0
               && Math.Abs(data.MissionControlPose[0].Y - y) < 1e-6f
               && Math.Abs(data.MissionControlPose[0].Z - z) < 1e-6f;
    }

    private static FarmPlacementData ReadFuelTank(Arm64Image im, Anchors at, List<string> miss, FarmPlacementData data) {
        if (Arm64Bits.TryFmovImm(im, at.FuelTankBaseOffsetAt, out double baseOffset, out _)) {
            data = data with { FuelTankBaseOffset = (float)baseOffset };
            if (im.TryWord(at.FuelTankZeroAt, out uint zeroWord) && Arm64Bits.IsFmovZeroToFp(zeroWord)
                && im.TryWord(at.FuelTankSelectAt, out uint selectWord) && Arm64Bits.IsFcsel(selectWord)) {
                data = data with { FuelTankLockedExtra = (float)baseOffset };
            } else {
                miss.Add("fuelTankLockedExtra");
            }
        } else {
            miss.Add("fuelTankBaseOffset");
            miss.Add("fuelTankLockedExtra");
        }

        if (Arm64Bits.TryConst(im, at.FuelTankZUnlockedAt, out ulong unlockedBits, out _)
            && Arm64Bits.TryConst(im, at.FuelTankZLockedAt, out ulong lockedBits, out _)) {
            data = data with {
                FuelTankZUnlocked = Arm64Bits.F32(unlockedBits),
                FuelTankZLocked = Arm64Bits.F32(lockedBits)
            };
        } else {
            miss.Add("fuelTankZ");
        }

        return data;
    }

    private static bool TryPoseTable(Arm64Image im, Anchors at, out Vec3[] pose) {
        pose = [];
        if (!im.TryPageRef(at.PoseLowAdrp, out ulong lowVa) || !im.TryF32Table(lowVa, 4, out float[] low))
            return false;
        if (!im.TryPageRef(at.PoseHighAdrp, out ulong highVa) || !im.TryF32Table(highVa, 4, out float[] high))
            return false;
        if (!Arm64Bits.TryConst(im, at.PoseTailAt, out ulong tailBits, out _)) return false;
        pose = [
            new Vec3(low[0], low[1], low[2]),
            new Vec3(low[3], high[0], high[1]),
            new Vec3(high[2], high[3], Arm64Bits.F32(tailBits))
        ];
        return true;
    }

    private static FarmPlacementData ReadCamera(Arm64Image im, Anchors at, List<string> miss, FarmPlacementData data) {
        if (im.TryPageRef(at.CameraDistanceAdrp, out ulong distanceVa)
            && im.TryF32Table(distanceVa, CameraElements, out float[] distance)) {
            data = data with { CameraDistance = distance };
        } else {
            miss.Add("cameraDistance");
        }

        if (im.TryPageRef(at.CameraHeightAdrp, out ulong heightVa)
            && im.TryF32Table(heightVa, CameraElements, out float[] height)) {
            data = data with { CameraHeight = height };
        } else {
            miss.Add("cameraHeight");
        }

        if (Arm64Bits.TryConst(im, at.CameraUiDivisorAt, out ulong divisorBits, out _)) {
            data = data with { CameraUiDivisor = Arm64Bits.F32(divisorBits) };
        } else {
            miss.Add("cameraUiDivisor");
        }

        if (Arm64Bits.TryFmovImm(im, at.CameraUiHeightScaleAt, out double heightScale, out _)) {
            data = data with { CameraUiHeightScale = (float)heightScale };
        } else {
            miss.Add("cameraUiHeightScale");
        }

        if (Arm64Bits.TryConst(im, at.CameraUiDistanceScaleAt, out ulong distanceScaleBits, out _)) {
            data = data with { CameraUiDistanceScale = Arm64Bits.F32(distanceScaleBits) };
        } else {
            miss.Add("cameraUiDistanceScale");
        }

        return ReadFocusBlocks(im, at, miss, data);
    }

    private static FarmPlacementData ReadFocusBlocks(Arm64Image im, Anchors at, List<string> miss, FarmPlacementData data) {
        if (!im.TryPageRef(at.FocusTableAdrp, out ulong tableVa)
            || !Arm64Bits.TryAdr(im, at.FocusTargetAdrAt, out ulong targetBase, out _)) {
            miss.Add("cameraFocusBlocks");
            return data;
        }

        var focus = new Vec3[CameraElements];
        foreach (int element in StaticFocusElements) {
            if (!TryBlock(im, tableVa, targetBase, element, out ulong block)
                || !TryStaticFocus(im, block, out focus[element - 1])) {
                miss.Add($"cameraStaticFocus{element}");
            }
        }

        data = data with { CameraStaticFocus = focus };

        if (TryBlock(im, tableVa, targetBase, HenHouseElement, out ulong habBlock)
            && TryFirstFmov(im, habBlock, 16, out double habZ)) {
            data = data with { HabFocusOffset = new Vec3(0f, 0f, (float)habZ) };
        } else {
            miss.Add("habFocusOffset");
        }

        if (TryBlock(im, tableVa, targetBase, HatcheryElement, out ulong hatcheryBlock)
            && TryHatcheryFocus(im, hatcheryBlock, out var hatchery, out float pivot, out float scale)) {
            data = data with {
                HatcheryFocusBase = hatchery,
                FocusExtentPivot = pivot,
                FocusExtentScale = scale
            };
        } else {
            miss.Add("hatcheryFocusBase");
        }

        if (TryBlock(im, tableVa, targetBase, HoaElement, out ulong hoaBlock)
            && Arm64Bits.TryFirstBranch(im, hoaBlock, 48, out ulong merge)
            && Arm64Bits.TryFmovImm(im, merge, out double hoaExtra, out _)) {
            data = data with { HoaFocusExtra = (float)hoaExtra };
        } else {
            miss.Add("hoaFocusExtra");
        }

        if (TryBlock(im, tableVa, targetBase, FuelTankElement, out ulong fuelBlock)
            && im.TryPageRef(fuelBlock + 0xc, out ulong fuelVa) && im.TryF32(fuelVa, out float fuelX)
            && im.TryF32(fuelVa + 4, out float fuelY)
            && Arm64Bits.TryFmovImm(im, fuelBlock + 0x20, out double fuelZ, out _)) {
            data = data with { FuelTankFocusOffset = new Vec3(fuelX, fuelY, (float)fuelZ) };
        } else {
            miss.Add("fuelTankFocusOffset");
        }

        return data;
    }

    private static bool TryBlock(Arm64Image im, ulong tableVa, ulong targetBase, int element, out ulong block) {
        block = 0;
        if (!im.TryByte(tableVa + (ulong)(element - 1), out byte slot)) return false;
        block = targetBase + (ulong)(4 * slot);
        return true;
    }

    private static bool TryStaticFocus(Arm64Image im, ulong block, out Vec3 focus) {
        var regs = new ulong?[32];
        var parts = new float[3];
        bool wrote = false;
        for (int i = 0; i < 10; i++) {
            if (!im.TryWord(block + (ulong)(4 * i), out uint word)) break;
            if (Arm64Bits.TryMovWide(word, out int rd, out ulong value, out var kind, out _)) {
                regs[rd] = kind == Arm64Bits.MovKind.Movk ? Arm64Bits.Merge(regs[rd] ?? 0, word) : value;
                continue;
            }

            if (Arm64Bits.TryStore(word, out var store) && store is { Size: 2, Fp: false }
                && store.Rn != 31 && store.Offset is 0 or 4 or 8) {
                parts[store.Offset / 4] = store.Rt == 31 ? 0f : Arm64Bits.F32(regs[store.Rt] ?? 0);
                wrote = true;
                continue;
            }

            break;
        }

        focus = new Vec3(parts[0], parts[1], parts[2]);
        return wrote;
    }

    private static bool TryFirstFmov(Arm64Image im, ulong block, int limit, out double value) {
        for (int i = 0; i < limit; i++) {
            if (Arm64Bits.TryFmovImm(im, block + (ulong)(4 * i), out value, out _)) return true;
        }

        value = 0d;
        return false;
    }

    private static bool TryHatcheryFocus(Arm64Image im, ulong block, out Vec3 focus, out float pivot,
        out float scale) {
        focus = Vec3.Zero;
        pivot = 0f;
        scale = 0f;
        if (!Arm64Bits.TryFmovImm(im, block + 0x08, out double negativePivot, out _)) return false;
        if (!Arm64Bits.TryFmovImm(im, block + 0x10, out double rawScale, out _)) return false;
        if (!Arm64Bits.TryFmovImm(im, block + 0x18, out double rawPivot, out _)) return false;
        if (Math.Abs(negativePivot + rawPivot) > 1e-9) return false;
        if (!Arm64Bits.TryConst(im, block + 0x28, out ulong zBits, out _)) return false;
        pivot = (float)rawPivot;
        scale = (float)rawScale;
        focus = new Vec3(pivot, 0f, Arm64Bits.F32(zBits));
        return true;
    }

    private static RoadGeometry ReadRoad(Arm64Image im, Anchors at, List<string> miss) {
        var road = new RoadGeometry();

        if (Arm64Bits.TryConst(im, at.RoadSpawnXAt, out ulong spawnBits, out _)) {
            road = road with { SpawnX = Arm64Bits.F32(spawnBits) };
        } else {
            miss.Add("roadSpawnX");
        }

        if (Arm64Bits.TryConst(im, at.RoadZAt, out ulong zBits, out _)) {
            road = road with { RoadZ = Arm64Bits.F32(zBits) };
        } else {
            miss.Add("roadZ");
        }

        if (im.TryWord(at.RoadZeroVectorAt, out uint zeroWord) && Arm64Bits.TryMoviZero(zeroWord, out int zeroRd)
            && im.TryWord(at.RoadZeroStoreAt, out uint zeroStore) && Arm64Bits.TryStur(zeroStore, out var stur)
            && stur.Rt == zeroRd && stur.Bytes == 16) {
            road = road with { RoadY = 0f };
        } else {
            miss.Add("roadY");
        }

        if (Arm64Bits.TryConst(im, at.RoadDepotStopXAt, out ulong stopBits, out _)) {
            road = road with { DepotStopX = Arm64Bits.F32(stopBits) };
        } else {
            miss.Add("roadDepotStopX");
        }

        if (Arm64Bits.TryConst(im, at.RoadDespawnXAt, out ulong despawnBits, out _)) {
            road = road with { DespawnX = Arm64Bits.F32(despawnBits) };
        } else {
            miss.Add("roadDespawnX");
        }

        if (Arm64Bits.TryFmovImm(im, at.RoadFollowGapAt, out double followGap, out _)) {
            road = road with { FollowGap = (float)followGap };
        } else {
            miss.Add("roadFollowGap");
        }

        if (Arm64Bits.TryFmovImm(im, at.RoadMaxSpeedMultAt, out double maxSpeed, out _)) {
            road = road with { MaxSpeedMult = (float)maxSpeed };
        } else {
            miss.Add("roadMaxSpeedMult");
        }

        if (Arm64Bits.TryConst(im, at.RoadRoundTripAt, out ulong roundBits, out bool roundIs64) && roundIs64) {
            road = road with { RoundTripSeconds = (float)Arm64Bits.F64(roundBits) };
        } else {
            miss.Add("roadRoundTripSeconds");
        }

        if (Arm64Bits.TryCmpImm(im, at.RoadHyperloopIndexAt, out ulong hyperloop, out _, out _)) {
            road = road with { HyperloopVehicleIndex = (int)hyperloop };
        } else {
            miss.Add("hyperloopVehicleIndex");
        }

        if (Arm64Bits.TryCmpImm(im, at.VehicleEmptyIndexAt, out ulong empty, out _, out _)) {
            road = road with { EmptyVehicleIndex = (int)empty };
        } else {
            miss.Add("emptyVehicleIndex");
        }

        return road;
    }
}
