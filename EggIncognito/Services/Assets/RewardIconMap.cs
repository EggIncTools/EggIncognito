using Ei;

namespace EggIncognito.Services.Assets;

public static class RewardIconMap {
    public static string? Stem(RewardType type, string? subType) =>
        type switch {
            RewardType.Cash => "icon_cash_pile",
            RewardType.Gold => "icon_golden_egg1000",
            RewardType.SoulEggs => "egg_soul",
            RewardType.EggsOfProphecy => "egg_of_prophecy",
            RewardType.EpicResearchItem => string.IsNullOrEmpty(subType) ? null : "r_icon_" + subType,
            RewardType.PiggyFill => "icon_piggy_golden_egg",
            RewardType.PiggyMultiplier or RewardType.PiggyLevelBump => "icon_piggy",
            RewardType.Boost => string.IsNullOrEmpty(subType)
                ? null
                : "b_icon_" + (subType.EndsWith("_v2", StringComparison.Ordinal) ? subType[..^3] : subType),
            RewardType.BoostToken => "b_icon_token",
            RewardType.Artifact => "afx",
            RewardType.ArtifactCase => "icon_afx_chest_3",
            RewardType.Chicken => "chicken_box",
            RewardType.ShellScript => "icon_shell_script_colored",
            RewardType.VirtueGem => "icon_virtue_gem",
            _ => null
        };

    public static string GradeStem(string grade) {
        string g = grade;
        if (g.StartsWith("GRADE_", StringComparison.Ordinal)) g = g["GRADE_".Length..];
        else if (g.StartsWith("Grade", StringComparison.Ordinal)) g = g["Grade".Length..];
        if (g.Length == 0) return "contract_grade_c";
        return "contract_grade_" + g.ToLowerInvariant();
    }
}
