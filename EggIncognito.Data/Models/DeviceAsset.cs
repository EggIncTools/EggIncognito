using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("device_assets")]
public sealed class DeviceAsset : IBlobRow {
    [Key][Column("id")] public long Id { get; set; }

    [Column("platform")] public string Platform { get; set; } = "";

    [Column("kind")] public string Kind { get; set; } = "";

    [Column("name")] public string Name { get; set; } = "";

    [Column("sha256")] public string Sha256 { get; set; } = "";

    [Column("bytes")] public byte[]? Bytes { get; set; }

    [Column("byte_size")] public long ByteSize { get; set; }

    [Column("content_type")] public string ContentType { get; set; } = "application/octet-stream";

    [Column("source_version")] public string? SourceVersion { get; set; }

    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}
