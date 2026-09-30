using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("proto_versions")]
public sealed class ProtoVersion {
    [Column("id")] public int Id { get; set; }
    [Column("platform")] public string Platform { get; set; } = "android";
    [Column("app_version")] public string AppVersion { get; set; } = "";
    [Column("build")] public string Build { get; set; } = "";
    [Column("client_version")] public string? ClientVersion { get; set; }
    [Column("source")] public string Source { get; set; } = "farm";
    [Column("package")] public string Package { get; set; } = "";
    [Column("proto_sha")] public string ProtoSha { get; set; } = "";
    [Column("apk_ref")] public string ApkRef { get; set; } = "";
    [Column("detected_at")] public DateTimeOffset DetectedAt { get; set; }
    [Column("detected_by")] public string? DetectedBy { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }

    [Column("deleted_at")] public DateTimeOffset? DeletedAt { get; set; }

    [Column("canonical_id")] public int? CanonicalId { get; set; }

    [Column("archive_sourced")] public bool ArchiveSourced { get; set; }
}
