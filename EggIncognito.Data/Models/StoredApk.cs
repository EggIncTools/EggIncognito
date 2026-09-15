using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

public static class ApkSplitNames {
    public const string Base = "base";
    public const string Arm64 = "arm64";
    public const string ConfigPrefix = "config.";

    public static bool IsConfig(string split) =>
        split.StartsWith(ConfigPrefix, StringComparison.OrdinalIgnoreCase);
}

[Table("stored_apks")]
public sealed class StoredApk : IBlobRow {
    [Key][Column("id")] public long Id { get; set; }

    [Column("platform")] public string Platform { get; set; } = "android";

    [Column("package")] public string Package { get; set; } = "";

    [Column("app_version")] public string AppVersion { get; set; } = "";

    [Column("build")] public string Build { get; set; } = "";

    [Column("split")] public string Split { get; set; } = ApkSplitNames.Base;

    [Column("sha256")] public string Sha256 { get; set; } = "";

    [Column("bytes")] public byte[]? Bytes { get; set; }

    [Column("byte_size")] public long ByteSize { get; set; }

    [Column("source_device_id")] public string? SourceDeviceId { get; set; }

    [Column("captured_at")] public DateTimeOffset CapturedAt { get; set; }
}
