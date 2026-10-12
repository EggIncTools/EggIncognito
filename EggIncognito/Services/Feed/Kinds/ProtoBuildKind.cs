using System.Globalization;
using EggIdentity.Filters;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;

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

    public IReadOnlyList<string> FlawList => Flaws ?? [];
    public bool Flawed => FlawList.Count > 0;

    public string Summary => $"{Platform} {AppVersion} ({Build}) {VersionDeltaCalc.Label(Delta)}";

    public FilterSubjectValue Subject(string field) => field switch {
        ProtoBuildKind.Platform => FilterSubjectValue.Of(Platform),
        ProtoBuildKind.AppVersion => FilterSubjectValue.Of(AppVersion),
        ProtoBuildKind.ClientVersion => FilterSubjectValue.Of(
            double.TryParse(ClientVersion, NumberStyles.Float, CultureInfo.InvariantCulture, out double cv) ? cv : null),
        ProtoBuildKind.Delta => FilterSubjectValue.Of(VersionDeltaCalc.Label(Delta)),
        ProtoBuildKind.Created => FilterSubjectValue.Of(Created),
        ProtoBuildKind.ProtoChangedField => FilterSubjectValue.Of(ProtoChanged),
        ProtoBuildKind.FlawsField => FilterSubjectValue.Of(FlawList),
        _ => FilterSubjectValue.Absent
    };

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
    public const string Platform = "platform";
    public const string AppVersion = "app_version";
    public const string ClientVersion = "client_version";
    public const string Delta = "delta";
    public const string Created = "created";
    public const string ProtoChangedField = "proto_changed";
    public const string FlawsField = "flaws";

    private const string SampleSha = "4a17bc8f0402d1e6b8c3f95a2e7d40b1c6839fae";

    private static readonly string[] AllFlaws = [
        ProtoVersionQuality.FlawNoClientVersion, ProtoVersionQuality.FlawNoProto,
        ProtoVersionQuality.FlawBuildPlatformMismatch
    ];

    private static readonly string Forward = VersionDeltaCalc.Label(VersionDelta.Forward);

    public static readonly FilterSet Defaults = FeedFilter.Rows(
        AllFlaws.Select(f => new[] { FeedFilter.Lacks(FlawsField, f) }).Prepend([FeedFilter.Is(Delta, Forward)]));

    public static FilterSet Legacy(FeedSubscription sub) {
        var rows = new List<FilterCondition[]> { new[] { FeedFilter.Is(Delta, Forward) } };
        switch (sub.Trigger) {
            case FeedEventKinds.TriggerProtoChanged:
                rows.Add([FeedFilter.Flag(ProtoChangedField, true)]);
                break;
            case FeedEventKinds.TriggerNewVersion:
                rows.Add([FeedFilter.Flag(Created, true)]);
                break;
            case FeedEventKinds.TriggerSuspect:
                rows.Add([.. AllFlaws.Select(f => FeedFilter.Has(FlawsField, f))]);
                break;
        }

        if (!(sub.Platforms.Contains("android") && sub.Platforms.Contains("ios")) &&
            FeedFilter.AnyOf(Platform, sub.Platforms) is { } platforms)
            rows.Add(platforms);

        if (sub.Trigger != FeedEventKinds.TriggerSuspect) {
            foreach (string filter in sub.Filters) {
                string? flaw = filter switch {
                    FeedEventKinds.FilterRequireClientVersion => ProtoVersionQuality.FlawNoClientVersion,
                    FeedEventKinds.FilterRequireProto => ProtoVersionQuality.FlawNoProto,
                    FeedEventKinds.FilterSaneBuild => ProtoVersionQuality.FlawBuildPlatformMismatch,
                    _ => null
                };
                if (flaw is not null) rows.Add([FeedFilter.Lacks(FlawsField, flaw)]);
            }
        }

        return FeedFilter.Rows(rows);
    }

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
            FeedFilter.Select(Platform, "Platform", FeedFilter.Options("android", "ios")),
            FeedFilter.Version(AppVersion, "App version"),
            FeedFilter.Number(ClientVersion, "Client version"),
            FeedFilter.Select(Delta, "Version delta",
                Enum.GetValues<VersionDelta>().Select(d => new FilterOption(VersionDeltaCalc.Label(d), VersionDeltaCalc.Label(d)))),
            FeedFilter.Bool(Created, "First insert"),
            FeedFilter.Bool(ProtoChangedField, "Proto changed"),
            FeedFilter.Set(FlawsField, "Extraction flaws", AllFlaws.Select(f => new FilterOption(f, FeedVars.Label(f).Replace('_', ' '))))
        ],
        Defaults,
        Legacy,
        [
            "platform", "appVersion", "build", "clientVersion", "protoSha", "protoChanged", "pageUrl",
            "delta", "prevAppVersion", "prevBuild", "flaws"
        ],
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
        ]);
}
