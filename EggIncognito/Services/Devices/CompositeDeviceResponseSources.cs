using EggIncognito.Capture;

namespace EggIncognito.Services.Devices;

public sealed class CompositeDeviceResponseSources(IEnumerable<IDeviceResponseSources> inner)
    : IDeviceResponseSources {
    private readonly IDeviceResponseSources[] _inner = [.. inner];

    public ICaptureResponseSource? For(string deviceId) {
        var sources = new List<ICaptureResponseSource>();
        foreach (var candidate in _inner) {
            if (candidate.For(deviceId) is { } source) sources.Add(source);
        }

        return sources.Count switch {
            0 => null,
            1 => sources[0],
            _ => new FirstAnswerSource([.. sources])
        };
    }

    private sealed class FirstAnswerSource(ICaptureResponseSource[] sources) : ICaptureResponseSource {
        public async ValueTask<CaptureOverrideResponse?> TryAnswerAsync(
            CaptureOverrideRequest request, CancellationToken ct) {
            foreach (var source in sources) {
                if (await source.TryAnswerAsync(request, ct) is { } answer) return answer;
            }

            return null;
        }
    }
}
