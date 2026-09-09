using EggIdentity.UI;
using EggIncognito.Components.Capture;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Workbench;

namespace EggIncognito.Services.Devices;

public sealed class DeviceWorkbenchState : WorkbenchStateBase {
    public const string TabDevice = "device";
    public const string TabScreen = "screen";
    public const string TabJobs = "jobs";
    public const string TabCapture = "capture";
    public const string TabBinaries = "binaries";

    private static readonly IReadOnlyList<WorkbenchMode> PhysicalModes = [
        new(TabScreen, "Screen"),
        new(TabJobs, "Jobs"),
        new(TabCapture, "Capture"),
        new(TabBinaries, "Binaries")
    ];

    private static readonly IReadOnlyList<WorkbenchMode> VirtualModes = [
        new(TabDevice, "Device"),
        new(TabJobs, "Jobs"),
        new(TabCapture, "Capture")
    ];

    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } =
        [.. PhysicalModes.Concat(VirtualModes).DistinctBy(m => m.Key).Select(m => (m.Key, m.Label, m.Count))];

    public string? SelectedId { get; set; }
    public bool FleetOpen { get; set; }
    public HashSet<long> Expanded { get; } = [];
    public CaptureViewState Capture { get; } = new();
    public Dictionary<string, DeviceConsoleCache> Console { get; } = [with(StringComparer.Ordinal)];

    public static IReadOnlyList<WorkbenchMode> ModesFor(bool virtualDevice) =>
        virtualDevice ? VirtualModes : PhysicalModes;

    public static bool IsConsoleTab(string mode) => mode is TabDevice or TabScreen;

    public DeviceConsoleCache ConsoleFor(string deviceId) {
        if (Console.TryGetValue(deviceId, out var cached)) return cached;
        var fresh = new DeviceConsoleCache();
        Console[deviceId] = fresh;
        return fresh;
    }

    public void AlignMode(bool virtualDevice) {
        var modes = ModesFor(virtualDevice);
        if (modes.Any(m => string.Equals(m.Key, Mode, StringComparison.Ordinal))) return;
        Mode = modes[0].Key;
    }
}
