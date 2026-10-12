using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("api_key_features")]
public sealed class ApiKeyFeature {
    [Column("id")] public int Id { get; set; }
    [Column("api_key_id")] public int ApiKeyId { get; set; }
    [Column("feature")] public string Feature { get; set; } = "";
    [Column("state")] public string State { get; set; } = ApiKeyFeatureStates.Pending;
    [Column("note")] public string? Note { get; set; }
    [Column("requested_at")] public DateTimeOffset RequestedAt { get; set; }
    [Column("decided_at")] public DateTimeOffset? DecidedAt { get; set; }
    [Column("decided_by")] public string? DecidedBy { get; set; }
}
