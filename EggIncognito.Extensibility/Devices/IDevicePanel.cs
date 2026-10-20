namespace EggIncognito.Core.Services.Devices;

public static class DevicePanelShelves {
    public const string Workflows = "workflows";
    public const string Artifacts = "artifacts";
}

public interface IDevicePanel {
    string Id { get; }
    string Title { get; }
    string Shelf { get; }
    Type ComponentType { get; }
}

public sealed record DevicePanel(string Id, string Title, string Shelf, Type ComponentType) : IDevicePanel;

public sealed record DevicePanelContext(string? DeviceId, int? AndroidUserId);

public sealed record ArtifactPanelContext(string Family, string? Level, string? Rarity, int SuggestedBatch);
