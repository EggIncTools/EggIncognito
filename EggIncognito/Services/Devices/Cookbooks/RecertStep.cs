using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class RecertStep(
    IServiceScopeFactory scopeFactory,
    IDeviceConnectionFactory connections,
    DeviceRecertConfig config) : CookbookStep {
    public override string Id => DeviceCookbookIds.Recert;
    public override string Title => "Recert";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(CookbookStepAvailability.No("recert is android-only"));
        if (!config.Enabled) return Task.FromResult(CookbookStepAvailability.No("recert is not enabled"));
        if (string.IsNullOrEmpty(config.KsuWebUiPackage))
            return Task.FromResult(CookbookStepAvailability.No("recert is not configured (KsuWebUiPackage missing)"));

        return Task.FromResult(CookbookStepAvailability.Ready);
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "recert is android-only");
        if (!config.Enabled) return Skipped(lines, "recert is not enabled");
        if (string.IsNullOrEmpty(config.KsuWebUiPackage))
            return Skipped(lines, "recert is not configured (KsuWebUiPackage missing)");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        if (!root.Ok)
            return Skipped(lines, $"device is not rooted ({root.Detail}); skipping recert");

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceRecertService)) is not DeviceRecertService recert)
            return Failed(lines, "no database configured, recert requires DeviceRecertService");

        await Add($"recertifying {target.Id}");
        var result = await recert.RunFlowAsync(target, ct);
        foreach (string line in result.Log) await Add(line);

        return result.Ok ? Ok(lines, "recert ok") : Failed(lines, "recert failed");
    }
}
