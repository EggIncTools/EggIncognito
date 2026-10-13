using EggSharp.GameData.Farm;
using AssetType = Ei.ShellSpec.Types.AssetType;
using FarmElement = Ei.ShellDB.Types.FarmElement;

namespace EggIncognito.Core.Services.Farm;

public static class FarmPlacementEngine {
    public const string HabLocator = "GameController::getHabPosition 0x10040593c";
    public const string SiloLocator = "FarmScene::updateSilo 0x10008e080";
    public const string TrophyLocator = "FarmScene::updateTrophyCase 0x10008bc0c";
    public const string GroundLocator = "FarmScene::updateGround $_1 0x1000acd80";
    public const string FarmLocator = "FarmScene::updateFarm $_1 0x1000acb1c";
    public const string HyperloopLocator = "FarmScene::updateHyperloop 0x1000907b8";
    public const string LabLocator = "FarmScene::updateLab 0x10008efb0";
    public const string DepotLocator = "FarmScene::updateDepot 0x10008fb38";
    public const string HatcheryLocator = "FarmScene::updateHatchery 0x100094324";
    public const string HoaLocator = "FarmScene::hoaPos 0x1000a46e0";
    public const string MissionControlLocator = "FarmScene::missionControlPos 0x1000a4ba0";
    public const string FuelTankLocator = "FarmScene::fuelTankPos 0x1000a4ca0";

    private const string BakedGeometry = "no transform lambda installed; geometry baked in the rpo";

    private const string InferredHabTier =
        "the appearance carries no shell_configs, so slot occupancy came from shell_set_configs and the hab tier "
        + "is a placeholder; hab width drives X, so these positions are not the game's";

    private const string InferredSiloTier =
        "silo count came from shell_set_configs because the appearance carries no shell_configs";

    public static readonly string[] TrophyStems =
        ["ei_bronze_trophy", "ei_silver_trophy", "ei_gold_trophy", "ei_plat_trophy", "ei_diamond_trophy"];

    public static FarmLayout Place(FarmState state, FarmPlacementData data) {
        var extents = ResolveExtents(state, data);
        var list = new List<FarmPlacement>();

        AddFarm(list, state);
        AddHabs(list, state, data);
        AddSilos(list, state, data);
        AddBuildings(list, state, data, extents);
        AddTrophyCase(list, state, data);

        return new FarmLayout(list, extents);
    }

    public static FarmExtents ResolveExtents(FarmState state, FarmPlacementData data) {
        float lab = Pick(data.LabExtents, state.LabTier);
        float depot = Pick(data.DepotExtents, state.DepotTier);
        bool resolved = data.TryHatcheryExtent(FarmState.EggTableIndex(state.EggType), state.EggType.ToString(),
            out float hatchery);
        return new FarmExtents(lab, depot, hatchery, resolved);
    }

    public static Vec3 HabPosition(FarmState state, FarmPlacementData data, int slot) {
        float half0 = (float)(data.HabWidth(state.HabTier(0)) * 0.5d);
        float halfN = (float)(data.HabWidth(state.HabTier(slot)) * 0.5d);
        float gap = data.HabGap;
        float x = slot switch {
            0 => data.HabAnchorX,
            1 => data.HabAnchorX + half0 + halfN + gap,
            2 => data.HabAnchorX - half0 - halfN - gap,
            3 => data.HabAnchorX + half0 + (float)data.HabWidth(state.HabTier(1)) + halfN + (gap * 2f),
            _ => data.HabAnchorX
        };

        return new Vec3(x, data.HabRowY, data.HabRowZ);
    }

    public static Vec3 SiloPosition(FarmPlacementData data, int index) =>
        new(data.SiloStepX * (index / 2) + data.SiloBaseX, data.SiloY,
            index % 2 == 0 ? data.SiloZEven : data.SiloZOdd);

    public static Vec3 HoaPosition(FarmState state, FarmPlacementData data, FarmExtents e) {
        float x = state.HomeFarm && state.MissionControlLevel <= 1
            ? Max4(e.Hatchery, e.Depot, e.Lab, data.SingletonFloor) + data.HoaHomeOffset
            : (float)(Math.Max(e.Lab, data.SingletonFloor) + (double)data.HoaAltOffset);
        return new Vec3(x, 0f, data.HoaZ);
    }

