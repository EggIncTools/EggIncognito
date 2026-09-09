using EggIdentity.UI;
using EggIncognito.Components.Capture;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceWorkbenchState : WorkbenchStateBase {
    public const string SectionActions = "actions";
    public const string SectionIsland = "island";
    public const string SectionInput = "input";
    public const string SectionActivity = "activity";
    public const string SectionCapture = "capture";
    public const string SectionJobs = "jobs";
    public const string SectionBinaries = "binaries";

    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public string? SelectedId { get; set; }
    public bool FleetOpen { get; set; }
    public HashSet<long> Expanded { get; } = [];
    public HashSet<string> Open { get; } = [SectionActions, SectionInput, SectionActivity];
    public CaptureViewState Capture { get; } = new();
    public Dictionary<string, DeviceConsoleCache> Console { get; } = [with(StringComparer.Ordinal)];

    public bool IsOpen(string section) => Open.Contains(section);

    public void SetOpen(string section, bool open) {
        if (open) Open.Add(section);
        else Open.Remove(section);
    }

    public DeviceConsoleCache ConsoleFor(string deviceId) {
        if (Console.TryGetValue(deviceId, out var cached)) return cached;
        var fresh = new DeviceConsoleCache();
        Console[deviceId] = fresh;
        return fresh;
    }
}
