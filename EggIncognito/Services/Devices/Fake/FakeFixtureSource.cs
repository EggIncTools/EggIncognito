using System.Globalization;
using System.Text;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices.Fake;

public sealed record FakeFixtureFile(string Name, string Sha256, string ContentType, int ByteSize);

public sealed record FakeFixtureSet(string Tier, IReadOnlyList<FakeFixtureFile> Files) {
    public string Canonical(string? appVersion) => string.Join('\n',
        Files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => $"{f.Name}:{f.Sha256}")
            .Append($"version:{appVersion ?? ""}"));
}

public sealed record FakeInstalledVersion(string? AppVersion, string? Build, int? ClientVersion = null);

public sealed class FakeFixtureSource(IServiceScopeFactory scopes) {
    private const int BinaryBytes = 64 * 1024;
    private const int PackageBytes = 16 * 1024;
    private const int MeshBytes = 4 * 1024;
    private const int IconBytes = 2 * 1024;
    private const int SyntheticStems = 8;

    private const string BinaryContentType = "application/octet-stream";
    private const string PackageContentType = "application/vnd.android.package-archive";
    private const string IconContentType = "image/png";
    private const string ManifestContentType = "text/plain";
    private const string ManifestFileName = "Info";

    private static readonly FakeFixtureSet NoClone = new(FakeFixtureTiers.Clone, []);

    public static string BinaryName(string platform) =>
        Platforms.Matches(platform, Platforms.Android) ? "libegginc.so" : "egginc";

    public async Task<FakeInstalledVersion> InstalledAsync(string platform, CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db)
            return new FakeInstalledVersion(null, null);

        string? version = null;
        if (sp.GetService(typeof(GameBinaryStore)) is GameBinaryStore binaries)
            version = (await binaries.GetLatestAsync(platform, ct))?.AppVersion;

