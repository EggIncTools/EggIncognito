using System.Text;
using EggIncognito.Core;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Data.Services;

public sealed record HarvestOutcome(string Status, string Revision, string? Note, int Changed, int Skipped,
    int Failed);

public sealed class DeviceHarvester(
    IDevicePlatforms platforms,
    DeviceAssetStore assets,
    DeviceStateStore states,
    DeviceJobStore jobs,
    GameBinaryStore binaries,
    ApkStore apks,
    ILogger<DeviceHarvester> logger) {
    private const string FingerprintPrefix = DeviceAssetStore.FingerprintPrefix;

    public async Task<HarvestOutcome> RunAsync(DeviceTarget target, bool force, CancellationToken ct) {
        var platform = platforms.For(target.Platform);
        var probe = await platform.ProbeAsync(target, ct);
        var observed = new DeviceRevision(target.Platform, target.Package, probe.InstalledAppVersion,
            probe.InstalledBuild, null);
        var state = await states.ObserveAsync(target.Id, observed, ct);

        if (!probe.Reachable) {
            await states.FinishAsync(target.Id, HarvestStatus.Unreachable, probe.Note ?? "device unreachable",
                state.Revision, ct);
            return new HarvestOutcome(HarvestStatus.Unreachable, state.Revision, probe.Note, 0, 0, 0);
        }

        if (!force && string.Equals(state.HarvestedRevision, state.Revision, StringComparison.Ordinal)) {
            await states.FinishAsync(target.Id, HarvestStatus.Ok, "revision unchanged", state.Revision, ct);
            return new HarvestOutcome(HarvestStatus.Ok, state.Revision, "revision unchanged", 0, 0, 0);
        }

        var job = await jobs.TryStartAsync(target.Id, DeviceJobKinds.Harvest, "agent",
            $"harvesting revision {state.Revision[..12]}", ct);
        if (job is null) {
            return new HarvestOutcome(HarvestStatus.Failed, state.Revision,
                "another job is already running on this device", 0, 0, 0);
        }

        int changed = 0, skipped = 0, failed = 0;

        foreach (var entry in platform.Manifest()) {
            if (!entry.Supported) {
                await jobs.LineAsync(job, entry.Name, "unsupported", entry.UnsupportedNote, 0, null, ct);
                skipped++;
                continue;
            }

            var fp = await platform.FingerprintAsync(target, entry, ct);
            string fpName = FingerprintPrefix + entry.Name;
            if (fp is { Ok: true, Value: { Length: > 0 } value } && !force) {
                var stored = await assets.GetAsync(DeviceAssetKinds.Manifest, fpName, target.Platform, ct);
                if (stored is not null
                    && string.Equals(Text(await assets.BytesAsync(stored, ct)), value, StringComparison.Ordinal)) {
                    await jobs.LineAsync(job, entry.Name, "unchanged", null, 0, value, ct);
                    skipped++;
                    continue;
                }
            }

            var known = await assets.ShaManifestAsync(target.Platform, entry.Kind, ct);
            var batch = await platform.HarvestAsync(target, entry, known, ct);
            if (batch is not { Ok: true, Value: { } pulled }) {
                await jobs.LineAsync(job, entry.Name, "failed", batch.Note, 0, null, ct);
                failed++;
                continue;
            }

            int wrote = 0;
            long bytes = 0;
            foreach (var item in pulled.Items) {
                bool stored = entry.Kind == DeviceAssetKinds.Binary
                    ? await StoreBinaryAsync(target.Platform, state.AppVersion, item, ct)
                    : await assets.PutAsync(target.Platform, entry.Kind, item.Name, item.Bytes, item.ContentType,
                        state.AppVersion, ct);
                if (entry.Kind == DeviceAssetKinds.Package) await StoreApkAsync(target, state, item, ct);
                if (!stored) continue;
                wrote++;
                bytes += item.Bytes.LongLength;
            }

            if (pulled.FailedPulls > 0) {
                string pullNote = $"{pulled.FailedPulls} of {pulled.Present.Count} pulls failed, {wrote} written";
                await jobs.LineAsync(job, entry.Name, "failed", pullNote, bytes, fp.Value, ct);
                logger.LogWarning("harvest {Device} entry {Entry}: {Note}", target.Id, entry.Name, pullNote);
                failed++;
                continue;
            }

            if (pulled.Authoritative && pulled.Present.Count > 0 && entry.Kind != DeviceAssetKinds.Binary)
                await assets.PruneAsync(target.Platform, entry.Kind, [.. pulled.Present], ct);

            if (fp is { Ok: true, Value: { Length: > 0 } fresh }) {
                await assets.PutAsync(target.Platform, DeviceAssetKinds.Manifest, fpName,
                    Encoding.UTF8.GetBytes(fresh), "text/plain", state.AppVersion, ct);
            }

            string? line = wrote > 0 ? null
                : pulled.Present.Count == 0 ? "nothing found on device"
                : $"{pulled.Items.Count} of {pulled.Present.Count} pulled, all already stored";
            await jobs.LineAsync(job, entry.Name, wrote > 0 ? "updated" : "unchanged", line, bytes, fp.Value, ct);
            if (wrote > 0) changed++; else skipped++;
        }

        string status = failed == 0 ? HarvestStatus.Ok : changed > 0 ? HarvestStatus.Partial : HarvestStatus.Failed;
        string note = $"{changed} updated, {skipped} unchanged, {failed} failed";
        await states.FinishAsync(target.Id, status, note, state.Revision, ct);
        await jobs.FinishAsync(job, status, note,
            new DeviceJobFacts(AppVersion: state.AppVersion, Revision: state.Revision), ct);
        logger.LogInformation("harvest {Device} rev {Revision}: {Note}", target.Id, state.Revision[..12], note);
        return new HarvestOutcome(status, state.Revision, note, changed, skipped, failed);
    }

    private async Task<bool> StoreBinaryAsync(string platform, string? version, HarvestItem item,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(version)) return false;
        string sha = Hashes.Sha256Hex(item.Bytes);
        var img = BinaryImage.Load(item.Bytes);
        var syms = img?.Symbols ?? MachoSymbols.Read(item.Bytes);
        await binaries.PutAsync(platform, version, sha, item.Bytes, syms.Count, syms.Count, "harvest", ct);
        return true;
    }

    private async Task StoreApkAsync(DeviceTarget target, DeviceState state, HarvestItem item,
        CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) return;
        if (state.AppVersion is not { Length: > 0 } appVersion || state.Build is not { Length: > 0 } build) return;
        await apks.PutAsync(target.Platform, target.Package, appVersion, build, ApkStore.SplitLabel(item.Name),
            item.Bytes, target.Id, ct);
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
