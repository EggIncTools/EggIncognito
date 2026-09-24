using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class CookbookExecutor {
    public Task<DeviceCookbookRun> RunAsync(
        IDeviceCookbook cookbook, DeviceCookbookContext context, CancellationToken ct) =>
        cookbook is IStepCookbook step
            ? RunStepsAsync(step, context, ct)
            : RunWholeAsync(cookbook, context, ct);

    public static async Task<DeviceCookbookRun> RunStepsAsync(
        IStepCookbook cookbook, DeviceCookbookContext context, CancellationToken ct) {
        var plan = await cookbook.PlanAsync(context.Target, context.Argument, ct);
        var steps = new List<CookbookStepResult>();
        var log = new List<string>();
        CookbookStepResult? failed = null;

        foreach (var step in plan) {
            string marker = $"> {step.Title}";
            log.Add(marker);
            await context.Progress(marker);

            var result = await step.RunAsync(context, ct);
            steps.Add(result);
            log.AddRange(result.Lines);
            if (result.Status == CookbookStepStatus.Failed && result.Note is { Length: > 0 } note
                && !result.Lines.Contains(note, StringComparer.Ordinal)) {
                log.Add(note);
                await context.Progress(note);
            }

            if (result.Status == CookbookStepStatus.Failed && !step.Soft) {
                failed = result;
                break;
            }
        }

        if (failed is null)
            return new DeviceCookbookRun(true, cookbook.Id, log, null, OkNote(cookbook, steps)) { Steps = steps };
        return new DeviceCookbookRun(false, cookbook.Id, log, failed.StepId, failed.Note, failed.Title) {
            Steps = steps
        };
    }

    private static async Task<DeviceCookbookRun> RunWholeAsync(
        IDeviceCookbook cookbook, DeviceCookbookContext context, CancellationToken ct) {
        var run = await cookbook.RunAsync(context, ct);
        var status = run.Ok ? CookbookStepStatus.Ok : CookbookStepStatus.Failed;
        var result = new CookbookStepResult(cookbook.Id, cookbook.Title, status, run.Note, run.Log);
        return run with {
            Steps = [result],
            FailedStepTitle = run.Ok ? null : run.FailedStepTitle ?? cookbook.Title
        };
    }

    private static string OkNote(IDeviceCookbook cookbook, List<CookbookStepResult> steps) {
        var skipped = steps.Where(s => s.Status == CookbookStepStatus.Skipped).Select(s => s.Title).ToList();
        if (skipped.Count > 0) return $"ok, skipped {string.Join(", ", skipped)}";
        return steps.Count == 1 && steps[0].Note is { Length: > 0 } single ? single : $"{cookbook.Title} ok";
    }
}
