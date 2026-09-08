using EggIncognito.Capture;

namespace EggIncognito.Services.Devices;

public sealed class CompositeDeviceResponseTransforms(IEnumerable<IDeviceResponseTransforms> inner)
    : IDeviceResponseTransforms {
    private readonly IDeviceResponseTransforms[] _inner = [.. inner];

    public ICaptureResponseTransform? For(string deviceId) {
        var transforms = new List<ICaptureResponseTransform>();
        foreach (var candidate in _inner) {
            if (candidate.For(deviceId) is { } transform) transforms.Add(transform);
        }

        return transforms.Count switch {
            0 => null,
            1 => transforms[0],
            _ => new ChainedTransform([.. transforms])
        };
    }

    private sealed class ChainedTransform(ICaptureResponseTransform[] transforms) : ICaptureResponseTransform {
        public async ValueTask<byte[]?> TransformAsync(
            CaptureOverrideRequest request, CaptureUpstreamResponse response, CancellationToken ct) {
            byte[]? replaced = null;
            var current = response;
            foreach (var transform in transforms) {
                if (await transform.TransformAsync(request, current, ct) is not { } body) continue;
                replaced = body;
                current = current with { Body = body };
            }

            return replaced;
        }
    }
}
