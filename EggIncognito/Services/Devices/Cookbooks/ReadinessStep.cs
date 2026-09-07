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
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var readiness = await probe.ProbeAsync(context.Target, ct);
        var missing = new List<string>();
        void Row(string name, ReadinessCheck check) {
            string suffix = check.Note is { Length: > 0 } note ? $" ({note})" : "";
            Add($"{name}: {(check.Ok ? "ok" : "missing")}{suffix}");
            if (!check.Ok) missing.Add(name);
        }

        Row("installed", readiness.Installed);
        Row("google play", readiness.GooglePlay);
        Row("rooted", readiness.Rooted);
        Row("integrity module", readiness.IntegrityModule);
        Row("launched", readiness.Launched);
        Row("capture ca", readiness.CaptureCa);
        Row("proxy reachable", readiness.ProxyReachable);

        return Ok(lines, missing.Count == 0 ? "all checks passed" : $"missing: {string.Join(", ", missing)}");
    }
}
