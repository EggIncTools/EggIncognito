using System.Globalization;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;

namespace EggIncognito.Services;

public sealed class GameBinaryProvider(
    IServiceProvider services,
    IConfiguration config,
    ILogger<GameBinaryProvider> logger,
    TimeProvider time) {
    private const string DefaultPlatform = Platforms.Ios;
    private static readonly Lock CvGate = new();
    private static readonly TimeSpan CvRecheckBackoff = TimeSpan.FromMinutes(15);

#pragma warning disable IDE0028
    private static readonly Dictionary<string, (string Version, int? ClientVersion, DateTimeOffset CheckedAt)>
        CvCache = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private DeviceJobStore? Jobs => services.GetService(typeof(DeviceJobStore)) as DeviceJobStore;
    private GameBinaryStore? BinaryStore => services.GetService(typeof(GameBinaryStore)) as GameBinaryStore;
    private SymbolizedReferenceStore? RefStore =>
        services.GetService(typeof(SymbolizedReferenceStore)) as SymbolizedReferenceStore;
    private IDeviceAgentClient? Agent => services.GetService(typeof(IDeviceAgentClient)) as IDeviceAgentClient;
    private IDeviceResolver? Resolver => services.GetService(typeof(IDeviceResolver)) as IDeviceResolver;

    private SymbolizedBinaryStore SymbolizedStore() {
        string? dir = config[DecompConfigKeys.SymbolizedIpaDir];
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine("captures", "ipas");
        return new SymbolizedBinaryStore(dir);
    }

    public async Task<BinaryFetchResult> GetBinaryAsync(string? deviceId,
        CancellationToken ct) {
        (bool ok, byte[]? bytes, _, string? diag) = await GetBinaryWithVersionAsync(deviceId, ct);
        return new BinaryFetchResult(ok, bytes, diag);
    }

    public sealed record BinaryFetchResult(bool Ok, byte[]? Bytes, string? Diagnostics);

    public async Task<VersionedBinaryResult> GetBinaryWithVersionAsync(
        string? deviceId, CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(deviceId, DefaultPlatform, ct)).Version;

        string? overridePath = config[DecompConfigKeys.BinaryPath];
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) {
            byte[] bytes = await File.ReadAllBytesAsync(overridePath, ct);
            return new VersionedBinaryResult(true, bytes, version ?? "unknown", null);
        }

        var r = SymbolizedStore().Get(version);
        if (!r.Ok || r.Bytes is null) return new VersionedBinaryResult(false, null, "", r.Diagnostics);

        if (!r.ExactVersion)
            logger.LogInformation("decomp: device version {Dev} not in stash, using symbolized {Use}", version ?? "?",
                r.Version);

        return new VersionedBinaryResult(true, r.Bytes, r.Version,
            r.ExactVersion ? null : $"version mismatch: device {version ?? "?"}, using symbolized {r.Version}");
    }

    public sealed record VersionedBinaryResult(bool Ok, byte[]? Bytes, string Version, string? Diagnostics);

    public sealed record ExtractionBinaryResult(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols,
        string Version, string? Diagnostics);

    public Task<ExtractionBinaryResult> GetExtractionBinaryAsync(CancellationToken ct) =>
        GetExtractionBinaryAsync(DefaultPlatform, ct);

    public async Task<ExtractionBinaryResult> GetExtractionBinaryAsync(string platform, CancellationToken ct) {
        bool isDefault = Platforms.Matches(platform, DefaultPlatform);

        if (isDefault) {
            string? overridePath = config[DecompConfigKeys.BinaryPath];
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) {
                byte[] ob = await File.ReadAllBytesAsync(overridePath, ct);
                string? ov = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
                return new(true, ob, null, ov ?? "override", $"override binary {overridePath}");
            }
        }

        var dev = await EnsureDeviceBinaryAsync(platform, ct);
        if (dev.Ok && dev.Bytes is not null) return dev;

        if (isDefault) {
            (bool sok, byte[]? sbytes, string sver, string? sdiag) = await GetBinaryWithVersionAsync(null, ct);
            if (sok && sbytes is not null)
                return new(true, sbytes, null, sver, $"stale stash fallback ({sver}); {dev.Diagnostics}");
            return new(false, null, null, "", $"{dev.Diagnostics}; stash: {sdiag}");
        }

        return new(false, null, null, dev.Version, dev.Diagnostics);
    }

    public async Task<int?> GetClientVersionAsync(string platform, CancellationToken ct, bool force = false) {
        string? installed = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
        if (string.IsNullOrEmpty(installed)) return null;

        if (!force) {
            lock (CvGate) {
                if (CvCache.TryGetValue(platform, out var c) &&
                    string.Equals(c.Version, installed, StringComparison.Ordinal) &&
                    (c.ClientVersion is not null || time.GetUtcNow() - c.CheckedAt < CvRecheckBackoff))
                    return c.ClientVersion;
            }
        }

        var bin = await GetExtractionBinaryAsync(platform, ct);
        if (!bin.Ok || bin.Bytes is null || !string.Equals(bin.Version, installed, StringComparison.Ordinal)) {
            lock (CvGate) {
                CvCache[platform] = (installed, null, time.GetUtcNow());
            }

            return null;
        }

        int? cv = LibegincClientVersion.ReadFromBinary(bin.Bytes, bin.Symbols);
        lock (CvGate) {
            CvCache[platform] = (installed, cv, time.GetUtcNow());
        }

        logger.LogInformation("client version: {Platform} {Version} -> {Cv}", platform, installed,
            cv?.ToString(CultureInfo.InvariantCulture) ?? "none");
        return cv;
    }

    public int? CachedClientVersion(string platform, string? version) {
        if (string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(version)) return null;
        lock (CvGate) {
            return CvCache.TryGetValue(platform, out var c) &&
                   string.Equals(c.Version, version, StringComparison.Ordinal)
                ? c.ClientVersion
                : null;
        }
    }

    public IReadOnlyList<string> ExtractablePlatformsFallback => [DefaultPlatform];

    public sealed record ExtractionCandidate(string Platform, string Version, byte[] Bytes,
        IReadOnlyList<MachoSymbols.Symbol>? Symbols, string? Diagnostics);

    public sealed record ExtractionCandidates(IReadOnlyList<ExtractionCandidate> Candidates,
        IReadOnlyList<string> Rejected) {
        public string Diagnostics => Rejected.Count == 0 ? "" : string.Join("; ", Rejected);
    }

    public async Task<ExtractionCandidates> GetExtractionCandidatesAsync(CancellationToken ct) {
        var platforms = new List<string>();
        var rejected = new List<string>();
        var store = Store;
        if (store is not null) {
            try {
                var devices = await store.EnabledDevicesAsync(ct);
                platforms.AddRange(devices.Select(d => d.Platform).Distinct(StringComparer.OrdinalIgnoreCase));
            } catch (Exception ex) {
                logger.LogWarning(ex, "enabled-device enumeration failed; falling back to {Platform}", DefaultPlatform);
                rejected.Add($"device enumeration failed: {ex.Message}");
            }
        } else {
            rejected.Add("device status store unavailable");
        }

        if (platforms.Count == 0) platforms.Add(DefaultPlatform);
        if (!platforms.Contains(DefaultPlatform, StringComparer.OrdinalIgnoreCase)) platforms.Add(DefaultPlatform);

        var candidates = new List<ExtractionCandidate>();
        foreach (string platform in platforms) {
            var r = await GetExtractionBinaryAsync(platform, ct);
            if (r.Ok && r.Bytes is not null)
                candidates.Add(new ExtractionCandidate(platform, r.Version, r.Bytes, r.Symbols, r.Diagnostics));
            else
                rejected.Add($"{platform}: {r.Diagnostics ?? "no binary"}");
        }

        candidates.Sort((a, b) => {
            int cmp = DeviceParsing.CompareVersions(b.Version, a.Version);
            return cmp != 0 ? cmp : PlatformRank(a.Platform).CompareTo(PlatformRank(b.Platform));
        });
        return new ExtractionCandidates(candidates, rejected);
    }

    private static int PlatformRank(string platform) =>
        Platforms.Matches(platform, DefaultPlatform) ? 0 : 1;

    public async Task<IReadOnlyList<VersionStoreStatus>>
        EnsureAllVersionsStoredAsync(CancellationToken ct) {
        var results = new List<VersionStoreStatus>();
        var store = Store;
        if (store is null) {
            results.Add(new VersionStoreStatus(DefaultPlatform, "no-store", null, "device status store unavailable"));
            return results;
        }

        List<Device> devices;
        try {
            devices = [.. await store.EnabledDevicesAsync(ct)];
        } catch (Exception ex) {
            results.Add(new VersionStoreStatus(DefaultPlatform, "store-error", null, ex.Message));
            return results;
        }

        var platforms = devices.Select(d => d.Platform).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (platforms.Count == 0) {
            results.Add(new VersionStoreStatus(DefaultPlatform, "no-device", null, "no enabled devices"));
            return results;
        }

        foreach (string platform in platforms) {
            (string status, string? version, string? note) = await EnsureCurrentVersionStoredAsync(platform, ct);
            results.Add(new VersionStoreStatus(platform, status, version, note));
        }

        return results;
    }

    public sealed record VersionStoreStatus(string Platform, string Status, string? Version, string? Note);

    public Task<VersionStoreResult> EnsureCurrentVersionStoredAsync(CancellationToken ct) =>
        EnsureCurrentVersionStoredAsync(DefaultPlatform, ct);

    public async Task<VersionStoreResult> EnsureCurrentVersionStoredAsync(string platform,
        CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
        if (string.IsNullOrEmpty(version)) return new VersionStoreResult("no-version", null, "no probe with an installed app version");

        var store = BinaryStore;
        if (store is null) return new VersionStoreResult("no-store", version, "binary store unavailable");

        try {
            if (await store.ExistsAsync(platform, version, ct)) return new VersionStoreResult("stored", version, null);
        } catch (Exception ex) {
            return new VersionStoreResult("store-error", version, ex.Message);
        }

        bool poked = await PokeAgentAsync(ct);
        return new VersionStoreResult("awaiting-harvest", version, AwaitingNote(platform, version, poked));
    }

    public sealed record VersionStoreResult(string Status, string? Version, string? Note);

    private static string AwaitingNote(string platform, string version, bool poked) =>
        poked
            ? $"{platform} {version} is not harvested yet; poked the device agent"
            : $"{platform} {version} is not harvested yet and no device agent is configured";

    private async Task<bool> PokeAgentAsync(CancellationToken ct) {
        if (Agent is not { Enabled: true } agent) return false;
        try {
            return await agent.PokeAsync(null, false, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "poke to the device agent failed");
            return false;
        }
    }

    private async Task<ExtractionBinaryResult> EnsureDeviceBinaryAsync(string platform, CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
        if (string.IsNullOrEmpty(version))
            return new(false, null, null, "", "no device version known (no probe with an installed app version)");

        var store = BinaryStore;
        if (store is not null) {
            StoredBinary? row = null;
            try {
                row = await store.GetAsync(platform, version, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "stored binary lookup failed for {Platform} {Version}", platform, version);
            }

            if (row is not null) {
                byte[] bytes = await store.BytesAsync(row, ct);
                var resolved = await ResolveSymbolsAsync(bytes, ct);
                string shaShort = row.Sha256.Length >= 12 ? row.Sha256[..12] : row.Sha256;
                return new(true, bytes, resolved.Syms, version,
                    $"stored binary {platform} {version} (sha {shaShort}); {resolved.Note}");
            }
        }

        bool poked = await PokeAgentAsync(ct);
        return new(false, null, null, version,
            poked
                ? $"no harvested binary for {platform} {version}; poked the device agent, retry once harvest lands"
                : $"no harvested binary for {platform} {version} and no device agent is configured");
    }

    private static bool IsElf(byte[] b) =>
        b.Length >= 4 && b[0] == 0x7f && b[1] == 0x45 && b[2] == 0x4c && b[3] == 0x46;

    private async Task<(IReadOnlyList<MachoSymbols.Symbol> Syms, bool Grafted, int NativeCount, string Note)>
        ResolveSymbolsAsync(byte[] bytes, CancellationToken ct) {
        var img = BinaryImage.Load(bytes);
        var syms = img?.Symbols ?? MachoSymbols.Read(bytes);
        int nativeCount = syms.Count;
        if (nativeCount >= 50_000) return (syms, false, nativeCount, $"{nativeCount} native symbols");

        if (IsElf(bytes))
            return (syms, false, nativeCount,
                $"{nativeCount} native ELF symbols (Mach-O stash graft not applicable)");

        var refStore = RefStore;
        if (refStore is not null) {
            SymbolizedBinary? dbRow = null;
            try {
                dbRow = await refStore.GetLatestAsync(Platforms.Ios, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "symbolized reference lookup failed");
            }

            if (dbRow is not null) {
                var dbReport = SymbolRecovery.Recover(dbRow.Bytes, bytes, []);
                if (dbReport.Symbols.Count > nativeCount) {
                    return (dbReport.Symbols, true, nativeCount,
                        $"stripped; grafted {dbReport.Recovered} symbols from db reference {dbRow.AppVersion} ({dbReport.Tier})");
                }
            }
        }

        var refr = SymbolizedStore().Get(null);
        if (refr.Ok && refr.Bytes is not null) {
            var report = SymbolRecovery.Recover(refr.Bytes, bytes, []);
            if (report.Symbols.Count > nativeCount)
                return (report.Symbols, true, nativeCount,
                    $"stripped; grafted {report.Recovered} symbols from {refr.Version} ({report.Tier})");
        }

        return (syms, false, nativeCount, $"{nativeCount} native symbols (no graft reference)");
    }

    public async Task<RecoveryInputs> GetRecoveryInputsAsync(
        string? refVersion, string? targetPathOverride, CancellationToken ct) {
        var refr = SymbolizedStore().Get(refVersion);
        if (!refr.Ok || refr.Bytes is null) return new RecoveryInputs(false, null, null, refr.Diagnostics);

        string? targetPath = targetPathOverride ?? config[DecompConfigKeys.StrippedTargetPath];
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            return new RecoveryInputs(false, refr.Bytes, null,
                $"no stripped target binary; set {DecompConfigKeys.StrippedTargetPath} or pass targetPath");

        byte[] targetBytes = await File.ReadAllBytesAsync(targetPath, ct);
        return new RecoveryInputs(true, refr.Bytes, targetBytes, null);
    }

    public sealed record RecoveryInputs(bool Ok, byte[]? RefBytes, byte[]? TargetBytes, string? Diagnostics);

    private async Task<(string? Version, Device? Device)> ResolveVersionAndDeviceAsync(string? deviceId,
        string platform, CancellationToken ct) {
        var store = Store;
        if (store is null) return (null, null);
        try {
            Device? device;
            if (deviceId is null) {
                device = Resolver is { } r ? await r.ResolveAsync(new DeviceQuery(Platform: platform), ct) : null;
            } else {
                var devices = await store.EnabledDevicesAsync(ct);
                device = devices.Find(d => d.Id == deviceId);
            }

            if (Jobs is not { } jobs) return (null, device);

            if (device is null) {
                if (deviceId is null) return (null, null);
                var latestForId = await jobs.LatestPerDeviceAsync(DeviceJobKinds.Probe, ct);
                return (latestForId.FirstOrDefault(p => p.DeviceId == deviceId)?.AppVersion, null);
            }

            var latest = await jobs.LatestPerDeviceAsync(DeviceJobKinds.Probe, ct);
            string? version = latest.FirstOrDefault(p => p.DeviceId == device.Id)?.AppVersion;
            return (version, device);
        } catch {
            return (null, null);
        }
    }
}
