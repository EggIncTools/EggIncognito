using System.Text;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public sealed class IntegrityAssets(
    IServiceScopeFactory scopeFactory,
    ModuleFetcher moduleFetcher,
    PixelFingerprintFetcher fingerprints,
    IHttpClientFactory httpFactory,
    VirtualDeviceConfig config,
    TimeProvider time,
    ILogger<IntegrityAssets> logger) {
    public const string IntegrityBoxModuleId = "playintegrityfix";
    public const string ProfileEntry = "pif-profile";
    public const string KeyboxEntry = "keybox";
    public const string OperatorSource = "operator";
    public const string SharedSource = "shared";
    public const string KeyboxClean = "clean";
    public const string KeyboxUnchecked = "unchecked";

    public Task<IntegrityBundle> DescribeAsync(CancellationToken ct) => ResolveAsync(false, ct);

    public async Task<IntegrityBundle> PeekAsync(CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceModuleStore)) is not DeviceModuleStore store)
            return IntegrityBundle.Fail("no database configured");

        var warnings = new List<string>();
        var modules = new List<IntegrityModuleAsset>();
        foreach (var spec in config.IntegrityModules) {
            var row = await store.LatestAsync(spec.Name, ct);
            if (row is null) return IntegrityBundle.Fail($"module '{spec.Name}' is not cached yet; press Refresh cache");
            byte[] zip = await store.BytesAsync(row, ct);
            string? id = MagiskModules.IdFromZip(zip);
            if (id is null) return IntegrityBundle.Fail($"cached module '{spec.Name}' has no module.prop id");
            modules.Add(new IntegrityModuleAsset(spec, id, row.Version, zip));
        }

        var ib = modules.FirstOrDefault(m => m.ModuleId.Equals(IntegrityBoxModuleId, StringComparison.Ordinal));
        if (ib is null) return IntegrityBundle.Fail("the module chain has no Integrity-Box (module id playintegrityfix)");
        string? patchDate = IntegrityBoxModule.PatchDate(ib.Zip);
        if (patchDate is null) return IntegrityBundle.Fail("Integrity-Box zip carries no security patch date");

        PifProfile? profile = null;
        if (await store.LatestAsync(ProfileEntry, ct) is { } cachedProfile)
            profile = PifProp.Parse(Encoding.UTF8.GetString(await store.BytesAsync(cachedProfile, ct))) is { } parsed ? parsed with { SecurityPatch = patchDate } : null;
        if (profile is null) {
            profile = IntegrityBoxModule.LegacyProfile(ib.Zip) is { } legacy ? legacy with { SecurityPatch = patchDate } : null;
            if (profile is null) return IntegrityBundle.Fail("no fingerprint cached and Integrity-Box ships no legacy profile; press Refresh cache");
            warnings.Add($"no fingerprint fetched yet; showing Integrity-Box's bundled profile {profile.Id}");
        }

        string? xml;
        string source;
        if (config.IntegrityKeyboxPath is { Length: > 0 } path) {
            var operatorKeybox = await KeyboxAsync(store, false, warnings, ct);
            if (operatorKeybox.Error is not null) return IntegrityBundle.Fail(operatorKeybox.Error);
            xml = operatorKeybox.Xml;
            source = operatorKeybox.Source ?? OperatorSource;
        } else if (await store.LatestAsync(KeyboxEntry, ct) is { } cachedKeybox) {
            xml = Encoding.UTF8.GetString(await store.BytesAsync(cachedKeybox, ct));
            source = SharedSource;
        } else {
            return IntegrityBundle.Fail("no keybox cached yet; press Refresh cache");
        }

        var serials = KeyboxRevocation.Serials(xml!);
        return new IntegrityBundle(true, null, profile, PifProp.Render(profile), xml, source, serials,
            KeyboxUnchecked, patchDate, modules, warnings);
    }

    public async Task<IntegrityBundle> ResolveAsync(bool forceRefresh, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceModuleStore)) is not DeviceModuleStore store)
            return IntegrityBundle.Fail("no database configured");

        var warnings = new List<string>();

        var modules = new List<IntegrityModuleAsset>();
        foreach (var spec in config.IntegrityModules) {
            var res = await moduleFetcher.ResolveAsync(spec, forceRefresh, ct);
            if (!res.Ok || res.Bytes is null) return IntegrityBundle.Fail(res.Error ?? $"module '{spec.Name}' did not resolve");
            string? id = MagiskModules.IdFromZip(res.Bytes);
            if (id is null) return IntegrityBundle.Fail($"module '{spec.Name}' has no module.prop id");
            modules.Add(new IntegrityModuleAsset(spec, id, res.Version, res.Bytes));
        }

        var ib = modules.FirstOrDefault(m => m.ModuleId.Equals(IntegrityBoxModuleId, StringComparison.Ordinal));
        if (ib is null) return IntegrityBundle.Fail("the module chain has no Integrity-Box (module id playintegrityfix)");

        string? patchDate = IntegrityBoxModule.PatchDate(ib.Zip);
        if (patchDate is null) return IntegrityBundle.Fail("Integrity-Box zip carries no security patch date");

        var profile = await ProfileAsync(store, ib.Zip, patchDate, forceRefresh, warnings, ct);
        if (profile is null) return IntegrityBundle.Fail("no Pixel fingerprint could be resolved and Integrity-Box ships no legacy profile");

        var keybox = await KeyboxAsync(store, forceRefresh, warnings, ct);
        if (keybox.Error is not null) return IntegrityBundle.Fail(keybox.Error);

        var serials = KeyboxRevocation.Serials(keybox.Xml!);
        var (revoked, revocationError) = await RevocationAsync(serials, ct);
        if (revoked.Count > 0) {
            return IntegrityBundle.Fail(
                $"keybox is on Google's revocation list: {string.Join(", ", revoked)}; "
                + "supply a clean one via Devices:Virtual:Integrity:KeyboxPath");
        }

        string keyboxNote = KeyboxClean;
        if (revocationError is not null) {
            warnings.Add($"revocation check skipped: {revocationError}");
            keyboxNote = KeyboxUnchecked;
        }

        return new IntegrityBundle(true, null, profile, PifProp.Render(profile), keybox.Xml, keybox.Source,
            serials, keyboxNote, patchDate, modules, warnings);
    }

    private async Task<PifProfile?> ProfileAsync(
        DeviceModuleStore store, byte[] ibZip, string patchDate, bool forceRefresh,
        List<string> warnings, CancellationToken ct) {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var row = await store.LatestAsync(ProfileEntry, ct);
        PifProfile? cached = null;
        if (row is { } stored) {
            cached = PifProp.Parse(Encoding.UTF8.GetString(await store.BytesAsync(stored, ct))) is { } parsed
                ? parsed with { SecurityPatch = patchDate }
                : null;
            var maxAge = TimeSpan.FromDays(Math.Max(1, config.IntegrityFingerprintRefreshDays));
            if (cached is not null && !forceRefresh && !cached.Expired(today) && now - stored.FetchedAt < maxAge)
                return cached;
        }

        try {
            var profile = await fingerprints.FetchAsync(config.IntegrityPixelProduct ?? cached?.Product, patchDate, ct);
            byte[] bytes = Encoding.UTF8.GetBytes(PifProp.Render(profile));
            await store.PutAsync(ProfileEntry, profile.Product, profile.Id, Hashes.Sha256Hex(bytes), bytes, ct);
            return profile;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "integrity assets: pixel fingerprint refresh failed");
            if (cached is not null) {
                warnings.Add($"fingerprint refresh failed: {ex.Message}; using cached {cached.Id}");
                return cached;
            }

            var legacy = IntegrityBoxModule.LegacyProfile(ibZip);
            if (legacy is null) return null;
            warnings.Add($"fingerprint fetch failed: {ex.Message}; using Integrity-Box's bundled profile {legacy.Id}");
            return legacy with { SecurityPatch = patchDate };
        }
    }

    private async Task<(string? Xml, string? Source, string? Error)> KeyboxAsync(
        DeviceModuleStore store, bool forceRefresh, List<string> warnings, CancellationToken ct) {
        if (config.IntegrityKeyboxPath is { Length: > 0 } path) {
            string xml;
            try {
                xml = await File.ReadAllTextAsync(path, ct);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
                return (null, null, $"operator keybox {path} could not be read: {ex.Message}");
            }

            if (!KeyboxCodec.LooksLikeKeybox(xml)) return (null, null, $"operator keybox {path} does not look like a keybox.xml");
            return (xml, OperatorSource, null);
        }

        string url = config.IntegrityKeyboxUrl;
        string source = SharedSource;
        var row = await store.LatestAsync(KeyboxEntry, ct);
        var maxAge = TimeSpan.FromHours(Math.Max(1, config.IntegrityRefreshHours));
        if (row is { } stored && !forceRefresh && time.GetUtcNow() - stored.FetchedAt < maxAge)
            return (Encoding.UTF8.GetString(await store.BytesAsync(stored, ct)), source, null);

        try {
            var http = httpFactory.CreateClient(ModuleFetcher.HttpClientName);
            byte[] megatron = await http.GetByteArrayAsync(url, ct);
            string xml = KeyboxCodec.Decode(megatron);
            if (!KeyboxCodec.LooksLikeKeybox(xml)) throw new InvalidOperationException("decoded payload is not a keybox.xml");
            byte[] bytes = Encoding.UTF8.GetBytes(xml);
            await store.PutAsync(KeyboxEntry, url, null, Hashes.Sha256Hex(bytes), bytes, ct);
            return (xml, source, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "integrity assets: shared keybox fetch failed");
            if (row is null) return (null, null, $"shared keybox fetch failed: {ex.Message}");
            warnings.Add($"shared keybox refresh failed: {ex.Message}; using cached copy");
            return (Encoding.UTF8.GetString(await store.BytesAsync(row, ct)), source, null);
        }
    }

    private async Task<(IReadOnlyList<string> Revoked, string? Error)> RevocationAsync(
        IReadOnlyList<string> serials, CancellationToken ct) {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        return await KeyboxRevocation.RevokedAsync(http, serials, ct);
    }
}
