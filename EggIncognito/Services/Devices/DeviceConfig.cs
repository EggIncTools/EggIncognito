using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices;

public sealed record DeviceConfig {
    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 30;
    public int HarvestIntervalMinutes { get; init; } = 120;
    public int HarvestSettleSeconds { get; init; } = 10;
    public IReadOnlyList<DeviceEntry> Devices { get; init; } = [];

    public static DeviceConfig Bind(IConfiguration config) {
        var poll = config.GetSection("DevicePolling");
        string? dir = config["Devices:Dir"];
        var fromDir = ReadDir(dir);
        var inline = ReadInline(config);

        return new DeviceConfig {
            Enabled = poll.GetValue("Enabled", true),
            IntervalMinutes = poll.GetValue("IntervalMinutes", 30),
            HarvestIntervalMinutes = poll.GetValue("HarvestIntervalMinutes", 120),
            HarvestSettleSeconds = poll.GetValue("HarvestSettleSeconds", 10),
            Devices = Merge(fromDir, inline)
        };
    }

    internal static IReadOnlyList<DeviceEntry> Merge(
        IReadOnlyList<DeviceEntry> fromDir, IReadOnlyList<DeviceEntry> inline) {
        var inlineIds = inline.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. fromDir.Where(e => !inlineIds.Contains(e.Id)), .. inline];
    }

    private static List<DeviceEntry> ReadInline(IConfiguration config) {
        var devices = new List<DeviceEntry>();
        foreach (var d in config.GetSection("Devices").GetChildren()) {
            if (int.TryParse(d.Key, out _))
                AddIfValid(devices, d["Id"], d["Platform"], d["Label"], d["Target"], d["Package"]);
        }

        return devices;
    }

    private static List<DeviceEntry> ReadDir(string? dir) {
        var devices = new List<DeviceEntry>();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return devices;

        var parsed = Directory.EnumerateFiles(dir)
            .Where(p => DeviceFileParser.IsDeviceFile(Path.GetFileName(p)))
            .Select(p => DeviceFileParser.Parse(Path.GetFileName(p), File.ReadAllText(p)))
            .OfType<DeviceFileParser.ParsedDevice>()
            .OrderBy(p => p.Order);

        foreach (var p in parsed)
            AddIfValid(devices, p.Id, p.Platform, p.Label, p.Target, p.Package);
        return devices;
    }

    private static void AddIfValid(
        List<DeviceEntry> devices, string? id, string? platform, string? label, string? target, string? package) {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target)) return;
        devices.Add(new DeviceEntry(
            id,
            (platform ?? "android").ToLowerInvariant(),
            string.IsNullOrWhiteSpace(label) ? id : label,
            target,
            string.IsNullOrWhiteSpace(package) ? "com.auxbrain.egginc" : package,
            DeviceOrigins.Config));
    }
}
