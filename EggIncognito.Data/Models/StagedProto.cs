using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("staged_protos")]
public sealed class StagedProto {
    [Key][Column("id")] public int Id { get; set; }
    [Column("platform")] public string Platform { get; set; } = "android";
    [Column("app_version")] public string? AppVersion { get; set; }
    [Column("build")] public string? Build { get; set; }
    [Column("client_version")] public string? ClientVersion { get; set; }
    [Column("package")] public string? Package { get; set; }
    [Column("proto_sha")] public string ProtoSha { get; set; } = "";
    [Column("proto_text")] public string ProtoText { get; set; } = "";
    [Column("message_index")] public string? MessageIndex { get; set; }
    [Column("source")] public string Source { get; set; } = "offer";
    [Column("status")] public string Status { get; set; } = "pending";
    [Column("submitted_by")] public string? SubmittedBy { get; set; }
    [Column("submitted_at")] public DateTimeOffset SubmittedAt { get; set; }
    [Column("reviewed_by")] public string? ReviewedBy { get; set; }
    [Column("reviewed_at")] public DateTimeOffset? ReviewedAt { get; set; }
    [Column("review_note")] public string? ReviewNote { get; set; }
    [Column("origin_repo")] public string? OriginRepo { get; set; }
    [Column("origin_commit")] public string? OriginCommit { get; set; }
    [Column("origin_date")] public DateTimeOffset? OriginDate { get; set; }
    [Column("confidence")] public string? Confidence { get; set; }
}
