namespace EggIncognito.Core.Services.Devices;

public static class DevicePanelShelves {
    public const string Workflows = "workflows";
    public const string CoverageCell = "coverage-cell";
    public const string CoverageSide = "coverage-side";
    public const string ReplacePrefix = "replace:";

    public static string Replace(string slotId) => ReplacePrefix + slotId;
}

public static class PanelSlots {
    public const string Coverage = "coverage";
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DeviceExtensionServiceAttribute : Attribute;

public interface IDevicePanel {
    string Id { get; }
    string Title { get; }
    string Shelf { get; }
    Type ComponentType { get; }
}

public sealed record DevicePanel(string Id, string Title, string Shelf, Type ComponentType) : IDevicePanel;
