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

    private const int LabFirstAssetType = 110;
    private const int FocusBlockWindow = 24;
    private const int FocusZSlot = 8;
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

    private sealed record Layout(
        ulong HabRowZ, ulong HabGap, ulong HabGapDoubled,
        ulong SiloZOdd, ulong SiloZEven, ulong SiloStep, ulong SiloBase, ulong SiloY,
        ulong TrophyCaseXy, ulong TrophyCaseZ, ulong TrophyColumnStep, ulong TrophyOriginX, ulong TrophyRowStep,
        ulong TrophyOriginYz, ulong TrophyColumns, ulong TrophyCount,
        ulong TrophyBonusScale, ulong TrophyBonusXy, ulong TrophyBonusZ,
        ulong LabFocusX, ulong LabFocusYz, ulong DepotFocusX, ulong DepotFocusYz,
        long LabExtentField, long DepotExtentField,
        ulong HoaFloor, ulong HoaHomeOffset, ulong HoaAltOffset, ulong HoaZ,
        ulong PoseLow, ulong PoseHigh, ulong PoseTail, ulong? MissionControlBaseYz, ulong MissionControlOffset,
        ulong FuelTankSpacing, ulong FuelTankBaseOffset, ulong FuelTankZero, ulong FuelTankSelect,
        ulong FuelTankRowOffset, ulong FuelTankZLocked, ulong FuelTankZUnlocked,
        ulong FocusTable, ulong FocusTargetAdr,
        ulong CameraDistance, ulong CameraHeight, ulong CameraUiDivisor, ulong CameraUiHeightScale,
        ulong CameraUiDistanceScale,
        ulong RoadZeroVector, ulong RoadZeroStore, ulong RoadSpawnX, ulong RoadZ, ulong RoadDespawnX,
        ulong RoadMaxSpeedMult, ulong RoadDepotStopX, ulong RoadFollowGap, ulong RoadRoundTrip,
        ulong RoadHyperloopIndex, ulong VehicleEmptyIndex,
        ulong? HatcheryFocusFmovs, ulong? HatcheryFocusZ, ulong? FuelFocusXy, ulong? FuelFocusZ) {
        public bool Fixed => HatcheryFocusFmovs is not null;
    }

    private static readonly Layout Apple = new(
        HabRowZ: 0xc, HabGap: 0xfc, HabGapDoubled: 0x2d4,
        SiloZOdd: 0x658, SiloZEven: 0x65c, SiloStep: 0x674, SiloBase: 0x678, SiloY: 0x694,
        TrophyCaseXy: 0x33c, TrophyCaseZ: 0x348, TrophyColumnStep: 0x6f8, TrophyOriginX: 0x700,
        TrophyRowStep: 0x708, TrophyOriginYz: 0x714, TrophyColumns: 0x880, TrophyCount: 0x738,
        TrophyBonusScale: 0x0, TrophyBonusXy: 0x1c, TrophyBonusZ: 0x28,
        LabFocusX: 0x64, LabFocusYz: 0x6c, DepotFocusX: 0x80, DepotFocusYz: 0x88,
        LabExtentField: 0x3d0, DepotExtentField: 0x3d4,
        HoaFloor: 0x58, HoaHomeOffset: 0x60, HoaAltOffset: 0x7c, HoaZ: 0x94,
        PoseLow: 0x40, PoseHigh: 0x48, PoseTail: 0x54, MissionControlBaseYz: 0x98, MissionControlOffset: 0xc0,
        FuelTankSpacing: 0x50, FuelTankBaseOffset: 0xc8, FuelTankZero: 0xe4, FuelTankSelect: 0xe8,
        FuelTankRowOffset: 0xf8, FuelTankZLocked: 0x10c, FuelTankZUnlocked: 0x118,
        FocusTable: 0x44, FocusTargetAdr: 0x4c,
        CameraDistance: 0x30, CameraHeight: 0x3c, CameraUiDivisor: 0x64, CameraUiHeightScale: 0x70,
        CameraUiDistanceScale: 0x7c,
        RoadZeroVector: 0x44c, RoadZeroStore: 0x450, RoadSpawnX: 0x47c, RoadZ: 0x484, RoadDespawnX: 0x720,
        RoadMaxSpeedMult: 0x748, RoadDepotStopX: 0x74c, RoadFollowGap: 0x8c8, RoadRoundTrip: 0x3a8,
        RoadHyperloopIndex: 0x300, VehicleEmptyIndex: 0x1e8,
        HatcheryFocusFmovs: 0x08, HatcheryFocusZ: 0x28, FuelFocusXy: 0xc, FuelFocusZ: 0x20);

    private static readonly Layout Ndk = new(
        HabRowZ: 0x08, HabGap: 0x114, HabGapDoubled: 0x1dc,
        SiloZOdd: 0x5f4, SiloZEven: 0x5f8, SiloStep: 0x610, SiloBase: 0x614, SiloY: 0x618,
        TrophyCaseXy: 0x2fc, TrophyCaseZ: 0x318, TrophyColumnStep: 0x678, TrophyOriginX: 0x63c,
        TrophyRowStep: 0x684, TrophyOriginYz: 0x688, TrophyColumns: 0x7f0, TrophyCount: 0x6a8,
        TrophyBonusScale: 0x0, TrophyBonusXy: 0x18, TrophyBonusZ: 0x20,
        LabFocusX: 0x64, LabFocusYz: 0x50, DepotFocusX: 0x88, DepotFocusYz: 0x74,
        LabExtentField: 0x3e8, DepotExtentField: 0x3ec,
        HoaFloor: 0x58, HoaHomeOffset: 0x60, HoaAltOffset: 0x74, HoaZ: 0x90,
        PoseLow: 0x3c, PoseHigh: 0x40, PoseTail: 0x50, MissionControlBaseYz: null, MissionControlOffset: 0xd0,
        FuelTankSpacing: 0x4c, FuelTankBaseOffset: 0xe0, FuelTankZero: 0xe4, FuelTankSelect: 0x10c,
        FuelTankRowOffset: 0x118, FuelTankZLocked: 0xf0, FuelTankZUnlocked: 0xe8,
        FocusTable: 0x38, FocusTargetAdr: 0x40,
        CameraDistance: 0x40, CameraHeight: 0x4c, CameraUiDivisor: 0x74, CameraUiHeightScale: 0x90,
        CameraUiDistanceScale: 0x7c,
        RoadZeroVector: 0x434, RoadZeroStore: 0x454, RoadSpawnX: 0x43c, RoadZ: 0x45c, RoadDespawnX: 0x6d4,
        RoadMaxSpeedMult: 0x6ec, RoadDepotStopX: 0xb3c, RoadFollowGap: 0x87c, RoadRoundTrip: 0x3a0,
        RoadHyperloopIndex: 0x2fc, VehicleEmptyIndex: 0x1e0,
        HatcheryFocusFmovs: null, HatcheryFocusZ: null, FuelFocusXy: null, FuelFocusZ: null);

    private sealed record Anchors(
        ulong Hab, ulong Silo, ulong Trophy, ulong TrophyBonus, ulong Lab, ulong Depot,
        ulong Hoa, ulong Mc, ulong Fuel, ulong Focus, ulong Cam, ulong Road, ulong Hire, Layout L) {
        public ulong HabAnchorAdrp => Hab;
        public ulong HabRowZAt => Hab + L.HabRowZ;
        public ulong HabGapAt => Hab + L.HabGap;
        public ulong HabGapDoubledAt => Hab + L.HabGapDoubled;

        public ulong SiloZOddAt => Silo + L.SiloZOdd;
        public ulong SiloZEvenAt => Silo + L.SiloZEven;
        public ulong SiloStepAt => Silo + L.SiloStep;
        public ulong SiloBaseAt => Silo + L.SiloBase;
        public ulong SiloYAt => Silo + L.SiloY;

        public ulong TrophyCaseXyAdrp => Trophy + L.TrophyCaseXy;
        public ulong TrophyCaseZAt => Trophy + L.TrophyCaseZ;
        public ulong TrophyColumnStepAdrp => Trophy + L.TrophyColumnStep;
        public ulong TrophyOriginXAt => Trophy + L.TrophyOriginX;
        public ulong TrophyRowStepAdrp => Trophy + L.TrophyRowStep;
        public ulong TrophyOriginYzAdrp => Trophy + L.TrophyOriginYz;
        public ulong TrophyColumnsAt => Trophy + L.TrophyColumns;
        public ulong TrophyCountAt => Trophy + L.TrophyCount;
        public ulong TrophyBonusScaleAt => TrophyBonus + L.TrophyBonusScale;
        public ulong TrophyBonusXyAdrp => TrophyBonus + L.TrophyBonusXy;
        public ulong TrophyBonusZAdrp => TrophyBonus + L.TrophyBonusZ;

        public ulong UpdateLabAt => Lab;
        public ulong LabFocusXAt => Lab + L.LabFocusX;
        public ulong LabFocusYzAdrp => Lab + L.LabFocusYz;
        public ulong UpdateDepotAt => Depot;
        public ulong DepotFocusXAt => Depot + L.DepotFocusX;
        public ulong DepotFocusYzAdrp => Depot + L.DepotFocusYz;
        public long LabExtentField => L.LabExtentField;
        public long DepotExtentField => L.DepotExtentField;

        public ulong HoaFloorAt => Hoa + L.HoaFloor;
        public ulong HoaHomeOffsetAt => Hoa + L.HoaHomeOffset;
        public ulong HoaAltOffsetAt => Hoa + L.HoaAltOffset;
        public ulong HoaZAdrp => Hoa + L.HoaZ;

        public ulong PoseLowAdrp => Mc + L.PoseLow;
        public ulong PoseHighAdrp => Mc + L.PoseHigh;
        public ulong PoseTailAt => Mc + L.PoseTail;
        public ulong? MissionControlBaseYzAdrp => L.MissionControlBaseYz is { } o ? Mc + o : null;
        public ulong MissionControlOffsetAt => Mc + L.MissionControlOffset;

        public ulong FuelTankSpacingAdrp => Fuel + L.FuelTankSpacing;
        public ulong FuelTankBaseOffsetAt => Fuel + L.FuelTankBaseOffset;
        public ulong FuelTankZeroAt => Fuel + L.FuelTankZero;
        public ulong FuelTankSelectAt => Fuel + L.FuelTankSelect;
        public ulong FuelTankRowOffsetAt => Fuel + L.FuelTankRowOffset;
        public ulong FuelTankZLockedAt => Fuel + L.FuelTankZLocked;
        public ulong FuelTankZUnlockedAt => Fuel + L.FuelTankZUnlocked;

        public ulong FocusTableAdrp => Focus + L.FocusTable;
        public ulong FocusTargetAdrAt => Focus + L.FocusTargetAdr;
        public ulong CameraDistanceAdrp => Cam + L.CameraDistance;
        public ulong CameraHeightAdrp => Cam + L.CameraHeight;
        public ulong CameraUiDivisorAt => Cam + L.CameraUiDivisor;
        public ulong CameraUiHeightScaleAt => Cam + L.CameraUiHeightScale;
        public ulong CameraUiDistanceScaleAt => Cam + L.CameraUiDistanceScale;

        public ulong RoadZeroVectorAt => Road + L.RoadZeroVector;
        public ulong RoadZeroStoreAt => Road + L.RoadZeroStore;
        public ulong RoadSpawnXAt => Road + L.RoadSpawnX;
        public ulong RoadZAt => Road + L.RoadZ;
        public ulong RoadDespawnXAt => Road + L.RoadDespawnX;
        public ulong RoadMaxSpeedMultAt => Road + L.RoadMaxSpeedMult;
        public ulong RoadDepotStopXAt => Road + L.RoadDepotStopX;
        public ulong RoadFollowGapAt => Road + L.RoadFollowGap;
        public ulong RoadRoundTripAt => Road + L.RoadRoundTrip;
        public ulong RoadHyperloopIndexAt => Road + L.RoadHyperloopIndex;
        public ulong VehicleEmptyIndexAt => Hire + L.VehicleEmptyIndex;
    }

    private static readonly (string Field, string[] Needles)[] AnchorSymbols = [
        ("Hab", ["GameController14getHabPosition"]),
        ("Silo", ["FarmScene10updateSilo"]),
        ("Trophy", ["FarmScene16updateTrophyCase"]),
        (TrophyBonusField, ["ZN9FarmScene16updateTrophyCase", "$_2", "clEv"]),
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

    private const string TrophyBonusField = "TrophyBonus";
    private const string TrophyBonusRtti = "FarmScene16updateTrophyCaseEP14GameControllerE3$_";
    private const int FunctorInvokeSlot = 0x30;
    private const int FunctorTypeNameSlot = 0x40;
    private const int FunctorScanWindow = 0x200;
    private const int TypeInfoNameSlot = 8;

    private static Anchors? ResolveAnchors(IBinaryImage img, Arm64Image im, Layout layout, out string diagnostics) {
        var found = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach ((string field, string[] needles) in AnchorSymbols) {
            if (img.TryFindFunc(needles, out var fn)) found[field] = fn.Start;
            else if (field == TrophyBonusField && img is ElfImage elf
                     && TryFindTrophyBonusInvoke(elf, im, found.GetValueOrDefault("Trophy"), out ulong invoke))
                found[field] = invoke;
            else unresolved.Add(field);
        }

        if (unresolved.Count > 0) {
            diagnostics = $"{unresolved.Count} placement functions not found by symbol: "
                          + string.Join(", ", unresolved);
            return null;
        }

        diagnostics = "";
        return new Anchors(found["Hab"], found["Silo"], found["Trophy"], found[TrophyBonusField], found["Lab"],
            found["Depot"], found["Hoa"], found["Mc"], found["Fuel"], found["Focus"], found["Cam"],
            found["Road"], found["Hire"], layout);
    }

    private static bool TryFindTrophyBonusInvoke(ElfImage elf, Arm64Image im, ulong trophyStart, out ulong invoke) {
        invoke = 0;
        if (trophyStart == 0) return false;
        var vtables = new List<ulong>();
        for (int i = 0; i < FunctorScanWindow * 4; i++) {
            ulong va = trophyStart + (ulong)(4 * i);
            if (!im.TryWord(va, out uint w) || !Arm64Bits.TryAdrp(va, w, out _, out _)) continue;
            if (im.TryPageRef(va, out ulong target) && target % 8 == 0 && !vtables.Contains(target))
                vtables.Add(target);
        }

        vtables.Reverse();
        foreach (ulong vtable in vtables) {
            if (!elf.TryResolveRelative(vtable + FunctorTypeNameSlot, out ulong typeNameFn)) continue;
            if (!im.TryFirstAdrp(typeNameFn, 8, out ulong tiAdrp) || !im.TryPageRef(tiAdrp, out ulong typeInfo))
                continue;
            if (!elf.TryResolveRelative(typeInfo + TypeInfoNameSlot, out ulong namePtr)) namePtr = typeInfo;
            if (!TryCString(im, namePtr, out string name) || !name.Contains(TrophyBonusRtti, StringComparison.Ordinal))
                continue;
            if (!elf.TryResolveRelative(vtable + FunctorInvokeSlot, out ulong candidate)) continue;
            if (!IsBonusShape(im, candidate)) continue;
            invoke = candidate;
            return true;
        }

        return false;
    }

    private static bool TryCString(Arm64Image im, ulong va, out string text) {
        var bytes = new List<byte>();
        for (int i = 0; i < 128; i++) {
            if (!im.TryByte(va + (ulong)i, out byte b)) break;
            if (b == 0) {
                text = System.Text.Encoding.ASCII.GetString([.. bytes]);
                return bytes.Count > 0;
            }

            bytes.Add(b);
        }

        text = "";
        return false;
    }

    public static Result Extract(byte[] bin,
        IReadOnlyList<HabCatalogExtractor.HabEntry> habs,
        IReadOnlyList<EggCatalogExtractor.EggEntry> eggs,
        IReadOnlyList<VehicleCatalogExtractor.VehicleEntry> vehicles,
        string binaryVersion) {
        if (BinaryImage.Load(bin) is not { } img)
            return new Result(false, new FarmPlacementData(), [], "binary is neither Mach-O nor ELF");

        var im = new Arm64Image(bin, img);
        if (ResolveAnchors(img, im, img is ElfImage ? Ndk : Apple, out string anchorDiag) is not { } at)
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

    private static bool IsBonusShape(Arm64Image im, ulong fn) {
        if (!Arm64Bits.TryConst(im, fn + Ndk.TrophyBonusScale, out ulong bits, out bool is64) || is64) return false;
        float scale = Arm64Bits.F32(bits);
        if (!float.IsFinite(scale) || scale <= 0f) return false;
        return im.TryPageRef(fn + Ndk.TrophyBonusXy, out _, out int xyBytes) && xyBytes == 16
               && im.TryPageRef(fn + Ndk.TrophyBonusZ, out _, out int zBytes) && zBytes == 8;
    }

    private static FarmPlacementData ReadExtentTables(Arm64Image im, Anchors at, List<string> miss, FarmPlacementData data) {
        if (Arm64Switch.TryExtents(im, at.UpdateLabAt, at.LabExtentField, LabFirstAssetType, LabTierCount,
                out float[] lab)) {
            data = data with { LabExtents = lab };
        } else {
            miss.Add("labExtents");
        }

        if (Arm64Switch.TryExtents(im, at.UpdateDepotAt, at.DepotExtentField, DepotFirstAssetType, DepotTierCount,
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
        if (at.MissionControlBaseYzAdrp is not { } adrp) return data.MissionControlPose.Count > 0;
        if (!im.TryPageRef(adrp, out ulong va) || !im.TryF32(va, out float y)
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
            if (im.TryWord(at.FuelTankZeroAt, out uint zeroWord) && Arm64Bits.IsZeroToFp(zeroWord)
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
            && TryHabFocus(im, habBlock, out float habZ)) {
            data = data with { HabFocusOffset = new Vec3(0f, 0f, habZ) };
        } else {
            miss.Add("habFocusOffset");
        }

        if (TryBlock(im, tableVa, targetBase, HatcheryElement, out ulong hatcheryBlock)
            && TryHatcheryFocus(im, at, hatcheryBlock, out var hatchery, out float pivot, out float scale)) {
            data = data with {
                HatcheryFocusBase = hatchery,
                FocusExtentPivot = pivot,
                FocusExtentScale = scale
            };
        } else {
            miss.Add("hatcheryFocusBase");
        }

        if (TryBlock(im, tableVa, targetBase, HoaElement, out ulong hoaBlock)
            && TryHoaFocusExtra(im, at, hoaBlock, out float hoaExtra)) {
            data = data with { HoaFocusExtra = hoaExtra };
        } else {
            miss.Add("hoaFocusExtra");
        }

        if (TryBlock(im, tableVa, targetBase, FuelTankElement, out ulong fuelBlock)
            && TryFuelFocus(im, at, fuelBlock, out var fuelFocus)) {
            data = data with { FuelTankFocusOffset = fuelFocus };
        } else {
            miss.Add("fuelTankFocusOffset");
        }

        return data;
    }

    private static bool TryHabFocus(Arm64Image im, ulong block, out float z) {
        z = 0f;
        if (!TryFirstFmov(im, block, 16, out double habZ)) return false;
        z = (float)habZ;
        return true;
    }

    private static bool TryHoaFocusExtra(Arm64Image im, Anchors at, ulong block, out float extra) {
        extra = 0f;
        if (!Arm64Bits.TryFirstBranch(im, block, 48, out ulong merge)) return false;
        int limit = at.L.Fixed ? 1 : 4;
        if (!TryFirstFmov(im, merge, limit, out double v)) return false;
        extra = (float)v;
        return true;
    }

    private static bool TryStaticStore(Arm64Image im, ulong block, int offset, out float value) {
        value = 0f;
        var regs = new ulong?[32];
        for (int i = 0; i < FocusBlockWindow; i++) {
            ulong va = block + (ulong)(4 * i);
            if (!im.TryWord(va, out uint w)) return false;
            if (Arm64Bits.TryMovWide(w, out int rd, out ulong v, out var kind, out _)) {
                regs[rd] = kind == Arm64Bits.MovKind.Movk ? Arm64Bits.Merge(regs[rd] ?? 0, w) : v;
                continue;
            }

            if (Arm64Bits.TryStore(w, out var store) && store is { Size: 2, Fp: false } && store.Rn != 31
                && store.Offset == offset && regs[store.Rt] is { } bits) {
                value = Arm64Bits.F32(bits);
                return true;
            }

            if (Arm64Bits.TryBranch(w, va, out _)) return false;
        }

        return false;
    }

    private static bool TryHatcheryFocus(Arm64Image im, Anchors at, ulong block, out Vec3 focus, out float pivot,
        out float scale) {
        focus = Vec3.Zero;
        pivot = 0f;
        scale = 0f;
        if (at.L.HatcheryFocusFmovs is { } fmovs && at.L.HatcheryFocusZ is { } zAt) {
            if (!Arm64Bits.TryFmovImm(im, block + fmovs, out double negativePivot, out _)) return false;
            if (!Arm64Bits.TryFmovImm(im, block + fmovs + 8, out double rawScale, out _)) return false;
            if (!Arm64Bits.TryFmovImm(im, block + fmovs + 16, out double rawPivot, out _)) return false;
            if (Math.Abs(negativePivot + rawPivot) > 1e-9) return false;
            if (!Arm64Bits.TryConst(im, block + zAt, out ulong zBits, out _)) return false;
            pivot = (float)rawPivot;
            scale = (float)rawScale;
            focus = new Vec3(pivot, 0f, Arm64Bits.F32(zBits));
            return true;
        }

        var fmovValues = new List<double>();
        for (int i = 0; i < FocusBlockWindow; i++) {
            ulong va = block + (ulong)(4 * i);
            if (!im.TryWord(va, out uint w)) return false;
            if (Arm64Bits.TryFmovImm(w, out double v, out _, out _)) fmovValues.Add(v);
            if (Arm64Bits.TryBranch(w, va, out _)) break;
        }

        if (fmovValues.Count < 3 || Math.Abs(fmovValues[0] + fmovValues[2]) > 1e-9) return false;
        if (!TryStaticStore(im, block, FocusZSlot, out float z)) return false;
        pivot = (float)fmovValues[2];
        scale = (float)fmovValues[1];
        focus = new Vec3(pivot, 0f, z);
        return true;
    }

    private static bool TryFuelFocus(Arm64Image im, Anchors at, ulong block, out Vec3 focus) {
        focus = Vec3.Zero;
        ulong xyAdrp;
        ulong zAt;
        if (at.L.FuelFocusXy is { } xyOff && at.L.FuelFocusZ is { } zOff) {
            xyAdrp = block + xyOff;
            zAt = block + zOff;
        } else {
            if (!im.TryFirstAdrp(block, 16, out xyAdrp)) return false;
            if (!TryFirstFmovAt(im, xyAdrp + 4, 16, out zAt)) return false;
        }

        if (!im.TryPageRef(xyAdrp, out ulong fuelVa) || !im.TryF32(fuelVa, out float x)
            || !im.TryF32(fuelVa + 4, out float y)
            || !Arm64Bits.TryFmovImm(im, zAt, out double z, out _)) return false;
        focus = new Vec3(x, y, (float)z);
        return true;
    }

    private static bool TryFirstFmovAt(Arm64Image im, ulong start, int limit, out ulong at) {
        for (int i = 0; i < limit; i++) {
            at = start + (ulong)(4 * i);
            if (Arm64Bits.TryFmovImm(im, at, out _, out _)) return true;
        }

        at = 0;
        return false;
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
        bool followed = false;
        ulong pc = block;
        for (int i = 0; i < 10; i++) {
            if (!im.TryWord(pc, out uint word)) break;
            if (Arm64Bits.TryMovWide(word, out int rd, out ulong value, out var kind, out _)) {
                regs[rd] = kind == Arm64Bits.MovKind.Movk ? Arm64Bits.Merge(regs[rd] ?? 0, word) : value;
                pc += 4;
                continue;
            }

            if (Arm64Bits.TryStore(word, out var store) && store is { Size: 2, Fp: false }
                && store.Rn != 31 && store.Offset is 0 or 4 or 8) {
                parts[store.Offset / 4] = store.Rt == 31 ? 0f : Arm64Bits.F32(regs[store.Rt] ?? 0);
                wrote = true;
                pc += 4;
                continue;
            }

            if (!wrote && !followed && Arm64Bits.TryBranch(word, pc, out ulong target)) {
                followed = true;
                pc = target;
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
