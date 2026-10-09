using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class OwnerComparisonStep(
    LaunchAppStep launch,
    IslandIntegrityEvidenceStep evidence,
    IDeviceConnectionFactory connections) : CookbookStep {
    public override string Id => "owner-comparison";
    public override string Title => "Owner user comparison";

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        Task Add(string line) {
            lines.Add(line);
            return context.Progress(line);
        }

        if (connections.For(context.Target) is not { } conn)
            return Failed(lines, "no connection for this device");

        var switched = await IslandScope.SwitchAsync(conn, IslandScope.Owner, ct);
        if (!switched.Ok) return Failed(lines, switched.Note);
        await Add(switched.Note ?? "switched to the owner user");

        var owner = context with { AndroidUserId = null };
        var launched = await launch.RunAsync(owner, ct);
        foreach (string line in launched.Lines) await Add(line);
        string verdict = launched.Status == CookbookStepStatus.Ok
            ? "owner launch passed"
            : $"owner launch failed: {launched.Note}";
        await Add(verdict);

        var gathered = await evidence.RunAsync(owner, ct);
        lines.AddRange(gathered.Lines);
        return Ok(lines, verdict);
    }
}
