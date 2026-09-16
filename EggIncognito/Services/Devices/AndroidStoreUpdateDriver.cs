using System.Collections.Concurrent;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class AndroidStoreUpdateDriver(
    IProcessRunner runner,
    IDeviceConnectionFactory connections,
    AndroidStoreUpdateDriver.Options opts,
    AndroidStoreCatalog catalog,
    KnownVersionRecorder knownVersions,
    IEnumerable<IDeviceUiDriver> uiDrivers,
    DeviceActivity activity,
    ILogger<AndroidStoreUpdateDriver> logger) : IStoreUpdateDriver {
    private readonly IDeviceUiDriver? _ui = uiDrivers.FirstOrDefault(u => Platforms.Matches(u.Platform, Platforms.Android));

    private readonly ConcurrentDictionary<string, EntryState> _entry = new(StringComparer.OrdinalIgnoreCase);

    public string Platform => Platforms.Android;
    public string StoreName => "Play";

    public async Task<string?> ReadInstalledAsync(DeviceTarget target, CancellationToken ct) {
        var probe = await new AdbDeviceProbe(runner, target.Target, target.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }

    public async Task PrepareAsync(DeviceTarget target, CancellationToken ct) {
        try {
            var conn = connections.For(target)!;
            _entry[target.Id] = await ReadEntryStateAsync(conn, ct);
            await conn.ShellAsync("input keyevent KEYCODE_WAKEUP", ct);
            await conn.ShellAsync("wm dismiss-keyguard", ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device {Id} wake best-effort failed", target.Id);
        }
    }

    private static async Task<EntryState> ReadEntryStateAsync(IDeviceConnection conn, CancellationToken ct) {
        var state = await conn.ShellAsync(AndroidUiDriver.ScreenStateCommand, ct);
        var front = await DeviceForeground.ReadAsync(conn, ct);
        return new EntryState(AndroidUiDriver.ParseScreenState(state.Stdout).Awake, front.Package);
    }

    public async Task<StoreProbeOutcome> ProbeStoreAsync(
        DeviceTarget target, string installed, Action<string>? progress, CancellationToken ct) {
        string? latest = await catalog.LatestVersionAsync(target.Package, opts.LookupCountry, opts.LookupLocale, ct);
        bool storeAhead = false;
        if (latest is not null) {
            await knownVersions.RecordAsync(Platforms.Android, latest, "play-scrape", ct);
            storeAhead = DeviceParsing.CompareVersions(latest, installed) > 0;
            progress?.Invoke(storeAhead
                ? $"Play lists {latest} (installed {installed}); opening the Play page…"
                : $"Play lists {latest}; version name matches, asking the device…");
        }

        return await ProbeViaUiAsync(target, latest, storeAhead, progress, ct);
    }

    private async Task<StoreProbeOutcome> ProbeViaUiAsync(
        DeviceTarget target, string? latest, bool storeAhead, Action<string>? progress, CancellationToken ct) {
        string deepLink = opts.DriveTemplate.Replace("{package}", target.Package);
        var conn = connections.For(target)!;
        var open = await conn.ShellAsync(deepLink, ct);
        if (open.ExitCode != 0)
            return new StoreProbeOutcome(StoreAvailability.Unknown, latest,
                $"open page: {DeviceParsing.TrimNote(open.Stderr + open.Stdout)}");

        progress?.Invoke("Play page open; locating Update button…");
        for (int tries = 0; tries < 3; tries++) {
            int wait = tries == 0 ? opts.UiFirstWaitSeconds : opts.UiRetryWaitSeconds;
            try {
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            } catch (OperationCanceledException) {
                break;
            }

            var tree = await DumpAsync(target, ct);
            if (tree is null) continue;

            if (FindUpdateNode(tree) is not null)
                return new StoreProbeOutcome(StoreAvailability.UpdateOffered, latest, null);

            if (HasButton(tree, "Open") || HasButton(tree, "Uninstall")) {
                if (AdvertisesUpdate(tree))
                    return new StoreProbeOutcome(StoreAvailability.ManualNeeded, latest,
                        "Play advertises an update but no auto-tappable Update button (major update?); needs manual update");

                return storeAhead
                    ? new StoreProbeOutcome(StoreAvailability.ManualNeeded, latest,
                        $"Play lists {latest} but this device has no Update button yet (staged rollout); needs manual update")
                    : new StoreProbeOutcome(StoreAvailability.UpToDate, latest,
                        "no Update button on the Play page (already current, or update not yet offered)");
            }
        }

        return new StoreProbeOutcome(StoreAvailability.Unknown, latest,
            "Play page did not load an Update/Open/Uninstall button (store may be offline or slow)");
    }

    public async Task<TriggerOutcome> TriggerInstallAsync(
        DeviceTarget target, Action<string>? progress, CancellationToken ct) {
        var tree = await DumpAsync(target, ct);
        if (tree is null) return new TriggerOutcome(false, "could not dump Play UI");

        if (FindUpdateNode(tree) is not { } node)
            return new TriggerOutcome(false, "no Update button on the Play page");

        int x = node.Bounds.CenterX, y = node.Bounds.CenterY;
        progress?.Invoke("tapping Update…");
        var tap = await _ui!.TapPointAsync(target, x, y, ct);
        if (!tap.Ok)
            return new TriggerOutcome(false, tap.Note);
        logger.LogInformation("device check-update: {Id} android tapped Update at {X},{Y}", target.Id, x, y);
        return new TriggerOutcome(true, null);
    }

    public async Task<bool> ProbeInstallCompleteAsync(DeviceTarget target, CancellationToken ct) {
        var tree = await DumpAsync(target, ct);
        if (tree is null) return false;
        return (HasButton(tree, "Play") || HasButton(tree, "Open"))
               && HasButton(tree, "Uninstall")
               && FindUpdateNode(tree) is null;
    }

    public async Task CleanupAsync(DeviceTarget target, CancellationToken ct) {
        _entry.TryRemove(target.Id, out var entry);
        bool busy = activity.IsBusy(target.Id);
        try {
            var conn = connections.For(target)!;
            if (Restorable(entry.Package) is { } pkg)
                await conn.ShellAsync($"monkey -p {pkg} -c android.intent.category.LAUNCHER 1", ct);
            else if (!busy)
                await conn.ShellAsync("input keyevent KEYCODE_HOME", ct);

            if (busy) return;
            if (entry.Awake) return;
            await conn.ShellAsync("input keyevent KEYCODE_SLEEP", ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device {Id} screen-restore best-effort failed", target.Id);
        }
    }

    private static string? Restorable(string? package) {
        if (string.IsNullOrWhiteSpace(package)) return null;
        if (package.Equals(DeviceForeground.PlayStorePackage, StringComparison.OrdinalIgnoreCase)) return null;
        if (package.Equals("com.android.systemui", StringComparison.OrdinalIgnoreCase)) return null;
        if (package.Contains("launcher", StringComparison.OrdinalIgnoreCase)) return null;
        return package;
    }

    private readonly record struct EntryState(bool Awake, string? Package);

    private async Task<UiTree?> DumpAsync(DeviceTarget target, CancellationToken ct) {
        if (_ui is null) return null;
        var dump = await _ui.DumpAsync(target, ct);
        return dump.Ok ? dump.Value : null;
    }

    private static UiNode? FindUpdateNode(UiTree t) => UiSelector.Resolve(t, UiSelector.Text("Update"));

    private static bool HasButton(UiTree t, string label) =>
        t.Nodes().Any(n => string.Equals(n.Text, label, StringComparison.Ordinal));

    private static bool AdvertisesUpdate(UiTree t) =>
        t.Nodes().Any(n => n.Text is not null && n.Text.Contains("Update available", StringComparison.OrdinalIgnoreCase));

    public sealed record Options(
        string DriveTemplate,
        int UiFirstWaitSeconds = 3,
        int UiRetryWaitSeconds = 2,
        string? LookupCountry = null,
        string? LookupLocale = null);
}
