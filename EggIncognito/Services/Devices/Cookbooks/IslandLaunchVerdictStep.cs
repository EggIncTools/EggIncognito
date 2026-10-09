using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class IslandLaunchVerdictStep(LaunchIslandStep launch, IslandIntegrityEvidenceStep evidence) : CookbookStep {
    public override string Id => launch.Id;
    public override string Title => launch.Title;

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var launched = await launch.RunAsync(context, ct);
        if (launched.Status == CookbookStepStatus.Failed && launched.Note is { Length: > 0 } note)
            await context.Progress(note);

        await context.Progress($"> {evidence.Title}");
        var gathered = await evidence.RunAsync(context, ct);
        return launched with { Lines = [.. launched.Lines, $"> {evidence.Title}", .. gathered.Lines] };
    }
}
