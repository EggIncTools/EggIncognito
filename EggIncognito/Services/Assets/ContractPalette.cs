using EggIncognito.Models.Contracts;

namespace EggIncognito.Services.Assets;

public static class ContractPalette {
    public const string NewContract = "#10b981";
    public const string Leggacy = "#22d3ee";
    public const string Prophecy = "#f5c518";

    public static string ColorFor(bool leggacy, int prophecyEggs) =>
        !leggacy ? NewContract : prophecyEggs > 0 ? Prophecy : Leggacy;

    public static string BarStyle(bool ultraOnly, bool leggacy, int prophecyEggs) {
        if (ultraOnly) return EventPalette.BarStyle(null, true);
        var color = ColorFor(leggacy, prophecyEggs);
        return $"--evt-color:{color};--evt-from:{color};--evt-to:{color};";
    }

    public static string BarStyle(ContractSlotKind kind) =>
        kind switch {
            ContractSlotKind.NewContract => BarStyle(false, false, 0),
            ContractSlotKind.Leggacy => BarStyle(false, true, 0),
            ContractSlotKind.PeLeggacy => BarStyle(false, true, 1),
            _ => BarStyle(true, true, 1)
        };
}
