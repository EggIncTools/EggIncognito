using EggSharp.GameData.Farm;
using Ei;
using AssetType = Ei.ShellSpec.Types.AssetType;

namespace EggIncognito.Core.Services.Farm;

public static class FarmStateBuilder {
    public static FarmState FromConfiguration(ShellDB.Types.FarmConfiguration? config) {
        var state = new FarmState { Appearance = config };
        if (config is null) return state;

        var habs = new int[FarmState.HabSlots];
        Array.Fill(habs, FarmState.EmptyHabTier);

        int silos = 0;
        var siloType = AssetType.Silo0Small;
        bool sawSilo = false;
        int lab = 0;
        int depot = 0;
        int hoa = 0;
        int mc = 0;
        int fuel = 0;
        bool hyperloop = false;
        bool unreadMail = false;
        var hatchery = AssetType.HatcheryEdible;
        bool sawHatchery = false;

        foreach (var c in config.ShellConfigs) {
            var type = c.AssetType;
            int index = (int)c.Index;
            int raw = (int)type;

            if (raw is >= (int)AssetType.Coop and <= (int)AssetType.ChickenUniverse) {
                if (index is >= 0 and < FarmState.HabSlots) habs[index] = raw - 1;
                continue;
            }

            if (IsSilo(type)) {
                sawSilo = true;
                siloType = type;
                silos = Math.Max(silos, index + 1);
                continue;
            }

            if (raw is >= (int)AssetType.Depot1 and <= (int)AssetType.Depot7) {
                depot = raw - (int)AssetType.Depot1;
            } else if (raw is >= (int)AssetType.Lab1 and <= (int)AssetType.Lab6) {
                lab = raw - (int)AssetType.Lab1;
            } else if (raw is >= (int)AssetType.Hoa1 and <= (int)AssetType.Hoa3) {
                hoa = raw - (int)AssetType.Hoa1;
            } else if (raw is >= (int)AssetType.MissionControl1 and <= (int)AssetType.MissionControl3) {
                mc = raw - (int)AssetType.MissionControl1;
            } else if (raw is >= (int)AssetType.FuelTank1 and <= (int)AssetType.FuelTank4) {
                fuel = raw - (int)AssetType.FuelTank1;
            } else if (type is AssetType.Hyperloop or AssetType.HyperloopTrack) {
                hyperloop = true;
            } else if (type == AssetType.MailboxFull) {
                unreadMail = true;
            } else if (IsPrimaryHatchery(type)) {
                sawHatchery = true;
                hatchery = type;
            }
        }

        bool habsInferred = false;
        bool silosInferred = false;
        foreach (var s in config.ShellSetConfigs) {
            int index = (int)s.Index;
            if (s.Element == ShellDB.Types.FarmElement.HenHouse && index is >= 0 and < FarmState.HabSlots) {
                if (habs[index] != FarmState.EmptyHabTier) continue;
                habs[index] = FarmState.PlaceholderHabTier;
                habsInferred = true;
            } else if (s.Element == ShellDB.Types.FarmElement.Silo && !sawSilo && index >= silos) {
                silos = index + 1;
                silosInferred = true;
            }
        }

        return state with {
            Habs = habs,
            HabTiersInferred = habsInferred,
            SilosOwned = silos,
            SiloCountInferred = silosInferred,
            SiloAssetType = sawSilo ? siloType : state.SiloAssetType,
            LabTier = lab,
            DepotTier = depot,
            HoaTier = hoa,
            MissionControlLevel = mc,
            FuelTankTier = fuel,
            HyperloopStation = hyperloop,
            HasUnreadMail = unreadMail,
            HatcheryAssetType = sawHatchery ? hatchery : state.HatcheryAssetType,
            EggType = sawHatchery ? EggFor(hatchery) : state.EggType,
            ArtifactsEnabled = mc > 0 || fuel > 0
        };
    }

    public static bool CarriesNoAppearance(ShellDB.Types.FarmConfiguration? config) =>
        config is null
        || (config.ShellConfigs.Count == 0 && config.ShellSetConfigs.Count == 0
            && config.GroupConfigs.Count == 0 && config.ChickenConfigs.Count == 0
            && config.LockedElements.Count == 0 && config.LightingConfig is null);

    public static bool IsSilo(AssetType t) =>
        t is AssetType.Silo0Small or AssetType.Silo0Med or AssetType.Silo0Large
            or AssetType.Silo1Small or AssetType.Silo1Med or AssetType.Silo1Large or AssetType.SiloAll;

    public static bool IsPrimaryHatchery(AssetType t) {
        int raw = (int)t;
        return raw is (>= 120 and <= 143) or 150 or (>= 160 and <= 164);
    }

    public static bool IsHatcheryPiece(AssetType t) {
        int raw = (int)t;
        return raw is >= 500 and <= 554;
    }

    public static Egg EggFor(AssetType hatchery) => hatchery switch {
        AssetType.HatcheryEdible => Egg.Edible,
        AssetType.HatcherySuperfood => Egg.Superfood,
        AssetType.HatcheryMedical => Egg.Medical,
        AssetType.HatcheryRocketFuel => Egg.RocketFuel,
        AssetType.HatcherySupermaterial => Egg.SuperMaterial,
        AssetType.HatcheryFusion => Egg.Fusion,
        AssetType.HatcheryQuantum => Egg.Quantum,
        AssetType.HatcheryImmortality => Egg.Immortality,
        AssetType.HatcheryTachyon => Egg.Tachyon,
        AssetType.HatcheryGraviton => Egg.Graviton,
        AssetType.HatcheryDilithium => Egg.Dilithium,
        AssetType.HatcheryProdigy => Egg.Prodigy,
        AssetType.HatcheryTerraform => Egg.Terraform,
        AssetType.HatcheryAntimatter => Egg.Antimatter,
        AssetType.HatcheryDarkMatter => Egg.DarkMatter,
        AssetType.HatcheryAi => Egg.Ai,
        AssetType.HatcheryNebula => Egg.Nebula,
        AssetType.HatcheryUniverse => Egg.Universe,
        AssetType.HatcheryEnlightenment => Egg.Enlightenment,
        AssetType.HatcheryChocolate => Egg.Chocolate,
        AssetType.HatcheryEaster => Egg.Easter,
        AssetType.HatcheryWaterballoon => Egg.Waterballoon,
        AssetType.HatcheryFirework => Egg.Firework,
        AssetType.HatcheryPumpkin => Egg.Pumpkin,
        AssetType.HatcheryCuriosity => Egg.Curiosity,
        AssetType.HatcheryIntegrity => Egg.Integrity,
        AssetType.HatcheryHumility => Egg.Humility,
        AssetType.HatcheryResilience => Egg.Resilience,
        AssetType.HatcheryKindness => Egg.Kindness,
        _ => Egg.CustomEgg
    };

    public static PlacementProvenance ConfigProvenance =>
        new(PlacementOrigin.Config, "ShellDB.FarmConfiguration.shell_configs",
            "farm state inferred from the asset types present in the saved appearance");
}
