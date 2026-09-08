using System.Collections.Concurrent;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.Devices;

public sealed class RareCraftDismisser(
    DeviceCaptureConfig config,
    IDeviceFleet fleet,
    IDevicePlatforms platforms,
    ILogger<RareCraftDismisser> logger) : IProcessedFlowObserver {
    public const string CraftRoute = ConsumeObservationRecorder.CraftRoute;

    private readonly ConcurrentDictionary<string, UiScreenSize> _screens = new(StringComparer.Ordinal);

    public void OnFlowProcessed(string deviceId, DashboardFlow flow) {
        if (!config.CraftAutoDismiss || string.IsNullOrEmpty(deviceId)) return;
        if (RarityOf(flow) is not { } rarity) return;
        _ = DismissAsync(deviceId, rarity);
    }

    public static ArtifactSpec.Types.Rarity? RarityOf(DashboardFlow flow) {
        if (!string.Equals(flow.Path, CraftRoute, StringComparison.Ordinal)) return null;
        if (flow.ResponseJsonRaw is null) return null;

        CraftArtifactResponse response;
        try {
            response = JsonParser.Default.Parse<CraftArtifactResponse>(flow.ResponseJsonRaw);
        } catch (InvalidProtocolBufferException) {
            return null;
        }

        return response.ItemId != 0 && response.RarityAchieved > ArtifactSpec.Types.Rarity.Common
            ? response.RarityAchieved
            : null;
    }

    public static (int X, int Y) PointFor(UiScreenSize screen, double x, double y) => (
        (int)Math.Round(screen.Width * Math.Clamp(x, 0d, 1d)),
        (int)Math.Round(screen.Height * Math.Clamp(y, 0d, 1d)));

    private async Task DismissAsync(string deviceId, ArtifactSpec.Types.Rarity rarity) {
        try {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(config.CraftDismissDelayMs, 0)));
            var entries = await fleet.EnabledAsync(CancellationToken.None);
            if (entries.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal)) is not { } entry) {
                logger.LogDebug("rare craft dismiss: {Id} is not an enabled device", deviceId);
                return;
            }

            var target = new DeviceTarget(entry.Id, entry.Platform, entry.Target, entry.Package);
            var platform = platforms.For(target.Platform);
            if (await ScreenAsync(platform, target) is not { } screen) return;

            var (x, y) = PointFor(screen, config.CraftDismissX, config.CraftDismissY);
            var tap = await platform.TapPointAsync(target, x, y, CancellationToken.None);
            if (tap.Ok) {
                logger.LogInformation("rare craft dismiss: {Id} crafted {Rarity}, tapped ({X},{Y})", deviceId, rarity, x, y);
            } else {
                logger.LogWarning("rare craft dismiss: {Id} tap at ({X},{Y}) failed: {Note}", deviceId, x, y, tap.Note);
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "rare craft dismiss: {Id} failed", deviceId);
        }
    }

    private async Task<UiScreenSize?> ScreenAsync(IDevicePlatform platform, DeviceTarget target) {
        if (_screens.TryGetValue(target.Id, out var cached)) return cached;

        var probed = await platform.ScreenSizeAsync(target, CancellationToken.None);
        if (!probed.Ok || probed.Value.Width <= 0 || probed.Value.Height <= 0) {
            logger.LogWarning("rare craft dismiss: {Id} screen size unavailable: {Note}", target.Id, probed.Note);
            return null;
        }

        _screens[target.Id] = probed.Value;
        return probed.Value;
    }
}
