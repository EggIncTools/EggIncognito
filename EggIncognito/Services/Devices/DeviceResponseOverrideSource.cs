using EggIncognito.Capture;
using EggIncognito.Core.Services;

namespace EggIncognito.Services.Devices;

internal sealed class DeviceResponseOverrideSource(DeviceResponseOverrideStore store, string deviceId)
    : ICaptureResponseSource {
    public ValueTask<CaptureOverrideResponse?> TryAnswerAsync(CaptureOverrideRequest request, CancellationToken ct) {
        if (PathOf(request) is not { } path) return ValueTask.FromResult<CaptureOverrideResponse?>(null);
        if (store.Lookup(deviceId, path) is not { } hit) return ValueTask.FromResult<CaptureOverrideResponse?>(null);
        return ValueTask.FromResult<CaptureOverrideResponse?>(
            new CaptureOverrideResponse(hit.Body, 200, hit.ContentType));
    }

    private static string? PathOf(CaptureOverrideRequest request) {
        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)) return null;
        if (!AuxbrainHosts.IsAuxbrain(request.Host)) return null;
        try {
            string path = EndpointExtractor.NormalizePath($"https://{request.Host}{request.Path}");
            return path.Length == 0 ? null : path;
        } catch (UriFormatException) {
            return null;
        }
    }
}
