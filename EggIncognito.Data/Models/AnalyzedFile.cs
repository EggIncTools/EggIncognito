using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("analyzed_files")]
public sealed class AnalyzedFile {
    [Key][Column("file_sha")] public string FileSha { get; set; } = "";
    [Column("first_seen")] public DateTimeOffset FirstSeen { get; set; }
    [Column("source")] public string Source { get; set; } = "";
    [Column("platform")] public string? Platform { get; set; }
    [Column("proto_sha")] public string? ProtoSha { get; set; }
    [Column("app_version")] public string? AppVersion { get; set; }
    [Column("build")] public string? Build { get; set; }
    [Column("client_version")] public string? ClientVersion { get; set; }
    [Column("file_name")] public string? FileName { get; set; }
    [Column("extractor")] public string? Extractor { get; set; }
}
