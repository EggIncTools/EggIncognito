using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("artifact_consume_observations")]
public class ArtifactConsumeObservation {
    [Key][Column("id")] public long Id { get; set; }

    [Column("action")] public string Action { get; set; } = "";

    [Column("spec_name")] public string SpecName { get; set; } = "";

    [Column("spec_level")] public string SpecLevel { get; set; } = "";

    [Column("spec_rarity")] public string SpecRarity { get; set; } = "";

    [Column("count_requested")] public int CountRequested { get; set; }

    [Column("byproducts")] public string Byproducts { get; set; } = "[]";

    [Column("other_rewards")] public string OtherRewards { get; set; } = "[]";

    [Column("golden_eggs")] public double GoldenEggs { get; set; }

    [Column("piggy_fill")] public double PiggyFill { get; set; }

    [Column("server_denied")] public bool ServerDenied { get; set; }

    [Column("rarity_achieved")] public string? RarityAchieved { get; set; }

    [Column("gold_price_paid")] public double? GoldPricePaid { get; set; }

    [Column("crafting_count")] public int? CraftingCount { get; set; }

    [Column("success")] public bool Success { get; set; }

    [Column("client_version")] public string? ClientVersion { get; set; }

    [Column("device_id")] public string? DeviceId { get; set; }

    [Column("observed_at")] public DateTimeOffset ObservedAt { get; set; }
}
