using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class StoreUpdateOrchestrator(
    IStoreUpdateDriver driver,
    StoreUpdateOrchestrator.Options opts,
    KnownVersionRecorder knownVersions,
    ILogger logger) : IDeviceStoreChecker {
    public string Platform => driver.Platform;

    public async Task<StoreCheckResult> CheckAndUpdateAsync(
        DeviceTarget device, CancellationToken ct, Func<string, Task>? progress = null) {
        await progress.ReportAsync("reading installed version…");
        string? before = await driver.ReadInstalledAsync(device, ct);
        if (before is null) {
            logger.LogInformation("device check-update: {Id} {Platform} unreachable (no version read)",
                device.Id, driver.Platform);
            return new StoreCheckResult(false, null, null, false, false, "unreachable",
                "device unreachable or no version read");
        }

        try {
            await driver.PrepareAsync(device, ct);

            await progress.ReportAsync($"installed {before}; checking {driver.StoreName}…");
            var probe = await driver.ProbeStoreAsync(device, before, progress, ct);
            switch (probe.Availability) {
                case StoreAvailability.UpToDate:
                    await progress.ReportAsync($"{driver.StoreName} reports {probe.StoreVersion ?? before} current");
                    logger.LogInformation("device check-update: {Id} {Platform} up_to_date (store probe)",
                        device.Id, driver.Platform);
                    return new StoreCheckResult(true, before, before, false, false, "up_to_date",
                        probe.Note ?? $"{driver.StoreName} confirms current");
                case StoreAvailability.ManualNeeded:
                    logger.LogWarning("device check-update: {Id} {Platform} manual_needed: {Note}",
                        device.Id, driver.Platform, probe.Note);
                    return new StoreCheckResult(true, before, before, true, false, "manual_needed", probe.Note);
                case StoreAvailability.UpdateOffered:
                    await progress.ReportAsync(
                        $"update available ({before} -> {probe.StoreVersion ?? "?"}); triggering install…");
                    break;
                default:
                    await progress.ReportAsync($"store version unknown; driving {driver.StoreName} update…");
                    break;
            }

            var trig = await driver.TriggerInstallAsync(device, progress, ct);
            if (!trig.Ok) {
                logger.LogWarning("device check-update: {Id} {Platform} error: {Note}",
                    device.Id, driver.Platform, trig.Note);
                return new StoreCheckResult(true, before, before,
                    probe.Availability == StoreAvailability.UpdateOffered, false, "error", trig.Note);
            }

            await progress.ReportAsync(
                $"install triggered; waiting for {driver.StoreName} to install (up to {opts.PollAttempts * opts.PollSeconds}s)…");
            var result = await StorePoll.WaitForClimbAsync(device.Id, driver.Platform, driver.StoreName, before,
                c => driver.ReadInstalledAsync(device, c), opts.PollSeconds, opts.PollAttempts, logger, progress, ct,
                c => driver.ProbeInstallCompleteAsync(device, c));
            if (result is { Installed: true, InstalledAfter: not null }) {
                await knownVersions.RecordAsync(driver.Platform, result.InstalledAfter, "device-climb",
                    CancellationToken.None);
            }

            return result;
        } finally {
            try {
                await driver.CleanupAsync(device, CancellationToken.None);
            } catch (Exception ex) {
                logger.LogDebug(ex, "device check-update: {Id} {Platform} cleanup best-effort failed",
                    device.Id, driver.Platform);
            }
        }
    }

    public sealed record Options(int PollSeconds, int PollAttempts);
}
