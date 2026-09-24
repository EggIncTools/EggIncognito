using System.Globalization;
using System.Text;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class ActivateIntegrityStep(
    VirtualDeviceConfig config,
    IDeviceFleet fleet,
    IDeviceConnectionFactory connections,
    IProcessRunner runner,
    IntegrityAssets assets,
    TimeProvider time) : CookbookStep {
    private static readonly TimeSpan CheckinWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CheckinPoll = TimeSpan.FromSeconds(10);
    private const string PifLogCommand = "logcat -d -s PIF/Native 2>/dev/null | tail -n 12";

    public override string Id => DeviceCookbookIds.ActivateIntegrity;
    public override string Title => "Activate integrity chain";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(CookbookStepAvailability.No("activating the integrity chain is android-only"));
        if (!config.IntegrityEnabled)
            return Task.FromResult(CookbookStepAvailability.No("integrity provisioning is not enabled"));

        return Task.FromResult(CookbookStepAvailability.Ready);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "activating the integrity chain is android-only");
        if (!config.IntegrityEnabled)
            return Skipped(lines, "integrity provisioning is not enabled");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        if (!root.Ok)
            return Failed(lines, $"device is not rooted ({root.Detail}); activation needs uid=0");

        var before = await StateAsync(conn, root, ct);
        if (before is null) return Failed(lines, "chain state probe did not run");
        if (!before.BoxPresent)
            return Failed(lines, "Integrity-Box is not installed under /data/adb/modules; run install-integrity first");
        await Add($"before: {before.Describe()}");

        var bundle = await assets.ResolveAsync(false, ct);
        if (!bundle.Ok || bundle.Profile is not { } profile || bundle.PifPropText is not { } pifProp
            || bundle.KeyboxXml is not { } keybox || bundle.PatchDate is not { } patchDate)
            return Failed(lines, bundle.Error ?? "integrity assets did not resolve");
        await Add($"identity {profile.Model} {profile.Fingerprint} (expires {profile.Expiry?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown"})");
        await Add($"keybox {bundle.KeyboxSource}, {bundle.KeyboxSerials.Count} certs, {bundle.KeyboxNote}");
        foreach (string warning in bundle.Warnings) await Add(warning);

        string fingerprintBefore = await FingerprintAsync(conn, root, ct);

        var staged = await StageAsync(conn, pifProp, keybox,
            IntegrityChain.TargetsText(target.Package), IntegrityChain.SecurityPatchText(patchDate), ct);
        if (staged is not null) return Failed(lines, staged);

        var apply = await conn.ShellAsync(root.Wrap(IntegrityChain.ApplyCommand), ct);
        await conn.ShellAsync($"rm -rf {IntegrityChain.StageDir}", ct);
        if (!apply.Stdout.Contains("applied=1", StringComparison.Ordinal))
            return Failed(lines, $"identity files did not apply: {DeviceParsing.TrimNote(apply.Stderr + apply.Stdout)}");
        await Add($"identity, keybox, targets and patch level applied; {target.Package} listed for attestation");

        var after = await StateAsync(conn, root, ct);
        if (after is null || !after.Activated)
            return Failed(lines, $"chain still inert after applying: {after?.Describe() ?? "probe did not run"}");
        await Add($"after: {after.Describe()}");

        bool changed = !string.Equals(fingerprintBefore, await FingerprintAsync(conn, root, ct), StringComparison.Ordinal);
        if (!changed && before.Activated && await GsfIdentity.ReadAsync(conn, root, ct) is { } existing) {
            await Add($"chain unchanged and gms already checked in (gsf id {existing}); no reset");
            await ReportPifAsync(conn, Add, ct);
            return Ok(lines, after.Describe());
        }

        bool virtualDevice = await IsVirtualAsync(target.Id, ct);
        var reset = await conn.ShellAsync(root.Wrap(IntegrityChain.ResetPlayCommand(virtualDevice)), ct);
        if (!reset.Stdout.Contains("reset=1", StringComparison.Ordinal))
            return Failed(lines, $"play reset did not complete: {DeviceParsing.TrimNote(reset.Stderr + reset.Stdout)}");
        await Add(virtualDevice
            ? "play store + gsf cleared; rebooting so gms checks in under the spoofed identity"
            : "play store cleared; gms restarted under the spoofed identity");

        if (virtualDevice) {
            var bootTimeout = TimeSpan.FromSeconds(Math.Max(60, config.IntegrityBootTimeoutSeconds));
            if (await DeviceReboot.RebootAsync(conn, runner, time, target.Target, bootTimeout, Add, ct) is not { Ok: true } rebooted)
                return Failed(lines, "device did not come back rooted after the activation reboot");
            root = rebooted;

            string? gsf = await GsfIdentity.WaitAsync(conn, root, CheckinWait, CheckinPoll, Add, ct);
            if (gsf is null) {
                return Failed(lines,
                    $"gms did not check in within {CheckinWait.TotalMinutes:F0} min of boot; no android_id in gservices. "
                    + "Run integrity-audit and read its checkin log");
            }

            await Add($"gms checked in, gsf id {gsf}");
        }

        await ReportPifAsync(conn, Add, ct);
        return Ok(lines, after.Describe());
    }

    private static async Task<string?> StageAsync(
        IDeviceConnection conn, string pifProp, string keybox, string targets, string patch, CancellationToken ct) {
        await conn.ShellAsync($"rm -rf {IntegrityChain.StageDir}; mkdir -p {IntegrityChain.StageDir}", ct);
        (string Name, string Text)[] files = [
            (PifProp.FileName, pifProp),
            (IntegrityChain.KeyboxFileName, keybox),
            (IntegrityChain.TargetsFileName, targets),
            (IntegrityChain.SecurityPatchFileName, patch)
        ];
        foreach ((string name, string text) in files) {
            string local = DeviceShell.NewTempPath("-" + name);
            try {
                await File.WriteAllBytesAsync(local, new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n")), ct);
                if (!await conn.PushFileAsync(local, $"{IntegrityChain.StageDir}/{name}", ct))
                    return $"could not push {name} to {IntegrityChain.StageDir}";
            } finally {
                DeviceShell.TryDelete(local);
            }
        }

        return null;
    }

    private static async Task<string> FingerprintAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(IntegrityChain.FingerprintCommand), ct);
        return r.Stdout.Trim();
    }

    private static async Task ReportPifAsync(IDeviceConnection conn, Func<string, Task> add, CancellationToken ct) {
        var pif = await conn.ShellAsync(PifLogCommand, ct);
        var spoofed = pif.Stdout.Split('\n').Where(l => l.Contains("Spoofing", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf("Spoofing", StringComparison.Ordinal) + "Spoofing ".Length)..].Trim()).ToList();
        await add(spoofed.Count > 0
            ? $"pif injected into gms: {string.Join("; ", spoofed.Distinct())}"
            : "no PIF/Native lines in logcat yet; DroidGuard spawns on demand, re-check after launching the app");
    }

    private static async Task<IntegrityChainState?> StateAsync(
        IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(IntegrityChain.StateCommand), ct);
        return IntegrityChain.Ran(r.Stdout) ? IntegrityChain.Parse(r.Stdout) : null;
    }

    private async Task<bool> IsVirtualAsync(string deviceId, CancellationToken ct) {
        var entry = (await fleet.EnabledAsync(ct)).FirstOrDefault(d =>
            string.Equals(d.Id, deviceId, StringComparison.Ordinal));
        return entry is not null && DeviceOrigins.IsVirtual(entry.Origin);
    }
}
