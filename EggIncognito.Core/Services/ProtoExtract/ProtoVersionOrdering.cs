using System.Globalization;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed record VersionKey(
    string Platform, string? AppVersion, string? Build, string? ClientVersion,
    int? SortOrder, DateTime? DetectedAt, long? ReleaseId = null, string? ProtoSha = null);

public static class ProtoVersionOrdering {
    private static readonly IComparer<VersionKey> KeyComparer = Comparer<VersionKey>.Create(Compare);
    private static readonly IComparer<VersionKey> ReleaseComparer = Comparer<VersionKey>.Create(CompareReleaseThenPlatform);

    public static int Compare(VersionKey x, VersionKey y) {
        int cmp = CompareDotted(x.AppVersion, y.AppVersion);
        if (cmp != 0) return cmp;

        cmp = CompareDescending(x.SortOrder, y.SortOrder);
        if (cmp != 0) return cmp;

        cmp = CompareDotted(x.ClientVersion, y.ClientVersion);
        if (cmp != 0) return cmp;

        cmp = CompareBuild(x.Build, y.Build);
        if (cmp != 0) return cmp;

        cmp = CompareDescending(x.DetectedAt, y.DetectedAt);
        if (cmp != 0) return cmp;

        cmp = PlatformRank(x.Platform).CompareTo(PlatformRank(y.Platform));
        if (cmp != 0) return cmp;

        cmp = string.CompareOrdinal(y.Build, x.Build);
        return cmp != 0 ? cmp : string.CompareOrdinal(x.Platform, y.Platform);
    }

    public static int CompareRelease(VersionKey x, VersionKey y) {
        int cmp = CompareDotted(x.AppVersion, y.AppVersion);
        if (cmp != 0) return cmp;

        cmp = CompareDotted(x.ClientVersion, y.ClientVersion);
        return cmp != 0 ? cmp : CompareDescending(x.DetectedAt, y.DetectedAt);
    }

    public static int PlatformRank(string? platform) =>
        Platforms.Matches(platform, Platforms.Ios) ? 0 : Platforms.Matches(platform, Platforms.Android) ? 1 : 2;

    public static IReadOnlyList<T> Sort<T>(IEnumerable<T> items, Func<T, VersionKey> key) =>
        items.Select(item => (Item: item, Key: key(item)))
            .OrderBy(pair => PlatformRank(pair.Key.Platform))
            .ThenBy(pair => pair.Key.Platform, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, KeyComparer)
            .Select(pair => pair.Item)
            .ToList();

    public static IReadOnlyList<T> SortByRelease<T>(IEnumerable<T> items, Func<T, VersionKey> key) =>
        items.Select(item => (Item: item, Key: key(item)))
            .OrderBy(pair => pair.Key, ReleaseComparer)
            .Select(pair => pair.Item)
            .ToList();

    private static int CompareReleaseThenPlatform(VersionKey x, VersionKey y) {
        int cmp = CompareRelease(x, y);
        if (cmp != 0) return cmp;

        cmp = PlatformRank(x.Platform).CompareTo(PlatformRank(y.Platform));
        return cmp != 0 ? cmp : Compare(x, y);
    }

    public static T? Latest<T>(IEnumerable<T> items, Func<T, VersionKey> key) where T : class {
        T? best = null;
        VersionKey? bestKey = null;
        foreach (T item in LatestByPlatform(items, key)) {
            VersionKey candidate = key(item);
            if (bestKey is null) {
                best = item;
                bestKey = candidate;
                continue;
            }

            int cmp = CompareRelease(candidate, bestKey);
            bool wins = cmp < 0 || (cmp == 0 && PlatformRank(candidate.Platform) < PlatformRank(bestKey.Platform));
            if (!wins) continue;

            best = item;
            bestKey = candidate;
        }

        return best;
    }

    public static T? LatestFor<T>(string platform, IEnumerable<T> items, Func<T, VersionKey> key) where T : class {
        T? best = null;
        VersionKey? bestKey = null;
        foreach (T item in items) {
            VersionKey candidate = key(item);
            if (!string.Equals(candidate.Platform, platform, StringComparison.OrdinalIgnoreCase)) continue;
            if (bestKey is not null && Compare(candidate, bestKey) >= 0) continue;

            best = item;
            bestKey = candidate;
        }

        return best;
    }

    public static IReadOnlyList<T> LatestByPlatform<T>(IEnumerable<T> items, Func<T, VersionKey> key) where T : class =>
        items.Select(item => (Item: item, Key: key(item)))
            .GroupBy(pair => pair.Key.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Aggregate((best, next) => Compare(next.Key, best.Key) < 0 ? next : best))
            .OrderBy(pair => PlatformRank(pair.Key.Platform))
            .ThenBy(pair => pair.Key.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Item)
            .ToList();

    public static T? Previous<T>(T current, IEnumerable<T> all, Func<T, VersionKey> key) where T : class =>
        Neighbour(current, all, key, 1);

    public static T? Next<T>(T current, IEnumerable<T> all, Func<T, VersionKey> key) where T : class =>
        Neighbour(current, all, key, -1);

    private static T? Neighbour<T>(
        T current, IEnumerable<T> all, Func<T, VersionKey> key, int offset) where T : class {
        VersionKey currentKey = key(current);
        var ordered = all
            .Select(item => (Item: item, Key: key(item)))
            .Where(pair => string.Equals(pair.Key.Platform, currentKey.Platform, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, KeyComparer)
            .ToList();

        int index = ordered.FindIndex(pair => ReferenceEquals(pair.Item, current));
        if (index < 0) index = ordered.FindIndex(pair => SameBuild(pair.Key, currentKey));
        if (index < 0) return null;

        int target = index + offset;
        return target >= 0 && target < ordered.Count ? ordered[target].Item : null;
    }

    private static bool SameBuild(VersionKey x, VersionKey y) =>
        string.Equals(x.Platform, y.Platform, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Build, y.Build, StringComparison.OrdinalIgnoreCase);

    private static int CompareDotted(string? x, string? y) {
        long kx = ProtoVersionQuality.DottedVersionKey(x);
        long ky = ProtoVersionQuality.DottedVersionKey(y);
        return kx == long.MinValue || ky == long.MinValue ? 0 : ky.CompareTo(kx);
    }

    private static int CompareDescending<TValue>(TValue? x, TValue? y) where TValue : struct, IComparable<TValue> =>
        x is { } left && y is { } right ? right.CompareTo(left) : 0;

    private static int CompareBuild(string? x, string? y) {
        if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y)) return 0;
        if (TryParseCode(x, out long cx) && TryParseCode(y, out long cy)) return cy.CompareTo(cx);
        if (DottedBuildKey(x) is { } dx && DottedBuildKey(y) is { } dy) return dy.CompareTo(dx);
        return 0;
    }

    private static bool TryParseCode(string value, out long code) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);

    private static long? DottedBuildKey(string value) {
        string[] parts = value.Split('.');
        if (parts.Length < 2) return null;

        foreach (string part in parts) {
            if (!TryParseCode(part, out long component) || component < 0) return null;
        }

        return ProtoVersionQuality.DottedVersionKey(value);
    }
}
