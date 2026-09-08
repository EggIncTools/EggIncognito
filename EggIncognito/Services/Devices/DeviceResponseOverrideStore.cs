using System.Collections.Concurrent;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceResponseOverrideStore : IDeviceResponseOverrides, IDeviceResponseSources {
    private readonly ConcurrentDictionary<string, Dictionary<string, DeviceResponseOverrideBody>> _devices =
        new(StringComparer.Ordinal);

    public Task<DeviceResult> SetAsync(
        string deviceId, IReadOnlyList<DeviceResponseOverride> entries, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(deviceId)) return Task.FromResult(DeviceResult.Error("device id required"));

        var map = new Dictionary<string, DeviceResponseOverrideBody>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            if (string.IsNullOrWhiteSpace(entry.Path))
                return Task.FromResult(DeviceResult.Error("an override entry has an empty path"));

            byte[] body;
            try {
                body = Convert.FromBase64String(entry.BodyBase64);
            } catch (Exception ex) when (ex is FormatException or ArgumentNullException) {
                return Task.FromResult(DeviceResult.Error($"override body for {entry.Path} is not valid base64"));
            }

            string path;
            try {
                path = EndpointExtractor.NormalizePath($"{AuxbrainHosts.Origin}/{entry.Path.TrimStart('/')}");
            } catch (UriFormatException) {
                return Task.FromResult(DeviceResult.Error($"override path {entry.Path} is not a valid path"));
            }

            if (path.Length == 0)
                return Task.FromResult(DeviceResult.Error("an override entry has an empty path"));

            map[path] = new DeviceResponseOverrideBody(body, entry.ContentType);
        }

        _devices[deviceId] = map;
        return Task.FromResult(DeviceResult.Success(
            $"installed {map.Count} override(s) for {deviceId}"));
    }

    public Task<DeviceResult> ClearAsync(string deviceId, CancellationToken ct) {
        _devices.TryRemove(deviceId, out _);
        return Task.FromResult(DeviceResult.Success($"cleared overrides for {deviceId}"));
    }

    public bool Enabled(string deviceId) =>
        _devices.TryGetValue(deviceId, out var map) && map.Count > 0;

    public IReadOnlyCollection<string> Paths(string deviceId) =>
        _devices.TryGetValue(deviceId, out var map) ? [.. map.Keys] : [];

    public ICaptureResponseSource? For(string deviceId) =>
        string.IsNullOrEmpty(deviceId) ? null : new DeviceResponseOverrideSource(this, deviceId);

    internal DeviceResponseOverrideBody? Lookup(string deviceId, string path) =>
        _devices.TryGetValue(deviceId, out var map) && map.TryGetValue(path, out var body) ? body : null;
}
