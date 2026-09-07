using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class BridgeDeviceFleet(
    BridgeDeviceFleetSource source, ILogger<BridgeDeviceFleet> logger) : IDeviceFleet {
    private string? _lastNote;

    public async Task<IReadOnlyList<DeviceEntry>> EnabledAsync(CancellationToken ct) {
        var fleet = await source.GetAsync(ct);
        if (!fleet.Ok || fleet.Value is not { } value) {
            Note(fleet.Note ?? "the host bridge did not answer");
            return [];
        }

        _lastNote = null;
        return [.. value.Devices.Select(Entry)];
    }

    public Task PersistCapturePortAsync(string deviceId, int port, CancellationToken ct) => Task.CompletedTask;

    public async Task<string?> CaptureHostIpAsync(CancellationToken ct) =>
        (await source.GetAsync(ct)).Value?.CaptureHostIp;

    public async Task<int> CapturePortAsync(string deviceId, CancellationToken ct) {
        var fleet = await source.GetAsync(ct);
        var entry = fleet.Value?.Devices.FirstOrDefault(d =>
            string.Equals(d.Id, deviceId, StringComparison.Ordinal));
        return entry?.CapturePort ?? 0;
    }

    private void Note(string note) {
        if (string.Equals(_lastNote, note, StringComparison.Ordinal)) return;
        _lastNote = note;
        logger.LogWarning("device fleet: the host bridge did not return a fleet: {Note}", note);
    }

    private static DeviceEntry Entry(BridgeFleetEntry d) =>
        new(d.Id, d.Platform, d.Label, d.Target, d.Package, d.Origin, d.CapturePort);
}
