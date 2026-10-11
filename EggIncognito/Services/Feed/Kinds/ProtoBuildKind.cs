using System.Globalization;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Services.Feed.Kinds;

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
    public string Kind => FeedEventKinds.ProtoBuild;
    public string DedupKey => ProtoVersionId.ToString(CultureInfo.InvariantCulture);
    string? INotificationEvent.Platform => Platform;

    public IReadOnlyList<string> FlawList => Flaws ?? [];
    public bool Flawed => FlawList.Count > 0;
    public bool Forward => Delta == VersionDelta.Forward;

    public string Summary => $"{Platform} {AppVersion} ({Build}) {VersionDeltaCalc.Label(Delta)}";

    public IReadOnlyDictionary<string, string> Vars() => new Dictionary<string, string> {
        ["platform"] = Platform,
        ["appVersion"] = AppVersion,
        ["build"] = Build,
        ["clientVersion"] = ClientVersion ?? "",
        ["protoSha"] = ProtoSha,
        ["protoChanged"] = ProtoChanged ? "changed" : "unchanged",
        ["pageUrl"] = PageUrl,
        ["delta"] = VersionDeltaCalc.Label(Delta),
        ["prevAppVersion"] = PrevAppVersion ?? "",
        ["prevBuild"] = PrevBuild ?? "",
        ["flaws"] = FeedText.Joined(FlawList)
    };

    public FeedEmbed Embed() {
        var fields = new List<FeedEmbedField> {
            new("Proto", ProtoChanged ? "changed" : "unchanged", true),
            new("SHA", FeedText.Or(FeedText.Short(ProtoSha), "none"), true),
            new("Delta", VersionDeltaCalc.Label(Delta), true)
        };
        if (!string.IsNullOrEmpty(ClientVersion)) fields.Add(new FeedEmbedField("Client", ClientVersion, true));
        if (!string.IsNullOrEmpty(PrevAppVersion))
            fields.Add(new FeedEmbedField("Previous", $"{PrevAppVersion} ({PrevBuild})", true));
        if (Flawed) fields.Add(new FeedEmbedField("Flaws", string.Join(", ", FlawList), false));

        int color = Flawed || Delta is VersionDelta.Backfill or VersionDelta.Unknown
            ? 0xe05252
            : ProtoChanged ? 0xef7559 : 0x5aa9e6;
        return new FeedEmbed($"Egg, Inc. {AppVersion} (build {Build}, {Platform})", PageUrl, color, fields);
    }
}

public static class ProtoBuildKind {
    private const string SampleSha = "4a17bc8f0402d1e6b8c3f95a2e7d40b1c6839fae";

    private static bool Proto(INotificationEvent evt, Func<ProtoBuildEvent, bool> test) =>
        evt is ProtoBuildEvent p && p.Forward && test(p);

    private static ProtoBuildEvent Sample(
        string platform, string appVersion, string build, string? clientVersion, string sha,
        bool created, bool protoChanged, VersionDelta delta, string? prevAppVersion, string? prevBuild,
        bool hasProto) =>
        new(0, platform, appVersion, build, clientVersion, sha, created, protoChanged,
            FeedDispatcher.BuildPageUrl(null, platform, build), delta, prevAppVersion, prevBuild,
            ProtoVersionQuality.Flaws(platform, build, clientVersion, sha, hasProto));

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.ProtoBuild, "Proto build",
        "A new Egg, Inc. build landed in the proto registry, with its extracted proto.",
        [
            new FeedTriggerOption(FeedEventKinds.TriggerVersionUp, "New version", e => Proto(e, _ => true)),
            new FeedTriggerOption(FeedEventKinds.TriggerProtoChanged, "New version, proto changed",
                e => Proto(e, p => p.ProtoChanged)),
            new FeedTriggerOption(FeedEventKinds.TriggerNewVersion, "New version, first insert",
                e => Proto(e, p => p.Created)),
            new FeedTriggerOption(FeedEventKinds.TriggerSuspect, "New version, suspect extraction",
                e => Proto(e, p => p.Flawed))
        ],
        FeedEventKinds.TriggerVersionUp,
        [
            new FeedFilterOption(FeedEventKinds.FilterRequireClientVersion, "Require client version", true,
                e => e is ProtoBuildEvent p && p.FlawList.Contains(ProtoVersionQuality.FlawNoClientVersion)),
            new FeedFilterOption(FeedEventKinds.FilterRequireProto, "Require proto", true,
                e => e is ProtoBuildEvent p && p.FlawList.Contains(ProtoVersionQuality.FlawNoProto)),
            new FeedFilterOption(FeedEventKinds.FilterSaneBuild, "Require sane build", true,
                e => e is ProtoBuildEvent p && p.FlawList.Contains(ProtoVersionQuality.FlawBuildPlatformMismatch)),
            new FeedFilterOption(FeedEventKinds.FilterKnownDelta, "Require known delta", true,
                e => e is ProtoBuildEvent { Delta: VersionDelta.Unknown })
        ],
        [
            "platform", "appVersion", "build", "clientVersion", "protoSha", "protoChanged", "pageUrl",
            "delta", "prevAppVersion", "prevBuild", "flaws"
        ],
        true,
        [
            new FeedSample("forward", "New version",
                Sample("android", "1.37.0", "111358", "72", SampleSha, true, true,
                    VersionDelta.Forward, "1.36.4", "111357", true)),
            new FeedSample("proto_only", "Same version, proto changed",
                Sample("android", "1.37.0", "111358", "72", SampleSha, false, true,
                    VersionDelta.Repeat, "1.37.0", "111358", true)),
            new FeedSample("backfill", "Older build registered",
                Sample("android", "1.36.0", "111350", "71", SampleSha, true, true,
                    VersionDelta.Backfill, "1.37.0", "111358", true)),
            new FeedSample("broken", "Failed extraction",
                Sample("ios", "1.37.1", "111340", null, "", true, true,
                    VersionDelta.Forward, "1.37.0", "1.37.0.1", false))
        ],
        [FeedEventKinds.TriggerSuspect]);
}
