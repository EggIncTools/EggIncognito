using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("build_blobs")]
public sealed class BuildBlob : IBlobRow {
    [Key][Column("id")] public long Id { get; set; }

    [Column("key")] public string Key { get; set; } = "";

    [Column("source")] public string Source { get; set; } = "";

    [Column("sha256")] public string Sha256 { get; set; } = "";

    [Column("byte_size")] public long ByteSize { get; set; }

    [Column("bytes")] public byte[]? Bytes { get; set; }

    [Column("fetched_at")] public DateTimeOffset FetchedAt { get; set; }
}
