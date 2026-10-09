using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class IslandIntegrityCookbook(
    IslandIntegrityDiagnoseStep diagnose,
    IslandIntegrityRepairStep repair,
    OwnerRecertStep recert,
    IslandLaunchVerdictStep verdict,
    OwnerComparisonStep owner,
    IDeviceConnectionFactory connections) : IStepCookbook {
    private const string DiagnoseOnly = "diagnose";
    private const string Repair = "repair";

    public string Id => DeviceCookbookIds.IslandIntegrity;
    public string Title => "Island integrity";

    public string Summary =>
        "Diagnoses Play certification for a selected island, then fixes TrickyStore targets, runs the KsuWebUI recert, "
        + "resets the island's Play state, relaunches Egg Inc as the verdict and captures the integrity evidence.";

    public Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        string? unavailable = !Platforms.Matches(target.Platform, Platforms.Android) ? "islands are android-only"
            : connections.For(target) is null ? "no connection for this device"
            : null;
        DeviceCookbookOption[] options = [
            new(Repair, "Diagnose and repair", Recommended: true),
            new(DiagnoseOnly, "Diagnose only")
        ];
        return Task.FromResult(new DeviceCookbookInfo(Id, Title, Summary, unavailable is null, unavailable, "Mode", options) {
            Group = CookbookGroups.Workflow
        });
    }

    public Task<IReadOnlyList<CookbookStep>> PlanAsync(DeviceTarget target, string? argument, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CookbookStep>>(
            string.Equals(argument, DiagnoseOnly, StringComparison.OrdinalIgnoreCase)
                ? [diagnose, new SoftStep(verdict), owner]
                : [diagnose, new SoftStep(recert), repair, verdict]);

    public Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) =>
        CookbookExecutor.RunStepsAsync(this, context, ct);
}
