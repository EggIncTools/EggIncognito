using EggIncognito.Core;
using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Events;
using Ei;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Feed;

public sealed class ConfigChangeNotifier(
    IServiceScopeFactory scopes,
    IConfiguration config,
    DataCatalog catalog,
    IRouteCatalog routes,
    ILogger<ConfigChangeNotifier> logger,
    TimeProvider time)
    : IEndpointWriteObserver {
    public void OnEndpointWritten(string routePath, string json, string? previousJson = null) {
        if (catalog.ByWireRoute(routePath) is not { } source) return;
        if (source.Feed is not { } feed) {
            _ = Task.Run(async () => {
                try {
                    using var scope = scopes.CreateScope();
                    await UpsertStoredEndpointAsync(scope.ServiceProvider, routePath, json);
                } catch (Exception ex) {
                    logger.LogWarning(ex, "stored_endpoint upsert for {Route} failed", routePath);
                }
            });
            return;
        }

        var change = ConfigAspects.Diff(feed, previousJson, json);
        if (change is null) {
            logger.LogInformation("config change on {Route} (feed {Feed}) could not be characterised",
                routePath, feed);
        } else {
            logger.LogInformation(
                "config change on {Route} (feed {Feed}): changed [{Changed}], added [{Added}], removed [{Removed}]",
                routePath, feed,
                string.Join(", ", change.Changed), string.Join(", ", change.Added),
                string.Join(", ", change.Removed));
        }

        string fixtureSha = Hashes.Sha256Hex(ProtoJson.StripVolatile(json));
        string dedupSha = ChangeSha(change) ?? fixtureSha;
        string pageUrl = PageUrl(config["Feed:PageBaseUrl"], feed);
        _ = Task.Run(async () => {
            try {
                using var scope = scopes.CreateScope();
                await UpsertStoredEndpointAsync(scope.ServiceProvider, routePath, json);
                await InsertSnapshotAsync(scope.ServiceProvider, routePath, json, fixtureSha);
                var dispatcher = scope.ServiceProvider.GetService<FeedDispatcher>();
                if (dispatcher is null) return;
                await dispatcher.DispatchAsync(new ConfigChangedEvent(feed, fixtureSha, pageUrl, change, dedupSha));
            } catch (Exception ex) {
                logger.LogWarning(ex, "config-change dispatch for {Feed} threw", feed);
            }
        });
    }

    private static string? ChangeSha(ConfigChangeSummary? change) =>
        change is null
            ? null
            : Hashes.Sha256Hex(string.Join('\n',
                [.. change.Changed, "--", .. change.Added, "--", .. change.Removed]));

    public static string PageUrl(string? baseUrl, string feed) {
        string root = string.IsNullOrEmpty(baseUrl) ? FeedDispatcher.DefaultPageBaseUrl : baseUrl.TrimEnd('/');
        return string.Equals(feed, ConfigFeeds.Periodicals, StringComparison.Ordinal)
            ? $"{root}/periodicals"
            : $"{root}/data";
    }

    private async Task UpsertStoredEndpointAsync(IServiceProvider sp, string route, string json) {
        try {
            if (sp.GetService<EggIncognitoDbContext>() is not { } db) return;
            string responseType = routes.Resolve(route)?.Response ?? "";
            var existing = await db.StoredEndpoints
                .FirstOrDefaultAsync(e => e.Path == route && e.Eid == null);
            if (existing is null) {
                db.StoredEndpoints.Add(new StoredEndpoint {
                    Path = route,
                    Eid = null,
                    ResponseJson = json,
                    ResponseType = responseType,
                    OwnerUserId = null
                });
            } else {
                existing.ResponseJson = json;
                existing.ResponseType = responseType;
                existing.UpdatedAt = time.GetUtcNow();
            }

            await db.SaveChangesAsync();
        } catch (Exception ex) {
            logger.LogWarning(ex, "config-change stored_endpoint upsert for {Route} failed", route);
        }
    }

    private async Task InsertSnapshotAsync(IServiceProvider sp, string route, string json, string sha) {
        try {
            if (!string.Equals(routes.Resolve(route)?.Response, PeriodicalsResponse.Descriptor.Name,
                    StringComparison.Ordinal))
                return;
            if (sp.GetService<EggIncognitoDbContext>() is not { } db) return;
            var response = await IngestEventsAsync(sp, json);
            await IngestContractsAsync(sp, json, response);
            if (await db.PeriodicalsSnapshots.AnyAsync(s => s.Sha == sha)) return;
            db.PeriodicalsSnapshots.Add(new PeriodicalsSnapshot {
                CapturedAt = time.GetUtcNow(),
                Sha = sha,
                ResponseJson = json
            });
            await db.SaveChangesAsync();
        } catch (Exception ex) {
            logger.LogWarning(ex, "periodicals snapshot insert for {Route} failed", route);
        }
    }

    private async Task<PeriodicalsResponse?> IngestEventsAsync(IServiceProvider sp, string json) {
        try {
            var response = (PeriodicalsResponse)JsonParser.Default.Parse(json, PeriodicalsResponse.Descriptor);
            if (sp.GetService<GameEventIngestor>() is { } ingestor) {
                var observations = GameEventMapper.FromPeriodicals(response, time.GetUtcNow());
                if (observations.Count > 0) await ingestor.IngestAsync(observations);
            }
            return response;
        } catch (Exception ex) {
            logger.LogWarning(ex, "game event ingest from periodicals snapshot failed");
            return null;
        }
    }

    private async Task IngestContractsAsync(IServiceProvider sp, string json, PeriodicalsResponse? response) {
        try {
            if (sp.GetService<ContractIngestor>() is not { } ingestor) return;
            response ??= (PeriodicalsResponse)JsonParser.Default.Parse(json, PeriodicalsResponse.Descriptor);
            var observations = ContractMapper.FromPeriodicals(response, time.GetUtcNow());
            if (observations.Count > 0) await ingestor.IngestAsync(observations);
        } catch (Exception ex) {
            logger.LogWarning(ex, "contract ingest from periodicals snapshot failed");
        }
    }
}
