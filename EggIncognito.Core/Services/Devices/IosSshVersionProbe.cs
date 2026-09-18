namespace EggIncognito.Core.Services.Devices;

public sealed class IosSshVersionProbe(SshDeviceConnection conn, string bundleId) : IDeviceProbe {
    private const int AppNotFound = 3;

    public async Task<DeviceProbeResult> ProbeAsync(CancellationToken ct) {
        using var timebox = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timebox.CancelAfter(DeviceProbeTimeout.Value);

        var r = await conn.ShellAsync(DeviceShell.LocateIosApp(bundleId) + Read, timebox.Token);
        if (r.ExitCode == AppNotFound) return new DeviceProbeResult(true, null, null, $"{bundleId} not installed");
        if (r.ExitCode != 0)
            return new DeviceProbeResult(false, null, null, DeviceParsing.TrimNote(r.Stderr + r.Stdout));

        (string? app, string? build) = Parse(r.Stdout);
        return app is null
            ? new DeviceProbeResult(true, null, null, "no CFBundleShortVersionString on device")
            : new DeviceProbeResult(true, app, build, "read over ssh; usbmux did not answer");
    }

    private static readonly string Read =
        $"v=$({DeviceShell.ReadPlistKey("CFBundleShortVersionString")}); echo \"$v\"; " +
        $"v=$({DeviceShell.ReadPlistKey("CFBundleVersion")}); echo \"$v\";";

    private static (string? App, string? Build) Parse(string stdout) {
        string[] lines = stdout.Split('\n', StringSplitOptions.TrimEntries);
        return (Nz(lines, 0), Nz(lines, 1));
    }

    private static string? Nz(string[] lines, int i) =>
        i < lines.Length && lines[i].Length > 0 ? lines[i] : null;
}
