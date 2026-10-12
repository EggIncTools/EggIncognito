using EggIncognito.Core;
using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Events;
using EggIncognito.Services.Feed.Kinds;
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
    TimeProvider time,
    FeedPublisher? publisher = null)
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
        string pageUrl = ConfigChangedKind.PageUrl(config["Feed:PageBaseUrl"], feed);
        _ = Task.Run(async () => {
            try {
                using var scope = scopes.CreateScope();
                await UpsertStoredEndpointAsync(scope.ServiceProvider, routePath, json);
                await InsertSnapshotAsync(scope.ServiceProvider, routePath, json, fixtureSha);
            } catch (Exception ex) {
                logger.LogWarning(ex, "config-change landing for {Feed} threw", feed);
            }

            if (publisher is not null)
                await publisher.PublishAsync(new ConfigChangedEvent(feed, fixtureSha, pageUrl, change, dedupSha),
                    CancellationToken.None);
        });
    }

    private static string? ChangeSha(ConfigChangeSummary? change) =>
        change is null
            ? null
            : Hashes.Sha256Hex(string.Join('\n',
                [.. change.Changed, "--", .. change.Added, "--", .. change.Removed]));

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
            if (observations.Count == 0) return;
            var firstSeen = await FirstSeenColleggtiblesAsync(sp, observations);
            await ingestor.IngestAsync(observations);
            PublishColleggtibles(response, observations, firstSeen);
        } catch (Exception ex) {
            logger.LogWarning(ex, "contract ingest from periodicals snapshot failed");
        }
    }

    private static async Task<IReadOnlyList<string>> FirstSeenColleggtiblesAsync(
        IServiceProvider sp, IReadOnlyList<ContractObservation> observations) {
        string[] carried = [.. observations.Select(o => o.CustomEggId).OfType<string>().Distinct(StringComparer.Ordinal)];
        if (carried.Length == 0 || sp.GetService<EggIncognitoDbContext>() is not { } db) return [];
        var known = await db.ContractReleases
            .Where(r => r.CustomEggId != null && carried.Contains(r.CustomEggId))
            .Select(r => r.CustomEggId!)
            .Distinct()
            .ToListAsync();
        return [.. carried.Except(known, StringComparer.Ordinal)];
    }

    private void PublishColleggtibles(
        PeriodicalsResponse response, IReadOnlyList<ContractObservation> observations, IReadOnlyList<string> firstSeen) {
        if (publisher is null || firstSeen.Count == 0) return;
        var eggs = response.Contracts?.CustomEggs ?? [];
        string pageUrl = publisher.PageUrl("periodicals");
        foreach (string id in firstSeen) {
            if (eggs.FirstOrDefault(e => e.Identifier == id) is not { } egg) continue;
            publisher.Publish(ColleggtibleKind.From(egg,
                [.. observations.Where(o => o.CustomEggId == id).Select(o => o.ContractId)], pageUrl));
        }
    }
}
