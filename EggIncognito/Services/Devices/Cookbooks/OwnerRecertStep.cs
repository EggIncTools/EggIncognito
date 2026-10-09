using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class OwnerRecertStep(RecertStep recert, IDeviceConnectionFactory connections) : CookbookStep {
    public override string Id => recert.Id;
    public override string Title => "Recert (owner user)";

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        if (connections.For(context.Target) is not { } conn)
            return Failed([], "no connection for this device");

        var switched = await IslandScope.SwitchAsync(conn, IslandScope.Owner, ct);
        if (!switched.Ok) return Failed([switched.Note ?? "switch failed"], switched.Note);
        await context.Progress(switched.Note ?? "switched to the owner user");

        var inner = await recert.RunAsync(context with { AndroidUserId = null }, ct);
        return inner with { StepId = Id, Title = Title };
    }
}
