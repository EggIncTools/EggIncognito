using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("proto_protos")]
public sealed class ProtoProto {
    [Column("proto_version_id")] public int ProtoVersionId { get; set; }
    [Column("proto_text")] public string ProtoText { get; set; } = "";
    [Column("message_index")] public string MessageIndex { get; set; } = "[]";
    [Column("archive_sourced")] public bool ArchiveSourced { get; set; }
}
