using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("device_islands")]
public sealed class DeviceIslandRow {
    [Column("device_id")] public string DeviceId { get; set; } = "";
    [Column("user_id")] public int UserId { get; set; }
    [Column("label")] public string Label { get; set; } = "";
    [Column("provisioned")] public bool Provisioned { get; set; }
    [Column("egg_account_id")] public string? EggAccountId { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}