    public static Vec3 MissionControlPosition(FarmState state, FarmPlacementData data, FarmExtents e) {
        var pose = Pose(state, data);
        return new Vec3(pose.X + Math.Max(e.Hatchery, e.Depot) + data.MissionControlOffset, pose.Y, pose.Z);
    }

    public static Vec3 FuelTankPosition(FarmState state, FarmPlacementData data, FarmExtents e) {
        float baseX = Pose(state, data).X;
        float spacing = Pick(data.FuelTankSpacing, state.MissionControlLevel);
        float extra = (float)((double)spacing + data.FuelTankBaseOffset
                              + (state.FuelTankUnlocked ? 0d : data.FuelTankLockedExtra));
        float x = baseX + Math.Max(e.Hatchery, e.Depot) + data.MissionControlOffset + extra;
        return new Vec3(x, 0f, state.FuelTankUnlocked ? data.FuelTankZUnlocked : data.FuelTankZLocked);
    }

    public static Vec3 TrophyPosition(FarmPlacementData data, int slot) {
        var t = data.Trophy;
        int columns = t.Columns > 0 ? t.Columns : 5;
        int row = slot / columns;
        int col = slot - (columns * row);
        return new Vec3(
            (col * t.ColumnStepX) + t.OriginX,
            (row * t.RowStepY) + t.OriginY,
            (row * t.RowStepZ) + t.OriginZ);
    }

    private static void AddFarm(List<FarmPlacement> list, FarmState state) {
        if (!state.ArMode) {
            list.Add(FarmPlacement.At(FarmElement.Ground, AssetType.Ground, 0, Vec3.Zero,
                PlacementProvenance.FromBinary(GroundLocator)));
        }

        list.Add(FarmPlacement.At(FarmElement.Hardscape, AssetType.Hardscape, 0, Vec3.Zero,
            PlacementProvenance.FromBinary(FarmLocator)));

        list.Add(FarmPlacement.At(FarmElement.Mailbox,
            state.HasUnreadMail ? AssetType.MailboxFull : AssetType.Mailbox, 0, Vec3.Zero,
            PlacementProvenance.FromBinary("FarmScene::updateMailbox 0x10009a164")));
    }

    private static void AddHabs(List<FarmPlacement> list, FarmState state, FarmPlacementData data) {
        var provenance = state.HabTiersInferred
            ? PlacementProvenance.Authored(InferredHabTier)
            : PlacementProvenance.FromBinary(HabLocator);
        for (int slot = 0; slot < FarmState.HabSlots; slot++) {
            if (!state.HabOccupied(slot)) continue;
            var type = (AssetType)(state.HabTier(slot) + 1);
            list.Add(FarmPlacement.At(FarmElement.HenHouse, type, slot, HabPosition(state, data, slot), provenance));
        }
    }

    private static void AddSilos(List<FarmPlacement> list, FarmState state, FarmPlacementData data) {
        var provenance = state.SiloCountInferred
            ? PlacementProvenance.Authored(InferredSiloTier)
            : PlacementProvenance.FromBinary(SiloLocator);
        int count = Math.Clamp(state.SilosOwned, 0, FarmState.MaxSilos);
        for (int i = 0; i < count; i++) {
            list.Add(FarmPlacement.At(FarmElement.Silo, state.SiloAssetType, i, SiloPosition(data, i), provenance));
        }
    }

