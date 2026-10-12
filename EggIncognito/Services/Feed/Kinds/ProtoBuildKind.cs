using System.Globalization;
using EggIdentity.Filters;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

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
        ProtoBuildKind.Clean => FilterSubjectValue.Of(!Flawed),
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
    public const string Clean = "clean";

    private static readonly string[] AllFlaws = [
        ProtoVersionQuality.FlawNoClientVersion, ProtoVersionQuality.FlawNoProto,
        ProtoVersionQuality.FlawBuildPlatformMismatch
    ];

    private static readonly string Forward = VersionDeltaCalc.Label(VersionDelta.Forward);

    public static readonly FilterSet Defaults = FeedFilter.Rows(
        [FeedFilter.Is(Delta, Forward)], [FeedFilter.Flag(Clean, true)]);

    private static readonly Dictionary<string, string> GuardFlaws = new(StringComparer.Ordinal) {
        [FeedEventKinds.FilterRequireClientVersion] = ProtoVersionQuality.FlawNoClientVersion,
        [FeedEventKinds.FilterRequireProto] = ProtoVersionQuality.FlawNoProto,
        [FeedEventKinds.FilterSaneBuild] = ProtoVersionQuality.FlawBuildPlatformMismatch
    };

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
                rows.Add([FeedFilter.Flag(Clean, false)]);
                break;
        }

        if (!(sub.Platforms.Contains("android") && sub.Platforms.Contains("ios")) &&
            FeedFilter.AnyOf(Platform, sub.Platforms) is { } platforms)
            rows.Add(platforms);

        if (sub.Trigger == FeedEventKinds.TriggerSuspect) return FeedFilter.Rows(rows);

        string[] guarded = [.. GuardFlaws.Where(g => sub.Filters.Contains(g.Key)).Select(g => g.Value)];
        if (guarded.Length == GuardFlaws.Count) rows.Add([FeedFilter.Flag(Clean, true)]);
        else rows.AddRange(guarded.Select(f => new[] { FeedFilter.Lacks(FlawsField, f) }));

        return FeedFilter.Rows(rows);
    }

    public static async Task<IReadOnlyList<FeedSample>> LoadSamples(FeedSampleContext ctx, CancellationToken ct) {
        var rows = await ctx.Db.ProtoVersions.AsNoTracking()
            .Where(p => p.DeletedAt == null)
            .OrderByDescending(p => p.DetectedAt)
            .Take(FeedSampleSource.PerKind * 4)
            .ToListAsync(ct);
        int[] ids = [.. rows.Select(r => r.Id)];
        var withText = (await ctx.Db.ProtoProtos.AsNoTracking()
            .Where(x => ids.Contains(x.ProtoVersionId))
            .Select(x => x.ProtoVersionId)
            .ToListAsync(ct)).ToHashSet();

        var samples = new List<FeedSample>();
        foreach (var row in rows.Take(FeedSampleSource.PerKind)) {
            var prev = rows
                .Where(p => p.Platform == row.Platform && p.DetectedAt < row.DetectedAt)
                .MaxBy(p => p.DetectedAt);
            var delta = VersionDeltaCalc.Classify(row.Platform, row.Build, row.AppVersion, prev?.Build, prev?.AppVersion);
            var evt = new ProtoBuildEvent(row.Id, row.Platform, row.AppVersion, row.Build, row.ClientVersion,
                row.ProtoSha, true, prev is null || prev.ProtoSha != row.ProtoSha,
                ctx.Pages.ProtoPageUrl(row.Platform, row.Build), delta, prev?.AppVersion, prev?.Build,
                ProtoVersionQuality.Flaws(row.Platform, row.Build, row.ClientVersion, row.ProtoSha, withText.Contains(row.Id)));
            samples.Add(new FeedSample(row.Id.ToString(CultureInfo.InvariantCulture),
                $"{row.Platform} {row.AppVersion} ({row.Build})", evt));
        }

        return samples;
    }

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.ProtoBuild, "Proto build",
        "A new build landed in the proto registry.",
        [
            FeedFilter.Select(Platform, "Platform", FeedFilter.Options("android", "ios")),
            FeedFilter.Version(AppVersion, "App version"),
            FeedFilter.Number(ClientVersion, "Client version"),
            FeedFilter.Select(Delta, "Version delta",
                Enum.GetValues<VersionDelta>().Select(d => new FilterOption(VersionDeltaCalc.Label(d), VersionDeltaCalc.Label(d)))),
            FeedFilter.Bool(Created, "First insert"),
            FeedFilter.Bool(ProtoChangedField, "Proto changed"),
            FeedFilter.Bool(Clean, "Clean extraction"),
            FeedFilter.Set(FlawsField, "Extraction flaw", AllFlaws.Select(f => new FilterOption(f, f.Replace('_', ' '))))
        ],
        Defaults,
        Legacy,
        [
            "platform", "appVersion", "build", "clientVersion", "protoSha", "protoChanged", "pageUrl",
            "delta", "prevAppVersion", "prevBuild", "flaws"
        ],
        LoadSamples);
}
