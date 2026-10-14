namespace EggIncognito.Core.Services.ProtoExtract;

public enum VersionDelta {
    Unknown,
    Forward,
    Repeat,
    Backfill
}

public static class VersionDeltaCalc {
    public static VersionDelta Classify(
        string? platform, string? build, string? appVersion,
        string? prevBuild, string? prevAppVersion) {
        if (string.IsNullOrEmpty(build) && string.IsNullOrEmpty(appVersion)) return VersionDelta.Unknown;
        if (ProtoVersionQuality.HasPlatformBuildMismatch(platform, build)) return VersionDelta.Unknown;
        if (string.IsNullOrEmpty(prevBuild) && string.IsNullOrEmpty(prevAppVersion)) return VersionDelta.Forward;

        string p = platform ?? "";
        VersionKey key = new(p, appVersion, build, null, null, null);
        VersionKey prev = new(p, prevAppVersion, prevBuild, null, null, null);
        int cmp = ProtoVersionOrdering.Compare(key, prev);

        return cmp < 0 ? VersionDelta.Forward : cmp == 0 ? VersionDelta.Repeat : VersionDelta.Backfill;
    }

    public static string Label(VersionDelta delta) => delta switch {
        VersionDelta.Forward => "forward",
        VersionDelta.Repeat => "repeat",
        VersionDelta.Backfill => "backfill",
        _ => "unknown"
    };
}
