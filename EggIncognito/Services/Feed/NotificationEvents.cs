using System.Globalization;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Services.DataApi;

namespace EggIncognito.Services.Feed;

public interface INotificationEvent {
    string EventKind { get; }
    string DedupKey { get; }
    string Summary { get; }
    bool Matches(FeedSubscription sub);
    IReadOnlyList<string> BlockedBy(FeedSubscription sub);
    string BuildBody(string? messageTemplate);
}

public sealed record ProtoBuildEvent(
    int ProtoVersionId,
    string Platform,
    string AppVersion,
    string Build,
    string? ClientVersion,
    string ProtoSha,
    bool Created,
    bool ProtoChanged,
    string PageUrl,
    VersionDelta Delta = VersionDelta.Unknown,
    string? PrevAppVersion = null,
    string? PrevBuild = null,
    IReadOnlyList<string>? Flaws = null) : INotificationEvent {
    public string EventKind => FeedEventKinds.ProtoBuild;
    public string DedupKey => ProtoVersionId.ToString(CultureInfo.InvariantCulture);

    public IReadOnlyList<string> FlawList => Flaws ?? [];

    public string Summary =>
        $"{Platform} {AppVersion} ({Build}) {VersionDeltaCalc.Label(Delta)}";

    public bool Matches(FeedSubscription sub) =>
        FeedTrigger.Matches(sub.Trigger, Created, ProtoChanged, Delta, FlawList.Count > 0,
            sub.Platforms, Platform);

    public IReadOnlyList<string> BlockedBy(FeedSubscription sub) {
        if (sub.Trigger == FeedEventKinds.TriggerSuspect) return [];

        List<string> blocked = [.. sub.Filters.Where(filter => filter switch {
            FeedEventKinds.FilterRequireClientVersion =>
                FlawList.Contains(ProtoVersionQuality.FlawNoClientVersion),
            FeedEventKinds.FilterRequireProto =>
                FlawList.Contains(ProtoVersionQuality.FlawNoProto),
            FeedEventKinds.FilterSaneBuild =>
                FlawList.Contains(ProtoVersionQuality.FlawBuildPlatformMismatch),
            FeedEventKinds.FilterKnownDelta => Delta == VersionDelta.Unknown,
            _ => false
        })];

        return blocked;
    }

    public string BuildBody(string? messageTemplate) =>
        DiscordFeedPayload.Build(Platform, AppVersion, Build, ClientVersion, ProtoSha, ProtoChanged, PageUrl,
            messageTemplate, Delta, PrevAppVersion, PrevBuild, FlawList);
}

public sealed record ConfigChangedEvent(
    string Feed,
    string Sha,
    string PageUrl,
    ConfigChangeSummary? Change = null,
    string? DedupSha = null) : INotificationEvent {
    public string EventKind => FeedEventKinds.ConfigChanged;
    public string DedupKey => $"{Feed}:{DedupSha ?? Sha}";

    public string FeedLabel => ConfigFeeds.LabelOf(Feed);

    public IReadOnlyList<string> Changed => Change?.Changed ?? [];
    public IReadOnlyList<string> Added => Change?.Added ?? [];
    public IReadOnlyList<string> Removed => Change?.Removed ?? [];

    public string Summary => Changed.Count == 0
        ? $"{FeedLabel} changed"
        : $"{FeedLabel}: {string.Join(", ", Changed)}";

    public bool Matches(FeedSubscription sub) =>
        sub.Trigger is FeedEventKinds.TriggerAnyFeed
        || string.Equals(sub.Trigger, Feed, StringComparison.Ordinal);

    public IReadOnlyList<string> BlockedBy(FeedSubscription sub) {
        return [.. sub.Filters.Where(filter => filter switch {
            FeedEventKinds.FilterRequireAspects => Changed.Count == 0,
            FeedEventKinds.FilterRequireIds => Added.Count == 0 && Removed.Count == 0,
            _ => false
        })];
    }

    public string BuildBody(string? messageTemplate) =>
        DiscordFeedPayload.BuildConfig(Feed, FeedLabel, Sha, PageUrl, messageTemplate, Changed, Added, Removed);
}

public sealed record GameDataRebuiltEvent(
    string BinaryVersion,
    string? PrevBinaryVersion,
    string Platform,
    string InputSha,
    IReadOnlyList<string> ChangedDocs,
    string PageUrl) : INotificationEvent {
    public string EventKind => FeedEventKinds.GameDataRebuilt;
    public string DedupKey => InputSha;

    public bool BinaryMoved =>
        !string.Equals(BinaryVersion, PrevBinaryVersion, StringComparison.Ordinal);

    public string Summary =>
        $"game data {BinaryVersion} ({ChangedDocs.Count} doc{(ChangedDocs.Count == 1 ? "" : "s")})";

    public bool Matches(FeedSubscription sub) {
        if (ChangedDocs.Count == 0) return false;
        return sub.Trigger != FeedEventKinds.TriggerBinaryUp || BinaryMoved;
    }

    public IReadOnlyList<string> BlockedBy(FeedSubscription sub) => [];

    public string BuildBody(string? messageTemplate) =>
        DiscordFeedPayload.BuildGameData(BinaryVersion, PrevBinaryVersion, Platform, InputSha, ChangedDocs,
            PageUrl, messageTemplate);
}
