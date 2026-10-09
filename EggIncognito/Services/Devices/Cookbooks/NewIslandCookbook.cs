using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class NewIslandCookbook(
    CreateIslandStep create,
    InstallAppIslandStep install,
    CloneIslandTrustStep clone,
    LaunchIslandStep launch,
    IDeviceConnectionFactory connections,
    IServiceScopeFactory scopeFactory) : IStepCookbook {
    public string Id => DeviceCookbookIds.NewIsland;
    public string Title => "New island";

    public string Summary =>
        "Creates an island, installs Egg Inc into it, clones Play's anti-tamper trust from the selected island so Play never re-checks device certification, then launches the app.";

    public Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        string? unavailable = !Platforms.Matches(target.Platform, Platforms.Android) ? "islands are android-only"
            : connections.For(target) is null ? "no connection for this device"
            : null;
        return Task.FromResult(new DeviceCookbookInfo(Id, Title, Summary, unavailable is null, unavailable, "Label") {
            Group = CookbookGroups.Workflow
        });
    }

    public Task<IReadOnlyList<CookbookStep>> PlanAsync(DeviceTarget target, string? argument, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CookbookStep>>([create, new NewestIslandStep(install, scopeFactory), clone, new NewestIslandStep(launch, scopeFactory)]);

    public Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) =>
        CookbookExecutor.RunStepsAsync(this, context, ct);
}
