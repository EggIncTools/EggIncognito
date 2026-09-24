using System.Text;
using System.Text.Json;
using EggIdentity.Contract;
using EggIncognito.Bot;
using EggIncognito.Core;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;
using EggIncognito.Services;

namespace EggIncognito.Startup;

public static class SyncIngestServices {
    public static void AddSyncIngest(this WebApplicationBuilder builder, BootFlags boot) {
        if (!boot.SyncIngestEnabled) return;

        string syncContentRoot = ContentRoot.Resolve(builder.Configuration["ContentRoot"]);
        var syncOptions = new SyncEventOptions {
            EventSecret = boot.EventSecret!,
            ApkFetchRoot = builder.Configuration["SyncEvent:ApkFetchRoot"] ?? ""
        };
        builder.Services.AddSingleton(syncOptions);
        builder.Services.AddSingleton<ISyncNotifier, DiscordSyncNotifier>();
        builder.Services.AddSingleton(sp => Ingest(sp, syncOptions, syncContentRoot));
    }

    private static NewVersionIngestService Ingest(
        IServiceProvider sp, SyncEventOptions syncOptions, string syncContentRoot) {
        string expectedProtoSha = ProtoHash.Current();
        var notifier = sp.GetRequiredService<ISyncNotifier>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("sync.ingest");

        async Task Registry(NewVersionEvent evt, CancellationToken ct) {
            using var scope = sp.CreateScope();
            var store = scope.ServiceProvider.GetService<ProtoRegistryStore>();
            if (store is null) return;
            string? protoText = string.IsNullOrEmpty(evt.ProtoTextB64)
                ? null
                : Encoding.UTF8.GetString(Convert.FromBase64String(evt.ProtoTextB64));
            string protoSha = evt.ProtoSha;
            if (protoText is not null) {
                var norm = ProtoCanonicalForm.Normalize(protoText);
                if (norm.Ok) protoSha = norm.Sha;
            }

            string? appVersion = string.IsNullOrEmpty(evt.AppVersion) ? evt.Version : evt.AppVersion;
            string? build = string.IsNullOrEmpty(evt.Build) ? evt.Version : evt.Build;
            if (string.IsNullOrEmpty(build) || string.IsNullOrEmpty(appVersion)) return;

            await store.UpsertAsync(
                evt.Platform ?? "android", appVersion, build, evt.ClientVersion, evt.Package, protoSha, evt.ApkRef,
                DateTimeOffset.TryParse(evt.DetectedAt, out var dt) ? dt : sp.GetRequiredService<TimeProvider>().GetUtcNow(),
                null, protoText, ct: ct);
        }

        Task Fetch(NewVersionEvent evt, CancellationToken ct) {
            if (string.IsNullOrEmpty(syncOptions.ApkFetchRoot) || string.IsNullOrEmpty(evt.ApkRef)) {
                logger.LogInformation("sync: no ApkFetchRoot or apkRef for {Version}, skipping fetch", evt.Version);
                return Task.CompletedTask;
            }

            string apk = Path.Combine(syncOptions.ApkFetchRoot, evt.ApkRef.TrimStart('/', '\\'));
            if (!File.Exists(apk))
                logger.LogWarning("sync: apk not found at {Apk} for {Version}", apk, evt.Version);
            return Task.CompletedTask;
        }

        Task Regen(NewVersionEvent evt, CancellationToken ct) {
            EndpointExtractor.ForRepo(syncContentRoot, null, "EI0000000000000000", true);
            logger.LogInformation("sync: staged area ready for {Version}; apk-driven regen not yet wired", evt.Version);
            return Task.CompletedTask;
        }

        Task Stash(NewVersionEvent evt, CancellationToken ct) {
            string stashDir = Path.Combine(syncContentRoot, "Endpoints", "staged", "proto-refresh");
            Directory.CreateDirectory(stashDir);
            string manifest = JsonSerializer.Serialize(new {
                version = evt.Version,
                oldProtoSha = expectedProtoSha,
                newProtoSha = evt.ProtoSha,
                apkRef = evt.ApkRef,
                detectedAt = evt.DetectedAt
            });
            File.WriteAllText(Path.Combine(stashDir, $"{evt.Version}.json"), manifest);
            logger.LogWarning("sync: proto changed for {Version}, stashed refresh manifest", evt.Version);
            return Task.CompletedTask;
        }

        return new NewVersionIngestService(expectedProtoSha, notifier, Registry, Fetch, Regen, Stash);
    }
}
