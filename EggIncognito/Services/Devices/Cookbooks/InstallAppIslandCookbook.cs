using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallAppIslandCookbook(InstallAppIslandStep step) : IStepCookbook {
    public string Id => DeviceCookbookIds.InstallAppIsland;
    public string Title => "Install app into island";

    public string Summary =>
        "Shares Egg Inc into a selected island with install-existing, falling back to stored splits when the device lacks it.";

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
