using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Feed;

public sealed class ProtoUpsertNotifier(
    IConfiguration config,
    ILogger<ProtoUpsertNotifier> logger,
    FeedDispatcher? dispatcher = null) : IProtoUpsertObserver {
    public async Task OnUpsertAsync(ProtoUpsertNotice notice, CancellationToken ct) {
        if (dispatcher is null) return;
        try {
            string pageUrl = FeedDispatcher.BuildPageUrl(
                config["Feed:PageBaseUrl"], notice.Platform, notice.Build);
            var flaws = ProtoVersionQuality.Flaws(
                notice.Platform, notice.Build, notice.ClientVersion, notice.ProtoSha, notice.HasProtoText);
            await dispatcher.DispatchAsync(new ProtoBuildEvent(
                notice.ProtoVersionId, notice.Platform, notice.AppVersion, notice.Build, notice.ClientVersion,
                notice.ProtoSha, notice.Created, notice.ProtoChanged, pageUrl,
                notice.Delta, notice.PrevAppVersion, notice.PrevBuild, flaws), ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "proto-build dispatch for {Platform} {Build} threw",
                notice.Platform, notice.Build);
        }
    }
}
