using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class ReadinessStep(VirtualDeviceReadinessProbe probe) : CookbookStep {
    public override string Id => DeviceCookbookIds.Readiness;
    public override string Title => "Readiness";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(Platforms.Matches(target.Platform, Platforms.Android)
            ? CookbookStepAvailability.Ready
            : CookbookStepAvailability.No("readiness probing is android-only"));

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        var readiness = await probe.ProbeAsync(context.Target, ct);
        var missing = new List<string>();
        Task Row(string name, ReadinessCheck check) {
            string suffix = check.Note is { Length: > 0 } note ? $" ({note})" : "";
            string line = $"{name}: {(check.Ok ? "ok" : "missing")}{suffix}";
            lines.Add(line);
            if (!check.Ok) missing.Add(name);
            return context.Progress(line);
        }

        await Row("installed", readiness.Installed);
        await Row("google play", readiness.GooglePlay);
        await Row("rooted", readiness.Rooted);
        await Row("integrity module", readiness.IntegrityModule);
        await Row("launched", readiness.Launched);
        await Row("capture ca", readiness.CaptureCa);
        await Row("proxy reachable", readiness.ProxyReachable);

        return Ok(lines, missing.Count == 0 ? "all checks passed" : $"missing: {string.Join(", ", missing)}");
    }
}
