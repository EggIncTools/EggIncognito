using System.Text;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallIntegrityStep(
    VirtualDeviceConfig config,
    ModuleFetcher fetcher,
    IDeviceConnectionFactory connections,
    IHostFacts hostFacts,
    IProcessRunner runner) : CookbookStep {
    private const string ZygiskOffSql =
        "--sqlite \"REPLACE INTO settings (key,value) VALUES('zygisk',0)\"";
    private const string ShellSuPolicySql =
        "--sqlite \"REPLACE INTO policies (uid,policy,until,logging,notification) VALUES(2000,2,0,1,1)\"";
    private const string RemoteAdbKey = "/data/local/tmp/egi-adbkey.pub";
    private const string AdbKeysDir = "/data/misc/adb";
    private const string AdbKeysFile = AdbKeysDir + "/adb_keys";
    private const string AuthorizeAdbKey =
        "mkdir -p " + AdbKeysDir + "; touch " + AdbKeysFile + "; "
        + "grep -qxF -f " + RemoteAdbKey + " " + AdbKeysFile + " || cat " + RemoteAdbKey + " >> " + AdbKeysFile + "; "
        + "chown system:shell " + AdbKeysDir + " " + AdbKeysFile + "; "
        + "chmod 750 " + AdbKeysDir + "; chmod 640 " + AdbKeysFile + "; "
        + "rm -f " + RemoteAdbKey;
    private static string NoAdbKeyWarning(VirtualDeviceConfig config) =>
        "no adb public key found at "
        + string.Join(", ", AdbHostKey.Candidates(config.AdbPublicKeyPath,
            Environment.GetEnvironmentVariable("ANDROID_USER_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Distinct(StringComparer.Ordinal))
        + "; the adb server that owns the key may run in another container on the same host network. "
        + "Mount one shared dir at /root/.android in every container with ADB_SERVER_SOCKET, or set "
        + "Devices:Virtual:AdbPublicKeyPath. After PlayIntegrityFix sets ro.adb.secure=1 adbd rejects unauthorized hosts";
    private static readonly string[] MagiskPaths = [
        "/sbin/magisk", "/debug_ramdisk/magisk",
        "/system/etc/init/magisk/magisk", "/data/adb/magisk/magisk"
    ];
    private const string ScanMarker = "egi-scan-done";
    private const string MagiskBinDir = "/data/adb/magisk";
    private const string MagiskSysDir = "/system/etc/init/magisk";
    private const string BusyboxMarker = "egi-busybox-ok";
    private const string UtilMarker = "egi-util-ok";
    private const string MagiskBinProbe =
        "[ -x " + MagiskBinDir + "/busybox ] && echo " + BusyboxMarker + "; "
        + "[ -f " + MagiskBinDir + "/util_functions.sh ] && echo " + UtilMarker + "; "
        + "ls -l " + MagiskBinDir + " 2>&1; "
        + "echo " + ScanMarker;
    private const string MagiskBinSeed =
        "mkdir -p " + MagiskBinDir + "; "
        + "cp -f " + MagiskSysDir + "/* " + MagiskBinDir + "/ 2>/dev/null; "
        + MagiskBinDir + "/busybox unzip -o -j " + MagiskSysDir + "/magisk.apk \"assets/*.sh\" -d " + MagiskBinDir
        + " 2>/dev/null "
        + "|| busybox unzip -o -j " + MagiskSysDir + "/magisk.apk \"assets/*.sh\" -d " + MagiskBinDir + " 2>/dev/null "
        + "|| unzip -o -j " + MagiskSysDir + "/magisk.apk \"assets/*.sh\" -d " + MagiskBinDir + " 2>/dev/null; "
        + "rm -f " + MagiskBinDir + "/magisk.apk; "
        + "chmod 755 " + MagiskBinDir + "/* 2>/dev/null; "
        + "chcon u:object_r:magisk_file:s0 " + MagiskBinDir + " " + MagiskBinDir + "/* 2>/dev/null; "
        + "true";
    public override string Id => DeviceCookbookIds.InstallIntegrity;
    public override string Title => "Install integrity chain";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(CookbookStepAvailability.No("installing the integrity chain is android-only"));
        if (!config.IntegrityEnabled)
            return Task.FromResult(CookbookStepAvailability.No("integrity provisioning is not enabled"));

        return Task.FromResult(CookbookStepAvailability.Ready);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "installing the integrity chain is android-only");
        if (!config.IntegrityEnabled)
            return Skipped(lines, "integrity provisioning is not enabled");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var root = await DeviceRoot.EnsureAsync(conn, runner, target.Target, ct);
        if (!root.Ok)
            return Failed(lines, $"device is not rooted ({root.Detail}); integrity install needs uid=0 (adb root or su)");
        Add($"root: {root.Detail}");

        string? magisk = await MagiskBinaryAsync(conn, root, ct);
        if (magisk is null) {
            return Failed(lines,
                "magisk binary not found; the image has no Magisk bootstrapped into /data "
                + "(rebuild the gapps+magisk image or seed the /data volume). integrity install needs Magisk");
        }

        Add($"magisk binary: {magisk}");
        await PersistAdbAccessAsync(conn, root, magisk, Add, ct);

        var magiskBin = await EnsureMagiskBinAsync(conn, root, Add, ct);
        if (!magiskBin.Ok) {
            return Failed(lines,
                $"MAGISKBIN incomplete at {MagiskBinDir}: missing {magiskBin.Missing}; "
                + $"found: {(magiskBin.Listing.Length > 0 ? magiskBin.Listing : "nothing")}");
        }

        if (config.IntegrityDisableMagiskZygisk) {
            Add("disabling Magisk built-in Zygisk before installing the zygisk provider");
            var zygisk = await conn.ShellAsync(root.Wrap($"{magisk} {ZygiskOffSql}"), ct);
            Add(zygisk.ExitCode == 0
                ? "zygisk setting written"
                : $"zygisk setting write failed (exit {zygisk.ExitCode}): "
                  + DeviceParsing.TrimNote(zygisk.Stderr + zygisk.Stdout));
            if (await RebootAsync(conn, target.Target, Add, ct) is not { Ok: true } after)
                return Failed(lines, "device did not come back rooted after the Zygisk-toggle reboot");
            root = after;
        }

        var installedIds = new List<string>();
        foreach (var spec in config.IntegrityModules) {
            var fetched = await fetcher.ResolveAsync(spec, false, ct);
            if (!fetched.Ok || fetched.Bytes is not { } bytes)
                return Failed(lines, $"could not resolve module '{spec.Name}': {fetched.Error ?? "no bytes"}");

            if (spec.Sha256 is { Length: > 0 } expected) {
                string sha = Hashes.Sha256Hex(bytes);
                if (!sha.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    return Failed(lines, $"checksum mismatch for '{spec.Name}' before install: expected {expected}, got {sha}");
            }

            string version = fetched.Version is { Length: > 0 } v ? $" ({v})" : "";
            string origin = fetched.FromCache ? "cache" : "upstream";
            if (MagiskModules.IdFromZip(bytes) is not { } moduleId)
                return Failed(lines, $"'{spec.Name}' zip carries no module.prop id; not a Magisk module");
            installedIds.Add(moduleId);
            Add($"{spec.Name}{version} [{moduleId}]: {bytes.Length} bytes from {origin}");

            string remote = $"/data/local/tmp/{spec.Name}.zip";
            string? staged = await PushAsync(conn, bytes, $"-{spec.Name}.zip", remote, ct);
            if (staged is null) return Failed(lines, $"could not push module '{spec.Name}' to {remote}");

            var install = await conn.ShellAsync(root.Wrap($"{magisk} --install-module {remote}"), ct);
            await conn.ShellAsync(root.Wrap($"rm -f {remote}"), ct);
            if (install.ExitCode != 0) {
                var binDir = await conn.ShellAsync(root.Wrap($"ls -l {MagiskBinDir} 2>&1"), ct);
                var magiskVer = await conn.ShellAsync(root.Wrap($"{magisk} -V 2>&1"), ct);
                return Failed(lines,
                    $"install of '{spec.Name}' failed: {DeviceParsing.TrimNote(install.Stderr + install.Stdout)}"
                    + $"; magisk -V: {DeviceParsing.TrimNote(magiskVer.Stdout + magiskVer.Stderr)}"
                    + $"; {MagiskBinDir}: {DeviceParsing.TrimNote(binDir.Stdout + binDir.Stderr)}");
            }

            Add($"{spec.Name}: installed");
            if (spec.RebootAfter) {
                if (await RebootAsync(conn, target.Target, Add, ct) is not { Ok: true } after)
                    return Failed(lines, $"device did not come back rooted after installing '{spec.Name}'");
                root = after;
            }
        }

        bool lastRebooted = config.IntegrityModules is { Count: > 0 } m && m[^1].RebootAfter;
        if (!lastRebooted) {
            if (await RebootAsync(conn, target.Target, Add, ct) is not { Ok: true } after)
                return Failed(lines, "device did not come back rooted after the final reboot");
            root = after;
        }

        var verify = await conn.ShellAsync(root.Wrap(MagiskModules.ScanCommand), ct);
        if (!MagiskModules.Ran(verify.Stdout)) {
            return Failed(lines,
                $"integrity verify did not run (exit {verify.ExitCode}): "
                + DeviceParsing.TrimNote(verify.Stderr + verify.Stdout));
        }

        var mods = MagiskModules.Live(MagiskModules.Parse(verify.Stdout));
        string listing = MagiskModules.Describe(mods);
        var missing = installedIds.Where(id => !mods.Exists(m => m.Id == id)).ToList();
        if (missing.Count > 0)
            return Failed(lines, $"not under /data/adb/modules after install: {string.Join(", ", missing)}; have {listing}");

        var dead = mods.Where(m => installedIds.Contains(m.Id) && !m.Ok).ToList();
        if (dead.Count > 0)
            return Failed(lines, $"chain modules landed but are disabled: {MagiskModules.Describe(dead)}");

        Add($"modules live: {listing}");
        return Ok(lines, $"installed {installedIds.Count} module(s): {string.Join(", ", installedIds)}");
    }

    private static async Task<string?> MagiskBinaryAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        foreach (string p in MagiskPaths) {
            var probe = await conn.ShellAsync(root.Wrap($"[ -x {p} ] && echo yes"), ct);
            if (probe.Stdout.Contains("yes", StringComparison.Ordinal)) return p;
        }

        var onPath = await conn.ShellAsync(root.Wrap("command -v magisk"), ct);
        return onPath.Stdout.Trim().Length > 0 ? "magisk" : null;
    }

    private static async Task<(bool Ok, string Missing, string Listing)> MagiskBinStateAsync(
        IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var probe = await conn.ShellAsync(root.Wrap(MagiskBinProbe), ct);
        bool busybox = probe.Stdout.Contains(BusyboxMarker, StringComparison.Ordinal);
        bool util = probe.Stdout.Contains(UtilMarker, StringComparison.Ordinal);

        var missing = new List<string>();
        if (!busybox) missing.Add("busybox (executable)");
        if (!util) missing.Add("util_functions.sh");
        return (busybox && util, string.Join(" + ", missing), DeviceParsing.TrimNote(WithoutMarkers(probe.Stdout)));
    }

    private static async Task<(bool Ok, string Missing, string Listing)> EnsureMagiskBinAsync(
        IDeviceConnection conn, RootAccess root, Action<string> add, CancellationToken ct) {
        var state = await MagiskBinStateAsync(conn, root, ct);
        if (state.Ok) {
            add($"MAGISKBIN already complete at {MagiskBinDir}");
            return state;
        }

        add($"seeding {MagiskBinDir} from {MagiskSysDir} (missing {state.Missing})");
        var seed = await conn.ShellAsync(root.Wrap(MagiskBinSeed), ct);
        if (seed.ExitCode != 0)
            add($"MAGISKBIN seed shell exit {seed.ExitCode}: {DeviceParsing.TrimNote(seed.Stderr + seed.Stdout)}");

        var after = await MagiskBinStateAsync(conn, root, ct);
        if (after.Ok) add($"MAGISKBIN seeded: applets + magisk.apk assets/*.sh in {MagiskBinDir}");
        return after;
    }

    private static string WithoutMarkers(string stdout) =>
        string.Join(" ", stdout.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l != ScanMarker && l != BusyboxMarker && l != UtilMarker));

    private async Task PersistAdbAccessAsync(
        IDeviceConnection conn, RootAccess root, string magisk, Action<string> add, CancellationToken ct) {
        var policy = await conn.ShellAsync(root.Wrap($"{magisk} {ShellSuPolicySql}"), ct);
        add(policy.ExitCode == 0
            ? "Magisk su granted to the shell uid (2000)"
            : $"Magisk su policy write failed (exit {policy.ExitCode}): "
              + DeviceParsing.TrimNote(policy.Stderr + policy.Stdout));

        if (await HostAdbKey.ResolveAsync(hostFacts, config, ct) is not { } resolved) {
            add(NoAdbKeyWarning(config));
            return;
        }

        add($"adb key {AdbHostKey.Label(resolved.Key)} from {resolved.Source}");
        if (await PushAsync(conn, Encoding.ASCII.GetBytes(resolved.Key + "\n"), "-adbkey.pub", RemoteAdbKey, ct) is null) {
            add($"adb key push to {RemoteAdbKey} failed; adbd will reject this host once ro.adb.secure=1");
            return;
        }

        var authorize = await conn.ShellAsync(root.Wrap(AuthorizeAdbKey), ct);
        add(authorize.ExitCode == 0
            ? "adb key authorized"
            : $"adb key authorize failed (exit {authorize.ExitCode}): "
              + DeviceParsing.TrimNote(authorize.Stderr + authorize.Stdout));
    }

    private static async Task<string?> PushAsync(
        IDeviceConnection conn, byte[] bytes, string localSuffix, string remote, CancellationToken ct) {
        string local = DeviceShell.NewTempPath(localSuffix);
        try {
            await File.WriteAllBytesAsync(local, bytes, ct);
            return await conn.PushFileAsync(local, remote, ct) ? remote : null;
        } finally {
            DeviceShell.TryDelete(local);
        }
    }

    private Task<RootAccess?> RebootAsync(IDeviceConnection conn, string serial, Action<string> add, CancellationToken ct) =>
        DeviceReboot.RebootAsync(conn, runner, serial,
            TimeSpan.FromSeconds(Math.Max(60, config.IntegrityBootTimeoutSeconds)), add, ct);
}
