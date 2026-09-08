using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class LaunchIslandCookbook(LaunchIslandStep step) : IStepCookbook {
    public string Id => DeviceCookbookIds.LaunchIsland;
    public string Title => "Launch island";

    public string Summary =>
        "Switches to a selected island, launches Egg Inc, and handles the Play account nag per the PlayNag config.";

    public async Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        var a = await step.DescribeAsync(target, ct);
        return new DeviceCookbookInfo(Id, Title, Summary, a.Available, a.Unavailable, a.ArgumentLabel, a.Options) {
            Group = CookbookGroups.Step
        };
    }

    public Task<IReadOnlyList<CookbookStep>> PlanAsync(DeviceTarget target, string? argument, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CookbookStep>>([step]);

    public Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) =>
        CookbookExecutor.RunStepsAsync(this, context, ct);
}
