using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("stored_binaries")]
public class StoredBinary : IBlobRow {
    [Key][Column("id")] public long Id { get; set; }

    [Column("platform")] public string Platform { get; set; } = "ios";

    [Column("app_version")] public string AppVersion { get; set; } = "";

    [Column("sha256")] public string Sha256 { get; set; } = "";

    [Column("bytes")] public byte[]? Bytes { get; set; }

    [Column("byte_size")] public long ByteSize { get; set; }

    [Column("native_symbol_count")] public int NativeSymbolCount { get; set; }

    [Column("effective_symbol_count")] public int EffectiveSymbolCount { get; set; }

    [Column("source")] public string Source { get; set; } = "";

    [Column("pulled_at")] public DateTimeOffset PulledAt { get; set; }
}
