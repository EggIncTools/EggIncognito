namespace EggIncognito.Core.Services.Devices;

public sealed class IosDeviceProbe(IProcessRunner runner, string udid, string bundleId) : IDeviceProbe {
    public async Task<DeviceProbeResult> ProbeAsync(CancellationToken ct) {
        using var timebox = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timebox.CancelAfter(DeviceProbeTimeout.Value);
        var r = await RunFirstSupportedAsync(timebox.Token);
        if (r.ExitCode != 0)
            return new DeviceProbeResult(false, null, null, DeviceParsing.TrimNote(r.Stderr + r.Stdout));

        (string? app, string? build) = DeviceParsing.IosVersion(r.Stdout, bundleId);
        return app is null
            ? new DeviceProbeResult(true, null, null, $"{bundleId} not installed")
            : new DeviceProbeResult(true, app, build, null);
    }

    private static readonly string[][] ListArgs = [
        ["list", "--xml"], ["-l", "-o", "xml"], ["list"], ["-l"]
    ];

    private async Task<ProcessResult> RunFirstSupportedAsync(CancellationToken ct) {
        ProcessResult last = new(-1, "", "no ideviceinstaller invocation ran");
        foreach (string[] args in ListArgs) {
            last = await runner.RunAsync("ideviceinstaller", ["-u", udid, .. args], ct);
            if (last.ExitCode == 0) return last;
            if (!last.Stderr.Contains("invalid option", StringComparison.Ordinal)) return last;
        }

        return last;
    }
}
