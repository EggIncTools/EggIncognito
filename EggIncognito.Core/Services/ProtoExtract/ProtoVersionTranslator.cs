namespace EggIncognito.Core.Services.ProtoExtract;

public enum VersionLinkKind { None, Canonical, ProtoSha, AppVersion, ClientVersion }

public sealed record VersionLink<T>(T? Row, VersionLinkKind Kind) where T : class;

public static class ProtoVersionTranslator {
    public static VersionLink<T> Translate<T>(
        T row, string targetPlatform, IEnumerable<T> all, Func<T, VersionKey> key) where T : class {
        var source = key(row);
        var candidates = new List<(T Row, VersionKey Key)>();
        foreach (var other in all) {
            if (ReferenceEquals(other, row)) continue;
            var candidate = key(other);
            if (!string.Equals(candidate.Platform, targetPlatform, StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add((other, candidate));
        }

        var link = MatchTier(candidates, source.ReleaseId is not null,
            k => k.ReleaseId == source.ReleaseId, VersionLinkKind.Canonical);
        link ??= MatchTier(candidates, !string.IsNullOrWhiteSpace(source.ProtoSha),
            k => SharesProtoSha(k, source), VersionLinkKind.ProtoSha);
        link ??= MatchTier(candidates, !string.IsNullOrWhiteSpace(source.AppVersion),
            k => SameText(k.AppVersion, source.AppVersion), VersionLinkKind.AppVersion);
        link ??= MatchTier(candidates, !string.IsNullOrWhiteSpace(source.ClientVersion),
            k => SameText(k.ClientVersion, source.ClientVersion), VersionLinkKind.ClientVersion);
        return link ?? new VersionLink<T>(null, VersionLinkKind.None);
    }

    public static IReadOnlyList<VersionLink<T>> TranslateAll<T>(
        T row, IEnumerable<T> all, Func<T, VersionKey> key) where T : class {
        var rows = all.ToList();
        string? sourcePlatform = key(row).Platform;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var platforms = new List<string>();
        foreach (var other in rows) {
            string? platform = key(other).Platform;
            if (string.IsNullOrWhiteSpace(platform)) continue;
            if (string.Equals(platform, sourcePlatform, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(platform)) platforms.Add(platform);
        }

        platforms.Sort(ComparePlatforms);
        return [.. platforms.Select(platform => Translate(row, platform, rows, key))];
    }

    public static bool SharesProtoSha(VersionKey x, VersionKey y) => SameText(x.ProtoSha, y.ProtoSha);

    public static string Describe(VersionLinkKind kind) => kind switch {
        VersionLinkKind.Canonical => "same release",
        VersionLinkKind.ProtoSha => "same proto sha",
        VersionLinkKind.AppVersion => "same app version",
        VersionLinkKind.ClientVersion => "same client version",
        _ => "no match",
    };

    private static VersionLink<T>? MatchTier<T>(
        List<(T Row, VersionKey Key)> candidates, bool sourceKnown,
        Func<VersionKey, bool> matches, VersionLinkKind kind) where T : class {
        if (!sourceKnown) return null;
        (T Row, VersionKey Key)? best = null;
        foreach (var candidate in candidates) {
            if (!matches(candidate.Key)) continue;
            if (best is null || ProtoVersionOrdering.Compare(candidate.Key, best.Value.Key) < 0) best = candidate;
        }

        return best is null ? null : new VersionLink<T>(best.Value.Row, kind);
    }

    private static bool SameText(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static int ComparePlatforms(string x, string y) {
        int rank = ProtoVersionOrdering.PlatformRank(x).CompareTo(ProtoVersionOrdering.PlatformRank(y));
        return rank != 0 ? rank : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
