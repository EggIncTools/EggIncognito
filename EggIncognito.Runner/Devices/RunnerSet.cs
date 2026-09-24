using EggIncognito.Core.Services.Devices;
using EggIncognito.Runner.Runners;

namespace EggIncognito.Runner.Devices;

public sealed record RunnerSet(
    IReadOnlyList<IDeviceRunner> Runners,
    IReadOnlyDictionary<string, IDeviceRunner> ById) {
    public static RunnerSet Build(
        IReadOnlyList<DeviceFileParser.ParsedDevice> devices,
        RunnerDeps deps,
        Func<IDeviceRunner?> legacyFallback) {
        var byId = new Dictionary<string, IDeviceRunner>(StringComparer.OrdinalIgnoreCase);
        var list = new List<IDeviceRunner>();
        foreach (var d in devices) {
            var runner = RunnerFactory.Build(d, deps);
            if (runner is null || d.Id is null || !byId.TryAdd(d.Id, runner)) continue;
            list.Add(runner);
        }
        if (list.Count > 0) return new RunnerSet(list, byId);

        var legacy = legacyFallback();
        if (legacy is not null) {
            return new RunnerSet([legacy], new Dictionary<string, IDeviceRunner>(
            StringComparer.OrdinalIgnoreCase) { [legacy.Platform] = legacy });
        }

        return new RunnerSet([], new Dictionary<string, IDeviceRunner>(StringComparer.OrdinalIgnoreCase));
    }
}
