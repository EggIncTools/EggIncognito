namespace EggIncognito.Core.Services.Devices;

public static class DevicePanelShelves {
    public const string Workflows = "workflows";
    public const string CoverageHead = "coverage-head";
    public const string CoverageCell = "coverage-cell";
    public const string CoverageSide = "coverage-side";
    public const string CoverageDrops = "coverage-drops";
}

public interface IDevicePanel {
    string Id { get; }
    string Title { get; }
    string Shelf { get; }
    Type ComponentType { get; }
}

public sealed record DevicePanel(string Id, string Title, string Shelf, Type ComponentType) : IDevicePanel;

public sealed record DevicePanelContext(string? DeviceId, int? AndroidUserId);

public sealed record CoverageCellContext(
    DevicePanelContext Device, string Family, string Level, string Rarity, int SuggestedBatch);

public interface ICoverageAnnotations {
    string? RarityHead(string? deviceId, string family, string level, int rarity);
}
