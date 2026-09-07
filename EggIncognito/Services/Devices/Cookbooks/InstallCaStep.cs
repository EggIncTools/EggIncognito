using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallCaStep(
    IEnumerable<IDeviceCaInstaller> installers,
    IDeviceConnectionFactory connections,
    ProxyReachProbe proxyReach,
    IConfiguration configuration) : CookbookStep {
    private const string SystemCaCerts = "/system/etc/security/cacerts/";

    public override string Id => DeviceCookbookIds.InstallCa;
    public override string Title => "Install capture CA";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (Installer(target.Platform) is null)
            return Task.FromResult(CookbookStepAvailability.No($"no ca installer for platform '{target.Platform}'"));

        string caPath = CaptureCaPath.Resolve(configuration);
        if (!File.Exists(caPath)) {
            return Task.FromResult(CookbookStepAvailability.No(
                $"no capture CA at {caPath}; run capture once so one gets minted"));
        }

        return Task.FromResult(CookbookStepAvailability.Ready);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (Installer(target.Platform) is not { } installer)
            return Skipped(lines, $"no ca installer for platform '{target.Platform}'");

        string caPath = CaptureCaPath.Resolve(configuration);
        if (!File.Exists(caPath))
            return Failed(lines, $"no capture CA at {caPath}; run capture once so one gets minted");

        if (await TrustedAsync(target, ct) is { } file) {
            Add($"{file} already in the system trust store");
            return Ok(lines, "capture CA already in the system trust store");
        }

        var reach = await proxyReach.CheckAsync(target.Id, ct);
        if (!reach.Ok && reach.Outcome != DeviceOutcome.Unsupported) {
            return Failed(lines,
                $"the capture proxy is not reachable from the host, so a trusted CA would capture nothing: {reach.Note}");
        }

        Add(reach.Ok ? reach.Note ?? "capture proxy reachable" : $"proxy reach not tested: {reach.Note}");

        Add($"installing {Path.GetFileName(caPath)} on {target.Id}");
        (bool ok, string? note) = await installer.InstallAsync(target, caPath, ct);
        if (!ok) return Failed(lines, note ?? "ca install failed");

        Add(note ?? "ca installed");
        return Ok(lines, note);
    }

    private async Task<string?> TrustedAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) return null;
        if (CaptureCaPath.AndroidTrustFile(configuration) is not { } file) return null;
        if (connections.For(target) is not { } conn) return null;

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        var r = await conn.ShellAsync(root.WrapMountMaster($"[ -s {SystemCaCerts}{file} ] && echo present"), ct);
        return r.Stdout.Contains("present", StringComparison.Ordinal) ? file : null;
    }

    private IDeviceCaInstaller? Installer(string platform) =>
        installers.FirstOrDefault(i => Platforms.Matches(i.Platform, platform));
}
