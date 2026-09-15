using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("device_modules")]
public sealed class StoredModule : IBlobRow {
    [Key][Column("id")] public long Id { get; set; }

    [Column("name")] public string Name { get; set; } = "";

    [Column("source")] public string Source { get; set; } = "";

    [Column("version")] public string? Version { get; set; }

    [Column("sha256")] public string Sha256 { get; set; } = "";

    [Column("bytes")] public byte[]? Bytes { get; set; }

    [Column("byte_size")] public long ByteSize { get; set; }

    [Column("fetched_at")] public DateTimeOffset FetchedAt { get; set; }
}
