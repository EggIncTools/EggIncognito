namespace EggIncognito.Models.Staging;

public sealed class StagedTarget {
    public string Platform { get; set; } = "";
    public string? AppVersion { get; set; }
    public string? Build { get; set; }
    public string? ClientVersion { get; set; }
    public string? ProtoSha { get; set; }
}

public sealed class StagedRow {
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public string Platform { get; set; } = "android";
    public string? AppVersion { get; set; }
    public string? Build { get; set; }
    public string? ClientVersion { get; set; }
    public string ProtoSha { get; set; } = "";
    public string? OriginRepo { get; set; }
    public string? OriginCommit { get; set; }
    public DateTimeOffset? OriginDate { get; set; }
    public string? Confidence { get; set; }
    public string Kind { get; set; } = "version";
    public int? TargetId { get; set; }
    public StagedTarget? Target { get; set; }

    public bool IsCorrection => Kind == "correction";

    public bool IsIncomplete =>
        string.IsNullOrWhiteSpace(AppVersion) || string.IsNullOrWhiteSpace(Build) || string.IsNullOrWhiteSpace(ClientVersion);

    public IEnumerable<string> Missing {
        get {
            if (string.IsNullOrWhiteSpace(AppVersion)) yield return "appVersion";
            if (string.IsNullOrWhiteSpace(Build)) yield return "build";
            if (string.IsNullOrWhiteSpace(ClientVersion)) yield return "clientVersion";
        }
    }
}
