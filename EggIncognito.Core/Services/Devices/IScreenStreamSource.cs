namespace EggIncognito.Core.Services.Devices;

public interface IScreenStreamSource {
    string Platform { get; }
    Task<string?> StreamAsync(DeviceTarget target, ScreenStreamOptions options, Stream output, CancellationToken ct);
}

public readonly record struct ScreenStreamOptions(int Width, int Height, int Bitrate);
