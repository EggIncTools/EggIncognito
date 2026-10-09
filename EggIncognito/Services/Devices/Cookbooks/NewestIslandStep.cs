using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class NewestIslandStep(CookbookStep inner, IServiceScopeFactory scopeFactory) : CookbookStep {
    public override string Id => inner.Id;
    public override string Title => inner.Title;

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        inner.DescribeAsync(target, ct);

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        int? newest = await NewestAsync(context.Target.Id, ct);
        if (newest is not { } id)
            return new CookbookStepResult(Id, Title, CookbookStepStatus.Failed, "no recorded island to target", []);
        return await inner.RunAsync(context with { AndroidUserId = id }, ct);
    }

    public static async Task<int?> NewestAsync(IServiceScopeFactory scopeFactory, string deviceId, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceIslandStore)) is not DeviceIslandStore store) return null;
        var islands = await store.ListAsync(deviceId, ct);
        return islands.Count == 0 ? null : islands.MaxBy(i => i.CreatedAt)?.AndroidUserId;
    }

    private Task<int?> NewestAsync(string deviceId, CancellationToken ct) => NewestAsync(scopeFactory, deviceId, ct);
}
