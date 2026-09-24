using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Data.Models;
using EggIncognito.Services.Devices;

namespace EggIncognito.Services;

public sealed class DeviceMeshProvider(IDeviceResolver resolver, GameAssetProvider assets) {
    private static readonly SearchValues<char> InvalidStemChars = SearchValues.Create(['/', '\\', '.']);

    public async Task<Result> GetGlbAsync(string stem, string? deviceId, CancellationToken ct) {
        if (string.IsNullOrEmpty(stem) || stem.AsSpan().IndexOfAny(InvalidStemChars) >= 0)
            return new Result(false, null, "invalid mesh name", 400);

        string? platform = (await ResolveDeviceAsync(deviceId, ct))?.Platform;
        var result = await assets.GetAsync(new GameAssetKey("mesh", platform, stem), ct);
        return result.Ok
            ? new Result(true, result.Asset.Bytes, null, 200)
            : new Result(false, null, result.Diagnostics ?? "mesh not cached and no asset-source device available",
                503);
    }

    private Task<Device?> ResolveDeviceAsync(string? deviceId, CancellationToken ct) =>
        resolver.ResolveAsync(new DeviceQuery(deviceId), ct);

    public sealed record Result(bool Ok, byte[]? Glb, string? Diagnostics, int Status) {
        [MemberNotNullWhen(true, nameof(Glb))]
        public bool Ok { get; init; } = Ok;
    }
}
