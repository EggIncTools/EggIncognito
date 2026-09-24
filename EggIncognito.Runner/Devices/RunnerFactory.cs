using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.Runners;
using EggIncognito.Runner.State;

namespace EggIncognito.Runner.Devices;

public sealed record RunnerDeps(
    IProtoExtractor Proto,
    IClientVersionReader ClientVersion,
    string ApkStashDir,
    string IosBinaryPath,
    int? PrevClientVersion,
    string DefaultPackage,
    Func<NewVersionEvent, Task> OnNewVersion);

public static class RunnerFactory {
    public static IDeviceRunner? Build(DeviceFileParser.ParsedDevice device, RunnerDeps deps) {
        if (string.IsNullOrWhiteSpace(device.Id)) return null;
        var id = device.Id;
        var platform = (device.Platform ?? Platforms.Android).ToLowerInvariant();
        var package = string.IsNullOrWhiteSpace(device.Package) ? deps.DefaultPackage : device.Package;
        var stateFile = Path.Combine(deps.ApkStashDir, $"state-{id}.json");

        switch (platform) {
            case Platforms.Android: {
                    if (string.IsNullOrWhiteSpace(device.Target)) return null;
                    var cvState = new ClientVersionState(
                        Path.Combine(deps.ApkStashDir, $"clientversion-{id}.txt"), deps.PrevClientVersion);
                    return new AndroidRunner(
                        new AdbClient(device.Target), deps.Proto, new VersionState(stateFile),
                        deps.ClientVersion, cvState, package, deps.ApkStashDir, deps.OnNewVersion);
                }
            case Platforms.Ios:
                return new IosRunner(deps.IosBinaryPath, new VersionState(stateFile), package, deps.OnNewVersion);
            default:
                return null;
        }
    }
}
