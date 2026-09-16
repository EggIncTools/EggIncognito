using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class IosStoreUpdateDriver(
    IProcessRunner runner,
    IosStoreUpdateDriver.Options opts,
    IosStoreCatalog catalog,
    KnownVersionRecorder knownVersions,
    DeviceActivity activity,
    ILogger<IosStoreUpdateDriver> logger) : IStoreUpdateDriver {
    public string Platform => Platforms.Ios;
    public string StoreName => "App Store";

    public async Task<string?> ReadInstalledAsync(DeviceTarget target, CancellationToken ct) {
        var probe = await new IosDeviceProbe(runner, target.Target, target.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }

    public async Task PrepareAsync(DeviceTarget target, CancellationToken ct) {
        if (!SshConfigured) return;
        try {
            await SshAsync("killall -9 AppStore 2>/dev/null || true", ct);
            try {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            } catch (OperationCanceledException cancelled) {
                logger.LogDebug(cancelled, "ios prepare settle delay cancelled");
            }
        } catch (Exception ex) {
            logger.LogDebug(ex, "ios prepare best-effort failed");
        }
    }

    public async Task<StoreProbeOutcome> ProbeStoreAsync(
        DeviceTarget target, string installed, Action<string>? progress, CancellationToken ct) {
        string? latest = await catalog.LatestVersionAsync(opts.AppId, opts.LookupCountry, ct);
        if (latest is null)
            return new StoreProbeOutcome(StoreAvailability.Unknown, null, "App Store lookup unavailable");
        await knownVersions.RecordAsync(Platforms.Ios, latest, "itunes-lookup", ct);
        return DeviceParsing.CompareVersions(latest, installed) > 0
            ? new StoreProbeOutcome(StoreAvailability.UpdateOffered, latest, null)
            : new StoreProbeOutcome(StoreAvailability.UpToDate, latest,
                $"App Store latest {latest}; installed {installed} current");
    }

    public async Task<TriggerOutcome> TriggerInstallAsync(
        DeviceTarget target, Action<string>? progress, CancellationToken ct) {
        if (!SshConfigured)
            return new TriggerOutcome(false, "ios ssh not configured (DeviceUpdate:Ios:SshHost/SshKeyPath)");

        progress?.Invoke("checking eggupdate listener…");
        if (await ListenerMissingNoteAsync(ct) is { } missing) return new TriggerOutcome(false, missing);

        await SshAsync($"uiopen itms-apps://itunes.apple.com/app/id{opts.AppId} || true", ct);
        try {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "ios store trigger settle delay cancelled");
        }

        var fire = await SshAsync($"touch {opts.TriggerPath}", ct);
        return fire.ExitCode != 0
            ? new TriggerOutcome(false, $"trigger ssh failed: {DeviceParsing.TrimNote(fire.Stderr + fire.Stdout)}")
            : new TriggerOutcome(true, null);
    }

    public async Task CleanupAsync(DeviceTarget target, CancellationToken ct) {
        if (!SshConfigured) return;
        if (activity.IsBusy(target.Id)) return;
        try {
            await SshAsync("killall -9 AppStore 2>/dev/null || true", ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "ios app-close best-effort failed");
        }
    }

    private async Task<string?> ListenerMissingNoteAsync(CancellationToken ct) {
        var probe = await SshAsync($"test -f {opts.TweakPath} && echo tweak-present || echo tweak-absent", ct);
        if (probe.Stdout.Contains("tweak-present", StringComparison.Ordinal)) return null;
        return probe.Stdout.Contains("tweak-absent", StringComparison.Ordinal)
            ? $"eggupdate tweak not installed at {opts.TweakPath}; nothing watches the trigger file, " +
              "so the App Store update can never fire (rebuild the tweak with EGGUPDATE_ARMED=1 and install it)"
            : $"could not verify the eggupdate tweak: {DeviceParsing.TrimNote(probe.Stderr + probe.Stdout)}";
    }

    private bool SshConfigured => !string.IsNullOrEmpty(opts.SshHost) && !string.IsNullOrEmpty(opts.SshKeyPath);

    private Task<ProcessResult> SshAsync(string remoteCmd, CancellationToken ct) =>
        runner.RunAsync("ssh", new SshEndpoint(opts.SshHost!, opts.SshPort, opts.SshKeyPath!).SshArgs(remoteCmd), ct);

    public sealed record Options(
        string? SshHost,
        string SshPort,
        string? SshKeyPath,
        string TriggerPath,
        string TweakPath,
        string AppId,
        string? LookupCountry);
}
