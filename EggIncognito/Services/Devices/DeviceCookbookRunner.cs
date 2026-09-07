using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;
using EggIncognito.Services.Devices.Cookbooks;

namespace EggIncognito.Services.Devices;

public sealed class DeviceCookbookRunner(
    IDeviceCookbooks cookbooks,
    IDeviceFleet fleet,
    DeviceJobStore jobs,
    CookbookExecutor executor,
    IServiceScopeFactory scopeFactory,
    CookbookCancellations cancellations,
    IDeviceClaims claims,
    DeviceTransportConfig transport,
    ILogger<DeviceCookbookRunner> logger) {
    private const string ClaimFailure = "the host refused a claim on this device";

    public async Task<DeviceTarget?> TargetAsync(string deviceId, CancellationToken ct) {
        var entry = (await fleet.EnabledAsync(ct)).FirstOrDefault(d =>
            string.Equals(d.Id, deviceId, StringComparison.Ordinal));
        return entry is null ? null : new DeviceTarget(entry.Id, entry.Platform, entry.Target, entry.Package);
    }

    public async Task<IReadOnlyList<DeviceCookbookInfo>> DescribeAsync(string deviceId, CancellationToken ct) {
        if (await TargetAsync(deviceId, ct) is not { } target) return [];
        return await cookbooks.DescribeAllAsync(target, ct);
    }

    public async Task<DeviceCookbookStart> StartAsync(string deviceId, DeviceCookbookRequest request, string trigger,
        CancellationToken ct) {
        if (await TargetAsync(deviceId, ct) is not { } target)
            return new DeviceCookbookStart(DeviceCookbookStartOutcome.UnknownDevice, Error: "unknown device");

        if (cookbooks.Find(request.CookbookId) is not { } cookbook) {
            return new DeviceCookbookStart(DeviceCookbookStartOutcome.UnknownCookbook,
                Error: $"unknown cookbook '{request.CookbookId}'");
        }

        var info = await cookbook.DescribeAsync(target, ct);
        if (!info.Available) {
            return new DeviceCookbookStart(DeviceCookbookStartOutcome.Unavailable,
                Error: info.Unavailable ?? $"'{cookbook.Id}' is not available on this device");
        }

        var job = await jobs.TryStartAsync(deviceId, DeviceJobKinds.Cookbook, trigger,
            $"{cookbook.Title} starting...", StartFacts(cookbook), ct);
        if (job is null) {
            return new DeviceCookbookStart(DeviceCookbookStartOutcome.Busy,
                Error: "another job is already running on this device");
        }

        var cts = new CancellationTokenSource();
        cancellations.Register(deviceId, job.Id, cts);
        _ = Task.Run(() => RunDetachedAsync(job, target, request with { CookbookId = cookbook.Id }, cts),
            CancellationToken.None);
        return new DeviceCookbookStart(DeviceCookbookStartOutcome.Started, job.Id);
    }

    public async Task<DeviceCookbookRun> RunNowAsync(string deviceId, DeviceCookbookRequest request, string trigger,
        CancellationToken ct) {
        if (await TargetAsync(deviceId, ct) is not { } target)
            return new DeviceCookbookRun(false, request.CookbookId, ["unknown device"], "target", "unknown device");

        if (cookbooks.Find(request.CookbookId) is not { } cookbook) {
            return new DeviceCookbookRun(false, request.CookbookId, [$"unknown cookbook '{request.CookbookId}'"],
                "cookbook", $"unknown cookbook '{request.CookbookId}'");
        }

        var job = await jobs.TryStartAsync(deviceId, DeviceJobKinds.Cookbook, trigger,
            $"{cookbook.Title} starting...", StartFacts(cookbook), ct);
        if (job is null) {
            return new DeviceCookbookRun(false, cookbook.Id, ["another job is already running on this device"],
                "busy", "another job is already running on this device");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellations.Register(deviceId, job.Id, cts);
        using var renewals = new CancellationTokenSource();
        Task? renewal = null;
        try {
            var claim = await ClaimAsync(deviceId, cts.Token);
            if (!claim.Ok) {
                string note = claim.Note ?? ClaimFailure;
                await jobs.FinishAsync(job, DeviceOutcomes.Error, note, StartFacts(cookbook), CancellationToken.None);
                return new DeviceCookbookRun(false, cookbook.Id, [note], "claim", note);
            }

            renewal = RenewAsync(deviceId, renewals.Token);
            var context = new DeviceCookbookContext(target, request.Argument,
                line => jobs.ProgressAsync(job, line, ct: cts.Token).GetAwaiter().GetResult());
            var run = await executor.RunAsync(cookbook, context, cts.Token);
            await jobs.FinishAsync(job, run.Ok ? DeviceOutcomes.Ok : DeviceOutcomes.Error, Summarize(cookbook, run),
                Facts(cookbook, run, request), CancellationToken.None);
            return run;
        } catch (Exception ex) {
            logger.LogError(ex, "cookbook: {Cookbook} on {Device} threw", request.CookbookId, deviceId);
            await jobs.FailAsync(job, ex.Message, CancellationToken.None);
            return new DeviceCookbookRun(false, cookbook.Id, [ex.Message], "exception", ex.Message);
        } finally {
            await StopRenewalAsync(renewals, renewal);
            await ReleaseAsync(deviceId);
            cancellations.Release(deviceId, job.Id, cts);
        }
    }

    private async Task RunDetachedAsync(JobRef job, DeviceTarget target, DeviceCookbookRequest request,
        CancellationTokenSource cts) {
        using var scope = scopeFactory.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<DeviceJobStore>();
        using var renewals = new CancellationTokenSource();
        Task? renewal = null;
        try {
            var claim = await ClaimAsync(job.DeviceId, cts.Token);
            if (!claim.Ok) {
                await scoped.FailAsync(job, claim.Note ?? ClaimFailure, CancellationToken.None);
                return;
            }

            renewal = RenewAsync(job.DeviceId, renewals.Token);
            var cookbook = cookbooks.Find(request.CookbookId)!;
            var context = new DeviceCookbookContext(target, request.Argument,
                line => scoped.ProgressAsync(job, line).GetAwaiter().GetResult());
            var run = await executor.RunAsync(cookbook, context, cts.Token);

            await scoped.FinishAsync(job, run.Ok ? DeviceOutcomes.Ok : DeviceOutcomes.Error, Summarize(cookbook, run),
                Facts(cookbook, run, request), CancellationToken.None);
        } catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            logger.LogInformation("cookbook: {Cookbook} on {Device} was stopped by an admin",
                request.CookbookId, job.DeviceId);
            string title = cookbooks.Find(request.CookbookId)?.Title ?? request.CookbookId;
            await scoped.CancelAsync(job, $"{title} stopped by an admin", CancellationToken.None);
        } catch (Exception ex) {
            logger.LogError(ex, "cookbook: {Cookbook} on {Device} threw", request.CookbookId, job.DeviceId);
            await scoped.ProgressAsync(job, ex.ToString(), DeviceJobLevels.Error, CancellationToken.None);
            await scoped.FailAsync(job, ex.Message, CancellationToken.None);
        } finally {
            await StopRenewalAsync(renewals, renewal);
            await ReleaseAsync(job.DeviceId);
            cancellations.Release(job.DeviceId, job.Id, cts);
            cts.Dispose();
        }
    }

    private TimeSpan Ttl => TimeSpan.FromSeconds(Math.Max(2, transport.ClaimTtlSeconds));

    private async Task<DeviceResult<DateTimeOffset>> ClaimAsync(string deviceId, CancellationToken ct) {
        if (!claims.Active) return DeviceResult<DateTimeOffset>.Success(default);
        return await claims.ClaimAsync(deviceId, Ttl, ct);
    }

    private async Task RenewAsync(string deviceId, CancellationToken ct) {
        if (!claims.Active) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Ttl.TotalSeconds / 2));
        try {
            while (await timer.WaitForNextTickAsync(ct)) {
                var renewed = await claims.ClaimAsync(deviceId, Ttl, ct);
                if (!renewed.Ok)
                    logger.LogWarning("cookbook: re-claiming {Device} failed: {Note}", deviceId, renewed.Note);
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "cookbook: claim renewal for {Device} stopped", deviceId);
        }
    }

    private static async Task StopRenewalAsync(CancellationTokenSource renewals, Task? renewal) {
        await renewals.CancelAsync();
        if (renewal is not null) await renewal;
    }

    private async Task ReleaseAsync(string deviceId) {
        if (!claims.Active) return;
        await claims.ReleaseAsync(deviceId, CancellationToken.None);
    }

    private static DeviceJobFacts StartFacts(IDeviceCookbook cookbook) =>
        new(Detail: new { cookbook = cookbook.Id, cookbookTitle = cookbook.Title });

    private static DeviceJobFacts Facts(IDeviceCookbook cookbook, DeviceCookbookRun run,
        DeviceCookbookRequest request) =>
        new(Detail: new {
            cookbook = run.CookbookId,
            cookbookTitle = cookbook.Title,
            argument = request.Argument,
            failedStep = run.FailedStep,
            failedStepTitle = run.FailedStepTitle,
            steps = run.Steps.Select(s => new {
                id = s.StepId,
                title = s.Title,
                status = s.Status.ToString(),
                note = s.Note
            })
        });

    private static string Summarize(IDeviceCookbook cookbook, DeviceCookbookRun run) {
        if (run.Ok) return run.Note is { Length: > 0 } ok ? ok : $"{cookbook.Title} ok";
        return run.Failure ?? $"{cookbook.Title} failed";
    }
}
