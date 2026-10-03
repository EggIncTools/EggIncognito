using EggIdentity.Hosting;

namespace EggIncognito.Services.DataApi;

public sealed class EndpointCatalogAutoRefreshService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    TimeProvider time,
    ILogger<EndpointCatalogAutoRefreshService> logger) : PeriodicScopedService(scopeFactory, time, logger) {
    protected override bool Enabled => config.GetValue("Routes:AutoRefresh:Enabled", true);

    protected override TimeSpan Interval =>
        TimeSpan.FromMinutes(Math.Max(5, config.GetValue("Routes:AutoRefresh:IntervalMinutes", 60)));

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct) {
        var result = await services.GetRequiredService<EndpointCatalogRebuilder>().RebuildAsync(ct);
        logger.LogInformation(
            "binary route auto-refresh: {Discovered} discovered, {New} new, {Drift} drift ({Note})",
            result.Discovered, result.New, result.DriftCount, result.Note ?? "no binary");
    }
}
