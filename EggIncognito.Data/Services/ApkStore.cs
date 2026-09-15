using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record StoredApkHead(
    string Platform, string Package, string AppVersion, string Build, string Split,
    string Sha256, long ByteSize, string? SourceDeviceId, DateTimeOffset CapturedAt);

public sealed record ApkVersionSet(
    string Platform, string Package, string AppVersion, string Build,
    IReadOnlyList<StoredApkHead> Splits, DateTimeOffset CapturedAt) {
    public string Key => $"{AppVersion}@{Build}";

    public long ByteSize => Splits.Sum(s => s.ByteSize);

    public bool HasSplit(string split) =>
        Splits.Any(s => string.Equals(s.Split, split, StringComparison.OrdinalIgnoreCase));

    public bool Installable => HasSplit(ApkSplitNames.Base);

    public bool Complete => Installable && Splits.Any(s => ApkSplitNames.IsConfig(s.Split));

    public string Label {
        get {
            string version = string.IsNullOrEmpty(AppVersion) ? "unknown version" : AppVersion;
            string build = string.IsNullOrEmpty(Build) ? "" : $" ({Build})";
            return $"{version}{build}";
        }
    }

    public string Detail {
        get {
            string splits = string.Join("+", Splits.Select(s => s.Split).Order(StringComparer.Ordinal));
            var sources = Splits.Select(s => s.SourceDeviceId).OfType<string>()
                .Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            string from = sources.Count == 0 ? "" : $", from {string.Join("+", sources)}";
            string incomplete = Complete ? "" : "; no config splits, renders broken";
            return $"{splits}, captured {CapturedAt:yyyy-MM-dd}{from}{incomplete}";
        }
    }
}

public sealed class ApkStore(
    EggIncognitoDbContext db,
    TimeProvider time,
    BlobBytes blobs,
    IEnumerable<IApkStoreObserver>? observers = null) {
    public Task<byte[]> BytesAsync(StoredApk row, CancellationToken ct) =>
        blobs.ResolveAsync(BlobTables.StoredApks, row.Bytes, row.Sha256, ct);

    public static string SplitLabel(string nameOrPath) {
        string name = Path.GetFileNameWithoutExtension(nameOrPath);
        if (name.Length == 0) return ApkSplitNames.Base;
        if (name.Contains("arm", StringComparison.OrdinalIgnoreCase)) return ApkSplitNames.Arm64;
        if (string.Equals(name, "base", StringComparison.OrdinalIgnoreCase)) return ApkSplitNames.Base;
        return name.StartsWith("split_", StringComparison.OrdinalIgnoreCase)
            ? name["split_".Length..]
            : name;
    }

    public async Task<bool> PutAsync(string platform, string package, string appVersion, string build, string split,
        byte[] bytes, string? sourceDeviceId, CancellationToken ct) {
        string sha = Hashes.Sha256Hex(bytes);
        var existing = await db.StoredApks.FirstOrDefaultAsync(
            a => a.Platform == platform && a.Package == package && a.AppVersion == appVersion
                 && a.Build == build && a.Split == split, ct);
        if (existing is not null && string.Equals(existing.Sha256, sha, StringComparison.Ordinal)) return false;

        var stored = await blobs.StoreAsync(BlobTables.StoredApks, sha, bytes, ct);

        if (existing is null) {
            db.StoredApks.Add(new StoredApk {
                Platform = platform,
                Package = package,
                AppVersion = appVersion,
                Build = build,
                Split = split,
                Sha256 = sha,
                Bytes = stored,
                ByteSize = bytes.LongLength,
                SourceDeviceId = sourceDeviceId,
                CapturedAt = time.GetUtcNow()
            });
        } else {
            existing.Sha256 = sha;
            existing.Bytes = stored;
            existing.ByteSize = bytes.LongLength;
            existing.SourceDeviceId = sourceDeviceId;
            existing.CapturedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        await NotifyAsync(new ApkStoreNotice(ApkChangeKinds.Stored, platform, package, appVersion, build, 1), ct);
        return true;
    }

    public async Task<IReadOnlyList<ApkVersionSet>> VersionsAsync(string platform, string package,
        CancellationToken ct) {
        var heads = await HeadsAsync(db.StoredApks.Where(a => a.Platform == platform && a.Package == package), ct);
        var sets = Group(heads);
        sets.Sort(NewestFirst);
        return sets;
    }

    public async Task<IReadOnlyList<ApkVersionSet>> AllVersionsAsync(CancellationToken ct) {
        var sets = Group(await HeadsAsync(db.StoredApks, ct));
        sets.Sort(PackageThenNewest);
        return sets;
    }

    public async Task<int> DeleteVersionAsync(string platform, string package, string appVersion, string build,
        CancellationToken ct) {
        int removed = await db.StoredApks
            .Where(a => a.Platform == platform && a.Package == package && a.AppVersion == appVersion
                        && a.Build == build)
            .ExecuteDeleteAsync(ct);
        if (removed > 0) {
            await NotifyAsync(
                new ApkStoreNotice(ApkChangeKinds.Deleted, platform, package, appVersion, build, removed), ct);
        }

        return removed;
    }

    private static Task<List<StoredApkHead>> HeadsAsync(IQueryable<StoredApk> query, CancellationToken ct) =>
        query.AsNoTracking()
            .Select(a => new StoredApkHead(a.Platform, a.Package, a.AppVersion, a.Build, a.Split, a.Sha256,
                a.ByteSize, a.SourceDeviceId, a.CapturedAt))
            .ToListAsync(ct);

    private static List<ApkVersionSet> Group(IEnumerable<StoredApkHead> heads) =>
    [
        .. heads
            .GroupBy(h => (h.Platform, h.Package, h.AppVersion, h.Build))
            .Select(g => new ApkVersionSet(g.Key.Platform, g.Key.Package, g.Key.AppVersion, g.Key.Build,
                [.. g.OrderBy(s => s.Split, StringComparer.Ordinal)], g.Max(s => s.CapturedAt)))
    ];

    private async Task NotifyAsync(ApkStoreNotice notice, CancellationToken ct) {
        foreach (var observer in observers ?? []) await observer.OnChangedAsync(notice, ct);
        await PgNotify.SendAsync(db, PgChannels.Apks, PgNotify.ApkPayload(notice), ct);
    }

    public static int PackageThenNewest(ApkVersionSet a, ApkVersionSet b) {
        int byPlatform = string.CompareOrdinal(a.Platform, b.Platform);
        if (byPlatform != 0) return byPlatform;
        int byPackage = string.CompareOrdinal(a.Package, b.Package);
        return byPackage != 0 ? byPackage : NewestFirst(a, b);
    }

    public async Task<IReadOnlyList<StoredApk>> SplitsAsync(string platform, string package, string appVersion,
        string build, CancellationToken ct) =>
        await db.StoredApks.AsNoTracking()
            .Where(a => a.Platform == platform && a.Package == package && a.AppVersion == appVersion
                        && a.Build == build)
            .OrderBy(a => a.Split)
            .ToListAsync(ct);

    public static int NewestFirst(ApkVersionSet a, ApkVersionSet b) {
        if (long.TryParse(a.Build, out long ab) && long.TryParse(b.Build, out long bb) && ab != bb)
            return bb.CompareTo(ab);
        int byVersion = DeviceParsing.CompareVersions(b.AppVersion, a.AppVersion);
        return byVersion != 0 ? byVersion : b.CapturedAt.CompareTo(a.CapturedAt);
    }
}
