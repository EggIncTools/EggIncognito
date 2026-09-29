using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("consume_coverage_targets")]
public class ConsumeCoverageTarget {
    [Key][Column("id")] public long Id { get; set; }

    [Column("spec_name")] public string? SpecName { get; set; }

    [Column("spec_level")] public string? SpecLevel { get; set; }

    [Column("spec_rarity")] public string? SpecRarity { get; set; }

    [Column("item_target")] public int ItemTarget { get; set; }

    [Column("observation_target")] public int ObservationTarget { get; set; }

    [Column("enabled")] public bool Enabled { get; set; } = true;

    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }

    [Column("updated_by")] public string? UpdatedBy { get; set; }
}
