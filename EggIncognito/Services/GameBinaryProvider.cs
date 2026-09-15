using System.Globalization;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;

namespace EggIncognito.Services;

public sealed class GameBinaryProvider(
    IServiceProvider services,
    IConfiguration config,
    ILogger<GameBinaryProvider> logger) {
    private const string DefaultPlatform = Platforms.Ios;
    private static readonly Lock CvGate = new();
    private static readonly Lock StageGate = new();
    private static readonly TimeSpan CvRecheckBackoff = TimeSpan.FromMinutes(15);

    private static (string Version, string Sha)? StagedIos;

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

    public async Task<(bool Ok, byte[]? Bytes, string? Diagnostics)> GetBinaryAsync(string? deviceId,
        CancellationToken ct) {
        (bool ok, byte[]? bytes, _, string? diag) = await GetBinaryWithVersionAsync(deviceId, ct);
        return (ok, bytes, diag);
    }

    public async Task<(bool Ok, byte[]? Bytes, string Version, string? Diagnostics)> GetBinaryWithVersionAsync(
        string? deviceId, CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(deviceId, DefaultPlatform, ct)).Version;

        string? overridePath = config[DecompConfigKeys.BinaryPath];
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) {
            byte[] bytes = await File.ReadAllBytesAsync(overridePath, ct);
            return (true, bytes, version ?? "unknown", null);
        }

        var r = SymbolizedStore().Get(version);
        if (!r.Ok || r.Bytes is null) return (false, null, "", r.Diagnostics);

        if (!r.ExactVersion)
            logger.LogInformation("decomp: device version {Dev} not in stash, using symbolized {Use}", version ?? "?",
                r.Version);

        return (true, r.Bytes, r.Version,
            r.ExactVersion ? null : $"version mismatch: device {version ?? "?"}, using symbolized {r.Version}");
    }

    public Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, string Version, string?
        Diagnostics)> GetExtractionBinaryAsync(CancellationToken ct) => GetExtractionBinaryAsync(DefaultPlatform, ct);

    public async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, string Version, string?
            Diagnostics)>
        GetExtractionBinaryAsync(string platform, CancellationToken ct) {
        bool isDefault = Platforms.Matches(platform, DefaultPlatform);

        if (isDefault) {
            string? overridePath = config[DecompConfigKeys.BinaryPath];
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) {
                byte[] ob = await File.ReadAllBytesAsync(overridePath, ct);
                string? ov = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
                return (true, ob, null, ov ?? "override", $"override binary {overridePath}");
            }
        }

        var dev = await EnsureDeviceBinaryAsync(platform, ct);
        if (dev.Ok && dev.Bytes is not null)
            return (true, dev.Bytes, dev.Symbols, dev.Version, dev.Diagnostics);

        if (isDefault) {
            (bool sok, byte[]? sbytes, string sver, string? sdiag) = await GetBinaryWithVersionAsync(null, ct);
            if (sok && sbytes is not null)
                return (true, sbytes, null, sver, $"stale stash fallback ({sver}); {dev.Diagnostics}");
            return (false, null, null, "", $"{dev.Diagnostics}; stash: {sdiag}");
        }

        return (false, null, null, dev.Version, dev.Diagnostics);
    }

    public async Task<int?> GetClientVersionAsync(string platform, CancellationToken ct, bool force = false) {
        string? installed = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
        if (string.IsNullOrEmpty(installed)) return null;

        if (!force) {
            lock (CvGate) {
                if (CvCache.TryGetValue(platform, out var c) &&
                    string.Equals(c.Version, installed, StringComparison.Ordinal) &&
                    (c.ClientVersion is not null || DateTimeOffset.UtcNow - c.CheckedAt < CvRecheckBackoff))
                    return c.ClientVersion;
            }
        }

        var bin = await GetExtractionBinaryAsync(platform, ct);
        if (!bin.Ok || bin.Bytes is null || !string.Equals(bin.Version, installed, StringComparison.Ordinal)) {
            lock (CvGate) {
                CvCache[platform] = (installed, null, DateTimeOffset.UtcNow);
            }

            return null;
        }

        int? cv = LibegincClientVersion.ReadFromBinary(bin.Bytes, bin.Symbols);
        lock (CvGate) {
            CvCache[platform] = (installed, cv, DateTimeOffset.UtcNow);
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
                platforms.AddRange(devices
                    .Where(d => !DeviceOrigins.IsVirtual(d.Origin))
                    .Select(d => d.Platform).Distinct(StringComparer.OrdinalIgnoreCase));
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

    public async Task<IReadOnlyList<(string Platform, string Status, string? Version, string? Note)>>
        EnsureAllVersionsStoredAsync(CancellationToken ct) {
        var results = new List<(string, string, string?, string?)>();
        var store = Store;
        if (store is null) {
            results.Add((DefaultPlatform, "no-store", null, "device status store unavailable"));
            return results;
        }

        List<Device> devices;
        try {
            devices = [.. (await store.EnabledDevicesAsync(ct)).Where(d => !DeviceOrigins.IsVirtual(d.Origin))];
        } catch (Exception ex) {
            results.Add((DefaultPlatform, "store-error", null, ex.Message));
            return results;
        }

        var platforms = devices.Select(d => d.Platform).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (platforms.Count == 0) {
            results.Add((DefaultPlatform, "no-device", null, "no enabled devices"));
            return results;
        }

        foreach (string platform in platforms) {
            (string status, string? version, string? note) = await EnsureCurrentVersionStoredAsync(platform, ct);
            results.Add((platform, status, version, note));
        }

        return results;
    }

    public Task<(string Status, string? Version, string? Note)> EnsureCurrentVersionStoredAsync(CancellationToken ct) =>
        EnsureCurrentVersionStoredAsync(DefaultPlatform, ct);

    public async Task<(string Status, string? Version, string? Note)> EnsureCurrentVersionStoredAsync(string platform,
        CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
        if (string.IsNullOrEmpty(version)) return ("no-version", null, "no probe with an installed app version");

        var store = BinaryStore;
        if (store is null) return ("no-store", version, "binary store unavailable");

        try {
            if (await store.ExistsAsync(platform, version, ct)) return ("stored", version, null);
        } catch (Exception ex) {
            return ("store-error", version, ex.Message);
        }

        bool poked = await PokeAgentAsync(ct);
        return ("awaiting-harvest", version, AwaitingNote(platform, version, poked));
    }

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

    private async Task<(bool Ok, byte[]? Bytes, IReadOnlyList<MachoSymbols.Symbol>? Symbols, string Version, string?
        Diagnostics)> EnsureDeviceBinaryAsync(string platform, CancellationToken ct) {
        string? version = (await ResolveVersionAndDeviceAsync(null, platform, ct)).Version;
        if (string.IsNullOrEmpty(version))
            return (false, null, null, "", "no device version known (no probe with an installed app version)");

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
                return (true, bytes, resolved.Syms, version,
                    $"stored binary {platform} {version} (sha {shaShort}); {resolved.Note}");
            }
        }

        bool poked = await PokeAgentAsync(ct);
        return (false, null, null, version,
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

    public async Task<(bool Staged, string? Note)> EnsureIosBinaryStagedAsync(CancellationToken ct) {
        string? stashPath = config["Runner:IosBinaryStashPath"];
        if (string.IsNullOrEmpty(stashPath)) return (false, "no Runner:IosBinaryStashPath configured");

        string? installed = (await ResolveVersionAndDeviceAsync(null, "ios", ct)).Version;
        if (string.IsNullOrEmpty(installed)) return (false, "no installed ios version known");

        lock (StageGate) {
            if (StagedIos is { } s && string.Equals(s.Version, installed, StringComparison.Ordinal)
                                   && File.Exists(stashPath))
                return (true, $"already staged {installed}");
        }

        var bin = await GetExtractionBinaryAsync("ios", ct);
        if (!bin.Ok || bin.Bytes is null)
            return (false, $"no ios binary for {installed}: {bin.Diagnostics}");
        if (!string.Equals(bin.Version, installed, StringComparison.Ordinal))
            return (false, $"skipping stale ios binary {bin.Version} for installed {installed}");

        return await StageIosBinaryAsync("ios", bin.Bytes, ct)
            ? (true, $"staged {installed}")
            : (false, $"stage write failed for {installed}");
    }

    private async Task<bool> StageIosBinaryAsync(string platform, byte[] bytes, CancellationToken ct) {
        if (!Platforms.Matches(platform, Platforms.Ios)) return false;
        string? stashPath = config["Runner:IosBinaryStashPath"];
        if (string.IsNullOrEmpty(stashPath)) return false;

        string? installed = (await ResolveVersionAndDeviceAsync(null, Platforms.Ios, ct)).Version;
        string sha = Hashes.Sha256Hex(bytes);
        lock (StageGate) {
            if (StagedIos is { } s && string.Equals(s.Sha, sha, StringComparison.Ordinal) && File.Exists(stashPath))
                return true;
        }

        try {
            string? dir = Path.GetDirectoryName(stashPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = stashPath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, stashPath, true);
            lock (StageGate) StagedIos = (installed ?? "", sha);
            logger.LogInformation("binary store: staged ios binary to {Path} ({Bytes} bytes, sha {Sha})", stashPath,
                bytes.Length, sha[..12]);
            return true;
        } catch (Exception ex) {
            logger.LogWarning(ex, "binary store: could not stage ios binary to {Path}", stashPath);
            return false;
        }
    }

    public async Task<(bool Ok, byte[]? RefBytes, byte[]? TargetBytes, string? Diagnostics)> GetRecoveryInputsAsync(
        string? refVersion, string? targetPathOverride, CancellationToken ct) {
        var refr = SymbolizedStore().Get(refVersion);
        if (!refr.Ok || refr.Bytes is null) return (false, null, null, refr.Diagnostics);

        string? targetPath = targetPathOverride ?? config[DecompConfigKeys.StrippedTargetPath];
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            return (false, refr.Bytes, null,
                $"no stripped target binary; set {DecompConfigKeys.StrippedTargetPath} or pass targetPath");

        byte[] targetBytes = await File.ReadAllBytesAsync(targetPath, ct);
        return (true, refr.Bytes, targetBytes, null);
    }

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
                device = devices.FirstOrDefault(d => d.Id == deviceId);
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
