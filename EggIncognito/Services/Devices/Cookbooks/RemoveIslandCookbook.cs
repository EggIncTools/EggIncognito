using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class RemoveIslandCookbook(RemoveIslandStep step) : IStepCookbook {
    public string Id => DeviceCookbookIds.RemoveIsland;
    public string Title => "Remove island";

    public string Summary => "Removes a selected island (secondary user) from the device and deletes its record.";

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
