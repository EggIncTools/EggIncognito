using EggIdentity.Client;
using EggIdentity.Hosting;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Auth;

public sealed class UserMergeSyncService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    TimeProvider time,
    ILogger<UserMergeSyncService> logger) : PeriodicScopedService(scopeFactory, time, logger) {
    internal const int PageSize = 500;

    protected override bool Enabled => config.GetValue("Identity:MergeSync:Enabled", true);

    protected override TimeSpan Interval =>
        TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Identity:MergeSync:IntervalMinutes", 15)));

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken ct) =>
        SweepAsync(services.GetRequiredService<IdentityApiClient>(), services.GetRequiredService<IUserMergeRemapper>(), logger, ct);

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
