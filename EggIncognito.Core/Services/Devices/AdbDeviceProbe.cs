namespace EggIncognito.Core.Services.Devices;

public sealed class AdbDeviceProbe(IProcessRunner runner, string serial, string package) : IDeviceProbe {
    public async Task<DeviceProbeResult> ProbeAsync(CancellationToken ct) {
        var r = await AdbDeviceConnection.RunAsync(runner, serial, ["shell", "dumpsys", "package", package], DeviceProbeTimeout.Value, ct);
        if (r.ExitCode != 0)
            return new DeviceProbeResult(false, null, null, DeviceParsing.TrimNote(r.Stderr + r.Stdout));

        (string? app, string? build) = DeviceParsing.AndroidVersion(r.Stdout);
        return build is null && app is null
            ? new DeviceProbeResult(false, null, null, "no version in dumpsys")
            : new DeviceProbeResult(true, app, build, null);
    }
}
