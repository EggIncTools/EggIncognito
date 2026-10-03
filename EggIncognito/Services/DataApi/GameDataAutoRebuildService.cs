using EggIdentity.Hosting;

namespace EggIncognito.Services.DataApi;

public sealed class GameDataAutoRebuildService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    TimeProvider time,
    ILogger<GameDataAutoRebuildService> logger) : PeriodicScopedService(scopeFactory, time, logger) {
    protected override bool Enabled => config.GetValue("GameData:AutoRebuild:Enabled", true);

    protected override TimeSpan Interval =>
        TimeSpan.FromMinutes(Math.Max(1, config.GetValue("GameData:AutoRebuild:IntervalMinutes", 5)));

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct) {
        (var results, string? binaryNote) = await services.GetRequiredService<GameDataRebuilder>().RebuildAsync(ct);

        int built = results.Count(r => r.Status == "built");
        int failed = results.Count(r => r.Status == "failed");
        int skipped = results.Count(r => r.Status == "skipped");
        int current = results.Count(r => r.Status == "current");

        if (built + failed + skipped == 0) return;

        logger.LogInformation(
            "game data auto-rebuild: {Built} built, {Current} current, {Skipped} skipped, {Failed} failed ({Binary})",
            built, current, skipped, failed, binaryNote ?? "no binary");

        foreach (var r in results.Where(r => r.Status == "failed"))
            logger.LogWarning("game data auto-rebuild: {Id} failed: {Note}", r.Id, r.Note);
    }
}
