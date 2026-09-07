using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallCaStep(
    IEnumerable<IDeviceCaInstaller> installers,
    IDeviceConnectionFactory connections,
    ProxyReachProbe proxyReach,
    CaptureCaSource captureCa) : CookbookStep {
    private const string SystemCaCerts = "/system/etc/security/cacerts/";
    private const string NoCa = "no capture CA available; run capture once so one gets minted";

    public override string Id => DeviceCookbookIds.InstallCa;
    public override string Title => "Install capture CA";

    public override async Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (Installer(target.Platform) is null)
            return CookbookStepAvailability.No($"no ca installer for platform '{target.Platform}'");

        var ca = await captureCa.ResolveAsync(ct);
        if (ca is null || !File.Exists(ca.Path)) return CookbookStepAvailability.No(NoCa);
        return CookbookStepAvailability.Ready;
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

        var ca = await captureCa.ResolveAsync(ct);
        if (ca is null || !File.Exists(ca.Path)) return Failed(lines, NoCa);

        if (await TrustedAsync(target, ca, ct) is { } file) {
            Add($"{file} already in the system trust store");
            return Ok(lines, "capture CA already in the system trust store");
        }

        var reach = await proxyReach.CheckAsync(target.Id, ct);
        if (!reach.Ok && reach.Outcome != DeviceOutcome.Unsupported) {
            return Failed(lines,
                $"the capture proxy is not reachable from the host, so a trusted CA would capture nothing: {reach.Note}");
        }

        Add(reach.Ok ? reach.Note ?? "capture proxy reachable" : $"proxy reach not tested: {reach.Note}");

        Add($"installing {Path.GetFileName(ca.Path)} on {target.Id}");
        (bool ok, string? note) = await installer.InstallAsync(target, ca.Path, ct);
        if (!ok) return Failed(lines, note ?? "ca install failed");

        Add(note ?? "ca installed");
        return Ok(lines, note);
    }

    private async Task<string?> TrustedAsync(DeviceTarget target, CaptureCa ca, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) return null;
        if (ca.AndroidTrustFile is not { } file) return null;
        if (connections.For(target) is not { } conn) return null;

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        var r = await conn.ShellAsync(root.WrapMountMaster($"[ -s {SystemCaCerts}{file} ] && echo present"), ct);
        return r.Stdout.Contains("present", StringComparison.Ordinal) ? file : null;
    }

    private IDeviceCaInstaller? Installer(string platform) =>
        installers.FirstOrDefault(i => Platforms.Matches(i.Platform, platform));
}
