using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class VirtualDeviceReadinessProbe(
    IDeviceConnectionFactory connections,
    VirtualDeviceConfig config,
    ProxyReachProbe proxyReach,
    CaptureCaSource captureCa) {
    private const string SystemCaCerts = "/system/etc/security/cacerts/";

    public async Task<DeviceReadiness> ProbeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) {
            var na = new ReadinessCheck(false, "android only");
            return new DeviceReadiness(na, na, na, na, na, na, na);
        }

        if (connections.For(target) is not { } conn) {
            var no = new ReadinessCheck(false, "no connection");
            return new DeviceReadiness(no, no, no, no, no, no, no);
        }

        if (await OfflineAsync(conn, ct) is { } offline)
            return new DeviceReadiness(offline, offline, offline, offline, offline, offline, offline);

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        var installed = await InstalledAsync(conn, target.Package, ct);
        var play = await GooglePlayAsync(conn, root, ct);
        var rooted = RootedCheck(root);
        var integrity = await IntegrityAsync(conn, root, ct);
        var launched = await LaunchedAsync(conn, target.Package, ct);
        var ca = await CaptureCaAsync(conn, root, ct);
        var proxy = await ProxyReachableAsync(target, ct);
        return new DeviceReadiness(installed, ca, play, rooted, integrity, launched, proxy);
    }

    private async Task<ReadinessCheck> ProxyReachableAsync(DeviceTarget target, CancellationToken ct) {
        var reach = await proxyReach.CheckAsync(target.Id, ct);
        return new ReadinessCheck(reach.Ok, reach.Note);
    }

    private static async Task<ReadinessCheck?> OfflineAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync("getprop sys.boot_completed", ct);
        if (r.ExitCode != 0 || r.Stdout.Trim().Length == 0) return new ReadinessCheck(false, "device unreachable");
        return r.Stdout.Trim() == "1" ? null : new ReadinessCheck(false, "device is booting");
    }

    private static async Task<ReadinessCheck> InstalledAsync(IDeviceConnection conn, string package, CancellationToken ct) {
        var r = await conn.ShellAsync($"pm path {package}", ct);
        return r.ExitCode == 0 && r.Stdout.Contains("package:", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "not installed");
    }

    private async Task<ReadinessCheck> GooglePlayAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync($"pm list packages {config.GmsPackage}", ct);
        if (!r.Stdout.Contains("package:", StringComparison.Ordinal))
            return new ReadinessCheck(false, "no Play services, needs a gapps image");

        if (await GsfIdentity.ReadAsync(conn, root, ct) is { } id) return new ReadinessCheck(true, $"gsf id {id}");
        return new ReadinessCheck(false, root.Ok
            ? "Play services installed but no android_id in gservices yet; check-in has not completed"
            : "no root, so the gservices android_id cannot be read from the shell uid");
    }

    private static ReadinessCheck RootedCheck(RootAccess root) =>
        root.Ok ? new ReadinessCheck(true) : new ReadinessCheck(false, root.Detail);

    private async Task<ReadinessCheck> IntegrityAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(MagiskModules.ScanCommand), ct);
        if (!MagiskModules.Ran(r.Stdout))
            return new ReadinessCheck(false, $"module scan did not run: {DeviceParsing.TrimNote(r.Stderr + r.Stdout)}");

        var mods = MagiskModules.Live(MagiskModules.Parse(r.Stdout));
        if (mods.Count == 0) {
            var seed = IntegritySeed.Parse((await conn.ShellAsync(IntegritySeed.ProbeCommand, ct)).Stdout);
            return seed.SeededImage && seed.State != IntegritySeed.StateDone
                ? new ReadinessCheck(false, "first-boot seed still installing the chain")
                : new ReadinessCheck(false, "no module in /data/adb/modules");
        }

        string listing = MagiskModules.Describe(mods);
        if (mods.Exists(m => !m.Ok)) return new ReadinessCheck(false, $"disabled module: {listing}");

        int want = config.IntegrityModules.Count;
        if (mods.Count < want)
            return new ReadinessCheck(false, $"{mods.Count} of {want} chain modules present: {listing}");

        var chain = await ChainStateAsync(conn, root, ct);
        if (chain is null) return new ReadinessCheck(false, $"{listing}; chain state probe did not run");
        return chain.Activated
            ? new ReadinessCheck(true, $"{listing}; {chain.Describe()}")
            : new ReadinessCheck(false, $"installed but not activated ({chain.Describe()}); run activate-integrity");
    }

    public async Task<(bool ModulesLive, IntegrityChainState? Chain)> ChainAsync(DeviceTarget target, CancellationToken ct) {
        if (connections.For(target) is not { } conn) return (false, null);
        var root = await DeviceRoot.ProbeAsync(conn, ct);
        var scan = await conn.ShellAsync(root.Wrap(MagiskModules.ScanCommand), ct);
        if (!MagiskModules.Ran(scan.Stdout)) return (false, null);
        var mods = MagiskModules.Live(MagiskModules.Parse(scan.Stdout));
        bool live = mods.Count >= config.IntegrityModules.Count && mods.TrueForAll(m => m.Ok);
        return (live, await ChainStateAsync(conn, root, ct));
    }

    private static async Task<IntegrityChainState?> ChainStateAsync(
        IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(IntegrityChain.StateCommand), ct);
        return IntegrityChain.Ran(r.Stdout) ? IntegrityChain.Parse(r.Stdout) : null;
    }

    private static async Task<ReadinessCheck> LaunchedAsync(IDeviceConnection conn, string package, CancellationToken ct) {
        var r = await conn.ShellAsync($"pidof {package}", ct);
        if (r.Stdout.Trim().Length == 0) return new ReadinessCheck(false, "not running");

        var front = await DeviceForeground.ReadAsync(conn, ct);
        return front.Is(DeviceForeground.PlayStorePackage)
            ? new ReadinessCheck(false, DeviceForeground.PlayBlockNote)
            : new ReadinessCheck(true);
    }

    private async Task<ReadinessCheck> CaptureCaAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        if (await captureCa.ResolveAsync(ct) is not { AndroidTrustFile: { } file })
            return new ReadinessCheck(false, "no capture CA minted");

        var r = await conn.ShellAsync(root.WrapMountMaster($"[ -s {SystemCaCerts}{file} ] && echo present"), ct);
        return r.Stdout.Contains("present", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "not in the trust store");
    }
}
