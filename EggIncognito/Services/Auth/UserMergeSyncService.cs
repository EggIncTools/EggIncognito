using EggIdentity.Client;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Auth;

public sealed class UserMergeSyncService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    TimeProvider time,
    ILogger<UserMergeSyncService> logger) : BackgroundService {
    internal const int PageSize = 500;

    private bool Enabled => config.GetValue("Identity:MergeSync:Enabled", true);
    private int IntervalMinutes => config.GetValue("Identity:MergeSync:IntervalMinutes", 15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!Enabled) {
            logger.LogInformation("user merge sync disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes)), time);
        try {
            await RunOnceAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "user merge sync stopped by shutdown");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        try {
            await SweepAsync(
                sp.GetRequiredService<IdentityApiClient>(), sp.GetRequiredService<IUserMergeRemapper>(), logger, ct);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "user merge sync failed; retrying next interval");
        }
    }

    // The feed filters merged_at > since, so each query backs off 1us to re-read rows sharing the boundary
    // timestamp; the remap is idempotent, so a replayed merge moves nothing.
    internal static async Task<int> SweepAsync(
        IdentityApiClient identity, IUserMergeRemapper remapper, ILogger logger, CancellationToken ct) {
        int moved = 0;
        var since = Overlap(await remapper.WatermarkAsync(ct));
        while (true) {
            var page = await identity.ListMergesAsync(since, ct);
            foreach (var m in page) {
                int rows = await remapper.ApplyAsync(m.MergedUserId, m.KeptUserId, m.MergedAt, ct);
                if (rows > 0) {
                    logger.LogInformation("user merge {Merged} -> {Kept}: {Rows} rows remapped",
                        m.MergedUserId, m.KeptUserId, rows);
                }
                moved += rows;
            }

            if (page.Count < PageSize) return moved;
            var next = Overlap(page[^1].MergedAt);
            if (next <= since) return moved;
            since = next;
        }
    }

    private static DateTimeOffset? Overlap(DateTimeOffset? at) => at?.AddTicks(-TimeSpan.TicksPerMicrosecond);
}