    private static void AddBuildings(List<FarmPlacement> list, FarmState state, FarmPlacementData data,
        FarmExtents e) {
        list.Add(FarmPlacement.At(FarmElement.Depot, (AssetType)((int)AssetType.Depot1 + state.DepotTier), 0,
            Vec3.Zero, PlacementProvenance.FromBinary(DepotLocator, BakedGeometry)));

        AddHyperloop(list, state);

        list.Add(FarmPlacement.At(FarmElement.Lab, (AssetType)((int)AssetType.Lab1 + state.LabTier), 0,
            Vec3.Zero, PlacementProvenance.FromBinary(LabLocator, BakedGeometry)));

        list.Add(FarmPlacement.At(FarmElement.Hatchery, state.HatcheryAssetType, 0, Vec3.Zero,
            PlacementProvenance.FromBinary(HatcheryLocator, BakedGeometry)));

        list.Add(FarmPlacement.At(FarmElement.Hoa, (AssetType)((int)AssetType.Hoa1 + state.HoaTier), 0,
            HoaPosition(state, data, e), PlacementProvenance.FromBinary(HoaLocator)));

        list.Add(FarmPlacement.At(FarmElement.MissionControl,
            (AssetType)((int)AssetType.MissionControl1 + state.MissionControlLevel), 0,
            MissionControlPosition(state, data, e), PlacementProvenance.FromBinary(MissionControlLocator)));

        list.Add(FarmPlacement.At(FarmElement.FuelTank,
            (AssetType)((int)AssetType.FuelTank1 + state.FuelTankTier), 0,
            FuelTankPosition(state, data, e), PlacementProvenance.FromBinary(FuelTankLocator)));
    }

    private static void AddHyperloop(List<FarmPlacement> list, FarmState state) {
        var provenance = PlacementProvenance.FromBinary(HyperloopLocator, BakedGeometry);
        if (state.HyperloopStation) {
            list.Add(FarmPlacement.At(FarmElement.Hyperloop, AssetType.Hyperloop, 0, Vec3.Zero, provenance));
            list.Add(FarmPlacement.At(FarmElement.Hyperloop, AssetType.HyperloopTrack, 0, Vec3.Zero, provenance));
            return;
        }

        if (!state.HyperloopUnderConstruction) return;
        list.Add(new FarmPlacement(FarmElement.Hyperloop, null, 0, Vec3.Zero, Vec3.Zero, 1f, provenance) {
            Stem = "ei_hyperloop_construction"
        });
    }

    private static void AddTrophyCase(List<FarmPlacement> list, FarmState state, FarmPlacementData data) {
        var t = data.Trophy;
        list.Add(FarmPlacement.At(FarmElement.TrophyCase, AssetType.TrophyCase, 0, t.CasePos,
            PlacementProvenance.FromBinary(TrophyLocator)));

        int count = t.Count > 0 ? t.Count : 19;
        for (int i = 0; i < count; i++) {
            string? stem = TrophyStemFor(state, i);
            if (stem is null) continue;
            list.Add(new FarmPlacement(FarmElement.TrophyCase, null, i, TrophyPosition(data, i), Vec3.Zero, 1f,
                PlacementProvenance.FromBinary(TrophyLocator)) { Stem = stem });
        }

        if (!state.AllTrophiesComplete) return;
        list.Add(new FarmPlacement(FarmElement.TrophyCase, null, count, t.BonusPos, Vec3.Zero, t.BonusScale,
            PlacementProvenance.FromBinary(TrophyLocator + " $_2 0x1000b1d44")) {
            Stem = TrophyStems[^1]
        });
    }

    private static string? TrophyStemFor(FarmState state, int eggIndex) {
        if (eggIndex >= state.EggMedalLevel.Count) return null;
        int level = state.EggMedalLevel[eggIndex];
        return level <= 0 ? null : TrophyStems[Math.Clamp(level - 1, 0, TrophyStems.Length - 1)];
    }

    private static Vec3 Pose(FarmState state, FarmPlacementData data) {
        if (data.MissionControlPose.Count == 0) return Vec3.Zero;
        int level = state.ArtifactsEnabled
            ? Math.Clamp(state.MissionControlLevel, 0, data.MissionControlPose.Count - 1)
            : 0;
        return data.MissionControlPose[level];
    }

    private static float Pick(IReadOnlyList<float> table, int index) =>
        table.Count == 0 ? 0f : table[Math.Clamp(index, 0, table.Count - 1)];

    private static float Max4(float a, float b, float c, float d) =>
        Math.Max(Math.Max(a, b), Math.Max(c, d));
}