        var rows = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.Platform == platform && p.DeletedAt == null)
            .OrderByDescending(p => p.DetectedAt)
            .Select(p => new { p.AppVersion, p.Build, p.ClientVersion })
            .Take(50)
            .ToListAsync(ct);

        version ??= rows.Select(r => r.AppVersion).FirstOrDefault();
        var match = rows.FirstOrDefault(r => r.AppVersion == version);
        string? build = match?.Build ?? rows.Select(r => r.Build).FirstOrDefault();
        string? clientVersion = match?.ClientVersion
                                ?? rows.Select(r => r.ClientVersion).FirstOrDefault(c => !string.IsNullOrEmpty(c));
        return new FakeInstalledVersion(NullIfEmpty(version), NullIfEmpty(build),
            int.TryParse(clientVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cv) ? cv : null);
    }

    public async Task<FakeInstalledVersion> ResolveAsync(FakeDevice device, FakeDeviceVersions versions,
        CancellationToken ct) {
        (string? version, string? build) = versions.Get(device.Id);
        version ??= device.AppVersion;
        build ??= device.Build;
        if (version is not null && build is not null && device.ClientVersion is not null)
            return new FakeInstalledVersion(version, build, device.ClientVersion);

        var clone = await InstalledAsync(device.Platform, ct);
        return new FakeInstalledVersion(version ?? clone.AppVersion, build ?? clone.Build,
            device.ClientVersion ?? clone.ClientVersion);
    }

    public async Task<FakeFixtureSet> DescribeAsync(FakeDevice device, HarvestEntry entry, string? appVersion,
        CancellationToken ct) {
        var clone = await CloneAsync(device.Platform, entry, ct);
        return clone.Files.Count > 0 ? clone : Synthesize(device, entry, appVersion);
    }

    public Task<byte[]?> ReadAsync(FakeDevice device, HarvestEntry entry, string name, string? appVersion, string tier,
        CancellationToken ct) =>
        tier == FakeFixtureTiers.Clone
            ? CloneBytesAsync(device.Platform, entry, name, ct)
            : Task.FromResult<byte[]?>(SyntheticBytes(device, entry, name, appVersion));

    public async Task<byte[]?> ReadAsync(FakeDevice device, HarvestEntry entry, string name, string? appVersion,
        CancellationToken ct) {
        var set = await DescribeAsync(device, entry, appVersion, ct);
        return set.Files.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal))
            ? await ReadAsync(device, entry, name, appVersion, set.Tier, ct)
            : null;
    }

    public async Task<IReadOnlyList<string>> ListAsync(FakeDevice device, HarvestEntry entry, string? appVersion,
        CancellationToken ct) {
        var set = await DescribeAsync(device, entry, appVersion, ct);
        return [.. set.Files.Select(f => f.Name)];
    }

    private async Task<FakeFixtureSet> CloneAsync(string platform, HarvestEntry entry, CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;

        if (entry.Kind == DeviceAssetKinds.Binary) {
            if (sp.GetService(typeof(GameBinaryStore)) is not GameBinaryStore binaries) return NoClone;
            var row = await binaries.GetLatestAsync(platform, ct);
            return row is null || row.ByteSize == 0
                ? NoClone
                : new FakeFixtureSet(FakeFixtureTiers.Clone,
                    [new FakeFixtureFile(BinaryName(platform), row.Sha256, BinaryContentType, (int)row.ByteSize)]);
        }

        if (sp.GetService(typeof(DeviceAssetStore)) is not DeviceAssetStore assets) return NoClone;

        if (entry.Kind == DeviceAssetKinds.Manifest) {
            string? body = await ListingBodyAsync(assets, platform, ct);
            return body is null
                ? NoClone
                : new FakeFixtureSet(FakeFixtureTiers.Clone, [TextFile(ManifestFileName, body)]);
        }

        var heads = await assets.ListAsync(entry.Kind, platform, ct);
        var files = heads
            .Where(h => !h.Name.StartsWith(DeviceAssetStore.FingerprintPrefix, StringComparison.Ordinal))
            .Select(h => new FakeFixtureFile(h.Name, h.Sha256, h.ContentType, (int)h.ByteSize))
            .ToList();
        return files.Count == 0 ? NoClone : new FakeFixtureSet(FakeFixtureTiers.Clone, files);
    }

    private async Task<byte[]?> CloneBytesAsync(string platform, HarvestEntry entry, string name,
        CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;

        if (entry.Kind == DeviceAssetKinds.Binary) {
            if (sp.GetService(typeof(GameBinaryStore)) is not GameBinaryStore binaries) return null;
            var binary = await binaries.GetLatestAsync(platform, ct);
            return binary is null ? null : await binaries.BytesAsync(binary, ct);
        }

        if (sp.GetService(typeof(DeviceAssetStore)) is not DeviceAssetStore assets) return null;

        if (entry.Kind == DeviceAssetKinds.Manifest) {
            string? body = await ListingBodyAsync(assets, platform, ct);
            return body is null ? null : Encoding.UTF8.GetBytes(body);
        }

        var asset = await assets.GetAsync(entry.Kind, name, platform, ct);
        return asset is null ? null : await assets.BytesAsync(asset, ct);
    }

    private static async Task<string?> ListingBodyAsync(DeviceAssetStore assets, string platform,
        CancellationToken ct) {
        var meshes = await assets.ListAsync(DeviceAssetKinds.Mesh, platform, ct);
        var icons = await assets.ListAsync(DeviceAssetKinds.Icon, platform, ct);
        List<DeviceAssetHead> listing = [.. meshes, .. icons];
        if (listing.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append("platform=").Append(platform).Append('\n');
        sb.Append("entries=").Append(listing.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        var ordered = listing.OrderBy(a => a.Kind, StringComparer.Ordinal)
            .ThenBy(a => a.Name, StringComparer.Ordinal);
        foreach (var head in ordered)
            sb.Append(head.Kind).Append('/').Append(head.Name).Append(' ').Append(head.Sha256).Append('\n');

        return sb.ToString();
    }

    private static FakeFixtureSet Synthesize(FakeDevice device, HarvestEntry entry, string? appVersion) {
        var files = SyntheticNames(device.Platform, entry)
            .Select(n => {
                byte[] bytes = SyntheticBytes(device, entry, n, appVersion);
                return new FakeFixtureFile(n, Hashes.Sha256Hex(bytes), ContentTypeFor(entry), bytes.Length);
            })
            .ToList();
        return new FakeFixtureSet(FakeFixtureTiers.Synthesized, files);
    }

    private static byte[] SyntheticBytes(FakeDevice device, HarvestEntry entry, string name, string? appVersion) =>
        entry.Kind == DeviceAssetKinds.Manifest
            ? Encoding.UTF8.GetBytes(SyntheticListing(device, appVersion))
            : Filler(device, entry, name, appVersion, SizeFor(entry));

    private static string SyntheticListing(FakeDevice device, string? appVersion) {
        var sb = new StringBuilder();
        sb.Append("platform=").Append(device.Platform).Append('\n');
        sb.Append("package=").Append(device.Package).Append('\n');
        sb.Append("version=").Append(appVersion ?? "").Append('\n');
        sb.Append("tier=").Append(FakeFixtureTiers.Synthesized).Append('\n');
        foreach (string stem in Stems("fake-mesh")) sb.Append("mesh/").Append(stem).Append('\n');
        foreach (string stem in Stems("fake-icon")) sb.Append("icon/").Append(stem).Append('\n');
        return sb.ToString();
    }

    private static IReadOnlyList<string> SyntheticNames(string platform, HarvestEntry entry) => entry.Kind switch {
        DeviceAssetKinds.Binary => [BinaryName(platform)],
        DeviceAssetKinds.Package => [HarvestEntries.AndroidArmSplit, HarvestEntries.AndroidBaseSplit],
        DeviceAssetKinds.Mesh => [.. Stems("fake-mesh")],
        DeviceAssetKinds.Icon => [.. Stems("fake-icon")],
        _ => [ManifestFileName]
    };

    private static IEnumerable<string> Stems(string prefix) =>
        Enumerable.Range(0, SyntheticStems).Select(i => $"{prefix}-{i.ToString(CultureInfo.InvariantCulture)}");

    private static int SizeFor(HarvestEntry entry) => entry.Kind switch {
        DeviceAssetKinds.Binary => BinaryBytes,
        DeviceAssetKinds.Package => PackageBytes,
        DeviceAssetKinds.Mesh => MeshBytes,
        _ => IconBytes
    };

    private static string ContentTypeFor(HarvestEntry entry) => entry.Kind switch {
        DeviceAssetKinds.Package => PackageContentType,
        DeviceAssetKinds.Icon => IconContentType,
        DeviceAssetKinds.Manifest => ManifestContentType,
        _ => BinaryContentType
    };

    private static byte[] Filler(FakeDevice device, HarvestEntry entry, string name, string? appVersion, int size) {
        string seed = string.Join('|', device.Id, entry.Name, name, appVersion ?? "");
        byte[] block = Convert.FromHexString(Hashes.Sha256Hex(seed));
        byte[] bytes = new byte[Math.Max(1, size)];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = block[i % block.Length];
        bytes[0] = 0;
        return bytes;
    }

    private static FakeFixtureFile TextFile(string name, string body) =>
        new(name, Hashes.Sha256Hex(Encoding.UTF8.GetBytes(body)), ManifestContentType,
            Encoding.UTF8.GetByteCount(body));

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
