using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services.Devices;

public sealed class IosPlatform(
    IDeviceConnectionFactory connections,
    DeviceCaptureConfig config,
    IProcessRunner runner,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    IEnumerable<IDeviceProxyConfigurator> proxyConfigurators,
    IEnumerable<IDeviceCaInstaller> caInstallers,
    IEnumerable<IDeviceUiDriver> uiDrivers,
    IEnumerable<IScreenStreamSource> screenStreamSources,
    ILogger<IosPlatform> logger)
    : DevicePlatformBase(Platforms.Ios, storeCheckers, proxyConfigurators, caInstallers, uiDrivers,
        screenStreamSources) {
    private const string NoSshNote = "ios ssh not configured";
    private int _noSshWarned;

    private void WarnNoSshOnce() {
        if (Interlocked.Exchange(ref _noSshWarned, 1) != 0) return;
        logger.LogWarning(
            "ios ssh not configured on this host (DeviceCapture:Ios:SshHost/SshKeyPath, " +
            "falling back to DeviceUpdate:Ios:*): every ios harvest entry will fail and no ios binary, " +
            "mesh or texture can land until this process can ssh to the phone");
    }

    public override async Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn) {
            WarnNoSshOnce();
            return DeviceResult<byte[]>.Unreachable(NoSshNote);
        }
        byte[]? bytes = await new IosBinaryPuller(conn).PullBinaryAsync(target.Package, ct);
        return bytes is null
            ? DeviceResult<byte[]>.Error($"could not pull binary for {target.Package}")
            : DeviceResult<byte[]>.Success(bytes);
    }

    public override async Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind,
        string name, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn) {
            WarnNoSshOnce();
            return DeviceResult<byte[]>.Unreachable(NoSshNote);
        }

        var puller = new IosAssetPuller(conn);
        byte[]? bytes = kind switch {
            DeviceAssetKind.Mesh => await puller.PullOneRpoAsync(target.Package, name, ct),
            DeviceAssetKind.Texture => await puller.PullOneTextureAsync(target.Package, name, ct),
            _ => null
        };
        return bytes is null
            ? DeviceResult<byte[]>.Error($"asset not found: {kind} {name}")
            : DeviceResult<byte[]>.Success(bytes);
    }

    public override async Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target,
        DeviceAssetKind kind, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn) {
            WarnNoSshOnce();
            return DeviceResult<IReadOnlyList<string>>.Unreachable(NoSshNote);
        }

        var puller = new IosAssetPuller(conn);
        IReadOnlyList<string> names = kind switch {
            DeviceAssetKind.Mesh => await puller.ListRposAsync(target.Package, ct),
            DeviceAssetKind.Texture => await puller.ListTexturesAsync(target.Package, ct),
            _ => []
        };
        return DeviceResult<IReadOnlyList<string>>.Success(names);
    }

    private const string PackageUnsupported = "ios ships the app binary, not an installable package";

    public override IReadOnlyList<HarvestEntry> Manifest() => [
        new(HarvestEntries.AppBinary, DeviceAssetKinds.Binary),
        new(HarvestEntries.AppPackage, DeviceAssetKinds.Package, false, PackageUnsupported),
        new(HarvestEntries.Meshes, DeviceAssetKinds.Mesh),
        new(HarvestEntries.Textures, DeviceAssetKinds.Icon),
        new(HarvestEntries.PackageManifest, DeviceAssetKinds.Manifest)
    ];

    public override async Task<DeviceResult<string>> FingerprintAsync(DeviceTarget target, HarvestEntry entry,
        CancellationToken ct) {
        if (!entry.Supported) return DeviceResult<string>.Unsupported(entry.UnsupportedNote);
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<string>.Unreachable("ios ssh not configured");
        var listing = await ListingAsync(conn, target.Package, entry, ct);
        return listing.Count == 0
            ? DeviceResult<string>.Unreachable($"no files listed for '{entry.Name}'")
            : DeviceResult<string>.Success(Hashes.Sha256Hex(Canonical(listing)));
    }

    public override async Task<DeviceResult<HarvestBatch>> HarvestAsync(DeviceTarget target, HarvestEntry entry,
        IReadOnlyDictionary<string, string> known, CancellationToken ct) {
        if (!entry.Supported) return DeviceResult<HarvestBatch>.Unsupported(entry.UnsupportedNote);
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<HarvestBatch>.Unreachable("ios ssh not configured");
        var listing = await ListingAsync(conn, target.Package, entry, ct);
        if (listing.Count == 0) return DeviceResult<HarvestBatch>.Unreachable($"no files listed for '{entry.Name}'");

        var items = new List<HarvestItem>();
        string contentType = entry.Name switch {
            HarvestEntries.Textures => "image/png",
            HarvestEntries.PackageManifest => "application/xml",
            _ => "application/octet-stream"
        };

        int failedPulls = 0;
        foreach ((string name, RemoteFile file) in listing) {
            if (known.TryGetValue(name, out string? have) && string.Equals(have, file.Sha, StringComparison.Ordinal))
                continue;
            byte[]? bytes = await conn.PullBytesAsync(file.Path, ct);
            if (bytes is not null) {
                items.Add(new HarvestItem(name, bytes, contentType));
            } else {
                failedPulls++;
                logger.LogWarning("ios harvest: pull failed for '{Entry}' file {Path}", entry.Name, file.Path);
            }
        }

        return DeviceResult<HarvestBatch>.Success(new HarvestBatch(items, [.. listing.Keys], true, failedPulls));
    }

    private readonly record struct RemoteFile(string Sha, string Path);

    private static string Canonical(IReadOnlyDictionary<string, RemoteFile> listing) =>
        string.Join('\n', listing.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value.Sha}"));

    private async Task<IReadOnlyDictionary<string, RemoteFile>> ListingAsync(SshDeviceConnection conn, string bundleId,
        HarvestEntry entry, CancellationToken ct) {
        string find = entry.Name switch {
            HarvestEntries.AppBinary => "exe=\"$app/$(basename \"$app\" .app)\"; [ -f \"$exe\" ] && printf '%s\\n' \"$exe\"",
            HarvestEntries.Meshes => "find \"$app\" \\( -iname '*.rpo' -o -iname '*.rpoz' \\) 2>/dev/null",
            HarvestEntries.Textures => "find \"$app\" -iname '*.png' 2>/dev/null",
            HarvestEntries.PackageManifest => "[ -f \"$app/Info.plist\" ] && printf '%s\\n' \"$app/Info.plist\"",
            _ => ""
        };
        if (find.Length == 0) return new Dictionary<string, RemoteFile>(StringComparer.Ordinal);

        var listing = new Dictionary<string, RemoteFile>(StringComparer.Ordinal);
        foreach (string hasher in HashCommands) {
            var r = await conn.ShellAsync(
                DeviceShell.LocateIosApp(bundleId) + $"{find} | tr '\\n' '\\0' | xargs -0 {hasher} 2>/dev/null", ct);
            listing = Parse(r.Stdout);
            if (listing.Count > 0) return listing;
        }

        logger.LogWarning("ios harvest: no content hasher for '{Entry}', falling back to size+mtime", entry.Name);
        foreach (string fmt in StatFormats) {
            var r = await conn.ShellAsync(
                DeviceShell.LocateIosApp(bundleId) + $"{find} | tr '\\n' '\\0' | xargs -0 stat {fmt} 2>/dev/null", ct);
            listing = Parse(r.Stdout);
            if (listing.Count > 0) return listing;
        }

        return listing;
    }

    private static readonly string[] HashCommands = ["sha256sum", "shasum -a 256"];
    private static readonly string[] StatFormats = ["-c '%s-%Y %n'", "-f '%z-%m %N'"];

    private static Dictionary<string, RemoteFile> Parse(string output) {
        var map = new Dictionary<string, RemoteFile>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            int split = line.IndexOf(' ');
            if (split <= 0) continue;
            string sha = line[..split];
            string path = line[(split + 1)..].Trim().Trim('"');
            if (!path.StartsWith('/')) continue;
            string leaf = path[(path.LastIndexOf('/') + 1)..];
            int dot = leaf.LastIndexOf('.');
            string name = dot > 0 ? leaf[..dot] : leaf;
            map[name] = new RemoteFile(sha, path);
        }

        return map;
    }

    public override Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) =>
        new IosDeviceProbe(runner, target.Target, target.Package).ProbeAsync(ct);

    public override async Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath)) {
            WarnNoSshOnce();
            return DeviceResult.Unreachable(NoSshNote);
        }

        try {
            string bundle = target.Package;
            string? proc = string.IsNullOrEmpty(config.IosAppProcessName) ? "Egg, Inc." : config.IosAppProcessName;

            if (string.IsNullOrEmpty(config.IosRestartCommand)) {
                bool unlocked = await IosEnsureUnlockedAsync(ct);
                if (!unlocked)
                    logger.LogWarning("device capture: {Id} could not confirm unlock; launching anyway", target.Id);
            }

            string remote = string.IsNullOrEmpty(config.IosRestartCommand)
                ? "/bin/sh -c '" +
                  "for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; sleep 1; " +
                  $"uiopen --bundleid {bundle} 2>&1 | sed \"s/^/diag uiopen: /\"; " +
                  "sleep 3; echo diag ps-after:; " +
                  "if ps ax 2>/dev/null | grep -i egg | grep -v grep; then echo \"diag RESULT: running\"; else echo \"diag RESULT: NOT running\"; fi" +
                  "'"
                : config.IosRestartCommand.Replace("{bundle}", bundle).Replace("{proc}", proc);
            if (connections.Ios() is not { } conn) return DeviceResult.Unreachable("ios ssh not configured");
            var r = await conn.ShellAsync(remote, ct);
            string diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
            bool launched = r.Stdout.Contains("diag RESULT: running");
            logger.LogInformation("device capture: {Id} ios restart (running-after={Ok}): {Diag}", target.Id, launched,
                diag);
            return r.ExitCode == 0
                ? DeviceResult.Success($"{(launched ? "running" : "NOT running - see diag")}: {diag}")
                : DeviceResult.Error(diag);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} app restart failed (non-fatal)", target.Id);
            return DeviceResult.Error(ex.Message);
        }
    }

    public override async Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath)) {
            WarnNoSshOnce();
            return DeviceResult.Unreachable(NoSshNote);
        }

        await KillAppAsync(target, ct);
        (bool ok, string? note) = await IosSendCmdAsync("lock", ct);
        return ok ? DeviceResult.Success("app killed + locked") : DeviceResult.Error($"lock failed: {note}");
    }

    public override async Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath)) {
            WarnNoSshOnce();
            return DeviceResult.Unreachable(NoSshNote);
        }

        bool unlocked = await IosEnsureUnlockedAsync(ct);
        return unlocked ? DeviceResult.Success("unlocked") : DeviceResult.Error("could not confirm unlock");
    }

    public override async Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) {
        if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath)) {
            WarnNoSshOnce();
            return DeviceResult.Unreachable(NoSshNote);
        }

        if (connections.Ios() is not { } conn) return DeviceResult.Unreachable("ios ssh not configured");
        const string remote =
            "/bin/sh -c 'for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | " +
            "while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; echo killed'";
        await conn.ShellAsync(remote, ct);
        return DeviceResult.Success("killed");
    }

    public override async Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target,
        string scriptBody, string? addrOffset, CancellationToken ct) {
        if (connections.Ios(target.Target) is not { } conn)
            return DeviceResult<ParticleCaptureModel.Model>.Unreachable("ios ssh not configured");
        var model = await new IosParticleCapturer(conn, scriptBody, addrOffset).CaptureAsync(ct);
        return model is null
            ? DeviceResult<ParticleCaptureModel.Model>.Error("particle capture returned no model")
            : DeviceResult<ParticleCaptureModel.Model>.Success(model.Value);
    }

    private async Task<bool?> IosLockstateAsync(CancellationToken ct) {
        if (connections.Ios() is not { } conn) return null;
        var r = await conn.ShellAsync("lockstate", ct);
        return r.Stdout.Contains("locked=1")
            ? true
            : r.Stdout.Contains("locked=0")
                ? false
                : r.ExitCode switch { 10 => true, 0 => false, _ => null };
    }

    private async Task<(bool Ok, string? Note)> IosSendCmdAsync(string cmd, CancellationToken ct) {
        if (connections.Ios() is not { } conn) return (false, "ios ssh not configured");
        string remote = $"/bin/sh -c 'printf %s {cmd} > /tmp/ehp.cmd; chmod 666 /tmp/ehp.cmd; echo sent'";
        var r = await conn.ShellAsync(remote, ct);
        return r.ExitCode == 0
            ? (true, null)
            : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    private async Task<bool> IosEnsureUnlockedAsync(CancellationToken ct, int maxTries = 3) {
        for (int i = 0; i < maxTries; i++) {
            bool? locked = await IosLockstateAsync(ct);
            if (locked == false) return true;
            await IosSendCmdAsync("unlock", ct);
            try {
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
            } catch (OperationCanceledException) {
                return false;
            }
        }

        return await IosLockstateAsync(ct) == false;
    }
}
