namespace EggIncognito.Core.Services.Devices;

public sealed class AdbProxyConfigurator(IProcessRunner runner) : IDeviceProxyConfigurator {
    public string Platform => "android";

    public async Task<(bool Ok, string? Note)> SetProxyAsync(DeviceTarget device, string hostIp, int port,
        CancellationToken ct) {
        var r = await AdbDeviceConnection.RunAsync(runner, device.Target, ["shell", "settings", "put", "global", "http_proxy", $"{hostIp}:{port}"], null, ct);
        return r.ExitCode == 0 ? (true, null) : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    public async Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceTarget device, CancellationToken ct) {
        var r = await AdbDeviceConnection.RunAsync(runner, device.Target, ["shell", "settings", "put", "global", "http_proxy", ":0"], null, ct);
        return r.ExitCode == 0 ? (true, null) : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }
}
