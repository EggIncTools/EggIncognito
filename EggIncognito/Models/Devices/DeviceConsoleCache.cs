using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Models.Devices;

public sealed class DeviceConsoleCache {
    public DateTimeOffset? FetchedAt { get; set; }
    public IReadOnlyList<DeviceCookbookInfo> Cookbooks { get; set; } = [];
    public IReadOnlyList<IslandRow> Islands { get; set; } = [];
    public bool IslandsLoaded { get; set; }
    public int? CurrentAndroidUserId { get; set; }
    public int ScreenW { get; set; }
    public int ScreenH { get; set; }
}
