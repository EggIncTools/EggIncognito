using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;
using EggIncognito.Services.Feed.Kinds;

namespace EggIncognito.Services.Feed;

public sealed class ProtoUpsertNotifier(FeedPublisher? publisher = null) : IProtoUpsertObserver {
    public Task OnUpsertAsync(ProtoUpsertNotice notice, CancellationToken ct) {
        if (publisher is null) return Task.CompletedTask;
        var flaws = ProtoVersionQuality.Flaws(
            notice.Platform, notice.Build, notice.ClientVersion, notice.ProtoSha, notice.HasProtoText);
        return publisher.PublishAsync(new ProtoBuildEvent(
            notice.ProtoVersionId, notice.Platform, notice.AppVersion, notice.Build, notice.ClientVersion,
            notice.ProtoSha, notice.Created, notice.ProtoChanged,
            publisher.ProtoPageUrl(notice.Platform, notice.Build),
            notice.Delta, notice.PrevAppVersion, notice.PrevBuild, flaws), ct);
    }
}
