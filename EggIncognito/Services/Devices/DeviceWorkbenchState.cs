using EggIdentity.UI;
using EggIncognito.Components.Capture;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceWorkbenchState : WorkbenchStateBase {
    public const string ShelfWorkflows = "workflows";
    public const string ShelfRecord = "record";
    public const string ShelfCapture = "capture";
    public const string ShelfJobs = "jobs";
    public const string ShelfBinaries = "binaries";
    public const string ShelfCoverage = "coverage";

    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public string? SelectedId { get; set; }
    public string Shelf { get; set; } = ShelfWorkflows;
    public bool AutoOffer { get; set; }
    public HashSet<long> Expanded { get; } = [];
    public CaptureViewState Capture { get; } = new();
    public Dictionary<string, DeviceConsoleCache> Console { get; } = [with(StringComparer.Ordinal)];

    public bool ShelfIs(string tab) => string.Equals(Shelf, tab, StringComparison.Ordinal);

    public DeviceConsoleCache ConsoleFor(string deviceId) {
        if (Console.TryGetValue(deviceId, out var cached)) return cached;
        var fresh = new DeviceConsoleCache();
        Console[deviceId] = fresh;
        return fresh;
    }
}
