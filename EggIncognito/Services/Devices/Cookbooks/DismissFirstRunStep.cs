using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class DismissFirstRunStep(
    IEnumerable<IDeviceUiDriver> uiDrivers,
    GmsFirstRunConfig gms) : CookbookStep {
    public override string Id => DeviceCookbookIds.DismissFirstRun;
    public override string Title => "Dismiss first-run dialogs";

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(Driver(target.Platform) is null
            ? CookbookStepAvailability.No($"no ui driver for platform '{target.Platform}'")
            : CookbookStepAvailability.Ready);

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        if (Driver(context.Target.Platform) is not { } ui)
            return Skipped(lines, $"no ui driver for platform '{context.Target.Platform}'");

        var runner = new DeviceFlowRunner(ui);
        var result = await runner.RunAsync(context.Target, FirstRunDialogFlow.Build(gms), Add, ct);
        return result.Ok
            ? Ok(lines, "first-run dialogs cleared or already absent")
            : Failed(lines, "first-run flow failed");
    }

    private IDeviceUiDriver? Driver(string platform) =>
        uiDrivers.FirstOrDefault(u => Platforms.Matches(u.Platform, platform));
}
